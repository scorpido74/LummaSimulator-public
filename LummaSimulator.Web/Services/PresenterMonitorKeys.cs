using System.Security.Claims;

namespace LummaSimulator.Web.Services;
public sealed partial class PresenterService
{
    public async Task<string> MonitorAdministratorAsync(ClaimsPrincipal principal, bool fresh, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var actor = await Actor(c, tx, principal, true, fresh, ct); tx.Commit(); return actor.Id;
    }
    internal async Task AppendMonitorAuditAsync(MonitorKeyAudit item, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        if (Convert.ToInt64(await Scalar(c, tx, "SELECT COUNT(*) FROM PresenterAudit WHERE Action=$action AND Target=$target", ct, ("$action", item.Action), ("$target", item.OperationId))) == 0)
            await Audit(c, tx, item.At.ToUnixTimeMilliseconds(), item.ActorId, item.Action, item.OperationId, ct);
        tx.Commit();
    }
}
