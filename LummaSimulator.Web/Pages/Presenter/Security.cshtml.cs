using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-management")]
public class SecurityModel(PresenterService service, WebAuthnSettings webAuthn) : PageModel
{
    public IReadOnlyList<PresenterKey> Keys { get; private set; }=[];
    public IReadOnlyList<(string Id,long Expires,bool Current)> Sessions {get;private set;}=[];
    public async Task OnGetAsync() { Response.Headers["Referrer-Policy"]="same-origin"; PresenterEndpoints.Browser(HttpContext); Keys=await service.KeysAsync(User,null,HttpContext.RequestAborted);Sessions=await service.OwnSessionsAsync(User,HttpContext.RequestAborted); }
    public async Task<IActionResult> OnPostSessionAsync(string reference,bool confirmed)
    {
        if(!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal)||!ModelState.IsValid||!confirmed||!Guid.TryParseExact(reference,"D",out _))return BadRequest();
        try{if(await service.RevokeOwnSessionAsync(User,reference,HttpContext.RequestAborted)){await HttpContext.SignOutAsync(PresenterService.Scheme);return RedirectToPage("/Presenter/Login");}}
        catch(DemoRequestException error){TempData["OwnKeyNotice"]=error.Message;}return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRemoveAsync(string key, bool confirmed)
    {
        if(!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal)) return BadRequest();
        if(!ModelState.IsValid || !confirmed || !Guid.TryParseExact(key,"D",out _)) return BadRequest();
        try {
            await service.RemoveOwnKeyAsync(User,key,HttpContext.RequestAborted);
            await HttpContext.SignOutAsync(PresenterService.Scheme);
            return RedirectToPage("/Presenter/Login");
        } catch(DemoRequestException error) { TempData["OwnKeyNotice"]=error.Message;return RedirectToPage(); }
    }
}
