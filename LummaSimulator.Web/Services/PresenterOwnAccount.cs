using System.Security.Claims;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;
public sealed partial class PresenterService
{
    public async Task<IReadOnlyList<(string Id,long Expires,bool Current)>> OwnSessionsAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,false,false,ct);
        var current=DemoIdentity.Hash(principal.FindFirstValue("sid")!);var hashes=new List<(string Hash,long Expires)>();
        using(var q=Cmd(c,tx,"SELECT Hash,Expires FROM PresenterAuthSessions WHERE AccountId=$id AND Expires>$now ORDER BY Expires",("$id",actor.Id),("$now",Now)))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))hashes.Add((r.GetString(0),r.GetInt64(1)));
        var result=new List<(string,long,bool)>();foreach(var item in hashes){
            await Exec(c,tx,"INSERT OR IGNORE INTO PresenterSessionRefs VALUES($hash,$ref)",ct,("$hash",item.Hash),("$ref",Guid.NewGuid().ToString("D")));
            var reference=(string)(await Scalar(c,tx,"SELECT Ref FROM PresenterSessionRefs WHERE Hash=$hash",ct,("$hash",item.Hash)))!;
            result.Add((reference,item.Expires,item.Hash==current));
        }
        tx.Commit();return result;
    }
    public async Task<bool> RevokeOwnSessionAsync(ClaimsPrincipal principal,string reference,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,false,false,ct);
        var hash=await Scalar(c,tx,"SELECT s.Hash FROM PresenterSessionRefs r JOIN PresenterAuthSessions s ON s.Hash=r.Hash WHERE r.Ref=$ref AND s.AccountId=$id",ct,("$ref",reference),("$id",actor.Id)) as string??throw Denied();
        await Exec(c,tx,"DELETE FROM PresenterAuthSessions WHERE Hash=$hash",ct,("$hash",hash));await Audit(c,tx,Now,actor.Id,"OwnSessionRevoked",reference,ct);tx.Commit();return hash==DemoIdentity.Hash(principal.FindFirstValue("sid")!);
    }
    public async Task RemoveOwnKeyAsync(ClaimsPrincipal principal, string key, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var actor = await Actor(c, tx, principal, false, true, ct);
        if (Convert.ToInt64(await Scalar(c, tx, "SELECT COUNT(*) FROM PresenterKeys WHERE AccountId=$id", ct, ("$id", actor.Id))) <= 1)
            throw new DemoRequestException("PresenterLastKey");
        if (await Exec(c, tx, "DELETE FROM PresenterKeys WHERE AccountId=$id AND Ref=$key", ct, ("$id", actor.Id), ("$key", key)) != 1) throw Denied();
        // Credential removal revokes every own session; no session/credential of another account is touched.
        await Exec(c, tx, "DELETE FROM PresenterAuthSessions WHERE AccountId=$id", ct, ("$id", actor.Id));
        await Audit(c, tx, Now, actor.Id, "OwnKeyRemoved", key, ct); tx.Commit();
    }
}
