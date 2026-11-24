using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Services;
public sealed partial class PresenterService
{
    public async Task<string> BeginLocalSetupAsync(string login,string name,CancellationToken ct)
    {
        ValidateAccount(login,name,"Administrator");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterInstallation WHERE Id=1 AND Existing=0 AND CompletedAt IS NULL",ct))!=1
            ||Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Activated IS NOT NULL",ct))!=0
            ||Convert.ToInt64(await Scalar(c,tx,"SELECT (SELECT COUNT(*) FROM PresenterInvitations WHERE Bootstrap=1 AND Expires>$now)+(SELECT COUNT(*) FROM PresenterEnrollmentGrants WHERE Bootstrap=1 AND Expires>$now)",ct,("$now",Now)))!=0)throw Denied();
        // Resume only an unactivated, keyless pending attempt. Established identities never qualify.
        var pending=await Scalar(c,tx,"SELECT Id FROM PresenterAccounts a WHERE Login=$login AND Activated IS NULL AND Active=0 AND Role='Administrator' AND NOT EXISTS(SELECT 1 FROM PresenterKeys k WHERE k.AccountId=a.Id)",ct,("$login",login)) as string;
        PresenterInvitation invitation;
        if(pending is null)invitation=await CreatePending(c,tx,login,name,"Administrator",true,ct);
        else{
            var code=DemoIdentity.RandomToken();
            await Exec(c,tx,"UPDATE PresenterAccounts SET Pending=1 WHERE Id=$id; DELETE FROM PresenterInvitations WHERE AccountId=$id; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=$id; DELETE FROM PresenterCeremonies WHERE AccountId=$id; INSERT INTO PresenterInvitations VALUES($hash,$id,$expires,1)",ct,("$id",pending),("$hash",DemoIdentity.Hash(code)),("$expires",Now+600000));
            invitation=new(pending,code);
        }
        await Audit(c,tx,Now,null,"LocalWizardBootstrapIssued",invitation.AccountId,ct);tx.Commit();return invitation.Code;
    }
    internal Task<PresenterInvitation> CreateEventPendingCore(SqliteConnection c,SqliteTransaction tx,string login,string name,CancellationToken ct)
    {ValidateAccount(login,name,"Presenter");return CreatePending(c,tx,login,name,"Presenter",false,ct);}
}
