using LummaSimulator.Web.Services;
using LummaSimulator.Web.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages;
[EnableRateLimiting("demo")]
public sealed class SessionsModel(DemoStore store,SecurityAuditService audit,WebAuthnSettings settings,DemoService demos) : PageModel
{
    public List<IdentitySession> Sessions { get; private set; }=[];
    public int Offset {get;private set;}
    public async Task<IActionResult> OnGetAsync(int offset=0)
    {
        await demos.GetAsync(VisitorCookie.Read(HttpContext),true,HttpContext.RequestAborted);
        await store.Gate.WaitAsync(HttpContext.RequestAborted);
        try {
            var owner=await audit.OwnerAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
            if(owner is null) return RedirectToPage("/Demo");
            Offset=Math.Clamp(offset,0,100000);
            Sessions=await audit.SessionsAsync(owner,HttpContext.RequestAborted,Offset,50);return Page();
        } finally {store.Gate.Release();}
    }
    public async Task<IActionResult> OnPostRevokeAsync(string id,bool logout=false)
    {
        if(!settings.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal) || string.IsNullOrEmpty(id)) return BadRequest();
        await store.Gate.WaitAsync(HttpContext.RequestAborted);
        try {
            var owner=await audit.OwnerAsync(VisitorCookie.Read(HttpContext),HttpContext.RequestAborted);
            if(owner is null) return new UnauthorizedResult();
            var current=await audit.IsCurrentSessionAsync(owner,id,HttpContext.RequestAborted);
            if(current is null) return NotFound();
            await audit.RevokeAsync(owner,id,logout && current.Value,HttpContext.RequestAborted);
            if(current.Value) { VisitorCookie.Delete(HttpContext);return RedirectToPage("/Index"); }
            return RedirectToPage();
        } finally {store.Gate.Release();}
    }
}
