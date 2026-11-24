using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LummaSimulator.Web.Pages;
public sealed class SignedInModel(SecurityAuditService audit, Services.Prizes.PrizeRoundsService rounds) : PageModel
{
    public LummaSimulator.Web.Models.PrizeRoundSnapshot? Round {get;private set;}
    public bool PrizesEnabled {get;private set;}
    public string UserName { get; private set; } = "";
    public async Task<IActionResult> OnGetAsync()
    {
        var token = VisitorCookie.Read(HttpContext);
        var account = await audit.OwnerAsync(token, HttpContext.RequestAborted);
        if (account is null) return RedirectToPage("/Demo");
        var status=await rounds.VisitorStatusAsync(token,HttpContext.RequestAborted);
        Round=status?.Round;PrizesEnabled=await rounds.HasPrizeDrawAsync(token,HttpContext.RequestAborted);
        UserName = Round?.Alias ?? account.Name;
        return Page();
    }
    public async Task<IActionResult> OnPostBeginExerciseAsync()
    {
        if(!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        try{return new JsonResult(await HttpContext.RequestServices.GetRequiredService<VisitorExerciseService>().BeginAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted));}
        catch(LummaSimulator.Web.Models.DemoRequestException){return Unauthorized();}
    }
}
