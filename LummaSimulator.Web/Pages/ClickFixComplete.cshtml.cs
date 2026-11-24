using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LummaSimulator.Web.Pages;
public sealed class ClickFixCompleteModel(SecurityAuditService audit, DemoStore store,LummaSimulator.Web.Services.Prizes.PrizeRoundsService rounds) : PageModel
{
    public string UserName {get;private set;}="";
    public string ResultKey {get;private set;}="ExerciseNoPrizeAction";
    public string? PrizeName {get;private set;}
    public async Task<IActionResult> OnGetAsync()
    {
        var account = await audit.OwnerAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        if (account is null) return RedirectToPage("/Demo");
        if (await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",HttpContext.RequestAborted,("$id",account.Id)) is not long) return RedirectToPage("/Demo");
        var round=await rounds.ForDemoAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        UserName=round?.Alias??account.Name;
        if(round is not null){
            var enabled=Convert.ToInt64(await store.ScalarAsync("SELECT PrizeEnabled FROM PrizeEvents WHERE Id=$id",HttpContext.RequestAborted,("$id",round.EventId)))==1;
            if(round.Status=="Closed")ResultKey=round.PrizeMessageKey;
            else if(enabled)ResultKey="ExercisePrizePending";
            PrizeName=round.PrizeName;
        }
        return Page();
    }
    public async Task<IActionResult> OnGetResultAsync()
    {
        Response.Headers.CacheControl="no-store";
        var result=await OnGetAsync();
        return result is PageResult?new JsonResult(new{alias=UserName,key=ResultKey,prizeName=PrizeName}):Unauthorized();
    }
}
