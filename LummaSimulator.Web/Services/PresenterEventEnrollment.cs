using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed partial class PresenterService
{
    // Called in the same immediate transaction as redemption/options/credential insertion.
    // Generic administrator/bootstrap invitations have no event provenance and retain their flow.
    internal async Task<bool> EventEnrollmentValid(SqliteConnection c,SqliteTransaction tx,string account,CancellationToken ct)
    {
        var invites=new List<(string Kind,string Event,string Issuer,long Expires)>();
        using(var q=Cmd(c,tx,"SELECT Kind,EventId,IssuerId,Expires FROM PresenterEventInvites WHERE TargetId=$account AND EnrollmentHash IS NOT NULL",("$account",account)))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))invites.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3)));
        foreach(var invite in invites){
            if(invite.Expires<=Now)return false;
            var role=await Scalar(c,tx,"SELECT Role FROM PresenterAccounts WHERE Id=$id AND Active=1",ct,("$id",invite.Issuer)) as string;
            if(role!="Administrator"||!await EventAccess.Allowed(c,tx,invite.Issuer,role,invite.Kind,invite.Event,false,ct))return false;
        }
        return true;
    }

    internal static async Task RevokeEventEnrollment(SqliteConnection c,SqliteTransaction tx,string kind,string id,string? target,CancellationToken ct)
    {
        var accounts=new List<string>();
        using(var q=Cmd(c,tx,"""
            SELECT DISTINCT a.Id FROM PresenterEventInvites i JOIN PresenterAccounts a ON a.Id=i.TargetId
            WHERE i.Kind=$kind AND i.EventId=$id AND i.EnrollmentHash IS NOT NULL
              AND ($target IS NULL OR i.TargetId=$target) AND a.Pending=1 AND a.Active=0 AND a.Activated IS NULL
            """,("$kind",kind),("$id",id),("$target",target)))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))accounts.Add(r.GetString(0));
        // Legacy redeemed grants only retain AccountId, not their original invitation hash.
        // Revoke every unused enrollment capability for these never-activated event accounts,
        // including a pending reissue. Never touch an activated account, key or auth session.
        foreach(var account in accounts)await Exec(c,tx,"DELETE FROM PresenterInvitations WHERE AccountId=$account; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=$account; DELETE FROM PresenterCeremonies WHERE AccountId=$account AND Kind='enroll'",ct,("$account",account));
    }
}
