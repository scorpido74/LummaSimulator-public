using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-auth")]
public class SetupModel(PresenterService presenters,PresenterOnboarding onboarding,LummaSimulator.Web.Configuration.WebAuthnSettings webAuthn) : PageModel
{
    public bool Local => PresenterEndpoints.DirectLoopback(HttpContext);
    public bool NewInstallation {get;private set;}
    public string? IssuedCode {get;private set;}
    public string? Notice {get;private set;}
    public async Task<IActionResult> OnGetAsync()
    {
        if(!await presenters.IsSetupRequiredAsync(HttpContext.RequestAborted)) return RedirectToPage("/Presenter/Login");
        NewInstallation=!(await onboarding.StateAsync(HttpContext.RequestAborted)).Existing;
        PresenterEndpoints.Browser(HttpContext);Response.Headers["Referrer-Policy"]="same-origin";return Page();
    }
    public async Task<IActionResult> OnPostBeginAsync(string login,string name,bool confirmed)
    {
        if(!Local||!Request.IsHttps||!ModelState.IsValid||!confirmed||!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        try{IssuedCode=await presenters.BeginLocalSetupAsync(login??"",name??"",HttpContext.RequestAborted);}
        catch(LummaSimulator.Web.Models.DemoRequestException error){Notice=error.Message;}
        return await OnGetAsync();
    }
}
