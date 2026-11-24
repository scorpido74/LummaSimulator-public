using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Pages.Presenter.Prizes;

[EnableRateLimiting("presenter-management")]
public sealed class EventModel(PrizeEventService service,PrizeRoundsService rounds,WebAuthnSettings settings,TimeProvider clock) : PrizePageModel(settings)
{
    public PrizeManagedEvent Event { get; private set; }=null!;
    public PrizeRoundBoard RoundBoard {get;private set;}=null!;
    public bool ParticipationAvailable {get;private set;}
    public LummaSimulator.Web.Services.LoginRoutesEditor? LoginRoutesEditor {get;private set;}
    public string VisitorUrl {get;private set;}="";
    public IReadOnlyList<RoundPrizeDelivery> Deliveries {get;private set;}=[];
    public LummaSimulator.Web.Services.ParticipantQrEntry? ParticipantEntry {get;private set;}
    [BindProperty] public string ExpectedEventId { get; set; }="";
    [BindProperty] public string? RoundId {get;set;}
    [BindProperty] public int NextRoundCapacity {get;set;}=15;
    [BindProperty] public long Version { get; set; }
    [BindProperty] public string OperationKey { get; set; }= "";
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? ProfileName { get; set; }
    [BindProperty] public PrizeEventInput Input { get; set; }=new();
    public bool Expired => Event.PurgeAt is {} at && at<=clock.GetUtcNow().ToUnixTimeMilliseconds();
    private string RouteId => RouteData.Values["id"]?.ToString()??"";
    private async Task<bool> Load(bool populate)
    {
        var value=await service.GetAsync(User,RouteId,HttpContext.RequestAborted);
        if(value is null)return false;
        Event=value;
        RoundBoard=await rounds.BoardAsync(User,RouteId,HttpContext.RequestAborted);
        Deliveries=await rounds.DeliveriesAsync(User,RouteId,HttpContext.RequestAborted);
        var access=HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.EventAccess>();
        var canManageRoutes=!Expired&&Event.Status!="Purging"&&await access.CanAsync(User,"prize",RouteId,true,HttpContext.RequestAborted);
        var canJoin=Event.Status=="Open"&&Event.Configuration.Mode=="Group"
            &&await access.CanAcceptVisitorAsync("prize",RouteId,HttpContext.RequestAborted);
        var routes=canManageRoutes||canJoin?await access.ManagedLoginRoutesAsync(User,"prize",RouteId,HttpContext.RequestAborted):new LoginRoutes();
        LoginRoutesEditor=canManageRoutes?new("prize",RouteId,routes,true):null;
        ParticipationAvailable=canJoin&&routes.Mask!=0;
        VisitorUrl=settings.VisitorLink(RouteId,Request.PathBase.Value??"").AbsoluteUri;
        ParticipantEntry=await HttpContext.RequestServices.GetRequiredService<LummaSimulator.Web.Services.ParticipantQrService>()
            .ReadAsync(User,RouteId,Request.Scheme+"://"+Request.Host,Request.PathBase.Value??"",HttpContext.RequestAborted);
        if(populate){OperationKey=Guid.NewGuid().ToString("D");ExpectedEventId=value.Id;Version=value.Version;Name=value.Name;Input=PrizeEventInput.From(value.Configuration);}
        return true;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if(!await Load(true))return NotFound();
        NextRoundCapacity=RoundBoard.NextCapacity??RoundBoard.DefaultCapacity;
        RoundId=RoundBoard.Rounds.FirstOrDefault(r=>r.Status=="Open")?.GroupId??"";return Page();
    }
    public Task<IActionResult> OnPostStartAsync(bool confirmed)=>Change("Start",confirmed);
    public async Task<IActionResult> OnPostDeliverAsync(string participantId,bool confirmed)
    {
        if(!TrustedOrigin||!ModelState.IsValid||!confirmed)return BadRequest();
        try{await rounds.RegisterDeliveryAsync(User,RouteId,participantId,HttpContext.RequestAborted);return RedirectToPage(new{id=RouteId});}
        catch(Exception e)when(e is DemoRequestException or PrizeConflictException or SqliteException){return Failure(e);}
    }
    public Task<IActionResult> OnPostCloseAsync(bool confirmed)=>Change("Close",confirmed);
    public Task<IActionResult> OnPostReopenAsync(bool confirmed)=>Change("Reopen",confirmed);
    public async Task<IActionResult> OnPostNextRoundCapacityAsync()
    {
        if(!TrustedOrigin)return BadRequest();
        if(!await Load(false))return NotFound();
        if(!Guid.TryParseExact(RoundId,"D",out _))return BadRequest();
        try{await rounds.SetNextCapacityAsync(User,RouteId,RoundId,NextRoundCapacity,HttpContext.RequestAborted);return RedirectToPage(new{id=RouteId});}
        catch(Exception e) when(e is DemoRequestException or PrizeConflictException or ArgumentException or SqliteException){return Failure(e);}
    }
    public async Task<IActionResult> OnPostCloseRoundAsync(bool confirmed)
    {
        if(!TrustedOrigin)return BadRequest();
        if(!confirmed||!await Load(false))return !confirmed?BadRequest():NotFound();
        if(!Guid.TryParseExact(RoundId,"D",out _))return BadRequest();
        try{await rounds.CloseAsync(User,RouteId,RoundId,HttpContext.RequestAborted);return RedirectToPage(new{id=RouteId});}
        catch(Exception e) when(e is DemoRequestException or PrizeConflictException or ArgumentException or SqliteException){return Failure(e);}
    }
    public Task<IActionResult> OnPostRenameAsync()=>Change("Rename",true);
    public Task<IActionResult> OnPostConfigureAsync()=>Change("Configure",true);
    public Task<IActionResult> OnPostProfileAsync()=>Change("Profile",true);
    private async Task<IActionResult> Change(string action,bool confirmed)
    {
        if(!TrustedOrigin)return BadRequest();
        if(!await Load(false))return NotFound();
        if(ExpectedEventId!=RouteId)return BadRequest();
        if(!ModelState.IsValid){ErrorKey="PrizeInvalidSettings";Response.StatusCode=400;return Page();}
        try
        {
            switch(action)
            {
                case "Rename":await service.RenameAsync(User,RouteId,ExpectedEventId,Version,OperationKey,Name??"",HttpContext.RequestAborted);break;
                case "Configure":await service.ConfigureAsync(User,RouteId,ExpectedEventId,Version,OperationKey,Input.Configuration(),HttpContext.RequestAborted);break;
                case "Profile":await service.SaveProfileAsync(User,RouteId,ExpectedEventId,Version,OperationKey,ProfileName??"",HttpContext.RequestAborted);break;
                default:
                    await service.TransitionAsync(User,RouteId,ExpectedEventId,Version,OperationKey,
                        Enum.Parse<PrizeEventAction>(action),confirmed,HttpContext.RequestAborted);break;
            }
            return RedirectToPage(new{id=RouteId});
        }
        catch(Exception e) when(e is DemoRequestException or PrizeConflictException or ArgumentException or SqliteException)
        {return Failure(e);}
    }
}
