using System.Security.Claims;
namespace LummaSimulator.Web.Services;
public sealed record EventPerson(string Id,string Name,bool Owner,bool Member,bool Invited);
public sealed partial class EventAccess
{
    public async Task<IReadOnlyList<EventPerson>> PeopleAsync(ClaimsPrincipal principal,string kind,string id,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Require(c,tx,presenters,principal,kind,id,false,ct);
        var actor=await presenters.Actor(c,tx,principal,false,false,ct);var manage=await Allowed(c,tx,actor.Id,actor.Role,kind,id,true,ct);
        using var q=Command(c,tx,"""
            SELECT a.Id,a.Name,s.OwnerId=a.Id,
              EXISTS(SELECT 1 FROM PresenterEventMembers m WHERE m.Kind=$kind AND m.EventId=$id AND m.AccountId=a.Id),
              EXISTS(SELECT 1 FROM PresenterEventInvites i WHERE i.Kind=$kind AND i.EventId=$id AND i.TargetId=a.Id AND i.Expires>$now)
            FROM PresenterAccounts a JOIN PresenterEventScopes s ON s.Kind=$kind AND s.SourceId=$id
                        WHERE a.Active=1 OR ($manage=1 AND EXISTS(SELECT 1 FROM PresenterEventInvites i WHERE i.Kind=$kind AND i.EventId=$id AND i.TargetId=a.Id)) ORDER BY a.Name,a.Id
            """,("$kind",kind),("$id",id),("$manage",manage),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        using var r=await q.ExecuteReaderAsync(ct);var people=new List<EventPerson>();while(await r.ReadAsync(ct))people.Add(new(r.GetString(0),r.GetString(1),!r.IsDBNull(2)&&r.GetBoolean(2),r.GetBoolean(3),r.GetBoolean(4)));return people;
    }
}
