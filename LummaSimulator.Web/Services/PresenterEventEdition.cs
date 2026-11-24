using System.Security.Claims;

namespace LummaSimulator.Web.Services;

public sealed partial class PresenterService
{
    public async Task<(string Id, string SessionHash)> EventOperatorAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        var actor = await Actor(c, tx, principal, false, false, ct);
        return (actor.Id, Models.DemoIdentity.Hash(principal.FindFirstValue("sid")!));
    }
    public async Task<bool> EventOperatorValidAsync(string id, string sessionHash, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        return Convert.ToInt64(await Scalar(c, tx, """
            SELECT COUNT(*) FROM PresenterAuthSessions s JOIN PresenterAccounts a ON a.Id=s.AccountId
            WHERE s.Hash=$hash AND a.Id=$id AND a.Active=1 AND a.Role IN ('Presenter','Administrator') AND s.Expires>$now
            """, ct, ("$hash", sessionHash), ("$id", id), ("$now", Now))) == 1;
    }
}
