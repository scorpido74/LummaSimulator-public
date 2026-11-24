using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages;
[EnableRateLimiting("demo")]
public class IndexModel(DemoService demos,EventAccess events,PrizeRoundsService rounds, LummaSimulator.Web.Configuration.WebAuthnSettings webAuthn, Microsoft.Extensions.Localization.IStringLocalizer<LummaSimulator.Web.Localization.UiText> text) : PageModel
{
    public bool ParticipationAvailable {get;private set;}=true;
    public bool HasActiveDemo { get; private set; }
    public bool HasConflictingDemo { get; private set; }
    public bool DefaultEventUnavailable { get; private set; }
    public LoginRoutes LoginRoutes { get; private set; } = new();
    public PrizeRoundSnapshot? PrizeRound {get;private set;}
    [BindProperty(SupportsGet=true)] public string? EventKind {get;set;}
    [BindProperty(SupportsGet=true)] public string? EventId {get;set;}
    public async Task<IActionResult> OnGetAsync()
    {
        HasActiveDemo = await demos.GetAsync(VisitorCookie.Read(HttpContext), false, HttpContext.RequestAborted) is not null;
        if(HttpMethods.IsGet(Request.Method)&&EventKind is null&&EventId is null
            &&!Request.Query.ContainsKey("eventKind")&&!Request.Query.ContainsKey("eventId"))
        {
            Response.Headers.CacheControl="no-store";
            if(!HasActiveDemo)
            {
                var selected=await events.DefaultVisitorEventAsync(HttpContext.RequestAborted);
                if(selected is {Available:true})
                    return Redirect(webAuthn.VisitorLink(selected.Id,Request.PathBase,selected.Kind).AbsoluteUri);
                if(selected is not null)
                {
                    DefaultEventUnavailable=true;
                    ParticipationAvailable=false;
                    return Page();
                }
                if(await events.SingleDiscoverableEventAsync(HttpContext.RequestAborted) is {} legacy)
                    return Redirect(webAuthn.VisitorLink(legacy.Id,Request.PathBase,legacy.Kind).AbsoluteUri);
            }
        }
        try { LoginRoutes = await events.LoginRoutesAsync(EventKind, EventId, HttpContext.RequestAborted); }
        catch (DemoRequestException) { return NotFound(); }
        if(EventKind=="prize"&&EventId is not null){PrizeRound=await rounds.PublicJoinInfoAsync(EventId,HttpContext.RequestAborted);if(PrizeRound is null)return NotFound();}
        if(EventKind is not null && EventId is not null)
            ParticipationAvailable=await events.CanAcceptVisitorAsync(EventKind,EventId,HttpContext.RequestAborted);
        if(HasActiveDemo && (EventKind is not null || EventId is not null))
            HasConflictingDemo=!await events.MatchesVisitorEventAsync(VisitorCookie.Read(HttpContext)!,EventKind??"",EventId??"",HttpContext.RequestAborted);
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(string route,string? eventKind,string? eventId)
    {
        if (await demos.GetAsync(VisitorCookie.Read(HttpContext), false, HttpContext.RequestAborted) is not null) {
            EventKind=eventKind;EventId=eventId;
            if((eventKind is null && eventId is null) || await events.MatchesVisitorEventAsync(VisitorCookie.Read(HttpContext)!,eventKind??"",eventId??"",HttpContext.RequestAborted))return RedirectToPage("/Demo");
            var entry=await OnGetAsync();
            if(entry is not PageResult)return entry;
            ModelState.AddModelError("",text["VisitorEventMismatch"]);
            return Page();
        }
        EventKind=eventKind;EventId=eventId;
        if(eventKind is not null || eventId is not null){
            var entry=await OnGetAsync();
            if(entry is not PageResult)return entry;
            if(!ParticipationAvailable || LoginRoutes.Mask==0)return Page();
        }
        if(eventKind=="prize"&&eventId is not null)
        {
            PrizeRound=await rounds.PublicJoinInfoAsync(eventId,HttpContext.RequestAborted);
            if(PrizeRound is null)return NotFound();
            if(PrizeRound.Status=="Full"){EventKind=eventKind;EventId=eventId;return Page();}
        }
        try
        {
            EventKind = eventKind; EventId = eventId;
            LoginRoutes = await events.LoginRoutesAsync(eventKind, eventId, HttpContext.RequestAborted);
            if (!LoginRoutes.Allows(route)) throw new DemoRequestException("LoginRouteDisabled");
            var demo = await demos.CreateAsync(route ?? "", HttpContext.RequestAborted);
            if(eventKind is not null || eventId is not null)await events.BindVisitorAsync(demo.Token,eventKind??"",eventId??"",HttpContext.RequestAborted);
            VisitorCookie.Write(HttpContext, demo.Token);
            LummaSimulator.Web.Localization.LanguagePreference.ResetVisitor(HttpContext);
            return RedirectToPage("/Demo");
        }
        catch (DemoRequestException exception) { ModelState.AddModelError("", text[exception.Message]); return Page(); }
    }
}
