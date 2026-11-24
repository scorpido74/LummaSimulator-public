using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.Antiforgery;

namespace LummaSimulator.Web.Services;

public static class SessionDemoEndpoints
{
    public const string ViewerCookie="__Host-Lumma.SessionDemo";
    public static void MapSessionDemoEndpoints(this WebApplication app)
    {
        MapDesktop(app, "/api/local/participant-demo", false);
        if (app.Configuration.GetValue<bool>("Helper:Enabled")) MapDesktop(app, "/api/helper/participant-demo", true);
        MapBrowser(app);
    }
    private static void MapDesktop(WebApplication app, string prefix, bool remote)
    {
        var desktop=app.MapGroup(prefix).RequireRateLimiting("demo");
        desktop.AddEndpointFilter(async(context,next)=>{
            var http=context.HttpContext;
            if((!remote&&!PresenterEndpoints.DirectLoopback(http))||!http.Request.IsHttps||http.Request.Headers.ContainsKey("Forwarded")
                ||http.Request.Headers.ContainsKey("Origin")||http.Request.Headers.ContainsKey("Sec-Fetch-Site")
                ||http.Request.Headers["X-Lumma-App-Protocol"]!="1")return Results.StatusCode(403);
            try{return await next(context);}catch(DemoRequestException){return Results.NotFound(new{errorKey="SessionDemoUnavailable"});}
        });
        static string? Key(HttpContext http)=>http.Request.Headers["X-Lumma-App-Key"].ToString();
        if (remote) desktop.MapGet("/compatibility", () => Results.Ok(new { protocols = new[] { 1 }, webVersion = typeof(SessionDemoEndpoints).Assembly.GetName().Version!.ToString() }));
        desktop.MapPost("/connections",async(SessionDemoService service,HttpContext http)=>Results.Ok(await service.BeginAsync(http.RequestAborted,remote)));
        if(remote)desktop.MapPost("/connections/{id}/heartbeat",async(string id,HelperHeartbeat request,SessionDemoService service,HttpContext http)=>{
            await service.HelperHeartbeatAsync(id,Key(http),request,http.RequestAborted);
            return Results.Ok(new{recorded=true});
        });
        desktop.MapGet("/connections/{id}",async(string id,SessionDemoService service,HttpContext http)=>Results.Ok(await service.ConnectionAsync(id,Key(http),http.RequestAborted)));
        desktop.MapGet("/connections/{id}/events",async(string id,SessionDemoService service,HttpContext http)=>Results.Ok(await service.EventsAsync(id,Key(http),http.RequestAborted)));
        desktop.MapPost("/connections/{id}/selection",async(string id,SelectParticipantEvent request,SessionDemoService service,HttpContext http)=>{
            await service.SelectAsync(id,Key(http),request.EventId,request.Version,http.RequestAborted);return Results.Ok(new{eventId=request.EventId,selectionVersion=request.Version});
        });
        desktop.MapGet("/connections/{id}/events/{eventId}/participants",async(string id,string eventId,SessionDemoService service,HttpContext http)=>
            Results.Ok(await service.RosterAsync(id,Key(http),eventId,http.RequestAborted)));
        desktop.MapPost("/connections/{id}/events/{eventId}/consent",async(string id,string eventId,StartParticipantDemo request,SessionDemoService service,HttpContext http)=>
            Results.Ok(await service.RequestConsentAsync(id,Key(http),eventId,request.ParticipantId,request.WpfRunId,http.RequestAborted)));
        desktop.MapPost("/connections/{id}/events/{eventId}/open",async(string id,string eventId,OpenParticipantDemo request,SessionDemoService service,HttpContext http)=>
            Results.Ok(await service.OpenAsync(id,Key(http),eventId,request.ParticipantId,http.RequestAborted)));
        desktop.MapPost("/connections/{id}/disconnect",async(string id,SessionDemoService service,HttpContext http)=>{
            await service.DisconnectAsync(id,Key(http),http.RequestAborted);return Results.Ok(new{disconnected=true});
        });
        desktop.MapPost("/connections/{id}/cancel",async(string id,CancelParticipantDemo request,SessionDemoService service,HttpContext http)=>{
            await service.CancelAsync(id,Key(http),request.WpfRunId,http.RequestAborted);return Results.Ok(new{cancelled=true});
        });
    }
    private static void MapBrowser(WebApplication app)
    {
        var browser=app.MapGroup("/api/session-demo").RequireRateLimiting("demo");
        browser.AddEndpointFilter(async(context,next)=>{
            var http=context.HttpContext;
            try {
                if(HttpMethods.IsPost(http.Request.Method)) {
                    if(!http.RequestServices.GetRequiredService<WebAuthnSettings>().Origins.Contains(http.Request.Headers.Origin.ToString(),StringComparer.Ordinal))return Results.BadRequest();
                    await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
                }
                return await next(context);
            }
            catch(AntiforgeryValidationException){return Results.BadRequest();}
            catch(DemoRequestException){return Results.NotFound(new{errorKey="SessionDemoUnavailable"});}
        });
        browser.MapGet("/consent",async(SessionDemoService service,HttpContext http)=>Results.Ok(await service.OwnConsentAsync(VisitorCookie.Read(http),http.RequestAborted)));
        browser.MapPost("/consent",async(DecideSessionDemo request,SessionDemoService service,HttpContext http)=>
            Results.Ok(await service.DecideAsync(VisitorCookie.Read(http),request.RequestId,request.Allow,request.Confirmed,http.RequestAborted)));
        browser.MapPost("/views/{id}/redeem",async(string id,RedeemSessionDemo request,SessionDemoService service,HttpContext http)=>{
            var result=await service.RedeemAsync(id,request.Token,http.RequestAborted);
            http.Response.Cookies.Append(ViewerCookie,result.Cookie,new CookieOptions{
                HttpOnly=true,Secure=true,SameSite=SameSiteMode.Strict,Path="/",Expires=result.View.ExpiresAt,IsEssential=true});
            return Results.Ok(result.View);
        });
        browser.MapGet("/views/{id}",async(string id,SessionDemoService service,HttpContext http)=>
            Results.Ok(await service.ViewAsync(id,http.Request.Cookies[ViewerCookie],http.RequestAborted)));
    }
}
