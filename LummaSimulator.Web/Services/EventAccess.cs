using System.Security.Claims;
using LummaSimulator.Web.Models;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed record EventAccessRow(string Kind,string Id,string Name,string? Owner,string State);
public sealed record EventInvitationRow(string Id,string Kind,string EventId,string Name,long Expires);

// One authority for all existing event types. Unknown legacy ownership is admin-only.
public sealed partial class EventAccess
{
    private readonly DemoStore store;
    private readonly PresenterService presenters;
    private readonly TimeProvider clock;
    private readonly EventEditionStore? edition;
    public EventAccess(DemoStore store,PresenterService presenters,TimeProvider clock,EventEditionStore? edition=null){this.store=store;this.presenters=presenters;this.clock=clock;this.edition=edition;}
    public async Task<bool> CanInviteVisitorsAsync(ClaimsPrincipal user,string kind,string id,CancellationToken ct)
        => !(kind=="experience"&&id==ExperienceStatistics.UnassignedEvent)
            && await CanAsync(user,kind,id,false,ct) && await LiveAsync(kind,id,ct);
    private async Task<bool> LiveAsync(string kind,string id,CancellationToken ct)
    {
        if(kind=="experience"&&id==ExperienceStatistics.UnassignedEvent)return true; // Admin-only quarantine, never public admission.
        if(kind=="edition"){
            if(edition is null)return false;
            try{return await edition.AccessAsync(false,m=>Task.FromResult(m.Events.Any(e=>e.Id==id&&e.Status=="Active")),ct);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){return false;}
        }
        var sql=kind switch{"experience"=>"SELECT COUNT(*) FROM ExperienceEvents WHERE Id=$id AND Status='Active'","prize"=>"SELECT COUNT(*) FROM PrizeEvents WHERE Id=$id AND Status='Open'",_=>null};
        return sql is not null&&Convert.ToInt64(await store.ScalarAsync(sql,ct,("$id",id)))==1;
    }
    internal static SqliteCommand Command(SqliteConnection c,SqliteTransaction tx,string sql,params (string,object?)[] args)
    {var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;foreach(var(k,v) in args)q.Parameters.AddWithValue(k,v??DBNull.Value);return q;}
    internal static async Task<int> Execute(SqliteConnection c,SqliteTransaction tx,string sql,CancellationToken ct,params(string,object?)[] args)
    {using var q=Command(c,tx,sql,args);return await q.ExecuteNonQueryAsync(ct);}
    internal static async Task<object?> Scalar(SqliteConnection c,SqliteTransaction tx,string sql,CancellationToken ct,params(string,object?)[] args)
    {using var q=Command(c,tx,sql,args);return await q.ExecuteScalarAsync(ct);}
    internal static bool Valid(string kind,string id)=>(kind is "experience" or "prize" or "edition") && Guid.TryParseExact(id,"D",out _);
    internal static async Task<bool> Allowed(SqliteConnection c,SqliteTransaction tx,string actor,string role,string kind,string id,bool manage,CancellationToken ct)
    {
        if(!Valid(kind,id))return false;
        if(kind=="experience"&&id==ExperienceStatistics.UnassignedEvent)return role=="Administrator"&&!manage;
        if(role=="Administrator")return Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventScopes WHERE Kind=$kind AND SourceId=$id",ct,("$kind",kind),("$id",id)))==1;
        return Convert.ToInt64(await Scalar(c,tx,"""
            SELECT COUNT(*) FROM PresenterEventScopes e JOIN PresenterAccounts o ON o.Id=e.OwnerId AND o.Active=1 AND EXISTS(SELECT 1 FROM PresenterKeys WHERE AccountId=o.Id)
            WHERE e.Kind=$kind AND e.SourceId=$id AND (e.OwnerId=$actor OR ($manage=0 AND EXISTS(
              SELECT 1 FROM PresenterEventMembers m WHERE m.Kind=e.Kind AND m.EventId=e.SourceId AND m.AccountId=$actor)))
            """,ct,("$kind",kind),("$id",id),("$actor",actor),("$manage",manage)))==1;
    }
    internal static async Task<string> Require(SqliteConnection c,SqliteTransaction tx,PresenterService presenters,ClaimsPrincipal principal,string kind,string id,bool manage,CancellationToken ct,bool fresh=false)
    {
        var actor=await presenters.Actor(c,tx,principal,false,fresh,ct);
        if(!await Allowed(c,tx,actor.Id,actor.Role,kind,id,manage,ct))throw new DemoRequestException("PresenterDenied");
        return actor.Id;
    }
    internal static async Task<string> RequireOwner(SqliteConnection c,SqliteTransaction tx,PresenterService presenters,ClaimsPrincipal principal,string kind,string id,CancellationToken ct)
    {
        var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        if(!Valid(kind,id) || !string.Equals(await Scalar(c,tx,"SELECT OwnerId FROM PresenterEventScopes WHERE Kind=$kind AND SourceId=$id",ct,("$kind",kind),("$id",id)) as string,actor.Id,StringComparison.Ordinal))
            throw new DemoRequestException("PresenterDenied");
        return actor.Id;
    }
    internal static async Task Register(SqliteConnection c,SqliteTransaction tx,string kind,string id,string name,string? owner,CancellationToken ct)
    {
        if(!Valid(kind,id))throw new DemoRequestException("PresenterDenied");
        if(owner is not null&&Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts a WHERE Id=$owner AND Active=1 AND EXISTS(SELECT 1 FROM PresenterKeys WHERE AccountId=a.Id)",ct,("$owner",owner)))!=1)throw new DemoRequestException("PresenterDenied");
        await Execute(c,tx,"INSERT INTO PresenterEventScopes(Kind,SourceId,Name,OwnerId) VALUES($kind,$id,$name,$owner) ON CONFLICT(Kind,SourceId) DO NOTHING",ct,("$kind",kind),("$id",id),("$name",name),("$owner",owner));
    }
    public async Task RequireAsync(ClaimsPrincipal principal,string kind,string id,bool manage,CancellationToken ct)
    {await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Require(c,tx,presenters,principal,kind,id,manage,ct);}
    public async Task<bool> CanAsync(ClaimsPrincipal principal,string kind,string id,bool manage,CancellationToken ct)
    {try{await RequireAsync(principal,kind,id,manage,ct);return true;}catch(DemoRequestException){return false;}}
    public async Task<bool> ActorValidAsync(string actor,string session,string kind,string id,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var role=await Scalar(c,tx,"SELECT a.Role FROM PresenterAccounts a JOIN PresenterAuthSessions s ON s.AccountId=a.Id WHERE a.Id=$actor AND a.Active=1 AND s.Hash=$session AND s.Expires>$now",ct,("$actor",actor),("$session",session),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())) as string;
        return role is not null&&await Allowed(c,tx,actor,role,kind,id,false,ct);
    }
    public async Task RegisterEditionAsync(ClaimsPrincipal principal,string id,string name,bool newlyCreated,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        await Register(c,tx,"edition",id,name,newlyCreated?actor.Id:null,ct);tx.Commit();
    }
    internal async Task ImportEditionAsync(IEnumerable<EditionEvent> legacy,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        foreach(var item in legacy){
            if(!Valid("edition",item.Id))throw new InvalidDataException("Edition migration requires explicit review");
            await Register(c,tx,"edition",item.Id,item.Name,null,ct);
        }
        await Execute(c,tx,"INSERT OR IGNORE INTO PresenterEventMigrations VALUES(2)",ct);tx.Commit();
    }
    public async Task<IReadOnlyList<EventAccessRow>> ActivePresentationEventsAsync(ClaimsPrincipal user,CancellationToken ct)
    {
        var candidates=await ListAsync(user,ct);
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var actor=await presenters.Actor(c,tx,user,false,false,ct);var result=new List<EventAccessRow>();
        foreach(var item in candidates){
            var linked=Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventScopes e JOIN PresenterAccounts o ON o.Id=e.OwnerId AND o.Active=1 AND EXISTS(SELECT 1 FROM PresenterKeys WHERE AccountId=o.Id) WHERE e.Kind=$kind AND e.SourceId=$id AND (e.OwnerId=$actor OR EXISTS(SELECT 1 FROM PresenterEventMembers m WHERE m.Kind=e.Kind AND m.EventId=e.SourceId AND m.AccountId=$actor))",ct,("$kind",item.Kind),("$id",item.Id),("$actor",actor.Id)))==1;
            if(linked&&await LiveAsync(item.Kind,item.Id,ct))result.Add(item);
        }
        return result;
    }
    public async Task<string?> VisitorEventNameAsync(string? token,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(token))return null;
        return await store.ScalarAsync("SELECT COALESCE(e.Name,p.Name,s.Name) FROM Demos d JOIN PresenterDemoContexts x ON x.DemoId=d.Id JOIN PresenterEventScopes s ON s.Kind=x.Kind AND s.SourceId=x.EventId LEFT JOIN ExperienceEvents e ON x.Kind='experience' AND e.Id=x.EventId LEFT JOIN PrizeEvents p ON x.Kind='prize' AND p.Id=x.EventId WHERE d.SessionHash=$hash AND d.ClosedAt IS NULL AND NOT (x.Kind='experience' AND x.EventId=$unassigned)",ct,("$hash",DemoIdentity.Hash(token!)),("$unassigned",ExperienceStatistics.UnassignedEvent)) as string;
    }
    public async Task<IReadOnlyList<EventAccessRow>> ListAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        var rows=new List<EventAccessRow>();using(var q=Command(c,tx,"SELECT s.Kind,s.SourceId,COALESCE(e.Name,p.Name,s.Name),s.OwnerId,COALESCE(e.Status,p.Status,'') FROM PresenterEventScopes s LEFT JOIN ExperienceEvents e ON s.Kind='experience' AND e.Id=s.SourceId LEFT JOIN PrizeEvents p ON s.Kind='prize' AND p.Id=s.SourceId ORDER BY s.Name,s.Kind,s.SourceId"))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4)));
        var result=new List<EventAccessRow>();foreach(var row in rows)if(await Allowed(c,tx,actor.Id,actor.Role,row.Kind,row.Id,false,ct))result.Add(row);
        return result;
    }
    public async Task<string> CreateExperienceAsync(ClaimsPrincipal principal,string name,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>100||name.Any(char.IsControl))throw new DemoRequestException("PresenterConfirm");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        var id=Guid.NewGuid().ToString("D");var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        await Execute(c,tx,"INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES($id,$name,$now,'Active'); INSERT INTO EventPrizePolicies(EventId) VALUES($id)",ct,("$id",id),("$name",name.Trim()),("$now",now));
        await Register(c,tx,"experience",id,name.Trim(),actor.Id,ct);await Audit(c,tx,actor.Id,"EventCreated","experience:"+id+":created",ct);tx.Commit();return id;
    }
    Task Audit(SqliteConnection c,SqliteTransaction tx,string actor,string action,string target,CancellationToken ct)=>Execute(c,tx,"INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($now,$actor,$action,$target)",ct,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$actor",actor),("$action",action),("$target",target));
    public async Task ChangeExperienceAsync(ClaimsPrincipal principal,string id,bool close,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Require(c,tx,presenters,principal,"experience",id,false,ct);
        if(await Execute(c,tx,"UPDATE ExperienceEvents SET Status=$state,EndedAt=$ended WHERE Id=$id AND Status IN ('Active','Closed')",ct,("$state",close?"Closed":"Active"),("$ended",close?clock.GetUtcNow().ToUnixTimeMilliseconds():null),("$id",id))!=1)throw new DemoRequestException("PresenterEventChanged");
        await Audit(c,tx,actor,close?"EventClosed":"EventStarted","experience:"+id+":state",ct);tx.Commit();
    }
    public async Task InviteAsync(ClaimsPrincipal principal,string kind,string id,string target,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Require(c,tx,presenters,principal,kind,id,false,ct);
        if(target==actor || Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Id=$id AND Active=1 AND Pending=0",ct,("$id",target)))!=1)throw new DemoRequestException("PresenterDenied");
        await AddInvitation(c,tx,actor,kind,id,target,clock.GetUtcNow().AddHours(24).ToUnixTimeMilliseconds(),ct);tx.Commit();
    }
    async Task AddInvitation(SqliteConnection c,SqliteTransaction tx,string actor,string kind,string id,string target,long expires,CancellationToken ct,string? enrollmentHash=null)
    {
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventInvites WHERE Kind=$kind AND EventId=$id AND Expires>$now",ct,("$kind",kind),("$id",id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())))>=50)throw new DemoRequestException("PresenterPending");
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventMembers WHERE Kind=$kind AND EventId=$id AND AccountId=$target",ct,("$kind",kind),("$id",id),("$target",target)))!=0)throw new DemoRequestException("PresenterEventChanged");
        await Execute(c,tx,"DELETE FROM PresenterEventInvites WHERE Kind=$kind AND EventId=$id AND TargetId=$target AND Expires<=$now",ct,("$kind",kind),("$id",id),("$target",target),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventInvites WHERE Kind=$kind AND EventId=$id AND TargetId=$target",ct,("$kind",kind),("$id",id),("$target",target)))!=0)throw new DemoRequestException("PresenterEventChanged");
        await Execute(c,tx,"INSERT INTO PresenterEventInvites VALUES($row,$kind,$id,$target,$actor,$expires,$hash)",ct,("$row",Guid.NewGuid().ToString("D")),("$kind",kind),("$id",id),("$target",target),("$actor",actor),("$expires",expires),("$hash",enrollmentHash));
        await Audit(c,tx,actor,"EventInvited",kind+":"+id+":"+target,ct);
    }
    public async Task<string> InviteNewAsync(ClaimsPrincipal principal,string kind,string id,string login,string name,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var (actor,role)=await presenters.Actor(c,tx,principal,true,false,ct);
        if(!await Allowed(c,tx,actor,role,kind,id,false,ct))throw new DemoRequestException("PresenterDenied");
        var invitation=await presenters.CreateEventPendingCore(c,tx,login,name,ct);
        await AddInvitation(c,tx,actor,kind,id,invitation.AccountId,clock.GetUtcNow().AddMinutes(10).ToUnixTimeMilliseconds(),ct,DemoIdentity.Hash(invitation.Code));tx.Commit();return invitation.Code;
    }
    public async Task<IReadOnlyList<EventInvitationRow>> InvitationsAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        using var q=Command(c,tx,"SELECT i.Id,i.Kind,i.EventId,s.Name,i.Expires FROM PresenterEventInvites i JOIN PresenterEventScopes s ON s.Kind=i.Kind AND s.SourceId=i.EventId WHERE TargetId=$id AND Expires>$now",("$id",actor.Id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        using var r=await q.ExecuteReaderAsync(ct);var result=new List<EventInvitationRow>();while(await r.ReadAsync(ct))result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)));return result;
    }
    public async Task AcceptAsync(ClaimsPrincipal principal,string invitation,bool accept,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        string kind,id,issuer;
        using(var q=Command(c,tx,"SELECT Kind,EventId,IssuerId FROM PresenterEventInvites WHERE Id=$row AND TargetId=$actor AND Expires>$now",("$row",invitation),("$actor",actor.Id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())))
        using(var r=await q.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))throw new DemoRequestException("PresenterDenied");kind=r.GetString(0);id=r.GetString(1);issuer=r.GetString(2);}
        if(accept){
            var role=await Scalar(c,tx,"SELECT Role FROM PresenterAccounts WHERE Id=$id AND Active=1",ct,("$id",issuer)) as string;
            if(role is null||!await Allowed(c,tx,issuer,role,kind,id,false,ct))throw new DemoRequestException("PresenterDenied");
            await Execute(c,tx,"INSERT OR IGNORE INTO PresenterEventMembers VALUES($kind,$id,$actor)",ct,("$kind",kind),("$id",id),("$actor",actor.Id));
        }
        await Execute(c,tx,"DELETE FROM PresenterEventInvites WHERE Id=$row",ct,("$row",invitation));await Audit(c,tx,actor.Id,accept?"EventInvitationAccepted":"EventInvitationDeclined",kind+":"+id+":invitation",ct);tx.Commit();
    }
    public async Task RemoveAsync(ClaimsPrincipal principal,string kind,string id,string target,bool invite,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Require(c,tx,presenters,principal,kind,id,true,ct);
        if(Convert.ToString(await Scalar(c,tx,"SELECT OwnerId FROM PresenterEventScopes WHERE Kind=$kind AND SourceId=$id",ct,("$kind",kind),("$id",id)))==target)throw new DemoRequestException("PresenterDenied");
        if(invite){
            await PresenterService.RevokeEventEnrollment(c,tx,kind,id,target,ct);
        }
        await Execute(c,tx,invite?"DELETE FROM PresenterEventInvites WHERE Kind=$kind AND EventId=$id AND TargetId=$target":"DELETE FROM PresenterEventMembers WHERE Kind=$kind AND EventId=$id AND AccountId=$target",ct,("$kind",kind),("$id",id),("$target",target));
        await Execute(c,tx,"DELETE FROM PresenterEventTransfers WHERE Kind=$kind AND EventId=$id AND TargetId=$target",ct,("$kind",kind),("$id",id),("$target",target));
        await Audit(c,tx,actor,"EventPresenterRemoved",kind+":"+id+":"+target,ct);tx.Commit();
    }
    public async Task TransferAsync(ClaimsPrincipal principal,string kind,string id,string target,string action,CancellationToken ct)
    {
        if(kind=="experience"&&id==ExperienceStatistics.UnassignedEvent)throw new DemoRequestException("PresenterDenied");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,false,true,ct);
        var previousOwner=await Scalar(c,tx,"SELECT OwnerId FROM PresenterEventScopes WHERE Kind=$kind AND SourceId=$id",ct,("$kind",kind),("$id",id)) as string;
        if(action=="offer"&&(previousOwner is null||target==previousOwner))throw new DemoRequestException("PresenterEventChanged");
        if(action=="recover") {if(actor.Role!="Administrator")throw new DemoRequestException("PresenterDenied");}
        else if(action=="offer") {await Require(c,tx,presenters,principal,kind,id,true,ct);}
        else if(action=="accept") {
            if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventTransfers t JOIN PresenterEventScopes s ON s.Kind=t.Kind AND s.SourceId=t.EventId WHERE t.Kind=$kind AND t.EventId=$id AND t.TargetId=$actor AND t.OwnerId=s.OwnerId AND t.Expires>$now",ct,("$kind",kind),("$id",id),("$actor",actor.Id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())))!=1)throw new DemoRequestException("PresenterDenied");target=actor.Id;
        } else throw new DemoRequestException("PresenterDenied");
        if(!Valid(kind,id)||Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Id=$target AND Active=1 AND EXISTS(SELECT 1 FROM PresenterKeys WHERE AccountId=$target)",ct,("$target",target)))!=1)throw new DemoRequestException("PresenterDenied");
        if(action=="offer") {
            if(!await Allowed(c,tx,target,"Presenter",kind,id,false,ct))throw new DemoRequestException("PresenterDenied");
            await Execute(c,tx,"INSERT INTO PresenterEventTransfers SELECT Kind,SourceId,OwnerId,$target,$expires FROM PresenterEventScopes WHERE Kind=$kind AND SourceId=$id ON CONFLICT(Kind,EventId) DO UPDATE SET OwnerId=excluded.OwnerId,TargetId=excluded.TargetId,Expires=excluded.Expires",ct,("$target",target),("$expires",clock.GetUtcNow().AddHours(24).ToUnixTimeMilliseconds()),("$kind",kind),("$id",id));
        } else {
            await PresenterService.RevokeEventEnrollment(c,tx,kind,id,null,ct);
            if(await Execute(c,tx,"UPDATE PresenterEventScopes SET OwnerId=$target WHERE Kind=$kind AND SourceId=$id",ct,("$target",target),("$kind",kind),("$id",id))!=1)throw new DemoRequestException("PresenterDenied");
            await Execute(c,tx,"DELETE FROM PresenterEventTransfers WHERE Kind=$kind AND EventId=$id; DELETE FROM PresenterEventInvites WHERE Kind=$kind AND EventId=$id",ct,("$kind",kind),("$id",id));
        }
        await Audit(c,tx,actor.Id,"EventOwnership"+action,kind+":"+id+":"+(previousOwner??"unassigned")+":"+target,ct);tx.Commit();
    }
}
