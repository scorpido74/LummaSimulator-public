using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages;

[EnableRateLimiting("demo")]
public sealed class ClickFixModel(DemoService demos, TimeProvider clock, DemoStore store, SecurityAuditService audit, EventEditionService? events = null) : PageModel
{
    public string? Attempt { get; private set; }
    public bool NotificationUnavailable { get; private set; }
    public Task<IActionResult> OnGetAsync()=>Task.FromResult<IActionResult>(RedirectToPage("/Demo"));
    public async Task<IActionResult> OnPostCompleteAsync()
    {
        if (!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal)) return BadRequest();
        await store.Gate.WaitAsync(HttpContext.RequestAborted);
        try {
            var account = await audit.OwnerAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
            if (account is null) return Unauthorized();
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            await using var c=await store.OpenAsync(HttpContext.RequestAborted);using var tx=c.BeginTransaction(deferred:false);
            var changed = await EventAccess.Execute(c,tx,"UPDATE VisitorExercises SET CompletedAt=COALESCE(CompletedAt,$now) WHERE DemoId=$id AND StartedAt<=$cutoff",HttpContext.RequestAborted,("$id",account.Id),("$now",now),("$cutoff",now));
            if(changed==1)await EventAccess.Execute(c,tx,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) SELECT $audit,p.EventId,$now,$account,'RoundDemoCompleted',p.Id,$operation,e.Version,e.Version FROM PrizeParticipants p JOIN PrizeEvents e ON e.Id=p.EventId WHERE p.AccountId=$account AND NOT EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=p.EventId AND a.TargetId=p.Id AND a.Action='RoundDemoCompleted')",HttpContext.RequestAborted,("$audit",Guid.NewGuid().ToString("D")),("$now",now),("$account",account.Id),("$operation",Guid.NewGuid().ToString("D")));
            tx.Commit();
            return changed == 1 ? new JsonResult(new { completed = true }) : BadRequest();
        } finally { store.Gate.Release(); }
    }
    public async Task<IActionResult> OnPostChoiceAsync([FromBody] ChoiceRequest request)
    {
        if (events is null || !ModelState.IsValid || request is null || string.IsNullOrEmpty(request.Attempt)
            || !HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().Origins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal)) return BadRequest();
        try { return new JsonResult(await events.ChoiceAsync(VisitorCookie.Read(HttpContext), request.Attempt, request.ActionType ?? "", HttpContext.RequestAborted)); }
        catch (LummaSimulator.Web.Models.DemoRequestException) { return BadRequest(); }
    }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ChoiceRequest(string? Attempt, string? ActionType);
