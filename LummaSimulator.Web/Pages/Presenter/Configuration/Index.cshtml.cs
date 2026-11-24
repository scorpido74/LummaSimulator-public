using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Pages.Presenter.Configuration;
[Authorize(Policy = PresenterService.AdminPolicy)]
[EnableRateLimiting("presenter-management")]
[RequestSizeLimit(300000)]
public sealed class IndexModel(PresenterService presenters, MonitorKeyStore monitor, WebAuthnSettings webAuthn,PresentationSettings presentation,HelperDownload helper,SessionDemoService sessions) : PageModel
{
    public PresentationProfile Profile=>presentation.Current;
    public DemoNetworkPlan? SubmittedNetwork { get; private set; }
    public DemoNetworkPlan? Network=>SubmittedNetwork??Profile.Network;
    public HelperPackage? HelperPackage { get; private set; }
    public IReadOnlyList<HelperInstallation> Helpers { get; private set; }=[];
    public string? HelperDownloadUrl=>Url.Page("/Downloads/PresenterHelper", "Zip") is {} path?new Uri(new Uri(webAuthn.Origins[0]),path).AbsoluteUri:null;
    public MonitorKeyStatus Monitor { get; private set; } = new("Disabled", 0, false, null, null, null);
    public string RpId => webAuthn.RpId;
    public IReadOnlyList<string> Origins => webAuthn.Origins;
    public async Task OnGetAsync()
    {
        // Revalidate the current account/session against the existing authority, not just menu claims.
        await presenters.MonitorAdministratorAsync(User, false, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"]="same-origin";
        Monitor = monitor.Snapshot(); // Safe metadata only; never a hash, key, path or provider dump.
        HelperPackage=helper.Current();
        Helpers=await sessions.HelperInstallationsAsync(HttpContext.RequestAborted);
    }
    public async Task<IActionResult> OnPostSaveAsync(string organization,string displayName,string language,string eventType,int retentionDays,string? proposedBaseUrl,long revision,bool confirmed)
    {
        if(!ModelState.IsValid||!confirmed)return BadRequest();
        try{await presentation.SaveAsync(User,new(organization??"",displayName??"",language??"",eventType??"",retentionDays,proposedBaseUrl??"",revision),HttpContext.RequestAborted);}
        catch(DemoRequestException error){TempData["ConfigurationNotice"]=error.Message;}
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostLogoAsync(IFormFile? file,string slot,bool confirmed)
    {
        if(!ModelState.IsValid||!confirmed||file is null||file.Length is <1 or >262144)return BadRequest();
        try{using var bytes=new MemoryStream();await file.CopyToAsync(bytes,HttpContext.RequestAborted);if(bytes.Length>262144)return BadRequest();await presentation.UploadAsync(User,slot,bytes.ToArray(),HttpContext.RequestAborted);}
        catch(DemoRequestException error){TempData["ConfigurationNotice"]=error.Message;}
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostNetworkAsync(string portalOrigin,string appOrigin,string portalAddress,string appAddress,string demoSubnet,string helperSubnet,string profile,long revision,bool confirmed)
    {
        if(!ModelState.IsValid||!confirmed||!webAuthn.Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        var plan=new DemoNetworkPlan(portalOrigin??"",appOrigin??"",portalAddress??"",appAddress??"",demoSubnet??"",helperSubnet??"",profile??"");
        if(!plan.IsValid()) {
            foreach(var (field,message) in plan.Errors())ModelState.AddModelError(field,message);
            SubmittedNetwork=plan;
            await OnGetAsync();
            Response.StatusCode=400;
            return Page();
        }
        try{await presentation.SaveAsync(User,Profile with{Revision=revision,Network=plan},HttpContext.RequestAborted);TempData["NetworkSaved"]=true;}
        catch(DemoRequestException error){TempData["ConfigurationNotice"]=error.Message;}
        return RedirectToPage();
    }
}
