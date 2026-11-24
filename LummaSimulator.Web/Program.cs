using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.AuditExport;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using LummaSimulator.Configuration;
using LummaSimulator.Web.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

// Offline helper: no password in shell history, arguments, configuration files or application logs.
if (args.Contains("--generate-monitor-key"))
{
    var integrationKey = DemoIdentity.RandomToken();
    Console.WriteLine("WPF LUMMA_MONITOR_KEY (keep private): " + integrationKey);
    Console.WriteLine("Web Monitor__KeyHash: " + DemoIdentity.Hash(integrationKey));
    return;
}
if (args.Contains("--hash-presenter-password")) throw new InvalidOperationException("Password presenter login has been removed. Use --bootstrap-administrator.");
if (args.Contains("--bootstrap-administrator")) LocalMaintenance.RequireAdministrator();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
var webAuthn = builder.Configuration.GetSection("WebAuthn").Get<WebAuthnSettings>() ?? new();
var experience = builder.Configuration.GetSection("Experience").Get<ExperienceSettings>() ?? new();
// ConfigurationBinder appends configured arrays to property defaults. Read this
// array separately so an explicit language list replaces, rather than duplicates, defaults.
experience.AvailableLanguages = builder.Configuration.GetSection("Experience:AvailableLanguages").Get<string[]>() ?? ["nl-NL", "en-GB"];
webAuthn.Validate(builder.Environment.IsDevelopment(), experience.Hostname);
experience.Validate(webAuthn);
builder.Services.AddSingleton(experience);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(experience.DefaultLanguage)
        .AddSupportedCultures(experience.AvailableLanguages).AddSupportedUICultures(experience.AvailableLanguages);
    options.RequestCultureProviders = [new CustomRequestCultureProvider(context => LanguagePreference.Resolve(context, experience))];
});
builder.Services.AddSingleton(webAuthn);
builder.Services.AddScoped<ParticipantQrService>();
builder.Services.AddSingleton<HelperDownload>();
builder.Services.AddScoped<VisitorExerciseService>();
// No MDS provider or external metadata service is configured.
builder.Services.AddFido2(options =>
{
    options.RPID = webAuthn.RpId;
    options.RPName = WebAuthnSettings.DisplayName;
    options.Origins = webAuthn.Origins.ToHashSet(StringComparer.Ordinal);
    options.Timeout = 120000;
    options.ChallengeSize = 32;
    options.AllowCrossOriginRequests = false;
});
var databasePath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(databasePath))
    databasePath = Path.Combine(RuntimePaths.DataRoot(builder.Configuration["Storage:DataDirectory"]), "PasskeyPortal", "portal.db");
databasePath = RuntimePaths.Resolve(databasePath);
var wwwroot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
if (databasePath.StartsWith(wwwroot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The database must be outside wwwroot.");
builder.Services.AddSingleton(new DemoStore(databasePath));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SecurityAuditService>();
builder.Services.AddIdentityAuditExport(builder.Configuration);
builder.Services.AddScoped<DemoService>();
builder.Services.AddScoped<PresenterService>();
builder.Services.AddScoped<EventAccess>();
builder.Services.AddScoped<PresenterOnboarding>();
builder.Services.AddScoped<PresentationSettings>();
builder.Services.AddScoped<PresenterPageAccessFilter>();
builder.Services.AddSingleton(sp => new MonitorKeyStore(Path.Combine(Path.GetDirectoryName(databasePath)!, "monitor-integration.state"),
    sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), builder.Configuration, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new EventEditionStore(Path.Combine(Path.GetDirectoryName(databasePath)!, "event-edition.manifest"), sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()));
builder.Services.AddScoped<EventEditionService>();
builder.Services.AddScoped<PrizeEventService>();
builder.Services.AddScoped<PrizeRoundsService>();
builder.Services.AddSingleton<SessionDemoState>();
builder.Services.AddScoped<SessionDemoService>();
builder.Services.AddSingleton<DemoMetrics>();
builder.Services.AddSingleton<SessionStatusService>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<LauncherStopService>();
var keysPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "keys");
if (OperatingSystem.IsWindows()) Directory.CreateDirectory(keysPath);
else Directory.CreateDirectory(keysPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var protection = builder.Services.AddDataProtection().SetApplicationName("LummaSimulator.PasskeyPortal.v0.3")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Lumma.Antiforgery";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthentication(PresenterService.Scheme).AddCookie(PresenterService.Scheme, options =>
{
    options.Cookie.Name = "__Host-Lumma.Presenter";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.LoginPath = "/Presenter/Login";
    options.AccessDeniedPath = "/Presenter/Denied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        if (!await context.HttpContext.RequestServices.GetRequiredService<PresenterService>()
                .ValidateAsync(context.Principal!, context.HttpContext.RequestAborted))
            context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy(PresenterService.Policy, policy =>
    policy.AddAuthenticationSchemes(PresenterService.Scheme).RequireAuthenticatedUser().RequireRole("Presenter").RequireClaim("amr", "webauthn")));
builder.Services.AddAuthorization(options => options.AddPolicy(PresenterService.AdminPolicy, policy => policy.AddAuthenticationSchemes(PresenterService.Scheme).RequireAuthenticatedUser().RequireRole("Administrator").RequireClaim("amr", "webauthn")));
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(PresenterPageAccessFilter)));
    options.Conventions.AuthorizeFolder("/Presenter", PresenterService.Policy);
    options.Conventions.AllowAnonymousToPage("/Presenter/Login");
    options.Conventions.AllowAnonymousToPage("/Presenter/Enroll");
    options.Conventions.AllowAnonymousToPage("/Presenter/Setup");
    options.Conventions.AuthorizePage("/Presenter/Accounts", PresenterService.AdminPolicy);
    options.Conventions.AuthorizePage("/Presenter/Audit", PresenterService.AdminPolicy);
}).AddCookieTempDataProvider(options =>
{
    options.Cookie.Name = "__Host-Lumma.Notice";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("demo", limiter => { limiter.PermitLimit = 300; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
    options.AddFixedWindowLimiter("presenter-login", limiter => { limiter.PermitLimit = 8; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
});
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("presenter-auth", limiter => { limiter.PermitLimit=40; limiter.Window=TimeSpan.FromMinutes(1); limiter.QueueLimit=0; });
    options.AddFixedWindowLimiter("presenter-management", limiter => { limiter.PermitLimit=60; limiter.Window=TimeSpan.FromMinutes(1); limiter.QueueLimit=0; });
    options.AddFixedWindowLimiter("participant-qr", limiter => { limiter.PermitLimit=120; limiter.Window=TimeSpan.FromMinutes(1); limiter.QueueLimit=0; });
});
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = 65536);
var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
    options.ForwardLimit = 1;
});

var app = builder.Build();
app.UseRequestLocalization();
await app.Services.GetRequiredService<DemoStore>().InitializeAsync();
await using(var initialization=app.Services.CreateAsyncScope())await initialization.ServiceProvider.GetRequiredService<PresentationSettings>().LoadAsync(default,true);
await using(var mapping=app.Services.CreateAsyncScope())await app.Services.GetRequiredService<EventEditionStore>().AccessAsync(false,async manifest=>{
    await mapping.ServiceProvider.GetRequiredService<EventAccess>().ImportEditionAsync(manifest.Events,default);return true;
},default);
app.Use(async(context,next)=>{await context.RequestServices.GetRequiredService<PresentationSettings>().LoadAsync(context.RequestAborted);await next(context);});
if (args.Contains("--bootstrap-administrator")) {
    Console.Write("Administrator login (3–64 ASCII characters): "); var login=Console.ReadLine() ?? "";
    Console.Write("Display name: "); var name=Console.ReadLine() ?? "";
    Console.Write("Type BOOTSTRAP to issue a one-time local enrollment authorization: ");
    if (Console.ReadLine() != "BOOTSTRAP") return;
    await using var maintenanceScope=app.Services.CreateAsyncScope();
    var invitation=await maintenanceScope.ServiceProvider.GetRequiredService<PresenterService>().IssueBootstrapAsync(login,name,CancellationToken.None);
    Console.WriteLine("Open /Presenter/Enroll on this computer at the configured HTTPS origin. Authorization expires in 10 minutes. Enter the following code there; never put it in a URL or log:");
    Console.WriteLine(invitation.Code);
    return;
}
await using (var startupScope = app.Services.CreateAsyncScope())
    await startupScope.ServiceProvider.GetRequiredService<DemoService>().CleanupAsync(CancellationToken.None);
app.Use(async (context, next) => {
    if ((context.Request.Path.StartsWithSegments("/api/event-edition") || context.Request.Path.StartsWithSegments("/api/local/session-status") || context.Request.Path.StartsWithSegments("/api/local/participant-demo")) &&
        (context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote)
        || context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("X-Forwarded-Proto") || context.Request.Headers.ContainsKey("Forwarded")))
    { context.Response.StatusCode = 403; return; }
    await next(context);
});
if (proxies.Length > 0) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    // Native form navigation under no-referrer sends Origin: null in Chromium.
    // Keep the exact Origin checks; retain a referrer only within this origin
    // for event management forms, never when navigating to another site.
    context.Response.Headers["Referrer-Policy"] = context.Request.Path.StartsWithSegments("/Presenter/Prizes") || context.Request.Path.StartsWithSegments("/Sessions")
        ? "same-origin" : "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";
    context.Response.Headers["Permissions-Policy"] = "publickey-credentials-create=(self), publickey-credentials-get=(self), camera=(), microphone=(), geolocation=()";
    var origin = context.Request.Scheme + "://" + context.Request.Host;
    if (!context.Request.IsHttps || !webAuthn.Origins.Contains(origin, StringComparer.Ordinal))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync(context.RequestServices.GetRequiredService<IStringLocalizer<UiText>>()["Gebruik de geconfigureerde HTTPS-origin."].Value);
        return;
    }
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception error)
    {
        app.Logger.LogError(new EventId(501, "RequestFailed"), "Portal request failed ({ExceptionType}); sensitive exception details suppressed", error.GetType().Name);
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = 500;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(context.RequestServices.GetRequiredService<IStringLocalizer<UiText>>()["Er is een technische fout opgetreden. Probeer opnieuw of neem contact op met de presentator."].Value);
        }
    }
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
    }
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
// Presenter authentication must not change the antiforgery identity of an open
// visitor page. Both applications may share a browser, but not authorization state.
app.Use(async (context,next) => {
    var presenterScope=context.Request.Path.StartsWithSegments("/Presenter") || context.Request.Path.StartsWithSegments("/api/presenter");
    if(context.Request.Path=="/api/language" && context.Request.HasFormContentType)
        presenterScope=(await context.Request.ReadFormAsync())["audience"]=="presenter";
    if(!presenterScope)context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
    await next(context);
});
// Only dashboard GETs may enter setup; mutations retain their authorization policy.
app.Use(async (context,next) => {
    var path=context.Request.Path.Value;
    if(HttpMethods.IsGet(context.Request.Method) &&
       (string.Equals(path,"/Presenter",StringComparison.OrdinalIgnoreCase) || string.Equals(path,"/Presenter/",StringComparison.OrdinalIgnoreCase) || string.Equals(path,"/Presenter/Index",StringComparison.OrdinalIgnoreCase)) &&
       await context.RequestServices.GetRequiredService<PresenterService>().IsSetupRequiredAsync(context.RequestAborted)) {
        context.Response.Redirect("/Presenter/Setup"); return;
    }
    await next(context);
});
app.UseAuthorization();

app.MapPost("/api/language", async (HttpContext http, IAntiforgery antiforgery) =>
{
    if (!webAuthn.Origins.Contains(http.Request.Headers.Origin.ToString(), StringComparer.Ordinal)) return Results.BadRequest();
    try { await antiforgery.ValidateRequestAsync(http); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await http.Request.ReadFormAsync();
    var language = form["language"].ToString();
    var audience = form["audience"].ToString();
    if (!experience.AvailableLanguages.Contains(language) || audience is not ("visitor" or "presenter")) return Results.BadRequest();
    http.Response.Cookies.Append(audience == "presenter" ? LanguagePreference.PresenterCookie : LanguagePreference.VisitorCookie,
        language, LanguagePreference.Options);
    return Results.NoContent();
}).RequireRateLimiting("demo");

var api = app.MapGroup("/api/webauthn").RequireRateLimiting("demo");
api.AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    var text = http.RequestServices.GetRequiredService<IStringLocalizer<UiText>>();
    if (!webAuthn.Origins.Contains(http.Request.Headers.Origin.ToString(), StringComparer.Ordinal))
        return Results.BadRequest(new { error = text["Ongeldige aanvraagorigin."].Value, errorKey = "Ongeldige aanvraagorigin." });
    try
    {
        await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
        return await next(context);
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = text["Je sessiecontrole is verlopen. Vernieuw de pagina."].Value, errorKey = "Je sessiecontrole is verlopen. Vernieuw de pagina." }); }
    catch (DemoOperationPendingException exception) { return Results.Conflict(new { error = http.RequestServices.GetRequiredService<IStringLocalizer<UiText>>()[exception.Message].Value, errorKey = exception.Message }); }
    catch (DemoRequestException exception) { return Results.BadRequest(new { error = http.RequestServices.GetRequiredService<IStringLocalizer<UiText>>()[exception.Message].Value, errorKey = exception.Message }); }
});
api.MapPost("/{operation}/options", async (string operation, HttpContext http, DemoService demos, IStringLocalizer<UiText> text) =>
{
    if (operation is not ("register" or "authenticate")) return Results.NotFound();
    var id = http.Request.Headers["X-Lumma-Operation"].ToString();
    if (!Guid.TryParseExact(id, "D", out _)) return Results.BadRequest(new { error = text["Ongeldige operatie-identificatie."].Value, errorKey = "Ongeldige operatie-identificatie." });
    return Results.Content(await demos.OptionsAsync(VisitorCookie.Read(http), operation == "register", http.RequestAborted, id), "application/json");
});
api.MapPost("/{operation}/verify", async (string operation, JsonElement response, HttpContext http, DemoService demos, PrizeRoundsService rounds, IStringLocalizer<UiText> text) =>
{
    if (operation is not ("register" or "authenticate")) return Results.NotFound();
    var id = http.Request.Headers["X-Lumma-Operation"].ToString();
    if (!Guid.TryParseExact(id, "D", out _)) return Results.BadRequest(new { error = text["Ongeldige operatie-identificatie."].Value, errorKey = "Ongeldige operatie-identificatie." });
    var receipt = await demos.VerifyAsync(VisitorCookie.Read(http), operation == "register", response, http.RequestAborted, id);
    var round=operation=="authenticate"?await rounds.JoinAuthenticatedAsync(VisitorCookie.Read(http),http.RequestAborted):null;
    var prizeStatus=operation=="authenticate"?await rounds.VisitorStatusAsync(VisitorCookie.Read(http),http.RequestAborted):null;
    return Results.Ok(new { verified = true, receipt.Operation, receipt.Username, receipt.VerifiedAt,
        receipt.CredentialCount, receipt.SuccessfulAuthentications, receipt.VerificationStatus, receipt.CredentialAlias,
        receipt.VisitorNumber, receipt.EventId, receipt.EventName,round,prizeLinkKey=prizeStatus?.LinkKey,prizesEnabled=operation=="authenticate"&&await rounds.HasPrizeDrawAsync(VisitorCookie.Read(http),http.RequestAborted) });
});
api.MapPost("/{operation}/cancel", async (string operation, HttpContext http, DemoService demos) =>
{
    if (operation is not ("register" or "authenticate")) return Results.NotFound();
    await demos.CancelAsync(VisitorCookie.Read(http), operation == "register", http.Request.Headers["X-Lumma-Operation"].ToString(), http.RequestAborted);
    return Results.Ok(new { cancelled = true });
});
app.MapPresenterEndpoints();
app.MapGet("/api/event-edition/poll/{wpfRunId}", async (string wpfRunId, HttpContext http, EventEditionService events) =>
{
    // Local-only even behind a proxy. No browser cookie is used as WPF authorization.
    if (http.Connection.RemoteIpAddress is not { } ip || !System.Net.IPAddress.IsLoopback(ip)
        || http.Request.Headers.ContainsKey("X-Forwarded-For") || http.Request.Headers.ContainsKey("X-Forwarded-Proto")) return Results.Unauthorized();
    try { return Results.Ok(await events.PollAsync(http.Request.Headers["X-Lumma-Event-Code"], wpfRunId, http.RequestAborted)); }
    catch (DemoRequestException) { return Results.Unauthorized(); }
}).RequireRateLimiting("demo");
app.MapIdentityEndpoints();
app.MapSessionDemoEndpoints();
app.MapRazorPages();
app.MapGet("/branding/logo/{slot}",async(string slot,PresentationSettings presentation,HttpContext http)=>{
    var image=await presentation.ReadLogoAsync(slot,http.RequestAborted);
    return image is null?Results.NotFound():Results.File(image,"image/png");
});
app.MapMethods("/api/local/session-status/{demoRunId}", ["GET", "HEAD"], async (string demoRunId, HttpContext http, SessionStatusService sessions,EventAccess events) =>
{
    if (!sessions.Authorize(http)) return Results.Unauthorized();
    if (HttpMethods.IsHead(http.Request.Method)) {
        if (!Guid.TryParseExact(demoRunId, "D", out _)) return Results.BadRequest();
        http.Response.Headers["X-Lumma-Monitor-Protocol"] = "1";
        return Results.StatusCode(StatusCodes.Status204NoContent);
    }
    if(!await events.VerifyMonitorAsync(demoRunId,http.Request.Headers["X-Lumma-Monitor-Grant"],http.RequestAborted))return Results.NotFound();
    var metadata = await sessions.GetAsync(demoRunId, http.RequestAborted);
    return metadata is null ? Results.NotFound() : Results.Ok(metadata);
}).RequireRateLimiting("demo");
app.Run();

public partial class Program { }
