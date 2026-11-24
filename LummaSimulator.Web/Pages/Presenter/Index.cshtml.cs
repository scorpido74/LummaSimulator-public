using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using LummaSimulator.Web.Configuration;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-management")]
public class IndexModel(DemoService demos, PresenterService presenters, TimeProvider clock, SessionStatusService sessions, DemoMetrics metrics, DemoStore store, ExperienceSettings settings, WebAuthnSettings webAuthn,EventAccess events) : PageModel
{
    public ExperienceSummary Summary { get; private set; } = null!;
    public List<DemoAccount> Demos { get; private set; } = [];
    public HashSet<string> MonitorGrants { get; private set; } = [];
    public bool MonitorConfigured => sessions.IsConfigured;
    public string? MonitorCapability { get; private set; }
    public long ExpiredCount => metrics.ExpiredDemonstrations;
    public async Task OnGetAsync()
    {
        // Preserve the exact Origin on native dashboard form POSTs; no external Referer.
        Response.Headers["Referrer-Policy"] = "same-origin";
        Demos = await demos.DashboardAsync(HttpContext.RequestAborted);
        MonitorGrants = await events.GrantedDemoIdsAsync(User,HttpContext.RequestAborted);
        Summary = await new ExperienceStatistics(store).SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),settings.AccountIdleMinutes,HttpContext.RequestAborted);
    }
    public string Elapsed(long created) => $"{(int)(clock.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(created)).TotalMinutes} min";
    public async Task<IActionResult> OnPostEndAsync(string id)
    {
        try { await presenters.RecordDemoActionAsync(User,"DemoEndRequested",id,HttpContext.RequestAborted);
            await demos.EndAsPresenterAsync(id ?? "", HttpContext.RequestAborted); }
        catch(DemoRequestException e) { TempData["MonitorNotice"]=e.Message; }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await presenters.SignOutAsync(User, HttpContext.RequestAborted);
        await HttpContext.SignOutAsync(PresenterService.Scheme);
        return RedirectToPage("/Presenter/Login");
    }
    public async Task<IActionResult> OnPostMonitorAsync([FromForm] string id, [FromForm] bool? enabled)
    {
        Response.Headers["Referrer-Policy"] = "same-origin";
        if (!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal)) {
            TempData["MonitorNotice"] = "MonitorGrantOriginInvalid"; return RedirectToPage();
        }
        // A malformed boolean used to bind to false and silently revoke instead.
        if (!ModelState.IsValid || !enabled.HasValue || string.IsNullOrWhiteSpace(id)
            || !Request.HasFormContentType || Request.Form["enabled"].Count != 1 || Request.Form["id"].Count != 1) {
            TempData["MonitorNotice"] = "MonitorGrantInvalid"; return RedirectToPage();
        }
        try {
            await presenters.RecordDemoActionAsync(User,"MonitorGrantChangeRequested",id,HttpContext.RequestAborted);
            if(enabled.Value&&!sessions.IsConfigured)throw new DemoRequestException("Configureer eerst het aparte monitorgeheim.");
            MonitorCapability=await events.SetMonitorAsync(User,id,enabled.Value,HttpContext.RequestAborted);
            if ((await events.GrantedDemoIdsAsync(User,HttpContext.RequestAborted)).Contains(id) != enabled.Value)
                TempData["MonitorNotice"] = "MonitorGrantFailed";
            if(MonitorCapability is not null){await OnGetAsync();return Page();}
        }
        catch (DemoRequestException exception) { TempData["MonitorNotice"] = exception.Message; }
        catch (Microsoft.Data.Sqlite.SqliteException) { TempData["MonitorNotice"] = "MonitorGrantFailed"; }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostEventAsync(string expected,string name,bool close,bool confirmed)
    {
        try { await presenters.EventAsync(User,expected??"",name??"",close,confirmed,HttpContext.RequestAborted); }
        catch(DemoRequestException e) { TempData["MonitorNotice"]=e.Message; }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRenameAsync(string id,string oldName,string name,bool confirmed)
    {
        try { await presenters.RenameEventAsync(User,id??"",oldName??"",name??"",confirmed,HttpContext.RequestAborted); }
        catch(DemoRequestException e) { TempData["MonitorNotice"]=e.Message; }
        return RedirectToPage();
    }
}
