using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages;
[EnableRateLimiting("demo")]
public class DemoModel(DemoService demos,PrizeRoundsService rounds,LummaSimulator.Web.Configuration.WebAuthnSettings webAuthn) : PageModel
{
    public DemoAccount Account { get; private set; } = null!;
    public VisitorPrizeStatus? PrizeStatus {get;private set;}
    public PrizeRoundSnapshot? PrizeRound {get;private set;}
    public bool HasPrizeDraw {get;private set;}
    public bool ExerciseCompleted {get;private set;}
    public bool IsAuthenticated {get;private set;}
    public string? EventName {get;private set;}
    public string DisplayName => PrizeRound?.Alias ?? Account.Name;
    public string RouteTitle => Account.Route switch { "phone" => "MET JE TELEFOON", "ipad" => "DE IPAD-DEMONSTRATIE", "yubikey" => "MET EEN FIDO2-SLEUTEL", _ => "OP DE DEMOLAPTOP" };
    public string RouteInstructions => Account.Route switch
    {
        "phone" => "Kies in de browser zo nodig 'ander apparaat' of een telefoon. Een QR-code en nabijheidscontrole worden door de browser aangeboden als jouw combinatie dit ondersteunt.",
        "ipad" => "Gebruik deze pagina op je iPad met een ondersteunde browser en ingestelde passkeymanager. Bevestig wanneer gevraagd met Face ID, Touch ID of de apparaatcode.",
        "yubikey" => "Sluit een FIDO2-geschikte YubiKey aan of gebruik NFC waar ondersteund. Kies een beveiligingssleutel en bevestig wanneer gevraagd met de sleutel en FIDO2-PIN.",
        _ => "Gebruik Windows Hello waar beschikbaar. Je browser kan daarnaast andere authenticators aanbieden. Vraag de presentator om hulp als Windows Hello nog niet is ingesteld."
    };
    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers["Referrer-Policy"]="same-origin";
        var account = await demos.GetAsync(VisitorCookie.Read(HttpContext), true, HttpContext.RequestAborted);
        if (account is null) { VisitorCookie.Delete(HttpContext); return RedirectToPage("/Index"); }
        Account = account;
        IsAuthenticated=await HttpContext.RequestServices.GetRequiredService<SecurityAuditService>().OwnerAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted) is not null;
        HasPrizeDraw=IsAuthenticated&&await rounds.HasPrizeDrawAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        ExerciseCompleted=IsAuthenticated&&await HttpContext.RequestServices.GetRequiredService<DemoStore>().ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",HttpContext.RequestAborted,("$id",account.Id)) is long;
        EventName=await HttpContext.RequestServices.GetRequiredService<EventAccess>().VisitorEventNameAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        PrizeStatus=await rounds.VisitorStatusAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        PrizeRound=PrizeStatus?.Round;
        return Page();
    }
    public async Task<IActionResult> OnPostEndAsync()
    {
        var deleted = await demos.EndOwnAsync(VisitorCookie.Read(HttpContext), HttpContext.RequestAborted);
        VisitorCookie.Delete(HttpContext);
        if (!deleted) return RedirectToPage("/Index");
        TempData["DemoDeleted"] = "yes";
        return RedirectToPage("/Complete");
    }
    public async Task<IActionResult> OnPostNewVisitorAsync()
    {
        if(!ModelState.IsValid||!Request.HasFormContentType||!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        // Rotation accepts no client-supplied event/run/identity selection, in form or query.
        if(Request.Query.Keys.Any(k=>!string.Equals(k,"handler",StringComparison.OrdinalIgnoreCase))
            ||Request.Form.Keys.Any(k=>k!="__RequestVerificationToken"))return BadRequest();
        var token = VisitorCookie.Read(HttpContext);
        var previous = await demos.GetAsync(token, false, HttpContext.RequestAborted);
        if (previous is null) return RedirectToPage("/Index");
        try
        {
            var next = await demos.CreateAsync(previous.Route, HttpContext.RequestAborted, token);
            VisitorCookie.Write(HttpContext, next.Token);
            LummaSimulator.Web.Localization.LanguagePreference.ResetVisitor(HttpContext);
            return RedirectToPage("/Demo");
        }
        catch (DemoRequestException exception) { TempData["VisitorNotice"] = exception.Message; return RedirectToPage("/Demo"); }
    }
}
