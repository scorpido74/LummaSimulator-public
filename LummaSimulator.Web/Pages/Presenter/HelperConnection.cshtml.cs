using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter;

[EnableRateLimiting("presenter-management")]
public sealed class HelperConnectionModel(SessionDemoService service, WebAuthnSettings settings, IConfiguration configuration) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? RequestId { get; set; }
    public bool Connected { get; private set; }
    public string? Notice { get; private set; }
    public IActionResult OnGet() => configuration.GetValue<bool>("Helper:Enabled") && Guid.TryParseExact(RequestId, "D", out _) ? Page() : NotFound();
    public async Task<IActionResult> OnPostAsync(bool confirmed)
    {
        if (!configuration.GetValue<bool>("Helper:Enabled") || !Request.IsHttps || !settings.Origins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal)
            || !Guid.TryParseExact(RequestId, "D", out _)) return BadRequest();
        try { await service.ApproveAsync(User, RequestId, confirmed, HttpContext.RequestAborted); Connected = true; }
        catch (DemoRequestException) { Notice = "SessionDemoUnavailable"; Response.StatusCode = 400; }
        return Page();
    }
}
