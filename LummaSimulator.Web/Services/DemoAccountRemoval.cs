using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Services;

internal static class DemoAccountRemoval
{
    // Caller holds DemoStore.Gate. Keep the existing audit, but detach only anonymous round history.
    internal static async Task DeleteAsync(DemoStore store,SecurityAuditService audit,DemoAccount account,bool logout,SessionDemoState? sessionDemos,CancellationToken ct)
    {
        await using(var connection=await store.OpenAsync(ct))
        {
            using var transaction=connection.BeginTransaction(deferred:false);
            await PrizeRoundHistoryMigration.DetachAccountAsync(connection,transaction,account.Id,ct);
            await audit.RevokeWithinAsync(connection,transaction,account,null,logout,ct);
            // Existing cascades remove server-side credentials, challenges, monitor grants and visitor context.
            await EventAccess.Execute(connection,transaction,"DELETE FROM Demos WHERE Id=$id",ct,("$id",account.Id));
            transaction.Commit();
        }
        // Database ownership is already gone, so even an in-flight viewer read fails closed.
        if(sessionDemos is not null)await sessionDemos.RevokeAccountAsync(account.Id,CancellationToken.None);
    }
}
