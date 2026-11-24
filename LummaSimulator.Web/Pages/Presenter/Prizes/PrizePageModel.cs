using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Pages.Presenter.Prizes;

public abstract class PrizePageModel(WebAuthnSettings webAuthn) : PageModel
{
    public string? ErrorKey { get; protected set; }
    protected bool TrustedOrigin => webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal);
    protected IActionResult Failure(Exception e)
    {
        if(e is DemoRequestException denied && denied.Message=="PresenterDenied" || e is UnauthorizedAccessException)
            return StatusCode(403);
        ErrorKey=e switch {
            PrizeConflictException conflict when conflict.Message.StartsWith("Prize",StringComparison.Ordinal)=>conflict.Message,
            PrizeConflictException=>"PrizeEventChanged",
            DemoRequestException error when error.Message.StartsWith("Prize",StringComparison.Ordinal)=>error.Message,
            _=>"PrizeInvalidSettings"
        };
        Response.StatusCode=e is PrizeConflictException?409:400;
        if(e is SqliteException sql && sql.SqliteErrorCode is 5 or 6) {
            ErrorKey="PrizeBusy";Response.StatusCode=503;Response.Headers.RetryAfter="1";
        }
        return Page();
    }
}
