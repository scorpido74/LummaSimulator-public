using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter.Integrations;
[Authorize(Policy = PresenterService.AdminPolicy)]
[EnableRateLimiting("presenter-management")]
public sealed class MonitorModel(MonitorKeyStore keys, PresenterService presenters, WebAuthnSettings webAuthn) : PageModel
{
    public MonitorKeyStatus Status { get; private set; } = new("Disabled", 0, false, null, null, null);
    public string? IssuedKey { get; private set; }
    public string? Notice { get; private set; }
    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers.Pragma = "no-cache";
        // Native POST navigation under no-referrer serializes Origin as null.
        // Retain it for same-origin forms; never send a referrer to another origin.
        // Origin validation below and Razor antiforgery remain mandatory.
        Response.Headers["Referrer-Policy"] = "same-origin";
    }
    private async Task Load()
    {
        NoStore(); await presenters.MonitorAdministratorAsync(User, false, HttpContext.RequestAborted);
        PresenterEndpoints.Browser(HttpContext);
        try { await keys.ReconcileAsync(presenters.AppendMonitorAuditAsync, HttpContext.RequestAborted); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException) { Notice = "MonitorManageStorage"; }
        Status = keys.Snapshot();
    }
    public Task OnGetAsync() => Load();
    public async Task<IActionResult> OnPostChangeAsync(string operation, long revision, bool confirmed)
    {
        NoStore();
        if (!confirmed || !webAuthn.Origins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal)) return BadRequest();
        try { IssuedKey = await keys.ChangeAsync(operation, revision, ct => presenters.MonitorAdministratorAsync(User, true, ct), presenters.AppendMonitorAuditAsync, HttpContext.RequestAborted); }
        catch (DemoRequestException e) { Notice = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException) { Notice = "MonitorManageStorage"; }
        await Load(); return Page();
    }
}
