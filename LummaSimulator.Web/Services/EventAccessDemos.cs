using System.Security.Claims;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Services;
public sealed partial class EventAccess
{
    internal const string VisitorOwnerSql="SELECT COUNT(*) FROM PresenterEventScopes s JOIN PresenterAccounts a ON a.Id=s.OwnerId AND a.Active=1 WHERE Kind=$kind AND SourceId=$id";
    public async Task<bool> CanAcceptVisitorAsync(string kind,string id,CancellationToken ct)
        => Valid(kind,id) && !(kind=="experience"&&id==ExperienceStatistics.UnassignedEvent)
            && await LiveAsync(kind,id,ct)
            && Convert.ToInt64(await store.ScalarAsync(VisitorOwnerSql,ct,("$kind",kind),("$id",id)))==1;

    internal async Task<(string Kind,string Id)?> SingleDiscoverableEventAsync(CancellationToken ct)
    {
        var ids=new List<(string Kind,string Id)>();
        await using(var connection=await store.OpenAsync(ct))
        {
            using var query=connection.CreateCommand();
            // Prize invitations are link-scoped. The current public Prize policy also requires a start code.
            query.CommandText="""
                SELECT s.Kind,s.SourceId FROM PresenterEventScopes s JOIN PresenterAccounts a ON a.Id=s.OwnerId AND a.Active=1
                LEFT JOIN EventLoginRoutes r ON r.Kind=s.Kind AND r.EventId=s.SourceId
                WHERE s.Kind IN ('experience','edition') AND COALESCE(r.Mask,15)<>0
                """;
            using var rows=await query.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))ids.Add((rows.GetString(0),rows.GetString(1)));
        }
        (string Kind,string Id)? selected=null;
        foreach(var item in ids)
        {
            if(!await CanAcceptVisitorAsync(item.Kind,item.Id,ct))continue;
            if(selected is not null)return null;
            selected=item;
        }
        return selected;
    }

    internal async Task<bool> MatchesVisitorEventAsync(string token,string kind,string id,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(token)||!Valid(kind,id))return false;
        return Convert.ToInt64(await store.ScalarAsync("""
            SELECT COUNT(*) FROM Demos d JOIN PresenterDemoContexts x ON x.DemoId=d.Id
            WHERE d.SessionHash=$hash AND d.ClosedAt IS NULL AND x.Kind=$kind AND x.EventId=$event
            """,ct,("$hash",DemoIdentity.Hash(token)),("$kind",kind),("$event",id)))==1;
    }
    public async Task<HashSet<string>> GrantedDemoIdsAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        using var q=Command(c,tx,"SELECT DISTINCT DemoId FROM PresenterMonitorCapabilities WHERE ActorId=$actor AND SessionHash=$session AND Expires>$now",("$actor",actor.Id),("$session",DemoIdentity.Hash(principal.FindFirstValue("sid")!)),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        using var r=await q.ExecuteReaderAsync(ct);var result=new HashSet<string>();while(await r.ReadAsync(ct))result.Add(r.GetString(0));return result;
    }
    public async Task BindVisitorAsync(string token,string kind,string id,CancellationToken ct)
    {
        if(!Valid(kind,id))throw new DemoRequestException("PresenterDenied");
        if(!await LiveAsync(kind,id,ct))throw new DemoRequestException("PresenterDenied");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        if(Convert.ToInt64(await Scalar(c,tx,VisitorOwnerSql,ct,("$kind",kind),("$id",id)))!=1)throw new DemoRequestException("PresenterDenied");
        var demo=await Scalar(c,tx,"SELECT Id FROM Demos WHERE SessionHash=$hash AND ClosedAt IS NULL AND Successes=0 AND NOT EXISTS(SELECT 1 FROM Credentials WHERE DemoId=Demos.Id)",ct,("$hash",DemoIdentity.Hash(token))) as string??throw new DemoRequestException("PresenterDenied");
        var route = await Scalar(c,tx,"SELECT Route FROM Demos WHERE Id=$id",ct,("$id",demo)) as string ?? "";
        var mask = Convert.ToInt32(await Scalar(c,tx,"SELECT COALESCE((SELECT Mask FROM EventLoginRoutes WHERE Kind=$kind AND EventId=$id),15)",ct,("$kind",kind),("$id",id)));
        if (!new LoginRoutes(mask).Allows(route)) throw new DemoRequestException("LoginRouteDisabled");
        await Execute(c,tx,"INSERT INTO PresenterDemoContexts VALUES($demo,$kind,$id) ON CONFLICT(DemoId) DO UPDATE SET Kind=excluded.Kind,EventId=excluded.EventId",ct,("$demo",demo),("$kind",kind),("$id",id));tx.Commit();
    }
    public async Task RequireDemoAsync(ClaimsPrincipal principal,string demo,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        using var q=Command(c,tx,"SELECT Kind,EventId FROM PresenterDemoContexts WHERE DemoId=$demo",("$demo",demo));
        string? kind=null,id=null;using(var r=await q.ExecuteReaderAsync(ct)){if(await r.ReadAsync(ct)){kind=r.GetString(0);id=r.GetString(1);}}
        if(kind is null ? actor.Role!="Administrator" : !await Allowed(c,tx,actor.Id,actor.Role,kind,id!,false,ct))throw new DemoRequestException("PresenterDenied");
    }
    public async Task<string?> SetMonitorAsync(ClaimsPrincipal principal,string demo,bool enabled,CancellationToken ct)
    {
        await RequireDemoAsync(principal,demo,ct);
        if(enabled){
            string contextKind,contextId;
            await using(var read=await store.OpenAsync(ct)){using var q=read.CreateCommand();q.CommandText="SELECT Kind,EventId FROM PresenterDemoContexts WHERE DemoId=$id";q.Parameters.AddWithValue("$id",demo);using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new DemoRequestException("PresenterDenied");contextKind=r.GetString(0);contextId=r.GetString(1);}
            if(!await LiveAsync(contextKind,contextId,ct))throw new DemoRequestException("PresenterDenied");
        }
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        string kind,id;
        using(var q=Command(c,tx,"SELECT Kind,EventId FROM PresenterDemoContexts WHERE DemoId=$demo",("$demo",demo)))
        using(var r=await q.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))throw new DemoRequestException("PresenterDenied");kind=r.GetString(0);id=r.GetString(1);}
        if(!await Allowed(c,tx,actor.Id,actor.Role,kind,id,false,ct))throw new DemoRequestException("PresenterDenied");
        var session=DemoIdentity.Hash(principal.FindFirstValue("sid")!);var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        await Execute(c,tx,"DELETE FROM PresenterMonitorCapabilities WHERE DemoId=$demo AND ActorId=$actor AND SessionHash=$session",ct,("$demo",demo),("$actor",actor.Id),("$session",session));
        string? code=null;
        if(enabled) {
            code=DemoIdentity.RandomToken();var expires=clock.GetUtcNow().AddMinutes(29).ToUnixTimeMilliseconds();
            await Execute(c,tx,"INSERT INTO PresenterMonitorCapabilities VALUES($hash,$demo,$kind,$event,$actor,$session,$expires); INSERT INTO MonitorGrants(DemoId,Expires) VALUES($demo,$expires) ON CONFLICT(DemoId) DO UPDATE SET Expires=MAX(Expires,excluded.Expires)",ct,("$hash",DemoIdentity.Hash(code)),("$demo",demo),("$kind",kind),("$event",id),("$actor",actor.Id),("$session",session),("$expires",expires));
        } else await Execute(c,tx,"DELETE FROM MonitorGrants WHERE DemoId=$demo AND NOT EXISTS(SELECT 1 FROM PresenterMonitorCapabilities WHERE DemoId=$demo AND Expires>$now)",ct,("$demo",demo),("$now",now));
        await Audit(c,tx,actor.Id,enabled?"ScopedMonitorGranted":"ScopedMonitorRevoked",demo,ct);tx.Commit();return code;
    }
    public async Task<bool> VerifyMonitorAsync(string run,string? code,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(code))return false;
        string actor,role,kind,id;
        bool allowed;
        await using(var c=await store.OpenAsync(ct)){using var tx=c.BeginTransaction(deferred:true);
        using(var q=Command(c,tx,"""
            SELECT p.ActorId,a.Role,p.Kind,p.EventId FROM PresenterMonitorCapabilities p
            JOIN Demos d ON d.Id=p.DemoId AND d.DemoRunId=$run AND d.ClosedAt IS NULL
            JOIN PresenterDemoContexts x ON x.DemoId=d.Id AND x.Kind=p.Kind AND x.EventId=p.EventId
            JOIN PresenterAccounts a ON a.Id=p.ActorId AND a.Active=1
            JOIN PresenterAuthSessions s ON s.Hash=p.SessionHash AND s.AccountId=a.Id AND s.Expires>$now
            WHERE p.Hash=$hash AND p.Expires>$now
            """,("$run",run),("$hash",DemoIdentity.Hash(code!)),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())))
        using(var r=await q.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))return false;actor=r.GetString(0);role=r.GetString(1);kind=r.GetString(2);id=r.GetString(3);}
        allowed=await Allowed(c,tx,actor,role,kind,id,false,ct);
        }
        return allowed&&await LiveAsync(kind,id,ct);
    }
    public async Task<IReadOnlyList<DemoAccount>> DemosAsync(ClaimsPrincipal principal,string kind,string id,CancellationToken ct)
    {
        await RequireAsync(principal,kind,id,false,ct);var ids=new HashSet<string>();
        await using(var c=await store.OpenAsync(ct)){using var q=c.CreateCommand();q.CommandText="SELECT DemoId FROM PresenterDemoContexts WHERE Kind=$kind AND EventId=$id";q.Parameters.AddWithValue("$kind",kind);q.Parameters.AddWithValue("$id",id);using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetString(0));}
        return (await store.AccountsAsync(null,ct)).Where(d=>ids.Contains(d.Id)).ToList();
    }
}
