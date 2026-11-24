using LummaSimulator.Web.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LummaSimulator.Web.Services;
public sealed class PresenterPageAccessFilter(EventAccess events,WebAuthnSettings settings,PresenterOnboarding onboarding) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)=>Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,PageHandlerExecutionDelegate next)
    {
        var http=context.HttpContext;var page=context.ActionDescriptor.ViewEnginePath;
        if(!page.StartsWith("/Presenter/",StringComparison.Ordinal) || http.User.Identity?.IsAuthenticated!=true){await next();return;}
        // Native same-origin management forms retain their Origin; external referrers remain suppressed.
        http.Response.Headers["Referrer-Policy"]="same-origin";
        if(page is not ("/Presenter/Onboarding" or "/Presenter/Security")&&!page.StartsWith("/Presenter/Configuration",StringComparison.Ordinal)){
            var installation=await onboarding.StateAsync(http.RequestAborted);
            if(!installation.Existing&&!installation.Completed){
                if(HttpMethods.IsPost(http.Request.Method)&&page=="/Presenter/Index"&&context.HandlerMethod?.Name=="Logout"){await next();return;}
                if(!http.User.IsInRole("Administrator")){
                    context.Result=new ContentResult{StatusCode=403,Content="Evenementbeheer is geblokkeerd totdat een beheerder de onboarding heeft afgerond."};return;
                }
                context.Result=new RedirectToPageResult("/Presenter/Onboarding");return;
            }
        }
        // These existing handlers perform the same exact comparison themselves, retaining
        // their safe diagnostics / PRG error notice. Neither trusts Fetch Metadata instead.
        var ownOriginCheck=page=="/Presenter/Prizes/New" || page=="/Presenter/Index"&&context.HandlerMethod?.Name=="Monitor";
        if(HttpMethods.IsPost(http.Request.Method)&& !ownOriginCheck && !settings.Origins.Contains(http.Request.Headers.Origin.ToString(),StringComparer.Ordinal)) {
            context.Result=new BadRequestResult();return;
        }
        var handler=context.HandlerMethod?.Name??"";
        if(page.StartsWith("/Presenter/Audit",StringComparison.Ordinal)&&!http.User.IsInRole("Administrator")){context.Result=new StatusCodeResult(403);return;}
        if(page=="/Presenter/Index" && !http.User.IsInRole("Administrator")) {
            if(HttpMethods.IsGet(http.Request.Method)){context.Result=new RedirectToPageResult("/Presenter/Events/Index");return;}
            if(handler.Contains("Event",StringComparison.Ordinal)||handler.Contains("Rename",StringComparison.Ordinal)){context.Result=new StatusCodeResult(403);return;}
        }
        if(page=="/Presenter/Prizes/Event") {
            var id=Convert.ToString(context.RouteData.Values["id"])??"";
            var manage=handler.Contains("Rename",StringComparison.Ordinal)||handler.Contains("Configure",StringComparison.Ordinal)||handler.Contains("Profile",StringComparison.Ordinal);
            if(!await events.CanAsync(http.User,"prize",id,manage,http.RequestAborted)){context.Result=new NotFoundResult();return;}
        }
        await next();
    }
}
