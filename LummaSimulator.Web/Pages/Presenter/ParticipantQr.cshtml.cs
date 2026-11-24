using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter;

// Presenter folder policy plus per-event service authorization on every read.
// Polling has a separate bounded budget; it cannot exhaust management POSTs.
[EnableRateLimiting("participant-qr")]
public sealed class ParticipantQrModel(ParticipantQrService service):PageModel
{
    private Task<ParticipantQrEntry?> Read(string id,string kind)=>service.ReadAsync(User,id,Request.Scheme+"://"+Request.Host,Request.PathBase.Value??"",HttpContext.RequestAborted,kind);
    public IActionResult OnGet()=>NotFound();
    public async Task<IActionResult> OnGetStateAsync(string id,string kind="prize")
    {
        Response.Headers.CacheControl="no-store";
        var entry=await Read(id,kind);
        return entry is null?NotFound():new JsonResult(entry);
    }
    public async Task<IActionResult> OnGetImageAsync(string id,string kind="prize")
    {
        Response.Headers.CacheControl="no-store";
        var entry=await Read(id,kind);
        if(entry is null)return NotFound();
        try{return Content(LummaSimulator.Shared.ParticipantQr.Encode(entry.Url).ToSvg(),"image/svg+xml; charset=utf-8");}
        catch(ArgumentException){return BadRequest();}
    }
}
