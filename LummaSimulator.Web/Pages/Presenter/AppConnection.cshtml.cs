using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter;

[EnableRateLimiting("presenter-management")]
public sealed class AppConnectionModel(SessionDemoService service,WebAuthnSettings settings):PageModel
{
    [BindProperty(SupportsGet=true)] public string? RequestId {get;set;}
    public string? Notice {get;private set;}
    public bool Connected {get;private set;}
    public IActionResult OnGet()=>Guid.TryParseExact(RequestId,"D",out _)?Page():BadRequest();
    public async Task<IActionResult> OnPostAsync(bool confirmed)
    {
        if(!PresenterEndpoints.DirectLoopback(HttpContext)||Request.Headers.ContainsKey("Forwarded")
            ||!settings.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal)
            ||!Guid.TryParseExact(RequestId,"D",out _))return BadRequest();
        try { await service.ApproveAsync(User,RequestId,confirmed,HttpContext.RequestAborted);Connected=true; }
        catch(DemoRequestException) { Notice="SessionDemoUnavailable";Response.StatusCode=400; }
        return Page();
    }
}
