using System.Security.Claims;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed partial class PresenterService
{
    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await Exec(c,tx,"DELETE FROM PresenterAuthSessions WHERE Expires<=$now; DELETE FROM PresenterInvitations WHERE Expires<=$now; DELETE FROM PresenterEnrollmentGrants WHERE Expires<=$now; DELETE FROM PresenterCeremonies WHERE Expires<=$now",ct,("$now",Now));tx.Commit();
    }
    public async Task<string> InviteAsync(ClaimsPrincipal principal,string target,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,true,ct);
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Id=$id AND Activated IS NULL AND Active=0",ct,("$id",target)))!=1)throw Denied();
        var code=DemoIdentity.RandomToken();
        await Exec(c,tx,"UPDATE PresenterAccounts SET Pending=1 WHERE Id=$id AND Active=0; DELETE FROM PresenterInvitations WHERE AccountId=$id; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=$id; DELETE FROM PresenterCeremonies WHERE AccountId=$id AND Kind='enroll'; INSERT INTO PresenterInvitations VALUES($hash,$id,$expires,0)",ct,("$id",target),("$hash",DemoIdentity.Hash(code)),("$expires",Now+600000));
        await Audit(c,tx,Now,actor.Id,"EnrollmentInvited",target,ct);tx.Commit();return code;
    }
    public async Task EventAsync(ClaimsPrincipal principal,string expected,string name,bool close,bool confirmed,CancellationToken ct)
    {
        if(!confirmed)throw new DemoRequestException("PresenterConfirm");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,false,ct);
        var target=expected;
        if(close) await ExperienceStatistics.CloseEventCoreAsync(c,tx,expected,Now,ct);
        else target=await ExperienceStatistics.StartEventCoreAsync(c,tx,expected,name,Now,ct);
        if(!close)await EventAccess.Register(c,tx,"experience",target,name,actor.Id,ct);
        await Audit(c,tx,Now,actor.Id,close?"EventClosed":"EventStarted",target,ct);tx.Commit();
    }
    public async Task RenameEventAsync(ClaimsPrincipal principal,string id,string oldName,string name,bool confirmed,CancellationToken ct)
    {
        if(!confirmed || string.IsNullOrWhiteSpace(name) || name.Length>100)throw new DemoRequestException("PresenterConfirm");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,false,ct);
        if(await Exec(c,tx,"UPDATE ExperienceEvents SET Name=$name WHERE Id=$id AND Name=$old AND Status='Active'",ct,("$id",id),("$name",name.Trim()),("$old",oldName))!=1)throw new DemoRequestException("PresenterEventChanged");
        await Audit(c,tx,Now,actor.Id,"EventRenamed",id,ct);tx.Commit();
    }
    public async Task RecordDemoActionAsync(ClaimsPrincipal principal,string action,string target,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,false,false,ct);
        if(action is not ("DemoEndRequested" or "MonitorGrantChangeRequested"))throw Denied();
        var reference=await Scalar(c,tx,"SELECT DemoRunId FROM Demos WHERE Id=$id",ct,("$id",target)) as string ?? throw Denied();
        string? kind=null,eventId=null;
        using(var q=Cmd(c,tx,"SELECT Kind,EventId FROM PresenterDemoContexts WHERE DemoId=$id",("$id",target)))
        using(var r=await q.ExecuteReaderAsync(ct)){if(await r.ReadAsync(ct)){kind=r.GetString(0);eventId=r.GetString(1);}}
        if(kind is null?actor.Role!="Administrator":!await EventAccess.Allowed(c,tx,actor.Id,actor.Role,kind,eventId!,false,ct))throw Denied();
        await Audit(c,tx,Now,actor.Id,action,reference,ct);tx.Commit();
    }
}
