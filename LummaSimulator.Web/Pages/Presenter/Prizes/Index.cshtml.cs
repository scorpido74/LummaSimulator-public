using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter.Prizes;

[EnableRateLimiting("presenter-management")]
public sealed class IndexModel(PrizeEventService service,WebAuthnSettings settings) : PrizePageModel(settings)
{
    public PrizeEventPage Events { get; private set; }=new([],0,false);
    public async Task<IActionResult> OnGetAsync(int pageIndex=0)
    {
        try {Events=await service.ListAsync(User,pageIndex,HttpContext.RequestAborted);return Page();}
        catch(DemoRequestException e){return Failure(e);}
    }
}
