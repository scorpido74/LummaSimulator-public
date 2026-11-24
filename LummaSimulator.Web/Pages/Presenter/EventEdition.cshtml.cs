using LummaSimulator.Web.Services;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-management")]
public sealed class EventEditionModel(EventEditionService events, PresenterService presenters) : PageModel
{
    public IReadOnlyList<EditionEvent> Events { get; private set; } = [];
    public string? Code { get; private set; }
    public string? Notice { get; private set; }
    public async Task OnGetAsync() => await Load();
    private async Task Load()
    {
        try { Events = await events.ListAsync(User, HttpContext.RequestAborted); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { Notice = "EventEditionStorageFailed"; }
    }
    public async Task<IActionResult> OnPostAsync(string operation, string? id, string? name, DateTimeOffset start, DateTimeOffset end,
        string? portalRun, string? wpfRun, bool confirmed)
    {
        try
        {
            if (!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().Origins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal)) return BadRequest();
            if (!ModelState.IsValid || !confirmed) throw new DemoRequestException("PresenterConfirm");
            await presenters.EventOperatorAsync(User, HttpContext.RequestAborted);
            switch (operation)
            {
                case "create": await events.CreateAsync(User, name ?? "", new DateTimeOffset(DateTime.SpecifyKind(start.DateTime, DateTimeKind.Utc)), new DateTimeOffset(DateTime.SpecifyKind(end.DateTime, DateTimeKind.Utc)), HttpContext.RequestAborted); break;
                case "start": await events.StatusAsync(User, id ?? "", false, HttpContext.RequestAborted); break;
                case "close": await events.StatusAsync(User, id ?? "", true, HttpContext.RequestAborted); break;
                case "link": Code = await events.LinkAsync(User, id ?? "", portalRun ?? "", wpfRun ?? "", HttpContext.RequestAborted); break;
                case "recover": await events.RecoverAsync(User, confirmed, HttpContext.RequestAborted); break;
                default: return BadRequest();
            }
            if (Code is null) return RedirectToPage();
        }
        catch (DemoRequestException e) { Notice = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { Notice = "EventEditionStorageFailed"; }
        await Load(); return Page();
    }
}
