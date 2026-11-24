using System.Security.Claims;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter.Events;
[EnableRateLimiting("presenter-management")]
public sealed class IndexModel(EventAccess access,PresenterService presenters,SessionStatusService monitor,PresentationSettings presentation) : PageModel
{
    public IReadOnlyList<EventAccessRow> ActiveEvents {get;private set;}=[];
    public IReadOnlyList<EventAccessRow> VisitorDefaultChoices {get;private set;}=[];
    public VisitorDefault? VisitorDefault {get;private set;}
    public string? VisitorDefaultSelection {get;private set;}
    public bool VisitorDefaultConfirmed {get;private set;}
    public string? VisitorDefaultError {get;private set;}
    public LummaSimulator.Web.Services.Prizes.RoundProgress? Progress {get;private set;}
    public IReadOnlyList<LummaSimulator.Web.Services.Prizes.RoundOption> RoundOptions {get;private set;}=[];
    public bool Overview {get;private set;}
    public IReadOnlyList<EventAccessRow> Events {get;private set;}=[];
    public IReadOnlyList<EventInvitationRow> Invitations {get;private set;}=[];
    public IReadOnlyList<DemoAccount> Demos {get;private set;}=[];
    public IReadOnlyList<EventPerson> People {get;private set;}=[];
    public IReadOnlyList<EventActivity> Activities {get;private set;}=[];
    public IReadOnlyList<EventInvitationRow> Transfers {get;private set;}=[];
    public HashSet<string> Grants {get;private set;}=[];
    public string? Kind {get;private set;}
    public string VisitorUrl {get;private set;}="";
    public ParticipantQrEntry? ParticipantEntry {get;private set;}
    public LummaSimulator.Web.Models.PrizeManagedEvent? SelectedPrize {get;private set;}
    public string OperationKey {get;}=Guid.NewGuid().ToString("D");
    public LoginRoutes LoginRoutes {get;private set;} = new();
    public string? Id {get;private set;}
    public bool CanManage {get;private set;}
    public bool CanInvite {get;private set;}
    public bool CanInviteNew=>User.IsInRole("Administrator");
    public string? Code {get;private set;}
    public string? Notice {get;private set;}
    public bool EnrollmentCode {get;private set;}
    public PresentationProfile Defaults=>presentation.Current;
    public bool MonitorConfigured=>monitor.IsConfigured;
    public string Actor=>User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
    public async Task<IActionResult> OnGetAsync(string? kind,string? id,bool overview=false,string? roundId=null)
    {
        if(string.IsNullOrEmpty(kind)&&string.IsNullOrEmpty(id)){kind=null;id=null;}
        Response.Headers["Referrer-Policy"]="same-origin";Response.Headers.CacheControl="no-store";
        Events=await access.ListAsync(User,HttpContext.RequestAborted);Invitations=await access.InvitationsAsync(User,HttpContext.RequestAborted);
        ActiveEvents=await access.ActivePresentationEventsAsync(User,HttpContext.RequestAborted);
        VisitorDefault=await access.DefaultVisitorEventAsync(HttpContext.RequestAborted);
        VisitorDefaultChoices=await access.VisitorDefaultChoicesAsync(User,HttpContext.RequestAborted);
        Overview=overview&&User.IsInRole("Administrator");
        if(kind is null&&id is null&&ActiveEvents.FirstOrDefault() is {} initial){kind=initial.Kind;id=initial.Id;}
        if(kind is not null&&id is not null&&!Overview&&!ActiveEvents.Any(e=>e.Kind==kind&&e.Id==id)&&!(roundId is not null&&await access.CanAsync(User,kind,id,false,HttpContext.RequestAborted)))return NotFound();
        Transfers=await access.TransfersAsync(User,HttpContext.RequestAborted);
        Grants=await access.GrantedDemoIdsAsync(User,HttpContext.RequestAborted);
        if(kind is not null&&id is not null) {
            if(!await access.CanAsync(User,kind,id,false,HttpContext.RequestAborted))return NotFound();Kind=kind;Id=id;
            CanManage=await access.CanAsync(User,kind,id,true,HttpContext.RequestAborted);CanInvite=true;Demos=await access.DemosAsync(User,kind,id,HttpContext.RequestAborted);
            People=await access.PeopleAsync(User,kind,id,HttpContext.RequestAborted);
            Activities=await access.ActivitiesAsync(User,kind,id,HttpContext.RequestAborted);
            LoginRoutes=await access.ManagedLoginRoutesAsync(User,kind,id,HttpContext.RequestAborted);
            VisitorUrl=HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().VisitorLink(id,Request.PathBase.Value??"",kind).AbsoluteUri;
            ParticipantEntry=await ReadParticipantEntry(id,kind);
            if(kind=="prize"){
                try{Progress=await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeRoundsService>().ProgressAsync(User,id,roundId,HttpContext.RequestAborted);
                RoundOptions=await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeRoundsService>().RoundOptionsAsync(User,id,HttpContext.RequestAborted);
                Progress=Progress! with{CanClose=Progress.CanClose&&ActiveEvents.Any(e=>e.Kind==kind&&e.Id==id)};
                }catch(DemoRequestException){return NotFound();}
            }
            if(kind=="prize")SelectedPrize=await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeEventService>().GetAsync(User,id,HttpContext.RequestAborted);
        }
        return Page();
    }
    public async Task<IActionResult> OnPostVisitorDefaultAsync(string? selection,bool useAsDefault)
    {
        var interactive=Request.Headers["X-Lumma-Visitor-Default"]=="1";
        if(!User.IsInRole("Administrator"))return Forbid();
        if(!ModelState.IsValid||!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>()
            .Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        if(!useAsDefault)return interactive?await Failure(400,"VisitorDefaultConfirm"):RedirectToPage();
        var parts=selection?.Split(':');
        if(parts is not {Length:2}||!EventAccess.Valid(parts[0],parts[1])||parts[0] is not ("experience" or "prize"))
            return await Failure(400,"VisitorDefaultConfirm");
        try
        {
            await presentation.SaveVisitorDefaultAsync(User,parts[0],parts[1],HttpContext.RequestAborted);
            TempData["VisitorDefaultNotice"]="VisitorDefaultSaved";
            return interactive?new JsonResult(new{saved=true,redirectUrl=Url.Page("/Presenter/Events/Index")+"#visitor-default"}):RedirectToPage();
        }
        catch(DemoRequestException error)
        {
            if(error.Message=="PresenterDenied")return Forbid();
            return await Failure(409,error.Message);
        }

        async Task<IActionResult> Failure(int status,string key)
        {
            if(interactive)return new JsonResult(new{errorKey=key}){StatusCode=status};
            VisitorDefaultSelection=selection;
            VisitorDefaultConfirmed=useAsDefault;
            VisitorDefaultError=key;
            var result=await OnGetAsync(null,null);
            Response.StatusCode=status;
            return result;
        }
    }
    public async Task<IActionResult> OnGetProgressAsync(string id,string? roundId)
    {
        Response.Headers.CacheControl="no-store";
        if(!await access.CanAsync(User,"prize",id,false,HttpContext.RequestAborted))return NotFound();
        try{var progress=await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeRoundsService>().ProgressAsync(User,id,roundId,HttpContext.RequestAborted);
        return new JsonResult(progress with{CanClose=progress.CanClose&&(await access.ActivePresentationEventsAsync(User,HttpContext.RequestAborted)).Any(e=>e.Kind=="prize"&&e.Id==id)});
        }catch(DemoRequestException){return NotFound();}
    }
    private Task<ParticipantQrEntry?> ReadParticipantEntry(string id,string kind)=>HttpContext.RequestServices.GetRequiredService<ParticipantQrService>()
        .ReadAsync(User,id,Request.Scheme+"://"+Request.Host,Request.PathBase.Value??"",HttpContext.RequestAborted,kind);
    public async Task<IActionResult> OnGetExportAsync(string kind,string id)
    {
        if(!await access.CanAsync(User,kind,id,false,HttpContext.RequestAborted))return NotFound();
        var demos=await access.DemosAsync(User,kind,id,HttpContext.RequestAborted);
        var json=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{kind,eventId=id,demonstrations=demos.Select(d=>new{d.DemoRunId,d.Name,d.CredentialCount,d.Successes,d.Failures})});
        return File(json,"application/json","event-report.json");
    }
    public async Task<IActionResult> OnPostLoginRoutesAsync(string kind,string id,bool confirmed,bool yubikey,bool laptop,bool phone,bool ipad,bool prizeDetails=false)
    {
        if(!ModelState.IsValid||!confirmed||!EventAccess.Valid(kind,id)||(prizeDetails&&kind!="prize")
            ||!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>()
                .Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return BadRequest();
        var noticeKey=$"LoginRoutesNotice:{kind}:{id}";
        try
        {
            await access.SetLoginRoutesAsync(User,kind,id,(yubikey?1:0)|(laptop?2:0)|(phone?4:0)|(ipad?8:0),HttpContext.RequestAborted);
            TempData[noticeKey]="LoginOptionsSaved";
        }
        catch(DemoRequestException error)
        {
            if(error.Message=="PresenterDenied")return NotFound();
            TempData[noticeKey]=error.Message;
        }
        return prizeDetails?RedirectToPage("/Presenter/Prizes/Event",new{id}):RedirectToPage(new{kind,id,overview=User.IsInRole("Administrator")});
    }
    public async Task<IActionResult> OnPostAsync(string operation,string? kind,string? id,string? target,string? name,string? login,string? invitation,string? demo,bool confirmed,bool yubikey=false,bool laptop=false,bool phone=false,bool ipad=false)
    {
        if(!ModelState.IsValid||!confirmed)return BadRequest();
        var ct=HttpContext.RequestAborted;
        try {
            switch(operation) {
                case "close-round":
                    if(kind!="prize"||!(await access.ActivePresentationEventsAsync(User,ct)).Any(e=>e.Kind==kind&&e.Id==id)
                        ||!HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Configuration.WebAuthnSettings>().Origins.Contains(Request.Headers.Origin.ToString(),StringComparer.Ordinal))return NotFound();
                    await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeRoundsService>().CloseAsync(User,id!,target??"",ct);
                    return RedirectToPage(new{kind,id,roundId=target});
                case "login-routes":
                    return await OnPostLoginRoutesAsync(kind??"",id??"",confirmed,yubikey,laptop,phone,ipad);
                case "create":
                    if(kind=="prize"){
                        var prizes=HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.Prizes.PrizeEventService>();var profile=await prizes.DefaultProfileAsync(User,ct);
                        id=(await prizes.CreateAsync(User,Guid.NewGuid().ToString("D"),name??"",profile.Id,profile.Configuration with{RetentionDays=Defaults.RetentionDays},false,ct)).Id;
                    }else if(kind=="edition")id=await HttpContext.RequestServices.GetRequiredService<EventEditionService>().CreateAsync(User,name??"",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(8),ct);
                    else if(kind=="experience")id=await access.CreateExperienceAsync(User,name??"",ct);else return BadRequest();
                    break;
                case "invite":await access.InviteAsync(User,kind??"",id??"",target??"",ct);break;
                case "new":Code=await access.InviteNewAsync(User,kind??"",id??"",login??"",name??"",ct);EnrollmentCode=true;break;
                case "accept":await access.AcceptAsync(User,invitation??"",true,ct);break;
                case "decline":await access.AcceptAsync(User,invitation??"",false,ct);break;
                case "remove":await access.RemoveAsync(User,kind??"",id??"",target??"",false,ct);break;
                case "revoke":await access.RemoveAsync(User,kind??"",id??"",target??"",true,ct);break;
                case "offer":case "transfer":case "recover":await access.TransferAsync(User,kind??"",id??"",target??"",operation=="transfer"?"accept":operation,ct);break;
                case "start":case "close":if(kind!="experience")return BadRequest();await access.ChangeExperienceAsync(User,id??"",operation=="close",ct);break;
                case "grant":
                    if(!monitor.IsConfigured)throw new DemoRequestException("Configureer eerst het aparte monitorgeheim.");
                    Code=await access.SetMonitorAsync(User,demo??"",true,ct);break;
                case "ungrant":await access.SetMonitorAsync(User,demo??"",false,ct);break;
                case "end":
                    await presenters.RecordDemoActionAsync(User,"DemoEndRequested",demo??"",ct);
                    await HttpContext.RequestServices.GetRequiredService<DemoService>().EndAsPresenterAsync(demo??"",ct);break;
                default:return BadRequest();
            }
        }catch(DemoRequestException error){Notice=error.Message;}catch(PrizeConflictException error){Notice=error.Message;}
        if(Code is null && Notice is null)return RedirectToPage(new{kind,id,overview=User.IsInRole("Administrator")});
        return await OnGetAsync(kind,id,User.IsInRole("Administrator"));
    }
}
