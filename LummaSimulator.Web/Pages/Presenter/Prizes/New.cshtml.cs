using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Pages.Presenter.Prizes;

[EnableRateLimiting("presenter-management")]
public sealed class NewModel(PrizeEventService service,WebAuthnSettings settings,ILogger<NewModel> logger) : PrizePageModel(settings)
{
    public IReadOnlyList<PrizeProfile> Profiles { get; private set; }=[];
    [BindProperty] public string ProfileId { get; set; }=PrizeProfiles.Cybertruck;
    [BindProperty] public string Name { get; set; }="";
    [BindProperty] public string OperationKey { get; set; }= "";
    [BindProperty] public PrizeEventInput Input { get; set; }=new();
    public async Task<IActionResult> OnGetAsync(string? profileId)
    {
        OperationKey=Guid.NewGuid().ToString("D");
        Profiles=await service.ProfilesAsync(User,HttpContext.RequestAborted);
        var selected=profileId is null?await service.DefaultProfileAsync(User,HttpContext.RequestAborted)
            :Profiles.FirstOrDefault(p=>p.Id==profileId);
        if(selected is null)return NotFound();
        ProfileId=selected.Id;Input=PrizeEventInput.From(selected.Configuration);return Page();
    }
    public async Task<IActionResult> OnPostAsync(bool start)
    {
        if(!TrustedOrigin)
        {
            logger.LogWarning(new EventId(511,"PrizeCreateOriginRejected"),
                "Prize create rejected at Origin check; OriginKind={OriginKind}; TraceId={TraceId}",
                Request.Headers.Origin.Count==0?"Missing":Request.Headers.Origin.ToString()=="null"?"Opaque":"Untrusted",
                HttpContext.TraceIdentifier);
            return BadRequest();
        }
        Profiles=await service.ProfilesAsync(User,HttpContext.RequestAborted);
        if(!ModelState.IsValid)
        {
            logger.LogWarning(new EventId(512,"PrizeCreateValidationRejected"),
                "Prize create rejected at model validation; ErrorCount={ErrorCount}; TraceId={TraceId}",
                ModelState.ErrorCount,HttpContext.TraceIdentifier);
            ErrorKey="PrizeInvalidSettings";Response.StatusCode=400;return Page();
        }
        try
        {
            var result=await service.CreateAsync(User,OperationKey,Name,ProfileId,Input.Configuration(),start,HttpContext.RequestAborted);
            return RedirectToPage("Event",new{id=result.Id});
        }
        catch(Exception e) when(e is DemoRequestException or PrizeConflictException or ArgumentException or SqliteException)
        {
            logger.LogWarning(new EventId(513,"PrizeCreateRejected"),
                "Prize create rejected at service/storage; ExceptionType={ExceptionType}; SqliteCode={SqliteCode}; SqliteExtendedCode={SqliteExtendedCode}; TraceId={TraceId}",
                e.GetType().Name,(e as SqliteException)?.SqliteErrorCode,(e as SqliteException)?.SqliteExtendedErrorCode,
                HttpContext.TraceIdentifier);
            return Failure(e);
        }
    }
}
