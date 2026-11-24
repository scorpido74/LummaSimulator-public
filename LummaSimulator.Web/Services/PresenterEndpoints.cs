using System.Net;
using System.Text.Json;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Localization;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Localization;

namespace LummaSimulator.Web.Services;

public sealed record PresenterRequest(string? Login, string? Code, string? KeyName, JsonElement Response, bool UseInvitation = false);
public static class PresenterEndpoints
{
    public static string Browser(HttpContext http)
    {
        var value=http.Request.Cookies[PresenterService.BrowserCookie];
        if(DemoIdentity.IsToken(value))return value!;
        value=DemoIdentity.RandomToken();http.Response.Cookies.Append(PresenterService.BrowserCookie,value,PresenterService.ShortCookie);return value;
    }
    public static bool DirectLoopback(HttpContext http) => http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
        && !http.Request.Headers.ContainsKey("X-Forwarded-For") && !http.Request.Headers.ContainsKey("X-Forwarded-Proto");
    public static void MapPresenterEndpoints(this WebApplication app)
    {
        var api=app.MapGroup("/api/presenter").RequireRateLimiting("presenter-auth");
        api.AddEndpointFilter(async(context,next)=>{
            var http=context.HttpContext;var text=http.RequestServices.GetRequiredService<IStringLocalizer<UiText>>();
            if(!http.RequestServices.GetRequiredService<WebAuthnSettings>().Origins.Contains(http.Request.Headers.Origin.ToString(),StringComparer.Ordinal))return Results.BadRequest(new{error=text["PresenterDenied"].Value});
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);return await next(context); }
            catch(AntiforgeryValidationException){return Results.BadRequest(new{error=text["PresenterDenied"].Value});}
            catch(DemoRequestException e){return Results.BadRequest(new{error=text[e.Message].Value,errorKey=e.Message});}
        });
        api.MapPost("/setup/open",async(PresenterRequest request,HttpContext http,PresenterService service)=>{
            if(!DirectLoopback(http)) return Results.BadRequest();
            var grant=await service.OpenEnrollmentAsync(request.Code??"",true,http.RequestAborted,bootstrapOnly:true);
            http.Response.Cookies.Append(PresenterService.EnrollmentCookie,grant,PresenterService.ShortCookie);
            return Results.Ok(new{verified=true});
        });
        api.MapPost("/enrollment/open",async(PresenterRequest request,HttpContext http,PresenterService service)=>{
            var grant=await service.OpenEnrollmentAsync(request.Code??"",DirectLoopback(http),http.RequestAborted);
            http.Response.Cookies.Append(PresenterService.EnrollmentCookie,grant,PresenterService.ShortCookie);return Results.Ok(new{verified=true});
        });
        api.MapPost("/{kind}/options",async(string kind,PresenterRequest request,HttpContext http,PresenterService service)=>{
            var options=await service.OptionsAsync(http.Request.Cookies[PresenterService.BrowserCookie]??"",http.Request.Headers["X-Lumma-Operation"].ToString(),kind,
                request.Login??"",request.UseInvitation ? http.Request.Cookies[PresenterService.EnrollmentCookie] : null,http.User,DirectLoopback(http),http.RequestAborted);
            return Results.Content(options,"application/json");
        });
        api.MapPost("/{kind}/verify",async(string kind,PresenterRequest request,HttpContext http,PresenterService service)=>{
            var principal=await service.VerifyAsync(http.Request.Cookies[PresenterService.BrowserCookie]??"",http.Request.Headers["X-Lumma-Operation"].ToString(),kind,
                request.Response,request.KeyName??"",request.UseInvitation ? http.Request.Cookies[PresenterService.EnrollmentCookie] : null,http.User,DirectLoopback(http),http.RequestAborted);
            if(principal is not null) await http.SignInAsync(PresenterService.Scheme,principal,new AuthenticationProperties{IsPersistent=false,AllowRefresh=false});
            if(kind=="enroll" && request.UseInvitation)http.Response.Cookies.Delete(PresenterService.EnrollmentCookie,PresenterService.ShortCookie);
            return Results.Ok(new{verified=true,kind,redirect=kind=="login"?"/Presenter/Events":kind=="enroll"?(http.User.Identity?.IsAuthenticated==true?"/Presenter/Security":"/Presenter/Login"):null});
        });
        api.MapPost("/{kind}/cancel",async(string kind,HttpContext http,PresenterService service)=>{
            await service.CancelAsync(http.Request.Cookies[PresenterService.BrowserCookie]??"",http.Request.Headers["X-Lumma-Operation"].ToString(),http.RequestAborted);return Results.Ok(new{cancelled=true});
        });
    }
}
