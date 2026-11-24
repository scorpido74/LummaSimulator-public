using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-auth")]
public class LoginModel(PresenterService presenters) : PageModel {
    public string? AppReturnUrl {get;private set;}
    public async Task<IActionResult> OnGetAsync(string? returnUrl=null) {
        foreach(var appPath in new[]{"/Presenter/AppConnection?requestId=","/Presenter/HelperConnection?requestId="})
            if(returnUrl?.StartsWith(appPath,StringComparison.Ordinal)==true&&Guid.TryParseExact(returnUrl[appPath.Length..],"D",out var request))AppReturnUrl=appPath+request.ToString("D");
        if(await presenters.IsSetupRequiredAsync(HttpContext.RequestAborted)) return RedirectToPage("/Presenter/Setup");
        PresenterEndpoints.Browser(HttpContext); return User.Identity?.IsAuthenticated==true ? RedirectToPage("/Presenter/Index") : Page(); }
}
