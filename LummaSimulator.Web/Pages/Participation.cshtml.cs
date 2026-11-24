using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LummaSimulator.Web.Pages;
public sealed class ParticipationModel(PrizeRoundsService rounds):PageModel
{
    public VisitorPrizeStatus Status {get;private set;}=null!;
    public async Task<IActionResult> OnGetStateAsync(){
        Response.Headers.CacheControl="no-store";var status=await rounds.VisitorStatusAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        return status is null?Unauthorized():new JsonResult(new{key=status.LinkKey,round=status.Round});
    }
    public async Task<IActionResult> OnGetAsync(){
        Response.Headers.CacheControl="no-store";
        var status=await rounds.VisitorStatusAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
        if(status is null)return RedirectToPage("/Demo");Status=status;return Page();
    }
}
