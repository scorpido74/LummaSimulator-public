using System.Security.Claims;
namespace LummaSimulator.Web.Services;
public sealed record EventActivity(long At,string ActorName,string Action);
public sealed partial class EventAccess
{
    public async Task<IReadOnlyList<EventActivity>> ActivitiesAsync(ClaimsPrincipal principal,string kind,string id,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Require(c,tx,presenters,principal,kind,id,false,ct);
        using var q=Command(c,tx,"""
            SELECT l.At,COALESCE(a.Name,''),l.Action FROM PresenterAudit l LEFT JOIN PresenterAccounts a ON a.Id=l.Actor
            WHERE ($kind='experience' AND l.Target=$id AND l.Action IN ('EventStarted','EventClosed','EventRenamed')) OR l.Target LIKE $prefix OR EXISTS(SELECT 1 FROM PresenterDemoContexts d JOIN Demos v ON v.Id=d.DemoId WHERE d.Kind=$kind AND d.EventId=$id AND (d.DemoId=l.Target OR v.DemoRunId=l.Target))
            ORDER BY l.Id DESC LIMIT 25
            """,("$id",id),("$kind",kind),("$prefix",kind+":"+id+":%"));
        using var r=await q.ExecuteReaderAsync(ct);var rows=new List<EventActivity>();while(await r.ReadAsync(ct))rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2)));return rows;
    }
    public async Task<IReadOnlyList<EventInvitationRow>> TransfersAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        using var q=Command(c,tx,"SELECT t.EventId,t.Kind,t.EventId,s.Name,t.Expires FROM PresenterEventTransfers t JOIN PresenterEventScopes s ON s.Kind=t.Kind AND s.SourceId=t.EventId AND s.OwnerId=t.OwnerId WHERE t.TargetId=$id AND t.Expires>$now",("$id",actor.Id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        using var r=await q.ExecuteReaderAsync(ct);var rows=new List<EventInvitationRow>();while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)));return rows;
    }
}
