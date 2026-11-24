using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-auth")]
public class EnrollModel : PageModel {
    public bool HasGrant => LummaSimulator.Web.Models.DemoIdentity.IsToken(Request.Cookies[PresenterService.EnrollmentCookie]);
    public void OnGet() => PresenterEndpoints.Browser(HttpContext);
}
