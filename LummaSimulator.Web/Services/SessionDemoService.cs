using System.Security.Claims;
using System.Security.Cryptography;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using Microsoft.Data.Sqlite;
using static LummaSimulator.Web.Services.SessionDemoState;

namespace LummaSimulator.Web.Services;

public sealed partial class SessionDemoService(SessionDemoState state,DemoStore store,PresenterService presenters,TimeProvider clock,ExperienceSettings settings)
{
    private DateTimeOffset Now=>clock.GetUtcNow();
    private static DemoRequestException Denied()=>new("SessionDemoUnavailable");
    private static bool Id(string? value)=>Guid.TryParseExact(value,"D",out _);
    private static bool Secret(string? value,string hash)=>DemoIdentity.IsToken(value)&&CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(DemoIdentity.Hash(value!)),Convert.FromHexString(hash));
    private static DateTimeOffset Earlier(DateTimeOffset a,DateTimeOffset b)=>a<b?a:b;
    private AppGrant App(string id,string? key,bool approved=true)
    {
        if(!Id(id)||!state.Apps.TryGetValue(id,out var app)||app.Expires<=Now||!Secret(key,app.KeyHash)
            ||approved&&(app.Actor is null||app.ActorSession is null))throw Denied();
        return app;
    }
    private async Task<string> Actor(SqliteConnection c,SqliteTransaction tx,AppGrant app,string? eventId,CancellationToken ct)
    {
        var role=await EventAccess.Scalar(c,tx,"""
            SELECT a.Role FROM PresenterAuthSessions s JOIN PresenterAccounts a ON a.Id=s.AccountId
            WHERE s.Hash=$session AND a.Id=$actor AND a.Active=1 AND a.Pending=0 AND s.Expires>$now
              AND a.Role IN ('Presenter','Administrator') AND EXISTS(SELECT 1 FROM PresenterKeys WHERE AccountId=a.Id)
            """,ct,("$session",app.ActorSession),("$actor",app.Actor),("$now",Now.ToUnixTimeMilliseconds())) as string;
        if(role is null||eventId is not null&&(!Id(eventId)||!await EventAccess.Allowed(c,tx,app.Actor!,role,"prize",eventId,false,ct)))throw Denied();
        return role;
    }
    private static Task<int> Audit(SqliteConnection c,SqliteTransaction tx,long now,string? actor,string action,string target,CancellationToken ct)=>
        EventAccess.Execute(c,tx,"INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($at,$actor,$action,$target)",ct,
            ("$at",now),("$actor",actor),("$action",action),("$target",target));
    private void Selected(AppGrant app,string eventId)
    { if(app.EventId!=eventId||app.SelectionVersion<1)throw Denied(); }

    private sealed record Participant(string Id,string EventId,string AccountId,string Binding,string Session,DateTimeOffset SessionExpires,int Round,string Alias,string EventName,string RoundStatus);
    private async Task<List<Participant>> Participants(SqliteConnection c,SqliteTransaction tx,string eventId,string? participantId,CancellationToken ct)
    {
        var now=Now.ToUnixTimeMilliseconds();
        using var query=EventAccess.Command(c,tx,"""
            SELECT p.Id,p.EventId,d.Id,d.SessionHash,s.Id,s.ExpiresAt,g.Number,r.Alias,e.Name,g.Status
            FROM PrizeEvents e JOIN PrizeParticipants p ON p.EventId=e.Id AND p.State='Active'
            JOIN PrizeRoundParticipants r ON r.EventId=p.EventId AND r.ParticipantId=p.Id AND r.GroupId=p.GroupId
            JOIN PrizeGroups g ON g.EventId=p.EventId AND g.Id=p.GroupId
            JOIN Demos d ON d.Id=p.AccountId
            JOIN PresenterDemoContexts context ON context.DemoId=d.Id AND context.Kind='prize' AND context.EventId=p.EventId
            JOIN IdentitySessions s ON s.UserId=d.Id AND s.BindingHash=d.SessionHash
            WHERE e.Id=$event AND e.Status='Open' AND e.Mode='Group' AND ($participant IS NULL OR p.Id=$participant)
              AND d.ClosedAt IS NULL AND d.LastSeen>$cutoff AND d.LastAuthenticatedAt>$auth AND d.LastAuthenticatedAt<=$now
              AND s.RevokedAt IS NULL AND s.ExpiresAt>$now AND s.LastActivity>$cutoff
            ORDER BY g.Number,p.Number LIMIT 201
            """,("$event",eventId),("$participant",participantId),("$cutoff",now-(long)settings.AccountIdleMinutes*60000),
            ("$auth",now-1800000),("$now",now));
        var result=new List<Participant>();using var reader=await query.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),reader.GetInt32(6),reader.GetString(7),reader.GetString(8),reader.GetString(9)));
        if(result.Count>200)throw Denied();return result;
    }
    private async Task<Participant> ParticipantById(SqliteConnection c,SqliteTransaction tx,string eventId,string participantId,CancellationToken ct)
    { if(!Id(participantId))throw Denied();return (await Participants(c,tx,eventId,participantId,ct)).SingleOrDefault()??throw Denied(); }
    private async Task<Participant?> Visitor(SqliteConnection c,SqliteTransaction tx,string? secret,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(secret))return null;
        string? participantId,eventId;
        using(var query=EventAccess.Command(c,tx,"""
            SELECT p.Id,p.EventId FROM Demos d JOIN PrizeParticipants p ON p.AccountId=d.Id
            JOIN PresenterDemoContexts context ON context.DemoId=d.Id AND context.Kind='prize' AND context.EventId=p.EventId
            WHERE d.SessionHash=$hash AND d.ClosedAt IS NULL
            """,("$hash",DemoIdentity.Hash(secret!))))
        using(var reader=await query.ExecuteReaderAsync(ct))
        { if(!await reader.ReadAsync(ct))return null;participantId=reader.GetString(0);eventId=reader.GetString(1); }
        return (await Participants(c,tx,eventId,participantId,ct)).SingleOrDefault();
    }
    private bool Bound(ConsentGrant consent,Participant participant)=>consent.ParticipantId==participant.Id&&consent.EventId==participant.EventId
        &&consent.AccountId==participant.AccountId&&consent.BindingHash==participant.Binding&&consent.IdentitySession==participant.Session;
    private string Status(ConsentGrant? consent,Participant participant)
    {
        if(consent is null||!Bound(consent,participant)||!state.Apps.TryGetValue(consent.AppId,out var app)
            ||app.EventId!=consent.EventId||app.SelectionVersion!=consent.SelectionVersion)return "None";
        return consent.Expires<=Now||participant.RoundStatus!="Open"?"Expired":consent.Status;
    }
    private static VisitorConsent EmptyConsent=>new(null,"None",null,null,null,null);
    private VisitorConsent Info(ConsentGrant consent,Participant p)=>new(consent.Id,Status(consent,p),p.EventName,p.Round,p.Alias,consent.Expires);
    private ConsentGrant Granted(AppGrant app,Participant p)
    {
        if(!state.Consents.TryGetValue(p.Id,out var consent)||consent.AppId!=app.Id||Status(consent,p)!="Granted")throw Denied();
        return consent;
    }

    public Task<DesktopConnection> BeginAsync(CancellationToken ct,bool remoteHelper=false)=>state.Access(()=>{
        if(state.Apps.Count>=20)throw Denied();
        var id=Guid.NewGuid().ToString("D");var key=DemoIdentity.RandomToken();var expires=Now.AddMinutes(2);
        state.Apps.Add(id,new(){Id=id,KeyHash=DemoIdentity.Hash(key),Expires=expires,RemoteHelper=remoteHelper});
        return Task.FromResult(new DesktopConnection(id,key,expires));
    },ct);
    public Task ApproveAsync(ClaimsPrincipal principal,string id,bool confirmed,CancellationToken ct)=>state.Access(async()=>{
        if(!confirmed||!Id(id)||!state.Apps.TryGetValue(id,out var app)||app.Expires<=Now)throw Denied();
        var actor=await presenters.EventOperatorAsync(principal,ct);
        if(app.Actor is not null) { if(app.Actor!=actor.Id||app.ActorSession!=actor.SessionHash)throw Denied();return true; }
        var approved=new AppGrant{Id=app.Id,KeyHash=app.KeyHash,Expires=Now.AddMinutes(10),Actor=actor.Id,ActorSession=actor.SessionHash,RemoteHelper=app.RemoteHelper};
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await Actor(c,tx,approved,null,ct);await Audit(c,tx,Now.ToUnixTimeMilliseconds(),actor.Id,"DesktopConnected","desktop:"+id,ct);tx.Commit();
        state.Apps[id]=approved;return true;
    },ct);
    public Task<DesktopConnectionStatus> ConnectionAsync(string id,string? key,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key,false);
        if(app.Actor is null)return new DesktopConnectionStatus("Pending",app.Expires);
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Actor(c,tx,app,null,ct);
        return new DesktopConnectionStatus("Ready",app.Expires);
    },ct);
    public Task<IReadOnlyList<ParticipantEvent>> EventsAsync(string id,string? key,CancellationToken ct)=>state.Access<IReadOnlyList<ParticipantEvent>>(async()=>{
        var app=App(id,key);await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var role=await Actor(c,tx,app,null,ct);var rows=new List<ParticipantEvent>();
        using(var query=EventAccess.Command(c,tx,"SELECT Id,Name FROM PrizeEvents WHERE Status='Open' AND Mode='Group' ORDER BY CreatedAt DESC LIMIT 100"))
        using(var reader=await query.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))rows.Add(new(reader.GetString(0),reader.GetString(1)));
        var result=new List<ParticipantEvent>();foreach(var row in rows)if(await EventAccess.Allowed(c,tx,app.Actor!,role,"prize",row.Id,false,ct))result.Add(row);
        return result;
    },ct);
    public Task SelectAsync(string id,string? key,string eventId,long version,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key);if(version<1)throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);await Actor(c,tx,app,eventId,ct);
        if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeEvents WHERE Id=$id AND Status='Open' AND Mode='Group'",ct,("$id",eventId)))!=1)throw Denied();
        if(version<=app.SelectionVersion){if(version!=app.SelectionVersion||eventId!=app.EventId)throw Denied();return true;}
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"DesktopEventSelected","prize:"+eventId,ct);tx.Commit();
        app.EventId=eventId;app.SelectionVersion=version;return true;
    },ct);
    public Task<ParticipantRoster> RosterAsync(string id,string? key,string eventId,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key);Selected(app,eventId);
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Actor(c,tx,app,eventId,ct);
        var rows=await Participants(c,tx,eventId,null,ct);var result=new List<EventParticipant>();
        foreach(var row in rows){state.Consents.TryGetValue(row.Id,out var consent);var status=Status(consent,row);
            if(consent is not null&&state.Apps.TryGetValue(consent.AppId,out var issuer)&&!await new EventAccess(store,presenters,clock).ActorValidAsync(issuer.Actor!,issuer.ActorSession!,"prize",eventId,ct))status="Expired";
            result.Add(new(row.Id,row.Round,row.Alias,status,status=="Granted"&&consent?.AppId==app.Id));}
        return new ParticipantRoster(eventId,app.SelectionVersion,result);
    },ct);
    public Task<VisitorConsent> RequestConsentAsync(string id,string? key,string eventId,string participantId,string wpfRunId,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key);Selected(app,eventId);if(!Id(wpfRunId))throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);await Actor(c,tx,app,eventId,ct);
        var p=await ParticipantById(c,tx,eventId,participantId,ct);
        if(p.RoundStatus!="Open")throw Denied();
        if(state.Consents.TryGetValue(p.Id,out var existing)&&Bound(existing,p)&&existing.Expires>Now&&Status(existing,p) is "Pending" or "Granted") {
            if(existing.AppId!=app.Id)throw Denied();
            if(existing.WpfRunId==wpfRunId)return Info(existing,p);
        }
        if(state.Consents.Values.Any(v=>v.WpfRunId==wpfRunId&&v.ParticipantId!=p.Id&&v.Expires>Now))throw Denied();
        if(!state.Consents.ContainsKey(p.Id)&&state.Consents.Count>=200)throw Denied();
        var grant=new ConsentGrant{Id=Guid.NewGuid().ToString("D"),AppId=app.Id,EventId=eventId,ParticipantId=p.Id,WpfRunId=wpfRunId,
            AccountId=p.AccountId,BindingHash=p.Binding,IdentitySession=p.Session,SelectionVersion=app.SelectionVersion,
            Expires=Earlier(Now.AddMinutes(2),Earlier(app.Expires,p.SessionExpires))};
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"SessionDemoRequested","prize:"+eventId+":participant:"+p.Id,ct);tx.Commit();
        state.Consents[p.Id]=grant;return Info(grant,p);
    },ct);
    public Task<VisitorConsent> OwnConsentAsync(string? cookie,CancellationToken ct)=>state.Access(async()=>{
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var p=await Visitor(c,tx,cookie,ct);if(p is null)return EmptyConsent;
        if(!state.Consents.TryGetValue(p.Id,out var consent)||Status(consent,p)=="None")return EmptyConsent;
        if(!state.Apps.TryGetValue(consent.AppId,out var app))return EmptyConsent;
        await Actor(c,tx,app,p.EventId,ct);return Info(consent,p);
    },ct);
    public Task<VisitorConsent> DecideAsync(string? cookie,string requestId,bool allow,bool confirmed,CancellationToken ct)=>state.Access(async()=>{
        if(!Id(requestId)||allow&&!confirmed)throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var p=await Visitor(c,tx,cookie,ct)??throw Denied();
        if(!state.Consents.TryGetValue(p.Id,out var consent)||consent.Id!=requestId||!Bound(consent,p)
            ||!state.Apps.TryGetValue(consent.AppId,out var app)||Status(consent,p) is "None" or "Expired")throw Denied();
        await Actor(c,tx,app,p.EventId,ct);
        if(allow&&consent.Status=="Granted")return Info(consent,p);
        if(allow&&consent.Status!="Pending")throw Denied();
        var status=allow?"Granted":consent.Status=="Pending"?"Declined":"Revoked";
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),null,"SessionDemoConsent"+status,"prize:"+p.EventId+":participant:"+p.Id,ct);tx.Commit();
        consent.Status=status;if(allow)consent.Expires=Earlier(Now.AddMinutes(5),Earlier(app.Expires,p.SessionExpires));
        return Info(consent,p);
    },ct);
    public Task<SessionDemoOpenLink> OpenAsync(string id,string? key,string eventId,string participantId,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key);Selected(app,eventId);
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);await Actor(c,tx,app,eventId,ct);
        var p=await ParticipantById(c,tx,eventId,participantId,ct);var consent=Granted(app,p);
        if(state.Views.Count>=200)throw Denied();
        var token=DemoIdentity.RandomToken();var view=new ViewGrant{Id=Guid.NewGuid().ToString("D"),ConsentId=consent.Id,OpenHash=DemoIdentity.Hash(token),OpenExpires=Earlier(Now.AddSeconds(30),consent.Expires)};
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"SessionDemoOpenIssued","prize:"+eventId+":participant:"+p.Id,ct);tx.Commit();
        state.Views.Add(view.Id,view);return new SessionDemoOpenLink(view.Id,token,view.OpenExpires);
    },ct);
    private async Task<(ConsentGrant Consent,Participant Participant)> ActiveView(SqliteConnection c,SqliteTransaction tx,ViewGrant view,CancellationToken ct)
    {
        var consent=state.Consents.Values.SingleOrDefault(v=>v.Id==view.ConsentId)??throw Denied();
        if(!state.Apps.TryGetValue(consent.AppId,out var app)||app.Expires<=Now)throw Denied();
        Selected(app,consent.EventId);await Actor(c,tx,app,consent.EventId,ct);
        var p=await ParticipantById(c,tx,consent.EventId,consent.ParticipantId,ct);
        if(Granted(app,p).Id!=consent.Id)throw Denied();return(consent,p);
    }
    public Task<(string Cookie,SyntheticSessionView View)> RedeemAsync(string viewId,string? token,CancellationToken ct)=>state.Access(async()=>{
        if(!Id(viewId)||!state.Views.TryGetValue(viewId,out var view)||view.CookieHash is not null||view.OpenExpires<=Now||!Secret(token,view.OpenHash))throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var active=await ActiveView(c,tx,view,ct);var p=active.Participant;
        var app=state.Apps[active.Consent.AppId];
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"SessionDemoOpened","prize:"+p.EventId+":participant:"+p.Id,ct);tx.Commit();
        var cookie=DemoIdentity.RandomToken();view.CookieHash=DemoIdentity.Hash(cookie);
        return(cookie,new SyntheticSessionView(p.EventId,p.EventName,p.Round,p.Alias,active.Consent.Expires));
    },ct);
    public Task<SyntheticSessionView> ViewAsync(string viewId,string? cookie,CancellationToken ct)=>state.Access(async()=>{
        if(!Id(viewId)||!state.Views.TryGetValue(viewId,out var view)||view.CookieHash is null||!Secret(cookie,view.CookieHash))throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var active=await ActiveView(c,tx,view,ct);var p=active.Participant;
        return new SyntheticSessionView(p.EventId,p.EventName,p.Round,p.Alias,active.Consent.Expires);
    },ct);
    public Task CancelAsync(string id,string? key,string wpfRunId,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key,false);if(!Id(wpfRunId))throw Denied();
        var requests=state.Consents.Values.Where(r=>r.AppId==app.Id&&r.WpfRunId==wpfRunId).ToArray();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        foreach(var request in requests)await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"SessionDemoCancelled","prize:"+request.EventId+":participant:"+request.ParticipantId,ct);
        tx.Commit();foreach(var request in requests)request.Status="Revoked";return true;
    },ct);
    public Task DisconnectAsync(string id,string? key,CancellationToken ct)=>state.Access(async()=>{
        var app=App(id,key,false);
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await Audit(c,tx,Now.ToUnixTimeMilliseconds(),app.Actor,"DesktopDisconnected","desktop:"+id,ct);tx.Commit();
        state.Apps.Remove(id);return true;
    },ct);
}
