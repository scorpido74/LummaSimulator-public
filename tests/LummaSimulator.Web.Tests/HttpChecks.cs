using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Identity;

namespace LummaSimulator.Web.Tests;

internal static class HttpChecks
{
    public static async Task RunAsync(string temporaryRoot, bool clickFixOnly = false, bool prizeConfigureDiagnostic = false, bool prizeOnly = false, bool roundVisitorBaseline = false, bool visitorDisplayChecks = false, bool sessionDemoOnly = false,bool onboardingOnly=false)
    {
        var projectRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot.FullName, "LummaSimulator.slnx"))) projectRoot = projectRoot.Parent;
        if (projectRoot is null) throw new InvalidOperationException("Run tests inside the repository.");
        var webRoot = Path.Combine(projectRoot.FullName, "LummaSimulator.Web");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var webDll = Environment.GetEnvironmentVariable("LUMMA_TEST_WEB_DLL")
            ?? Path.Combine(webRoot, "bin", configuration, "net10.0", "LummaSimulator.Web.dll");
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var origin = $"https://localhost:{port}";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificatePath = Path.Combine(temporaryRoot, "test-only.pfx");
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword));
        PresenterInvitation bootstrap;
        using (var bootstrapStore=new DemoStore(Path.Combine(temporaryRoot,"http.db"))) {
            await bootstrapStore.InitializeAsync();
            bootstrap=await PresenterChecks.Service(bootstrapStore,TimeProvider.System,origin).IssueBootstrapAsync("http-admin","HTTP Administrator",default);
        }
        var processInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = temporaryRoot };
        processInfo.ArgumentList.Add(webDll);
        processInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        processInfo.Environment["Helper__Enabled"] = "true"; // Isolated fixture only; production default stays disabled.
        var helperDownloads=Directory.CreateDirectory(Path.Combine(temporaryRoot,"helper-downloads")).FullName;
        var helperZip=Path.Combine(helperDownloads,"LummaSimulator-PresenterHelper-v0.1.0-win-x64.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(helperZip,System.IO.Compression.ZipArchiveMode.Create))
        {
            using var entry=zip.CreateEntry("Portal.cer").Open();
            entry.Write(certificate.Export(X509ContentType.Cert));
        }
        processInfo.Environment["Helper__PackageDirectory"] = helperDownloads;
        // Test child must never inherit an operator's opt-in to real Windows Event Log writes.
        processInfo.Environment["SecurityAudit__WindowsEventLog__Enabled"] = "false";
        processInfo.Environment["SecurityAudit__WindowsEventLog__BatchSize"] = "50";
        processInfo.Environment["SecurityAudit__WindowsEventLog__PollSeconds"] = "10";
        processInfo.Environment["ASPNETCORE_URLS"] = origin;
        processInfo.Environment["WebAuthn__Origins__0"] = origin;
        processInfo.Environment["Database__Path"] = Path.Combine(temporaryRoot, "http.db");
        // Only this test mode enables handler/model-state diagnostics. No request-body logging.
        if(prizeConfigureDiagnostic)
            processInfo.Environment["Logging__LogLevel__Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.PageActionInvoker"]="Information";

        var monitorKey = LummaSimulator.Web.Models.DemoIdentity.RandomToken();
        processInfo.Environment["Monitor__KeyHash"] = LummaSimulator.Web.Models.DemoIdentity.Hash(monitorKey);
        processInfo.Environment["Kestrel__Certificates__Default__Path"] = certificatePath;
        processInfo.Environment["Kestrel__Certificates__Default__Password"] = certificatePassword;
        using var server = Process.Start(processInfo) ?? throw new InvalidOperationException("Test server did not start.");
        var outputTask = server.StandardOutput.ReadToEndAsync();
        var errorTask = server.StandardError.ReadToEndAsync();
        var jars = new List<CookieContainer>();
        HttpClient Client()
        {
            var jar = new CookieContainer(); jars.Add(jar);
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false, CookieContainer = jar, UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == certificate.Thumbprint
            };
            return new(handler) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(10) };
        }
        using var visitor = Client();
        using var second = Client();
        using var presenter = Client();
        var secrets = new List<string> { monitorKey, bootstrap.Code, "HTTP Administrator", "HTTP Presenter" };
        string logs;
        try
        {
            var ready = false;
            for (var i = 0; i < 80; i++)
            {
                if (server.HasExited) throw new InvalidOperationException("Web test process exited before startup: " + await errorTask + await outputTask);
                try { using var health = await visitor.GetAsync("/"); if (health.StatusCode == HttpStatusCode.OK) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            TestProgram.Check(ready, "HTTPS Kestrel startup with isolated temporary database and pinned test certificate");
            if (clickFixOnly)
            {
                await StartDemo(visitor, "laptop");
                var clickFixDemoHtml = await visitor.GetStringAsync("/Demo");
                var clickFixCsrf = Csrf(clickFixDemoHtml);
                using var clickFixAuthenticator = new SoftwareAuthenticator();
                using var clickFixRegistrationOptions = await Api(visitor, "register/options", clickFixCsrf, new { }, origin);
                if (!clickFixRegistrationOptions.IsSuccessStatusCode) throw new InvalidOperationException("ClickFix-only setup could not request passkey registration options.");
                using var clickFixRegistration = await Api(visitor, "register/verify", clickFixCsrf,
                    clickFixAuthenticator.Register(await clickFixRegistrationOptions.Content.ReadAsStringAsync(), origin), origin);
                if (!clickFixRegistration.IsSuccessStatusCode) throw new InvalidOperationException("ClickFix-only setup could not register its synthetic passkey.");
                using var clickFixAuthenticationOptions = await Api(visitor, "authenticate/options", clickFixCsrf, new { }, origin);
                if (!clickFixAuthenticationOptions.IsSuccessStatusCode) throw new InvalidOperationException("ClickFix-only setup could not request authentication options.");
                using var clickFixAuthentication = await Api(visitor, "authenticate/verify", clickFixCsrf,
                    clickFixAuthenticator.Authenticate(await clickFixAuthenticationOptions.Content.ReadAsStringAsync(), origin), origin);
                if (!clickFixAuthentication.IsSuccessStatusCode) throw new InvalidOperationException("ClickFix-only setup could not authenticate its synthetic visitor.");
                var clickFixVisitorToken = jars[0].GetCookies(new Uri(origin))[VisitorCookie.Name]?.Value
                    ?? throw new InvalidOperationException("ClickFix-only setup did not receive a visitor cookie.");
                await ClickFixBrowserChecks.RunAsync(projectRoot.FullName, temporaryRoot, origin, clickFixVisitorToken,
                    Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())));
                TestProgram.Check(true, "ClickFix-only HTTPS browser fixture completed");
                return;
            }
            var anonymousClickFix = await visitor.GetAsync("/SignedIn");
            using(var downloadPage=await visitor.GetAsync("/downloads/presenter-helper"))
            using(var download=await visitor.GetAsync("/downloads/presenter-helper?handler=Zip"))
            {
                var bytes=await download.Content.ReadAsByteArrayAsync();
                var expectedBytes=await File.ReadAllBytesAsync(helperZip);
                var html=await downloadPage.Content.ReadAsStringAsync();
                TestProgram.Check(downloadPage.StatusCode==HttpStatusCode.OK&&download.StatusCode==HttpStatusCode.OK
                    &&bytes.SequenceEqual(expectedBytes)
                    &&html.Contains(certificate.Thumbprint)&&html.Contains(Convert.ToHexString(SHA256.HashData(bytes))),
                    "Anonymous helper page serves the advertised ZIP checksum and public certificate thumbprint");
            }
            TestProgram.Check(anonymousClickFix.StatusCode == HttpStatusCode.Redirect && anonymousClickFix.Headers.Location?.OriginalString == "/Demo", "ClickFix rejects anonymous access on the server");
            var landingResponse = await visitor.GetAsync("/");
            var landing = await landingResponse.Content.ReadAsStringAsync();
            TestProgram.Check(landing.Contains(">LummaSimulator: De kracht van passkeys</span>") && landing.Contains("Gebruik een YubiKey") && landing.Contains("Start de iPad-demo"), "Dutch visitor title and welcome choices rendered");
            TestProgram.Check(landing.Contains("<html lang=\"nl-NL\">") && landing.Contains("data-language=\"en-GB\""), "New browser defaults to Dutch with English available");
            var languageDenied = await visitor.PostAsync("/api/language", new FormUrlEncodedContent(new Dictionary<string, string> { ["language"] = "en-GB", ["audience"] = "visitor" }));
            TestProgram.Check(languageDenied.StatusCode == HttpStatusCode.BadRequest, "Language mutation requires exact origin and antiforgery");
            async Task<HttpResponseMessage> Language(string language, string audience)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/language") {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["language"] = language, ["audience"] = audience }) };
                request.Headers.Add("Origin", origin);
                request.Headers.Add("X-CSRF-TOKEN", Csrf(await visitor.GetStringAsync("/Privacy")));
                return await visitor.SendAsync(request);
            }
            TestProgram.Check((await Language("en-GB", "visitor")).StatusCode == HttpStatusCode.NoContent, "Visitor can save English without authentication changes");
            var english = await visitor.GetStringAsync("/");
            TestProgram.Check(english.Contains("<html lang=\"en-GB\">") && english.Contains(">LummaSimulator: The power of passkeys</span>"), "English visitor title uses shared offline resources");
            TestProgram.Check((await visitor.GetStringAsync("/Presenter/Setup")).Contains("<html lang=\"nl-NL\">"), "Visitor language does not leak into presenter area");
            await Language("en-GB", "presenter");
            await Language("nl-NL", "visitor");
            TestProgram.Check((await visitor.GetStringAsync("/Presenter/Setup")).Contains("<html lang=\"en-GB\">")
                && (await visitor.GetStringAsync("/")).Contains("<html lang=\"nl-NL\">"), "Both language preferences coexist independently in the same browser");
            TestProgram.Check((await Language("fr-FR", "visitor")).StatusCode == HttpStatusCode.BadRequest, "Unimplemented language is rejected");
            await Language("en-GB", "visitor");
            TestProgram.Check((await visitor.GetAsync("/images/infinigate-official.svg")).IsSuccessStatusCode
                && (await visitor.GetAsync("/images/yubico-official.svg")).IsSuccessStatusCode
                && (await visitor.GetAsync("/css/site.css")).IsSuccessStatusCode, "Local assets work from an unrelated working directory");
            TestProgram.Check(!Regex.IsMatch(landing, "<(?:script|img|link)\\b[^>]*(?:src|href)=[\"'](?:https?:)?//", RegexOptions.IgnoreCase), "Portal resources have no runtime CDN dependency");
            foreach (var logo in new[] { "infinigate-official.svg", "yubico-official.svg" })
            {
                var svg = await File.ReadAllTextAsync(Path.Combine(webRoot, "wwwroot", "images", logo));
                TestProgram.Check(!Regex.IsMatch(svg, @"<(script|foreignObject|!DOCTYPE)\b|@import|url\(\s*['"" ]*https?", RegexOptions.IgnoreCase), "Official SVG has no active/external content: " + logo);
            }
            TestProgram.Check(landingResponse.Headers.CacheControl?.NoStore == true && landingResponse.Headers.Contains("Content-Security-Policy"), "No-store and security headers");
            var setupRedirect=await visitor.GetAsync("/Presenter/Login");
            TestProgram.Check(setupRedirect.Headers.Location?.OriginalString=="/Presenter/Setup","Unactivated installation redirects login to setup");
            TestProgram.Check((await visitor.GetStringAsync("/Presenter/Setup")).Contains("data-bootstrap=\"true\""),"Local setup offers bootstrap enrollment");
            var denied = await visitor.GetAsync("/Presenter");
            TestProgram.Check(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location!.ToString().Contains("/Presenter/Setup"), "Anonymous presenter dashboard denied");
            var forgedEnd = await visitor.PostAsync("/Presenter?handler=End", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "forged" }));
            TestProgram.Check(forgedEnd.StatusCode == HttpStatusCode.Redirect, "Anonymous presenter mutation denied");
            var missingCsrf = await visitor.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string> { ["route"] = "laptop" }));
            TestProgram.Check(missingCsrf.StatusCode == HttpStatusCode.BadRequest, "Account creation requires antiforgery");
            var laptopLandingPath = await StartDemo(visitor, "laptop");
            var demoHtml = await visitor.GetStringAsync("/Demo");
            var ipadLandingPath = await StartDemo(second, "ipad");
            TestProgram.Check(laptopLandingPath == "/Demo", "Laptop landing path: expected=/Demo; actual=" + SafeDemoPath(laptopLandingPath));
            TestProgram.Check(ipadLandingPath == "/Demo", "iPad landing path: expected=/Demo; actual=" + SafeDemoPath(ipadLandingPath));
            var secondHtml = await second.GetStringAsync("/Demo");
            TestProgram.Check(demoHtml.Contains("<html lang=\"nl-NL\">"), "New visitor resets visitor language to configured default");
            var portalRunId = Regex.Match(demoHtml, "data-demo-run-id=\"([^\"]+)\"").Groups[1].Value;
            var secondRunId = Regex.Match(secondHtml, "data-demo-run-id=\"([^\"]+)\"").Groups[1].Value;
            // Names are now readable aliases, not Visitor-XXXXXXXX. Read the actual name field.
            var firstName = WebUtility.HtmlDecode(Regex.Match(demoHtml, @"<dd\b[^>]*\bid=""verified-username""[^>]*>([^<]+)</dd>").Groups[1].Value).Trim();
            var secondName = WebUtility.HtmlDecode(Regex.Match(secondHtml, @"<dd\b[^>]*\bid=""verified-username""[^>]*>([^<]+)</dd>").Groups[1].Value).Trim();
            secrets.Add(firstName);
            secrets.Add(secondName);
            TestProgram.Check(firstName.Length > 0, "Laptop demo renders a nonempty name in its name field");
            TestProgram.Check(secondName.Length > 0, "iPad demo renders a nonempty name in its name field");
            TestProgram.Check(secondHtml.Contains("IPAD-DEMONSTRATIE"), "iPad demo renders the iPad heading");
            var laptopRoute = RenderedDemoRoute(demoHtml);
            var ipadRoute = RenderedDemoRoute(secondHtml);
            TestProgram.Check(laptopRoute == "laptop", "Laptop demo route: expected=laptop; actual=" + SafeDemoRoute(laptopRoute));
            TestProgram.Check(ipadRoute == "ipad", "iPad demo route: expected=ipad; actual=" + SafeDemoRoute(ipadRoute));
            TestProgram.Check(Guid.TryParse(portalRunId, out _), "Laptop demo renders a valid account reference (value omitted)");
            TestProgram.Check(Guid.TryParse(secondRunId, out _), "iPad demo renders a valid account reference (value omitted)");
            TestProgram.Check(portalRunId != secondRunId, $"Laptop and iPad accounts: expectedEqual=False; accountsEqual={portalRunId == secondRunId}");
            TestProgram.Check(firstName != secondName, $"Laptop and iPad displayed accounts: expectedEqual=False; accountsEqual={firstName == secondName}");
            // Interleave reloads after creating both browsers to catch account replacement/cross-over.
            var laptopReload = await visitor.GetStringAsync("/Demo");
            var ipadReload = await second.GetStringAsync("/Demo");
            var laptopReloadRun = RenderedDemoRun(laptopReload);
            var ipadReloadRun = RenderedDemoRun(ipadReload);
            TestProgram.Check(laptopReloadRun == portalRunId, $"Laptop retains its own account after iPad creation: expectedEqual=True; accountsEqual={laptopReloadRun == portalRunId}");
            TestProgram.Check(ipadReloadRun == secondRunId, $"iPad retains its own account after laptop reload: expectedEqual=True; accountsEqual={ipadReloadRun == secondRunId}");
            TestProgram.Check(laptopReloadRun != ipadReloadRun, $"Reloaded laptop and iPad accounts: expectedEqual=False; accountsEqual={laptopReloadRun == ipadReloadRun}");
            TestProgram.Check(RenderedDemoRoute(laptopReload) == "laptop", "Reloaded laptop demo route: expected=laptop; actual=" + SafeDemoRoute(RenderedDemoRoute(laptopReload)));
            TestProgram.Check(RenderedDemoRoute(ipadReload) == "ipad", "Reloaded iPad demo route: expected=ipad; actual=" + SafeDemoRoute(RenderedDemoRoute(ipadReload)));
            var rawToken = jars[0].GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value;
            secrets.Add(rawToken);
            TestProgram.Check(jars[0].GetCookies(new Uri(origin))[VisitorCookie.Name] is { Secure: true, HttpOnly: true }, "Secure HttpOnly visitor cookie");
            var csrf = Csrf(demoHtml);
            var noCsrfApi = await visitor.PostAsync("/api/webauthn/register/options", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            TestProgram.Check(noCsrfApi.StatusCode == HttpStatusCode.BadRequest, "WebAuthn endpoint denies missing origin/CSRF");
            var wrongOrigin = await Api(visitor, "register/options", csrf, new { }, "https://other.invalid");
            TestProgram.Check(wrongOrigin.StatusCode == HttpStatusCode.BadRequest, "WebAuthn API origin guard");
            using var authenticator = new SoftwareAuthenticator();
            var optionsResponse = await Api(visitor, "register/options", csrf, new { }, origin);
            TestProgram.Check(optionsResponse.IsSuccessStatusCode, "HTTPS registration options");
            var originalOperation = visitor.DefaultRequestHeaders.GetValues("X-Lumma-Operation").Single();
            var duplicateOptions = await Api(visitor, "register/options", csrf, new { }, origin);
            TestProgram.Check(duplicateOptions.StatusCode == HttpStatusCode.Conflict, "Second tab receives HTTP 409 instead of replacing challenge");
            await Api(visitor, "register/cancel", csrf, new { }, origin);
            visitor.DefaultRequestHeaders.Remove("X-Lumma-Operation");
            visitor.DefaultRequestHeaders.Add("X-Lumma-Operation", originalOperation);
            var registration = authenticator.Register(await optionsResponse.Content.ReadAsStringAsync(), origin);
            var registered = await Api(visitor, "register/verify", csrf, registration, origin);
            TestProgram.Check(registered.IsSuccessStatusCode, "Complete WebAuthn registration over real HTTPS with test authenticator");
            var registeredPage = await visitor.GetStringAsync("/Demo");
            TestProgram.Check(registeredPage.Contains("Registratie voltooid") && registeredPage.Contains("Nog een passkey toevoegen"), "Completed registration survives page reload");
            TestProgram.Check((await visitor.GetAsync("/api/identity/sessions")).StatusCode==HttpStatusCode.Unauthorized,"Registration alone cannot read identity sessions");
            TestProgram.Check((await visitor.GetAsync("/Presenter/AuditExport")).StatusCode==HttpStatusCode.Redirect,"Visitor cannot read presenter export status");
            var registeredClickFix = await visitor.GetAsync("/SignedIn");
            TestProgram.Check(registeredClickFix.StatusCode == HttpStatusCode.Redirect && registeredClickFix.Headers.Location?.OriginalString == "/Demo", "ClickFix requires authentication, not just a registered passkey");
            var options = await Api(visitor, "authenticate/options", csrf, new { }, origin);
            var authenticated = await Api(visitor, "authenticate/verify", csrf, authenticator.Authenticate(await options.Content.ReadAsStringAsync(), origin), origin);
            TestProgram.Check(authenticated.IsSuccessStatusCode, "Complete signed WebAuthn authentication over HTTPS");
            var identityResponse=await visitor.GetAsync("/api/identity/sessions?limit=999999");
            using(var identityJson=JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync())) {
                TestProgram.Check(identityResponse.StatusCode==HttpStatusCode.OK && identityJson.RootElement.GetProperty("limit").GetInt32()==100
                    && identityJson.RootElement.GetProperty("items").GetArrayLength()==1,"Own identity session API caps pages and returns current session");
            }
            TestProgram.Check((await second.GetAsync("/api/identity/sessions")).StatusCode==HttpStatusCode.Unauthorized,"Another browser cannot read session information");
            TestProgram.Check((await visitor.GetAsync("/api/identity/audit?eventType=untrusted")).StatusCode==HttpStatusCode.BadRequest,"Unknown audit event filters are refused");
            using(var identityAudit=JsonDocument.Parse(await visitor.GetStringAsync("/api/identity/audit?eventType=AUTHENTICATION_SUCCEEDED&limit=1"))) {
                TestProgram.Check(identityAudit.RootElement.GetProperty("items").GetArrayLength()==1 && !identityAudit.RootElement.GetRawText().Contains(rawToken),"Read-only audit API filters events without exposing the visitor token");
            }
            using(var foreignRun=JsonDocument.Parse(await visitor.GetStringAsync("/api/identity/sessions?demoRunId="+Guid.NewGuid().ToString("D"))))
                TestProgram.Check(foreignRun.RootElement.GetProperty("items").GetArrayLength()==0,"Foreign run filter cannot expand user scope");
            var sessionPage=await visitor.GetAsync("/Sessions");
            TestProgram.Check(sessionPage.StatusCode==HttpStatusCode.OK && (await sessionPage.Content.ReadAsStringAsync()).Contains("Actieve sessies")
                && sessionPage.Headers.GetValues("Referrer-Policy").Single()=="same-origin","Own sessions page uses safe native POST origin behavior");
            using(var revokedClient=Client()) {
                await StartDemo(revokedClient,"laptop");using var revokeKey=new SoftwareAuthenticator();
                var revokeCsrf=Csrf(await revokedClient.GetStringAsync("/Demo"));
                var ro=await Api(revokedClient,"register/options",revokeCsrf,new{},origin);
                await Api(revokedClient,"register/verify",revokeCsrf,revokeKey.Register(await ro.Content.ReadAsStringAsync(),origin),origin);
                ro=await Api(revokedClient,"authenticate/options",revokeCsrf,new{},origin);
                await Api(revokedClient,"authenticate/verify",revokeCsrf,revokeKey.Authenticate(await ro.Content.ReadAsStringAsync(),origin),origin);
                using var ownSessions=JsonDocument.Parse(await revokedClient.GetStringAsync("/api/identity/sessions"));
                var ownId=ownSessions.RootElement.GetProperty("items")[0].GetProperty("id").GetString()!;
                var revokePage=await revokedClient.GetStringAsync("/Sessions");
                var missingOrigin=await revokedClient.PostAsync("/Sessions?handler=Revoke",Form(Csrf(revokePage),("id",ownId)));
                TestProgram.Check(missingOrigin.StatusCode==HttpStatusCode.BadRequest && (await revokedClient.GetAsync("/api/identity/sessions")).StatusCode==HttpStatusCode.OK,"Session revocation retains exact Origin protection without changing state");
                var foreignMutation=new HttpRequestMessage(HttpMethod.Post,"/Sessions?handler=Revoke"){Content=Form(Csrf(revokePage),("id",Guid.NewGuid().ToString("D")))};
                foreignMutation.Headers.Add("Origin",origin);
                TestProgram.Check((await revokedClient.SendAsync(foreignMutation)).StatusCode==HttpStatusCode.NotFound,"Cross-user/unknown session revocation is refused");
                var revokeRequest=new HttpRequestMessage(HttpMethod.Post,"/Sessions?handler=Revoke"){Content=Form(Csrf(revokePage),("id",ownId),("logout","true"))};
                revokeRequest.Headers.Add("Origin",origin);
                TestProgram.Check((await revokedClient.SendAsync(revokeRequest)).StatusCode==HttpStatusCode.Redirect
                    && (await revokedClient.GetAsync("/api/identity/sessions")).StatusCode==HttpStatusCode.Unauthorized
                    && (await revokedClient.GetAsync("/SignedIn")).StatusCode==HttpStatusCode.Redirect,"Real session revocation denies API and ClickFix without deleting identity");
            }
            var clickFix = await visitor.GetAsync("/SignedIn?choice=%3Cscript%3Euntrusted%3C%2Fscript%3E");
            var clickFixHtml = await clickFix.Content.ReadAsStringAsync();
            TestProgram.Check(clickFix.IsSuccessStatusCode && clickFixHtml.Contains("id=\"clickfix-exercise\"") && clickFixHtml.Contains("/js/clickfix.js")
                && !clickFixHtml.Contains("untrusted") && clickFix.Headers.CacheControl?.NoStore == true, "Authenticated ClickFix renders no-store exercise without reflecting user input");
            var otherClickFix = await second.GetAsync("/SignedIn");
            TestProgram.Check(otherClickFix.StatusCode == HttpStatusCode.Redirect, "Another browser cannot inherit ClickFix authentication");
            var reloadedExerciseHtml = await visitor.GetStringAsync("/SignedIn");
            TestProgram.Check(ExerciseHasAttribute(reloadedExerciseHtml, "clickfix-exercise", "hidden"), "Reloaded ClickFix waits for an explicit start with the exercise hidden");
            TestProgram.Check(ExerciseHasAttribute(reloadedExerciseHtml, "clickfix-run", "hidden"), "Reloaded ClickFix does not retain the Run step");
            TestProgram.Check(ExerciseHasAttribute(reloadedExerciseHtml, "clickfix-debrief", "hidden"), "Reloaded ClickFix does not retain the debrief step");
            TestProgram.Check(ExerciseHasAttribute(reloadedExerciseHtml, "clickfix-ok", "disabled"), "Reloaded ClickFix resets OK to its initial disabled state");
            TestProgram.Check(Regex.IsMatch(reloadedExerciseHtml, "<button\\b[^>]*\\bid=\"start-clickfix\"[^>]*>"), "Reloaded ClickFix renders its start action");
            TestProgram.Check(!ExerciseHasAttribute(reloadedExerciseHtml, "start-clickfix", "disabled"), "Reloaded ClickFix start action is available");
            if(!onboardingOnly)
                await ClickFixBrowserChecks.RunAsync(projectRoot.FullName, temporaryRoot, origin, rawToken,
                    Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())));
            await Language("en-GB", "visitor");
            var switchedDemo = await visitor.GetStringAsync("/Demo");
            TestProgram.Check(switchedDemo.Contains("<html lang=\"en-GB\">") && switchedDemo.Contains(portalRunId)
                && switchedDemo.Contains(">Registration complete</span>"), "Language change preserves existing demo and registered credential");
            await Language("nl-NL", "visitor");
            using (var receipt = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync()))
                TestProgram.Check(receipt.RootElement.GetProperty("username").GetString() == firstName
                    && receipt.RootElement.GetProperty("verificationStatus").GetString() == "Verified"
                    && receipt.RootElement.GetProperty("successfulAuthentications").GetInt32() == 1,
                    "HTTPS authentication response supplies authoritative confirmation data");
            await Api(visitor, "authenticate/options", csrf, new { }, origin);
            var cancelled = await Api(visitor, "authenticate/cancel", csrf, new { }, origin);
            TestProgram.Check(cancelled.IsSuccessStatusCode, "HTTPS cancellation releases current challenge");
            options = await Api(visitor, "authenticate/options", csrf, new { }, origin);
            var bad = await Api(visitor, "authenticate/verify", csrf, authenticator.Authenticate(await options.Content.ReadAsStringAsync(), origin, badSignature: true), origin);
            TestProgram.Check(bad.StatusCode == HttpStatusCode.BadRequest, "Invalid signature rejected over HTTPS");
            var visitorDashboard = await visitor.GetAsync("/Presenter");
            TestProgram.Check(visitorDashboard.StatusCode == HttpStatusCode.Redirect, "Visitor session does not grant presenter access");
            var setupHtml = await presenter.GetStringAsync("/Presenter/Setup");
            await StartDemo(presenter,"phone");
            var sharedVisitorCsrf=Csrf(await presenter.GetStringAsync("/Demo"));
            TestProgram.Check(!setupHtml.Contains("name=\"Password\""), "Presenter setup has no password fallback");
            var passwordAttempt=await presenter.PostAsync("/Presenter/Login",Form(Csrf(setupHtml),("Password","Previously-Known-Password")));
            TestProgram.Check((await presenter.GetAsync("/Presenter")).StatusCode==HttpStatusCode.Redirect,"Password POST cannot create a presenter session");
            async Task<HttpResponseMessage> PresenterApi(string path,object body,HttpClient? client=null,bool forwarded=false) {
                client??=presenter;
                if(path.EndsWith("/options")) { client.DefaultRequestHeaders.Remove("X-Lumma-Operation");client.DefaultRequestHeaders.Add("X-Lumma-Operation",Guid.NewGuid().ToString("D")); }
                using var enrollmentPage=await HttpRateLimitFixture.GetAsync(client,"/Presenter/Enroll");
                var page=enrollmentPage.StatusCode==HttpStatusCode.Redirect&&enrollmentPage.Headers.Location?.OriginalString=="/Presenter/Onboarding"
                    ?await HttpRateLimitFixture.GetStringAsync(client,"/Presenter/Onboarding"):await enrollmentPage.Content.ReadAsStringAsync();
                var payload=JsonSerializer.Serialize(body);
                if(path=="login/options"){
                    using var requestBody=JsonDocument.Parse(payload);
                    TestProgram.Check(!requestBody.RootElement.EnumerateObject().Any(p=>new[]{"login","username","account","email"}.Contains(p.Name,StringComparer.OrdinalIgnoreCase)),"HTTPS presenter login options request sends no username or account identity");
                }
                var message=new HttpRequestMessage(HttpMethod.Post,"/api/presenter/"+path){Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json")};
                message.Headers.Add("Origin",origin);
                if(forwarded) message.Headers.Add("X-Forwarded-For","203.0.113.10");
                message.Headers.Add("X-CSRF-TOKEN",Csrf(page));return await client.SendAsync(message);
            }
            var noPresenterCsrf=new HttpRequestMessage(HttpMethod.Post,"/api/presenter/login/options") {Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json")};
            noPresenterCsrf.Headers.Add("Origin",origin);
            TestProgram.Check((await presenter.SendAsync(noPresenterCsrf)).StatusCode==HttpStatusCode.BadRequest,"Discoverable presenter login rejects missing antiforgery");
            var wrongPresenterOrigin=new HttpRequestMessage(HttpMethod.Post,"/api/presenter/login/options") {Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json")};
            wrongPresenterOrigin.Headers.Add("Origin","https://other.invalid");wrongPresenterOrigin.Headers.Add("X-CSRF-TOKEN",Csrf(setupHtml));
            TestProgram.Check((await presenter.SendAsync(wrongPresenterOrigin)).StatusCode==HttpStatusCode.BadRequest,"Discoverable presenter login rejects another origin before issuing options");
            TestProgram.Check((await PresenterApi("setup/open",new{code=bootstrap.Code},forwarded:true)).StatusCode==HttpStatusCode.BadRequest,"Forwarded external bootstrap request is refused without consuming authorization");
            TestProgram.Check((await PresenterApi("setup/open",new{code=bootstrap.Code})).IsSuccessStatusCode,"One-time local administrator enrollment authorization accepted over HTTPS");
            using var adminKey=new SoftwareAuthenticator();
            var enrollOptions=await PresenterApi("enroll/options",new{useInvitation=true});
            var enrolled=await PresenterApi("enroll/verify",new{useInvitation=true,keyName="Admin primary",response=adminKey.Register(await enrollOptions.Content.ReadAsStringAsync(),origin)});
            TestProgram.Check(enrolled.IsSuccessStatusCode && (await presenter.GetAsync("/Presenter")).StatusCode==HttpStatusCode.Redirect,"Enrollment creates no authenticated session before assertion");
            TestProgram.Check((await visitor.GetAsync("/Presenter/Setup")).Headers.Location?.OriginalString=="/Presenter/Login","Setup closes immediately after activation");
            using var loginPage=await presenter.GetAsync("/Presenter/Login");
            TestProgram.Check(loginPage.StatusCode==HttpStatusCode.OK,"Ordinary presenter login is available after administrator enrollment");
            var loginHtml=await loginPage.Content.ReadAsStringAsync();
            TestProgram.Check(loginHtml.Contains("data-presenter-action=\"login\"")&&!loginHtml.Contains("data-presenter-action=\"enroll\"")&&!loginHtml.Contains("handler=Begin"),"Ordinary sign-in remains separate from first-installation account creation");
            TestProgram.Check(!Regex.IsMatch(loginHtml,@"<input\b[^>]*\bname\s*=\s*[""'](?:login|username|account|email)[""']",RegexOptions.IgnoreCase),"HTTPS presenter login page has no username or account field");
            TestProgram.Check(!Regex.IsMatch(loginHtml,@"<input\b[^>]*\b(?:name\s*=\s*[""']password[""']|type\s*=\s*[""']password[""'])",RegexOptions.IgnoreCase),"Ordinary presenter login has no password field or password fallback");
            var registrationResult=JsonDocument.Parse(await enrolled.Content.ReadAsStringAsync());
            TestProgram.Check(registrationResult.RootElement.GetProperty("redirect").GetString()=="/Presenter/Login","Registration redirects to separate authentication");
            var adminOptions=await PresenterApi("login/options",new{});
            TestProgram.Check(adminOptions.StatusCode==HttpStatusCode.OK,$"HTTPS presenter login options succeeds; status={(int)adminOptions.StatusCode}");
            var loginJson=await adminOptions.Content.ReadAsStringAsync();
            using(var parsedLogin=JsonDocument.Parse(loginJson)){
                var allowed=parsedLogin.RootElement.TryGetProperty("allowCredentials",out var list)&&list.ValueKind==JsonValueKind.Array?list.GetArrayLength():-1;
                TestProgram.Check(allowed==0,$"HTTPS presenter login uses discoverable credentials; expected allowCredentials count=0, actual={allowed}");
                var uv=parsedLogin.RootElement.TryGetProperty("userVerification",out var verification)&&verification.ValueKind==JsonValueKind.String?verification.GetString():"missing-or-invalid";
                TestProgram.Check(uv=="required",$"HTTPS presenter login requires user verification; expected=required, actual={uv}");
            }
            var signedIn=await PresenterApi("login/verify",new{response=adminKey.Authenticate(await adminOptions.Content.ReadAsStringAsync(),origin)});
            TestProgram.Check(signedIn.IsSuccessStatusCode,"Personal presenter WebAuthn sign-in over real HTTPS");
            async Task<HttpResponseMessage> PresenterForm(string path,HttpContent content,HttpClient? client=null){
                using var message=new HttpRequestMessage(HttpMethod.Post,path){Content=content};message.Headers.Add("Origin",origin);
                return await (client??presenter).SendAsync(message);
            }
            TestProgram.Check((await presenter.GetAsync("/Presenter")).Headers.Location?.OriginalString=="/Presenter/Onboarding","Fresh installation gates normal presenter functionality");
            var setupProof=await PresenterApi("reauth/options",new{});
            await PresenterApi("reauth/verify",new{response=adminKey.Authenticate(await setupProof.Content.ReadAsStringAsync(),origin)});
            var setupConfig=await presenter.GetStringAsync("/Presenter/Configuration");
            await PresenterForm("/Presenter/Configuration?handler=Save",Form(Csrf(setupConfig),("organization","Synthetic"),("displayName","LummaSimulator"),("language","nl-NL"),("eventType","experience"),("retentionDays","7"),("revision","0"),("confirmed","true")));
            async Task Wizard(string action){var html=await presenter.GetStringAsync("/Presenter/Onboarding");using var response=await PresenterForm("/Presenter/Onboarding",Form(Csrf(html),("action",action),("name","Synthetic First Event"),("certificateConfirmed","true"),("confirmed","true")));TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect,"HTTPS onboarding step: "+action);}
            var onboardingPage=await presenter.GetStringAsync("/Presenter/Onboarding");
            using(var missing=await PresenterForm("/Presenter/Onboarding",Form(Csrf(onboardingPage),("action","hosting"),("confirmed","true"))))
                TestProgram.Check(missing.StatusCode==HttpStatusCode.BadRequest&&(await missing.Content.ReadAsStringAsync()).Contains("OnboardingHostingInput"),"Onboarding renders an error when a hosting confirmation is missing");
            using(var missing=await PresenterForm("/Presenter/Onboarding",Form(Csrf(onboardingPage),("action","hosting"),("certificateConfirmed","true"))))
                TestProgram.Check(missing.StatusCode==HttpStatusCode.BadRequest&&(await missing.Content.ReadAsStringAsync()).Contains("OnboardingHostingInput"),"Onboarding requires the action confirmation as well as the certificate check");
            await Wizard("hosting");await Wizard("event");
            onboardingPage=await presenter.GetStringAsync("/Presenter/Onboarding");
            TestProgram.Check(onboardingPage.Contains("OnboardingHostingSaved")&&onboardingPage.Contains("Synthetic First Event")
                &&onboardingPage.Contains("data-onboarding-submit"),"Onboarding reload shows saved hosting and the first event by name");
            using(var eventsBefore=await presenter.GetAsync("/Presenter/Events"))
                TestProgram.Check(eventsBefore.StatusCode==HttpStatusCode.Redirect&&eventsBefore.Headers.Location?.OriginalString=="/Presenter/Onboarding","Event management remains gated until finish is saved");
            using(var missingProof=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Onboarding"){Content=Form(Csrf(onboardingPage),("action","finish"),("confirmed","true"))}){
                missingProof.Headers.Add("Origin",origin);missingProof.Headers.Add("X-Lumma-Onboarding","1");
                using var response=await presenter.SendAsync(missingProof);
                TestProgram.Check(response.StatusCode==HttpStatusCode.Conflict&&(await response.Content.ReadAsStringAsync()).Contains("PresenterReauthenticate"),"Finish requests fresh verification before saving");
            }
            if(onboardingOnly)
                await PrizeBrowserChecks.RunAsync(projectRoot.FullName,temporaryRoot,origin,jars[2].GetCookies(new Uri(origin))["__Host-Lumma.Presenter"]!.Value,
                    Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())),onboarding:true);
            setupProof=await PresenterApi("reauth/options",new{});await PresenterApi("reauth/verify",new{response=adminKey.Authenticate(await setupProof.Content.ReadAsStringAsync(),origin)});
            await Wizard("finish");
            using(var eventsAfter=await presenter.GetAsync("/Presenter/Events"))
                TestProgram.Check(eventsAfter.StatusCode==HttpStatusCode.OK,"Completed onboarding opens event management");
            if(onboardingOnly)
            {
                using var returningAdmin=Client();
                var loginCsrf=Csrf(await returningAdmin.GetStringAsync("/Presenter/Login"));
                var loginOperation=Guid.NewGuid().ToString("D");
                async Task<HttpResponseMessage> ResumeLogin(string step,object body)
                {
                    using var request=new HttpRequestMessage(HttpMethod.Post,"/api/presenter/login/"+step){
                        Content=System.Net.Http.Json.JsonContent.Create(body)};
                    request.Headers.Add("Origin",origin);
                    request.Headers.Add("X-CSRF-TOKEN",loginCsrf);
                    request.Headers.Add("X-Lumma-Operation",loginOperation);
                    return await returningAdmin.SendAsync(request);
                }
                using var resumeOptions=await ResumeLogin("options",new{});
                resumeOptions.EnsureSuccessStatusCode();
                using var resumeVerified=await ResumeLogin("verify",new{response=adminKey.Authenticate(await resumeOptions.Content.ReadAsStringAsync(),origin)});
                using var wizardAfterLogin=await returningAdmin.GetAsync("/Presenter/Onboarding");
                using var eventManagement=await returningAdmin.GetAsync("/Presenter/Events");
                TestProgram.Check(resumeVerified.IsSuccessStatusCode&&wizardAfterLogin.Headers.Location?.OriginalString=="/Presenter/Events"
                    &&eventManagement.StatusCode==HttpStatusCode.OK,"A new administrator sign-in retains completed onboarding and opens event management");
                using var newVisitor=Client();
                using var firstDefault=await newVisitor.GetAsync("/Index");
                var defaultLink=firstDefault.Headers.Location!;
                TestProgram.Check(firstDefault.StatusCode==HttpStatusCode.Redirect&&firstDefault.Headers.CacheControl?.NoStore==true
                    &&defaultLink.Query.Contains("eventKind=experience"),"Unscoped first visit uses the existing Experience default without caching");
                var firstIndex=await newVisitor.GetStringAsync(defaultLink);
                using(var start=await PresenterForm("/Index",Form(Csrf(firstIndex),("route","laptop"),("eventKind","experience"),
                    ("eventId",Regex.Match(defaultLink.Query,@"eventId=([0-9a-f-]{36})").Groups[1].Value)),newVisitor))
                    TestProgram.Check(start.StatusCode==HttpStatusCode.Redirect,"Visitor starts a session in the original default");
                var eventsHtml=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Events");
                var eventCsrf=Csrf(eventsHtml);
                using var newEvent=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Events",Form(eventCsrf,
                    ("operation","create"),("kind","experience"),("name","Synthetic replacement"),("confirmed","true"))));
                var replacement=Regex.Match(newEvent.Headers.Location?.OriginalString??"",@"[?&]id=([0-9a-f-]{36})").Groups[1].Value;
                TestProgram.Check(Guid.TryParse(replacement,out _),"Replacement Experience can be selected in management");
                await HttpRateLimitFixture.GetStringAsync(presenter,$"/Presenter/Events?kind=experience&id={replacement}");
                Task<HttpResponseMessage> SaveDefault(bool checkedBox,bool interactive=true,string? selection=null)=>HttpRateLimitFixture.SendAsync(async()=>{
                    using var request=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Events?handler=VisitorDefault"){
                        Content=Form(eventCsrf,("selection",selection??"experience:"+replacement),("useAsDefault",checkedBox?"true":"false"))};
                    request.Headers.Add("Origin",origin);
                    if(interactive)request.Headers.Add("X-Lumma-Visitor-Default","1");
                    return await presenter.SendAsync(request);
                });
                using var uncheckedSave=await SaveDefault(false);
                using var clean=Client();
                using(var stillOriginal=await clean.GetAsync("/"))
                    TestProgram.Check(uncheckedSave.StatusCode==HttpStatusCode.BadRequest&&stillOriginal.Headers.Location==defaultLink,
                        "Browsing management and submitting an unchecked box preserve the original default");
                using(var native=await SaveDefault(true,false)){
                    var body=await native.Content.ReadAsStringAsync();
                    TestProgram.Check(native.StatusCode==HttpStatusCode.Conflict&&body.Contains("PresenterReauthenticate")
                        &&Regex.IsMatch(body,$"""value="experience:{replacement}"[^>]*selected""")
                        &&Regex.IsMatch(body,"""name="useAsDefault"[^>]*checked"""),"Visitor default preserves the submitted choice and checkbox on native verification failure");
                }
                using var missingFresh=await SaveDefault(true);
                using(var stillOriginal=await clean.GetAsync("/"))
                    TestProgram.Check(missingFresh.StatusCode==HttpStatusCode.Conflict&&stillOriginal.Headers.Location==defaultLink
                        &&(await missingFresh.Content.ReadAsStringAsync()).Contains("PresenterReauthenticate"),
                        "A checked request without fresh verification is not saved");
                var proof=await PresenterApi("reauth/options",new{});
                await PresenterApi("reauth/verify",new{response=adminKey.Authenticate(await proof.Content.ReadAsStringAsync(),origin)});
                using var saved=await SaveDefault(true);
                using var savedResult=JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
                using(var changed=await clean.GetAsync("/"))
                    TestProgram.Check(saved.StatusCode==HttpStatusCode.OK
                        &&savedResult.RootElement.GetProperty("redirectUrl").GetString()=="/Presenter/Events#visitor-default"
                        &&changed.Headers.Location?.Query==$"?eventKind=experience&eventId={replacement}",
                        "Explicit verified save changes the homepage event");
                using var duplicate=await SaveDefault(true);
                var savedPage=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Events");
                using(var verificationStore=new DemoStore(Path.Combine(temporaryRoot,"http.db")))
                    TestProgram.Check(duplicate.StatusCode==HttpStatusCode.Conflict&&savedPage.Contains("VisitorDefaultSaved")
                        &&Convert.ToInt64(await verificationStore.ScalarAsync("SELECT COUNT(*) FROM PresenterAudit WHERE Action='DefaultVisitorEventChanged'",default))==1,
                        "Visitor default reports success and repeat requests do not write another audit event");
                using(var direct=await clean.GetAsync(defaultLink))
                    TestProgram.Check(direct.StatusCode==HttpStatusCode.OK,"Explicit visitor link remains valid after changing the default");
                using(var existing=await newVisitor.GetAsync("/"))
                    TestProgram.Check(existing.StatusCode==HttpStatusCode.OK&&(await newVisitor.GetStringAsync("/Demo")).Contains("Synthetic First Event"),
                        "An existing visitor session keeps the original event");
                using var closedDefault=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Events",Form(eventCsrf,
                    ("operation","close"),("kind","experience"),("id",replacement),("confirmed","true"))));
                using(var unavailable=await clean.GetAsync("/"))
                    TestProgram.Check(closedDefault.StatusCode==HttpStatusCode.Redirect&&unavailable.StatusCode==HttpStatusCode.OK
                        &&(await unavailable.Content.ReadAsStringAsync()).Contains("VisitorDefaultUnavailable"),
                        "Closed saved standard explains unavailability without redirecting to the still active Experience");
                var prizePage=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Prizes/New");
                var operation=WebUtility.HtmlDecode(Regex.Match(prizePage,"name=\"OperationKey\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
                using var createdPrize=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Prizes/New",Form(Csrf(prizePage),
                    ("OperationKey",operation),("ProfileId","builtin-cybertruck"),("Name","Synthetic default Prize"),
                    ("start","true"),("Input.GroupSize","2"),("Input.PrizeEnabled","true"),("Input.Stock","1"),("Input.WinnersPerGroup","1"))));
                var prizeDetail=createdPrize.Headers.Location?.OriginalString??"";
                var prizeId=prizeDetail.Split('/').Last();
                TestProgram.Check(createdPrize.StatusCode==HttpStatusCode.Redirect&&Guid.TryParse(prizeId,out _),"Create a Prize event for the unscoped visitor route");
                var prizeSettings=await HttpRateLimitFixture.GetStringAsync(presenter,prizeDetail);
                using var methods=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Events?handler=LoginRoutes",Form(Csrf(prizeSettings),
                    ("kind","prize"),("id",prizeId),("confirmed","true"),("prizeDetails","true"),("phone","true"))));
                TestProgram.Check(methods.Headers.Location?.OriginalString==prizeDetail,"Save a single Prize sign-in method from its detail page");
                using var prizeProof=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/options",new{}));
                prizeProof.EnsureSuccessStatusCode();
                var prizeAssertion=adminKey.Authenticate(await prizeProof.Content.ReadAsStringAsync(),origin);
                using var prizeProofResult=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/verify",new{response=prizeAssertion}));
                prizeProofResult.EnsureSuccessStatusCode();
                using var prizeDefault=await SaveDefault(true,selection:"prize:"+prizeId);
                using var prizeLanding=await clean.GetAsync("/");
                var target=prizeLanding.Headers.Location;
                var explicitLanding=await clean.GetStringAsync(PrizeRoundsService.FixedJoinPath(prizeId));
                TestProgram.Check(prizeDefault.StatusCode==HttpStatusCode.OK&&prizeLanding.StatusCode==HttpStatusCode.Redirect
                    &&target?.Query==$"?eventKind=prize&eventId={prizeId}"
                    &&explicitLanding.Contains("data-method=\"phone\"")&&Regex.Matches(explicitLanding,"class=\"choice-card").Count==1
                    &&Regex.Matches(await clean.GetStringAsync(defaultLink),"class=\"choice-card").Count==4,
                    "Default and explicit Prize links share the saved sign-in method while Experience retains its choices");
                return;
            }
            var exportPage=await presenter.GetAsync("/Presenter/AuditExport");
            TestProgram.Check(exportPage.StatusCode==HttpStatusCode.OK && (await exportPage.Content.ReadAsStringAsync()).Contains("WindowsEventLog"),"Authenticated presenter sees export status through existing policy");
            TestProgram.Check(JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync()).RootElement.GetProperty("redirect").GetString()=="/Presenter/Events","Setup sign-in redirects to scoped dashboard");
            TestProgram.Check((await presenter.GetAsync("/api/identity/audit")).StatusCode==HttpStatusCode.Unauthorized,"Administrator authentication grants no visitor audit access");
            using var exhibitionVisitor=Client();
            var exhibitionJar=jars[^1];
            using(var firstEventRedirect=await exhibitionVisitor.GetAsync("/"))
            {
                var location=firstEventRedirect.Headers.Location;
                var firstEventId=location is {IsAbsoluteUri:true}?Regex.Match(location.Query,@"[?&]eventId=([0-9a-f-]{36})").Groups[1].Value:"";
                TestProgram.Check(firstEventRedirect.StatusCode==HttpStatusCode.Redirect&&location?.Query.Contains("eventKind=experience")==true
                    &&Guid.TryParse(firstEventId,out _),"Onboarding's single active Experience is selected on the ordinary start page");
                var management=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Events");
                using var closed=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Events",Form(Csrf(management),
                    ("operation","close"),("kind","experience"),("id",firstEventId),("confirmed","true"))));
                TestProgram.Check(closed.StatusCode==HttpStatusCode.Redirect,"Fixture closes the onboarding event before the exhibition selection matrix");
            }
            secrets.Add(await EventEditionHttpChecks.RunAsync(exhibitionVisitor, presenter, origin,
                () => ClickFixBrowserChecks.RunAsync(projectRoot.FullName, temporaryRoot, origin, exhibitionJar.GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value,
                    Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())), paired: true)));
            TestProgram.Check(jars[2].GetCookies(new Uri(origin))["__Host-Lumma.Presenter"] is {Secure:true,HttpOnly:true},"Presenter authentication uses a separate Secure HttpOnly cookie");
            TestProgram.Check((await Api(presenter,"register/options",sharedVisitorCsrf,new{},origin)).IsSuccessStatusCode,
                "Visitor page opened before presenter login keeps valid antiforgery and session state in the same browser");
            await Api(presenter,"register/cancel",sharedVisitorCsrf,new{},origin);
            TestProgram.Check((await presenter.GetAsync("/Presenter")).StatusCode==HttpStatusCode.OK,
                "Visitor requests do not clear the presenter's independent authentication cookie");
            var wrongAudience=new HttpRequestMessage(HttpMethod.Post,"/api/presenter/login/options") {Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json")};
            wrongAudience.Headers.Add("Origin",origin);wrongAudience.Headers.Add("X-CSRF-TOKEN",sharedVisitorCsrf);
            TestProgram.Check((await presenter.SendAsync(wrongAudience)).StatusCode==HttpStatusCode.BadRequest,"Visitor antiforgery token cannot authorize authenticated presenter requests in the same browser");
            var dashboard = await presenter.GetStringAsync("/Presenter");
            TestProgram.Check(dashboard.Contains(firstName) && dashboard.Contains("Geregistreerde credentials") && !dashboard.Contains("publicKey") && !dashboard.Contains(rawToken), "Authenticated dashboard, no keys or tokens");
            var languageRequest=new HttpRequestMessage(HttpMethod.Post,"/api/language") {
                Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"language","en-GB"},{"audience","presenter"}}) };
            languageRequest.Headers.Add("Origin",origin);languageRequest.Headers.Add("X-CSRF-TOKEN",Csrf(dashboard));
            TestProgram.Check((await presenter.SendAsync(languageRequest)).StatusCode==HttpStatusCode.NoContent,
                "Authenticated presenter can switch its independent language with matching antiforgery identity");
            var englishDashboard=await presenter.GetStringAsync("/Presenter");
            TestProgram.Check(englishDashboard.Contains("class=\"presenter-sidebar\"") && englishDashboard.Contains("id=\"presenter-navigation\""),"Authenticated presenter receives separate navigation shell");
            TestProgram.Check(!(await visitor.GetStringAsync("/")).Contains("class=\"presenter-sidebar\""),"Visitor portal retains its own independent layout");
            TestProgram.Check((await visitor.GetAsync("/Presenter/Configuration")).StatusCode==HttpStatusCode.Redirect,"Visitor cannot access configuration inventory");
            var configurationPage=await presenter.GetStringAsync("/Presenter/Configuration");
            TestProgram.Check(configurationPage.Contains("WebAuthn RP ID") && !configurationPage.Contains(monitorKey),"Administrator configuration exposes safe hosting metadata, never monitor secret");
            TestProgram.Check(configurationPage.Contains("data-helper-download=\""+origin+"/downloads/presenter-helper?handler=Zip\"")
                &&configurationPage.Contains("placeholder=\"https://portal.example.test\"")
                &&configurationPage.Contains("placeholder=\"192.0.2.0/24\""),"Administrator configuration renders the canonical helper link and network field examples");
            using(var invalidPlan = new HttpRequestMessage(HttpMethod.Post,"/Presenter/Configuration?handler=Network") {
                Content=Form(Csrf(configurationPage),("revision","0"),("confirmed","true"),("portalOrigin","http://wrong.example"),
                    ("appOrigin","https://lumma-app.intern.example"),("portalAddress","192.0.2.153"),("appAddress","192.0.2.154"),
                    ("demoSubnet","192.168.1.8/24"),("helperSubnet","192.0.2.0/24"),("profile","Private")) })
            {
                invalidPlan.Headers.Add("Origin",origin);
                using var response=await presenter.SendAsync(invalidPlan);
                var errorPage=await response.Content.ReadAsStringAsync();
                TestProgram.Check(response.StatusCode==HttpStatusCode.BadRequest&&errorPage.Contains("field-validation-error")
                    &&errorPage.Contains("http://wrong.example")&&errorPage.Contains("192.168.1.8/24"),"Network plan returns field validation and preserves rejected values");
            }
            var ownSecurity=await presenter.GetStringAsync("/Presenter/Security");
            using(var rejectedOwnKey=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Security?handler=Remove") {
                Content=Form(Csrf(ownSecurity),("key",Guid.NewGuid().ToString("D")),("confirmed","true")) }) {
                rejectedOwnKey.Headers.Add("Origin","null");
                TestProgram.Check((await presenter.SendAsync(rejectedOwnKey)).StatusCode==HttpStatusCode.BadRequest,"Own-key action rejects opaque Origin before mutation");
            }
            using(var noOwnCsrf=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Security?handler=Remove") {
                Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"key",Guid.NewGuid().ToString("D")},{"confirmed","true"}}) }) {
                noOwnCsrf.Headers.Add("Origin",origin);
                TestProgram.Check((await presenter.SendAsync(noOwnCsrf)).StatusCode==HttpStatusCode.BadRequest,"Own-key action retains Razor antiforgery validation");
            }
            var monitorPage = await presenter.GetStringAsync("/Presenter/Integrations/Monitor");
            TestProgram.Check(monitorPage.Contains("name=\"revision\"") && !monitorPage.Contains(monitorKey), "Administrator monitor page never displays existing key");
            TestProgram.Check((await visitor.GetAsync("/Presenter/Integrations/Monitor")).StatusCode == HttpStatusCode.Redirect, "Visitor cannot enter monitor management");
            using (var wrongMonitorOrigin = new HttpRequestMessage(HttpMethod.Post, "/Presenter/Integrations/Monitor?handler=Change") {
                Content = Form(Csrf(monitorPage), ("operation", "revoke"), ("revision", "0"), ("confirmed", "true")) }) {
                wrongMonitorOrigin.Headers.Add("Origin", "https://other.invalid");
                TestProgram.Check((await presenter.SendAsync(wrongMonitorOrigin)).StatusCode == HttpStatusCode.BadRequest, "Monitor management rejects foreign origin with valid antiforgery token");
            }
            TestProgram.Check((await presenter.PostAsync("/Presenter/Integrations/Monitor?handler=Change", new FormUrlEncodedContent(new Dictionary<string,string>{{"operation","revoke"},{"confirmed","true"},{"revision","0"}}))).StatusCode == HttpStatusCode.BadRequest, "Monitor management rejects missing antiforgery token");
            using (var head = new HttpRequestMessage(HttpMethod.Head, "/api/local/session-status/00000000-0000-0000-0000-000000000000")) {
                head.Headers.Add("X-Lumma-Monitor-Key", monitorKey);
                var confirmation = await visitor.SendAsync(head);
                TestProgram.Check(confirmation.StatusCode == HttpStatusCode.NoContent && confirmation.Headers.Contains("X-Lumma-Monitor-Protocol"), "Authenticated HEAD verifies monitor key without a visitor grant or metadata");
            }
            TestProgram.Check(englishDashboard.Contains("<html lang=\"en-GB\">") && englishDashboard.Contains(">Total successful demo accounts</span>")
                && englishDashboard.Contains(">Expired demos since startup</span>"),"New and retained dashboard labels use the same English resource catalog");
            TestProgram.Check((await presenter.GetStringAsync("/Demo")).Contains("<html lang=\"nl-NL\">"),
                "Presenter preference does not leak into visitor pages in the same authenticated browser");
            var id = WebUtility.HtmlDecode(Regex.Match(dashboard, "<tr><td><strong>"+Regex.Escape(firstName)+"</strong>.*?name=\"id\" value=\"([^\"]+)\"",RegexOptions.Singleline).Groups[1].Value);
            TestProgram.Check(!string.IsNullOrEmpty(id), "Presenter end action identifies an active demonstration");
            string? monitorGrantCode=null;
            async Task<HttpResponseMessage> Monitor(string run, string? key)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/local/session-status/" + run);
                if (key is not null) request.Headers.Add("X-Lumma-Monitor-Key", key);
                if(monitorGrantCode is not null)request.Headers.Add("X-Lumma-Monitor-Grant",monitorGrantCode);
                return await visitor.SendAsync(request);
            }
            TestProgram.Check((await Monitor(portalRunId, null)).StatusCode == HttpStatusCode.Unauthorized, "Visitor cookie cannot authorize metadata API");
            TestProgram.Check((await Monitor(portalRunId, monitorKey)).StatusCode == HttpStatusCode.NotFound, "Integration key alone cannot bypass per-demo presenter approval");
            var unauthorizedGrant = await visitor.PostAsync("/Presenter?handler=Monitor", Form(csrf, ("id", id), ("enabled", "true")));
            TestProgram.Check(unauthorizedGrant.StatusCode == HttpStatusCode.Redirect, "Visitors cannot grant monitor access");
            await MonitorGrantHttpChecks.AllowAsync(presenter, id, origin, async () => {
                using var response = await Monitor(portalRunId, monitorKey); return response.StatusCode;
            },code=>{monitorGrantCode=code;secrets.Add(code);});
            var metadataResponse = await Monitor(portalRunId, monitorKey);
            TestProgram.Check(metadataResponse.IsSuccessStatusCode && metadataResponse.Headers.CacheControl?.NoStore == true, "Approved local API returns no-store session metadata");
            var metadataJson = await metadataResponse.Content.ReadAsStringAsync();
            using (var metadata = JsonDocument.Parse(metadataJson))
                TestProgram.Check(metadata.RootElement.GetProperty("state").GetString() == "ActiveWebAuthnSession"
                    && metadata.RootElement.GetProperty("sessionActive").GetBoolean()
                    && !metadata.RootElement.GetProperty("sessionTokenExposed").GetBoolean(), "API confirms actual server-side WebAuthn state, never theft");
            TestProgram.Check(!metadataJson.Contains(rawToken) && !metadataJson.Contains(firstName) && !metadataJson.Contains("credentialId"), "Metadata API exposes no visitor identity, cookie or credential ID");
            TestProgram.Check((await Monitor(secondRunId, monitorKey)).StatusCode == HttpStatusCode.NotFound
                && (await Monitor(portalRunId, "wrong-key")).StatusCode == HttpStatusCode.Unauthorized, "Other demos and invalid integration keys denied");
            await MonitorGrantHttpChecks.RevokeAsync(presenter, id, origin);
            TestProgram.Check((await Monitor(portalRunId, monitorKey)).StatusCode == HttpStatusCode.NotFound, "Presenter revocation immediately closes metadata access");
            var ended = await PresenterForm("/Presenter?handler=End", Form(Csrf(dashboard), ("id", id)));
            TestProgram.Check(ended.StatusCode == HttpStatusCode.Redirect && (await visitor.GetAsync("/Demo")).StatusCode == HttpStatusCode.Redirect, "Authorized presenter end revokes visitor session");
            TestProgram.Check((await visitor.GetAsync("/SignedIn")).StatusCode == HttpStatusCode.Redirect, "Revoked visitor cannot reopen ClickFix");
            TestProgram.Check((await Monitor(portalRunId, monitorKey)).StatusCode == HttpStatusCode.NotFound, "Ended demo cannot be queried through metadata API");
            var stillActive = await second.GetStringAsync("/Demo");
            TestProgram.Check(stillActive.Contains("IPAD-DEMONSTRATIE"), "Presenter ends only selected account");
            var ownEnd = await second.PostAsync("/Demo?handler=End", Form(Csrf(stillActive)));
            TestProgram.Check(ownEnd.StatusCode == HttpStatusCode.Redirect, "Visitor can end own session");
            var thanks = await second.GetStringAsync("/Complete");
            TestProgram.Check(thanks.Contains("bijbehorende registratie zijn verwijderd") && thanks.Contains("passkey kan nog"), "Verified deletion farewell rendered");
            TestProgram.Check((await visitor.GetAsync("/Complete")).StatusCode == HttpStatusCode.Redirect, "Farewell does not make unsupported deletion claims");
            var reauthOptions=await PresenterApi("reauth/options",new{});
            TestProgram.Check((await PresenterApi("reauth/verify",new{response=adminKey.Authenticate(await reauthOptions.Content.ReadAsStringAsync(),origin)})).IsSuccessStatusCode,
                "Administrator security changes require fresh WebAuthn over HTTPS");
            var accountsPage=await presenter.GetStringAsync("/Presenter/Accounts");
            var createdPerson=await PresenterForm("/Presenter/Accounts?handler=Create",Form(Csrf(accountsPage),("login","http-presenter"),("name","HTTP Presenter"),("role","Presenter")));
            var personHtml=await createdPerson.Content.ReadAsStringAsync();
            var code=Regex.Match(personHtml,"<code>([A-Za-z0-9_-]{43})</code>").Groups[1].Value;
            TestProgram.Check(code.Length==43,"Administrator creates a personal account and one-time enrollment invitation");secrets.Add(code);
            using var colleague=Client();
            await PresenterApi("enrollment/open",new{code},colleague);
            using var colleagueKey=new SoftwareAuthenticator();
            var colleagueOptions=await PresenterApi("enroll/options",new{useInvitation=true},colleague);
            TestProgram.Check((await PresenterApi("enroll/verify",new{useInvitation=true,keyName="Colleague",response=colleagueKey.Register(await colleagueOptions.Content.ReadAsStringAsync(),origin)},colleague)).IsSuccessStatusCode,
                "Invited presenter enrolls an independent credential over HTTPS");
            var colleagueLogin=await PresenterApi("login/options",new{},colleague);
            TestProgram.Check((await PresenterApi("login/verify",new{response=colleagueKey.Authenticate(await colleagueLogin.Content.ReadAsStringAsync(),origin)},colleague)).IsSuccessStatusCode,
                "Second presenter signs in with an independent session");
            async Task<(HttpClient Client,SoftwareAuthenticator Key)> CreatePresenterAsync(string login,string name,string keyName)
            {
                using var proofOptions=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/options",new{}));
                var proofJson=await proofOptions.Content.ReadAsStringAsync();var adminProof=adminKey.Authenticate(proofJson,origin);
                using var proof=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/verify",new{response=adminProof}));
                TestProgram.Check(proof.IsSuccessStatusCode,"Administrator reauthenticates before creating presenter: "+login);
                var accountPage=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Accounts");
                using var created=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter/Accounts?handler=Create",Form(Csrf(accountPage),("login",login),("name",name),("role","Presenter"))));
                var code=Regex.Match(await created.Content.ReadAsStringAsync(),"<code>([A-Za-z0-9_-]{43})</code>").Groups[1].Value;
                TestProgram.Check(code.Length==43,"Administrator creates presenter for authorization matrix: "+login);secrets.Add(code);
                var client=Client();
                using var enrollment=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("enrollment/open",new{code},client));
                TestProgram.Check(enrollment.IsSuccessStatusCode,"Synthetic presenter enrollment opens: "+login);
                var key=new SoftwareAuthenticator();
                using var options=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("enroll/options",new{useInvitation=true},client));
                var enrollmentJson=await options.Content.ReadAsStringAsync();var registration=key.Register(enrollmentJson,origin);
                using var registered=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("enroll/verify",new{useInvitation=true,keyName,response=registration},client));
                TestProgram.Check(registered.IsSuccessStatusCode,"Synthetic presenter enrolls: "+login);
                using var loginOptions=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("login/options",new{},client));
                var loginJson=await loginOptions.Content.ReadAsStringAsync();var assertion=key.Authenticate(loginJson,origin);
                using var signedIn=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("login/verify",new{response=assertion},client));
                TestProgram.Check(signedIn.IsSuccessStatusCode,"Synthetic presenter authenticates: "+login);
                return(client,key);
            }
            if(roundVisitorBaseline||visitorDisplayChecks) {
                using var qrVisitor=Client();
                await RoundVisitorBaseline.RunAsync(presenter,qrVisitor,origin,temporaryRoot,projectRoot.FullName,visitorDisplayChecks);
                if(visitorDisplayChecks) {
                    using var openVisitor=Client();
                    await RoundVisitorBaseline.RunAsync(presenter,openVisitor,origin,temporaryRoot,projectRoot.FullName,true,true);
                }
                return;
            }
            var ownerAccount=await CreatePresenterAsync("http-event-owner","HTTP Event Owner","Owner key");
            using var eventOwner=ownerAccount.Client;using var eventOwnerKey=ownerAccount.Key;
            var outsiderAccount=await CreatePresenterAsync("http-event-outsider","HTTP Event Outsider","Outsider key");
            using var eventOutsider=outsiderAccount.Client;using var eventOutsiderKey=outsiderAccount.Key;
            if(!prizeConfigureDiagnostic&&!prizeOnly)
                await SessionDemoHttpChecks.RunAsync(eventOwner,colleague,eventOutsider,Client,origin,value=>secrets.Add(value));
            if(sessionDemoOnly)return;
            if(!prizeConfigureDiagnostic&&!prizeOnly)await Phase65eHttpChecks.RunAsync(presenter,colleague,origin,monitorKey,()=>{var client=Client();return(client,jars[^1]);},
                (path,body,client)=>HttpRateLimitFixture.SendAsync(()=>PresenterApi(path,body,client)),async()=>{
                    using var p1ReauthOptions=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/options",new{}));
                    var proof=adminKey.Authenticate(await p1ReauthOptions.Content.ReadAsStringAsync(),origin);
                    using var verified=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/verify",new{response=proof}));
                    TestProgram.Check(verified.IsSuccessStatusCode,"6.5e HTTPS administrator reauthenticates for ownership change");
                },async()=>{
                    using var p1ColleagueOptions=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/options",new{},colleague));
                    var p1ColleagueProof=colleagueKey.Authenticate(await p1ColleagueOptions.Content.ReadAsStringAsync(),origin);
                    using var p1ColleagueVerified=await HttpRateLimitFixture.SendAsync(()=>PresenterApi("reauth/verify",new{response=p1ColleagueProof},colleague));
                    TestProgram.Check(p1ColleagueVerified.IsSuccessStatusCode,"6.5e HTTPS recipient reauthenticates with own passkey");
                },value=>secrets.Add(value));
            await PrizeEventHttpChecks.RunAsync(visitor,presenter,eventOwner,colleague,eventOutsider,origin,temporaryRoot,prizeConfigureDiagnostic);
            if(prizeConfigureDiagnostic||prizeOnly)return;
            await PrizeBrowserChecks.RunAsync(projectRoot.FullName,temporaryRoot,origin,
                jars[2].GetCookies(new Uri(origin))["__Host-Lumma.Presenter"]!.Value,
                Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())));
            foreach(var adminPath in new[]{"/Presenter/Accounts","/Presenter/Audit","/Presenter/Integrations/Monitor","/Presenter/Configuration"})
                TestProgram.Check((await HttpRateLimitFixture.GetAsync(colleague,adminPath)).StatusCode!=HttpStatusCode.OK,"Presenter role cannot access administrator page: "+adminPath);
            var colleagueDashboard=await HttpRateLimitFixture.GetStringAsync(colleague,"/Presenter/Events");
            var expectedEvent=WebUtility.HtmlDecode(Regex.Match(colleagueDashboard,"name=\"expected\" value=\"([^\"]*)\"").Groups[1].Value);
            var changedEvent=await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter?handler=Event",Form(Csrf(colleagueDashboard),("expected",expectedEvent),("name","HTTP Presenter Event"),("confirmed","true")),colleague));
            TestProgram.Check(changedEvent.StatusCode==HttpStatusCode.Forbidden,"Legacy global event switch cannot bypass scoped authorization");
            TestProgram.Check((await HttpRateLimitFixture.GetAsync(presenter,"/Presenter/Audit")).StatusCode==HttpStatusCode.OK,"Administrator can inspect append-only audit records");
            await MonitorManagementHttpChecks.RunAsync(presenter, visitor, origin, async () => {
                var monitorReauth = await HttpRateLimitFixture.SendAsync(() => PresenterApi("reauth/options", new { }));
                var proof = adminKey.Authenticate(await monitorReauth.Content.ReadAsStringAsync(), origin);
                TestProgram.Check((await HttpRateLimitFixture.SendAsync(() => PresenterApi("reauth/verify", new { response = proof }))).IsSuccessStatusCode,
                    "Monitor management reauthenticates through existing WebAuthn API");
            }, secret => secrets.Add(secret));
            var logoutCsrf=Csrf(await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter"));
            var logout = await HttpRateLimitFixture.SendAsync(()=>PresenterForm("/Presenter?handler=Logout", Form(logoutCsrf)));
            TestProgram.Check(logout.StatusCode == HttpStatusCode.Redirect && (await HttpRateLimitFixture.GetAsync(presenter,"/Presenter")).StatusCode == HttpStatusCode.Redirect, "Presenter logout revokes dashboard access");
            TestProgram.Check((await HttpRateLimitFixture.GetAsync(colleague,"/Presenter/Events")).StatusCode==HttpStatusCode.OK,"Signing out one presenter does not affect another presenter's session");
            foreach (var path in new[] { "/api/events", "/api/experience/statistics", "/api/prizes" })
                TestProgram.Check((await visitor.GetAsync(path)).StatusCode == HttpStatusCode.NotFound,
                    "No public event/statistics/prize management endpoint: " + path);
            foreach (var language in new[] { "nl-NL", "en-GB" })
            {
                using var preference = await Language(language, "visitor");
                TestProgram.Check(preference.StatusCode == HttpStatusCode.NoContent, "HTTPS welcome language can be set to " + language);
                using var welcomeResponse=await visitor.GetAsync("/");
                var welcome=welcomeResponse.StatusCode==HttpStatusCode.Redirect&&welcomeResponse.Headers.Location is {} visitorLink
                    ?await visitor.GetStringAsync(visitorLink):await welcomeResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync();
                var methods = Regex.Matches(welcome, "data-method=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
                TestProgram.Check(welcome.Contains($"<html lang=\"{language}\">")
                    && methods.SequenceEqual(new[] { "yubikey", "windows-hello", "phone", "ipad" })
                    && welcome.Contains("windows-mark") && welcome.Contains("choice-card featured\" data-method=\"yubikey"),
                    "Localized cards retain YubiKey-first DOM order, emphasis, local Windows icon and iPad option: " + language);
            }
            foreach (var route in new[] { "yubikey", "laptop", "phone" })
            {
                var jarIndex = jars.Count; using var routeVisitor = Client();
                await StartDemo(routeVisitor, route);
                var routePage = await routeVisitor.GetStringAsync("/Demo");
                var oldToken = jars[jarIndex].GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value;
                secrets.Add(oldToken);
                var routeCsrf = Csrf(routePage);
                using var routeAuth = new SoftwareAuthenticator();
                var routeOptions = await Api(routeVisitor, "register/options", routeCsrf, new { }, origin);
                var optionJson = await routeOptions.Content.ReadAsStringAsync();
                using (var parsed = JsonDocument.Parse(optionJson))
                    TestProgram.Check(!parsed.RootElement.GetProperty("authenticatorSelection").TryGetProperty("authenticatorAttachment", out var attachment)
                        || attachment.ValueKind == JsonValueKind.Null, "Route does not force authenticator attachment: " + route);
                TestProgram.Check((await Api(routeVisitor, "register/verify", routeCsrf, routeAuth.Register(optionJson, origin), origin)).IsSuccessStatusCode,
                    "Independent HTTPS registration route: " + route);
                long? visitorNumber = null;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var authOptions = await Api(routeVisitor, "authenticate/options", routeCsrf, new { }, origin);
                    var verified = await Api(routeVisitor, "authenticate/verify", routeCsrf,
                        routeAuth.Authenticate(await authOptions.Content.ReadAsStringAsync(), origin), origin);
                    TestProgram.Check(verified.IsSuccessStatusCode, "Independent repeated HTTPS authentication route: " + route);
                    using var receipt = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
                    var number = receipt.RootElement.GetProperty("visitorNumber").GetInt64();
                    TestProgram.Check(number > 0 && (!visitorNumber.HasValue || visitorNumber == number), "HTTPS visitor number is stable: " + route);
                    visitorNumber = number;
                }
                var noCsrfNew = await routeVisitor.PostAsync("/Demo?handler=NewVisitor", new FormUrlEncodedContent([]));
                TestProgram.Check(noCsrfNew.StatusCode == HttpStatusCode.BadRequest, "New visitor requires antiforgery: " + route);
                using var rotationRequest=new HttpRequestMessage(HttpMethod.Post,"/Demo?handler=NewVisitor"){Content=Form(routeCsrf)};
                rotationRequest.Headers.Add("Origin",origin);
                var rotated = await routeVisitor.SendAsync(rotationRequest);
                TestProgram.Check(rotated.StatusCode == HttpStatusCode.Redirect
                    && jars[jarIndex].GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value != oldToken,
                    "Public new-visitor action rotates cookie without browser restart: " + route);
                var nextPage = await routeVisitor.GetStringAsync("/Demo");
                TestProgram.Check(nextPage.Contains("id=\"credential-count\">0</dd>") && nextPage.Contains("id=\"success-count\">0</dd>"),
                    "New visitor starts with zero credentials and authentications: " + route);
                var staleJar = jars.Count; using var staleClient = Client();
                jars[staleJar].Add(new Uri(origin), new Cookie(VisitorCookie.Name, oldToken, "/") { Secure = true, HttpOnly = true });
                TestProgram.Check((await staleClient.GetAsync("/Demo")).StatusCode == HttpStatusCode.Redirect,
                    "Previous visitor cookie cannot access either account: " + route);
            }
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            logs = await outputTask + await errorTask;
            File.Delete(certificatePath);
            if(prizeConfigureDiagnostic)PrizeEventHttpChecks.WriteConfigureServerDiagnostic(logs);
        }
        // Only assert coverage after all HTTP scenarios completed. An assertion in finally
        // would replace an earlier failure with missing logs for requests never reached.
        TestProgram.Check(secrets.All(secret => !logs.Contains(secret, StringComparison.Ordinal)), "Application logs contain no tested visitor names or session secrets");
        foreach (var stage in new[] { "Origin check", "model validation", "service/storage" })
            TestProgram.Check(logs.Contains("Prize create rejected at " + stage), "Event creation diagnostics include rejection stage: " + stage);
        TestProgram.Check(new[]{"Browser regression event","<script>Synthetic</script>","Updated synthetic prize"}
            .All(value=>!logs.Contains(value,StringComparison.Ordinal)),"Event creation diagnostics omit submitted event and prize names");
        async Task<string> StartDemo(HttpClient client, string route)
        {
            var html = await client.GetStringAsync("/Privacy");
            using var result = await client.PostAsync("/", Form(Csrf(html), ("route", route)));
            TestProgram.Check(result.StatusCode == HttpStatusCode.Redirect, "Temporary demo creation: " + route);
            return result.Headers.Location is {} location ? new Uri(client.BaseAddress!, location).AbsolutePath : "<missing>";
        }
    }
    private static string RenderedDemoRun(string html) => Regex.Match(html, "data-demo-run-id=\"([^\"]+)\"").Groups[1].Value;
    private static bool ExerciseHasAttribute(string html, string id, string attribute)
    {
        var tag = Regex.Match(html, "<[^>]+\\bid=\"" + Regex.Escape(id) + "\"[^>]*>").Value;
        return tag.Length > 0 && Regex.IsMatch(tag, "\\s" + Regex.Escape(attribute) + "(?:\\s|=|>)");
    }
    private static string RenderedDemoRoute(string html) => Regex.Match(html, "data-route=\"([^\"]+)\"").Groups[1].Value;
    // Only static route names/paths enter diagnostics; queries, fragments and dynamic IDs never do.
    private static string SafeDemoPath(string path) => path is "/Demo" or "/Index" or "/SignedIn" or "/Participation" or "/" ? path : "<unexpected or missing path>";
    private static string SafeDemoRoute(string route) => route is "laptop" or "ipad" or "phone" or "yubikey" ? route : "<unexpected or missing route>";
    private static string Csrf(string html)
    {
        var value = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (value.Length == 0) throw new InvalidOperationException("Antiforgery token not rendered.");
        return WebUtility.HtmlDecode(value);
    }
    private static FormUrlEncodedContent Form(string csrf, params (string Name, string Value)[] fields) => new(
        fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value)).Append(new("__RequestVerificationToken", csrf)));
    private static Task<HttpResponseMessage> Api(HttpClient client, string operation, string csrf, object body, string origin)
    {
        if (operation.EndsWith("/options", StringComparison.Ordinal))
        {
            client.DefaultRequestHeaders.Remove("X-Lumma-Operation");
            client.DefaultRequestHeaders.Add("X-Lumma-Operation", Guid.NewGuid().ToString("D"));
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webauthn/" + operation)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }
}
