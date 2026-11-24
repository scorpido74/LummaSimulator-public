using System.Security.Claims;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

// Synthetic databases and software WebAuthn signatures only; never operator runtime data.
internal static class EventAccessChecks
{
    internal static async Task RunAsync(string root)
    {
        await MigrationAsync(root);
        using var db=new DemoStore(Path.Combine(root,"event-access.db"));await db.InitializeAsync();
        var clock=new TestClock();var accounts=PresenterChecks.Service(db,clock);var access=new EventAccess(db,accounts,clock);
        using var adminKey=new SoftwareAuthenticator();using var aKey=new SoftwareAuthenticator();using var bKey=new SoftwareAuthenticator();using var cKey=new SoftwareAuthenticator();
        var bootstrap=await accounts.IssueBootstrapAsync("scope-admin","Synthetic Admin",default);
        await PresenterChecks.Enroll(accounts,bootstrap.Code,adminKey);var admin=await PresenterChecks.Login(accounts,"scope-admin",adminKey);
        async Task<ClaimsPrincipal> Person(string login,SoftwareAuthenticator key){
            await PresenterChecks.Reauth(accounts,admin,adminKey);var invite=await accounts.CreateAccountAsync(admin,login,login,"Presenter",default);
            await PresenterChecks.Enroll(accounts,invite.Code,key);return await PresenterChecks.Login(accounts,login,key);
        }
        var a=await Person("scope-a",aKey);var b=await Person("scope-b",bKey);var c=await Person("scope-c",cKey);
        var aid=a.FindFirstValue(ClaimTypes.NameIdentifier)!;var bid=b.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var cid=c.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ea=await access.CreateExperienceAsync(a,"Only A",default);var eb=await access.CreateExperienceAsync(b,"Only B",default);
        var shared=await access.CreateExperienceAsync(a,"Shared",default);
        TestProgram.Check(await access.CanAsync(admin,"experience",ea,true,default)&&await access.CanAsync(a,"experience",ea,true,default),"6.5 administrator and creator own the intended event");
        TestProgram.Check(!await access.CanAsync(a,"experience",eb,false,default)&&!await access.CanAsync(b,"experience",ea,true,default),"6.5 two independent identities cannot cross event boundaries");
        TestProgram.Check((await access.ListAsync(a,default)).Select(x=>x.Id).Order().SequenceEqual(new[]{ea,shared}.Order()),"6.5 dashboard list contains only authorized events");
        var old=(await access.ListAsync(admin,default)).First(x=>x.Owner is null&&x.State=="Active");
        TestProgram.Check(!await access.CanAsync(a,old.Kind,old.Id,false,default),"6.5 legacy unknown owner is administrator-only, without inferred ownership");
        await TestProgram.RejectAsync(()=>access.InviteAsync(b,"experience",shared,aid,default),"6.5 unrelated presenter cannot invite himself through a direct service call");
        await access.InviteAsync(a,"experience",shared,bid,default);
        TestProgram.Check(!await access.CanAsync(b,"experience",shared,false,default),"6.5 pending invitation grants no event access");
        var pending=(await access.InvitationsAsync(b,default)).Single();
        await TestProgram.RejectAsync(()=>access.AcceptAsync(a,pending.Id,true,default),"6.5 invitation requires the intended authenticated identity");
        await access.AcceptAsync(b,pending.Id,true,default);
        await TestProgram.RejectAsync(()=>access.AcceptAsync(b,pending.Id,true,default),"6.5 accepted invitation is one-time");
        TestProgram.Check(await access.CanAsync(b,"experience",shared,false,default)&&!await access.CanAsync(b,"experience",shared,true,default),"6.5 accepted presenter may present but cannot manage membership");
        await TestProgram.RejectAsync(()=>access.InviteAsync(c,"experience",shared,bid,default),"Event invite requires current event access");
        await access.InviteAsync(b,"experience",shared,cid,default);
        TestProgram.Check(!await access.CanAsync(c,"experience",shared,false,default),"Member invitation grants no access before acceptance");
        await access.AcceptAsync(c,(await access.InvitationsAsync(c,default)).Single().Id,true,default);
        TestProgram.Check(await access.CanAsync(c,"experience",shared,false,default),"Existing event member can invite an existing presenter");
        await TestProgram.RejectAsync(()=>access.InviteNewAsync(b,"experience",shared,"not-allowed","Denied",default),"6.5 linked presenter cannot create privileged invitations");
        await TestProgram.RejectAsync(()=>access.InviteNewAsync(a,"experience",shared,"owner-new","Denied",default),"Only administrator may invite a new presenter account");
        await TestProgram.RejectAsync(()=>access.InviteNewAsync(admin,"experience",shared,"scope-b","Existing",default),"Event invitation cannot replace an existing presenter account");
        var enrollment=await access.InviteNewAsync(admin,"experience",shared,"new-scoped","New scoped",default);
        TestProgram.Check(Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM PresenterEventInvites WHERE EnrollmentHash=$hash",default,("$hash",DemoIdentity.Hash(enrollment))))==1,"6.5 invitation stores only the enrollment token hash");
        using var newKey=new SoftwareAuthenticator();await PresenterChecks.Enroll(accounts,enrollment,newKey,false);
        var newcomer=await PresenterChecks.Login(accounts,"new-scoped",newKey);
        TestProgram.Check(!newcomer.IsInRole("Administrator")&&!await access.CanAsync(newcomer,"experience",shared,false,default),"6.5 registered invited account still requires explicit event acceptance and has no admin role");
        await access.AcceptAsync(newcomer,(await access.InvitationsAsync(newcomer,default)).Single().Id,true,default);
        TestProgram.Check(await access.CanAsync(newcomer,"experience",shared,false,default)&&!await access.CanAsync(newcomer,"experience",ea,false,default),"6.5 new account receives precisely the accepted event");
        var canceled=await access.InviteNewAsync(admin,"experience",ea,"revoked-new","Revoked",default);
        var canceledId=(string)(await db.ScalarAsync("SELECT Id FROM PresenterAccounts WHERE Login='revoked-new'",default))!;
        await access.RemoveAsync(a,"experience",ea,canceledId,true,default);
        await TestProgram.RejectAsync(()=>accounts.OpenEnrollmentAsync(canceled,true,default),"6.5 revoked invitation also revokes unopened enrollment capability");
        var demos=new DemoService(db,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance);
        var visitor=await demos.CreateAsync("laptop",default);await access.BindVisitorAsync(visitor.Token,"experience",shared,default);
        var privateVisitor=await demos.CreateAsync("laptop",default);await access.BindVisitorAsync(privateVisitor.Token,"experience",eb,default);
        await TestProgram.RejectAsync(()=>access.RequireDemoAsync(a,privateVisitor.Account.Id,default),"6.5 altered demo identifier cannot cross events");
        TestProgram.Check((await access.DemosAsync(a,"experience",shared,default)).All(x=>x.Id!=privateVisitor.Account.Id),"6.5 demo report excludes another event");
        TestProgram.Check(!await access.VerifyMonitorAsync(visitor.Account.DemoRunId,null,default),"6.5 integration credential alone never grants metadata access");
        var capA=await access.SetMonitorAsync(a,visitor.Account.Id,true,default);var capB=await access.SetMonitorAsync(b,visitor.Account.Id,true,default);
        TestProgram.Check(await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capA,default)&&await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capB,default),"6.5 shared event has independent presenter-session monitor capabilities");
        TestProgram.Check(!await access.VerifyMonitorAsync(privateVisitor.Account.DemoRunId,capA,default),"6.5 grant cannot be replayed for another portal run");
        await access.SetMonitorAsync(a,visitor.Account.Id,false,default);
        TestProgram.Check(!await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capA,default)&&await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capB,default),"6.5 revoking A's grant preserves B's independent context");
        await access.RemoveAsync(a,"experience",shared,bid,false,default);
        TestProgram.Check(!await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capB,default),"6.5 membership removal immediately invalidates an existing grant");
        await access.InviteAsync(a,"experience",shared,bid,default);await access.AcceptAsync(b,(await access.InvitationsAsync(b,default)).Single().Id,true,default);
        await TestProgram.RejectAsync(()=>access.TransferAsync(a,"experience",shared,bid,"offer",default),"6.5 ownership transfer requires fresh WebAuthn proof");
        await PresenterChecks.Reauth(accounts,a,aKey);await access.TransferAsync(a,"experience",shared,bid,"offer",default);
        TestProgram.Check(!await access.CanAsync(b,"experience",shared,true,default),"6.5 ownership offer does not transfer authority prematurely");
        await PresenterChecks.Reauth(accounts,b,bKey);await access.TransferAsync(b,"experience",shared,"","accept",default);
        TestProgram.Check(await access.CanAsync(b,"experience",shared,true,default)&&!await access.CanAsync(a,"experience",shared,true,default),"6.5 accepted transfer changes primary owner atomically");
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        await TestProgram.RejectAsync(()=>accounts.SetAccountAsync(admin,bid,"Presenter",false,default),"6.5 account deactivation cannot orphan owned events");
        await db.InitializeAsync();await db.InitializeAsync();
        TestProgram.Check(await access.CanAsync(b,"experience",shared,true,default)&&!await access.CanAsync(a,"experience",eb,false,default),"6.5 repeated migration and multi-active restart preserve isolation and ownership");
        await access.InviteAsync(b,"experience",eb,aid,default);var expiring=(await access.InvitationsAsync(a,default)).Single();
        await db.ExecuteAsync("UPDATE PresenterEventInvites SET Expires=$now WHERE Id=$id",default,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$id",expiring.Id));
        await TestProgram.RejectAsync(()=>access.AcceptAsync(a,expiring.Id,true,default),"6.5 expired invitation cannot grant access");
        await access.InviteAsync(b,"experience",eb,aid,default);var decline=(await access.InvitationsAsync(a,default)).Single();await access.AcceptAsync(a,decline.Id,false,default);
        TestProgram.Check(!await access.CanAsync(a,"experience",eb,false,default),"6.5 declining invitation grants no access");
        var other=await PresenterChecks.Login(accounts,"scope-a",aKey);
        var otherRef=(await accounts.OwnSessionsAsync(other,default)).Single(x=>x.Current).Id;
        await TestProgram.RejectAsync(()=>accounts.RevokeOwnSessionAsync(b,otherRef,default),"6.5 session reference cannot revoke another identity's session");
        await accounts.RevokeOwnSessionAsync(a,otherRef,default);
        TestProgram.Check(await accounts.ValidateAsync(a,default)&&!await accounts.ValidateAsync(other,default),"6.5 own-session revocation is scoped to the selected session");
        await SettingsAndWizard(db,accounts,admin,adminKey,clock);
    }
    private static async Task MigrationAsync(string root)
    {
        using(var fresh=new DemoStore(Path.Combine(root,"wizard-bootstrap-race.db"))){
            await fresh.InitializeAsync();var s=PresenterChecks.Service(fresh,new TestClock());
            async Task<string?> Begin(string login){try{return await s.BeginLocalSetupAsync(login,"Synthetic",default);}catch(DemoRequestException){return null;}}
            var attempts=await Task.WhenAll(Task.Run(()=>Begin("wizard-one")),Task.Run(()=>Begin("wizard-two")));
            TestProgram.Check(attempts.Count(x=>x is not null)==1,"6.5 concurrent local wizard setup issues only one live bootstrap authorization");
            var code=attempts.Single(x=>x is not null)!;using var firstKey=new SoftwareAuthenticator();await PresenterChecks.Enroll(s,code,firstKey);
            await TestProgram.RejectAsync(()=>s.BeginLocalSetupAsync("wizard-three","Denied",default),"6.5 local wizard registration closes after first passkey activation");
        }
        using var old=new DemoStore(Path.Combine(root,"event-access-upgrade.db"));
        await PrizeMigrationChecks.SeedLegacy(old);
        await LummaSimulator.Web.Services.Prizes.PrizeMigration.InitializeAsync(old,default);
        await IdentityMigration.InitializeAsync(old,default);
        var original=await PrizeMigrationChecks.Snapshot(old);
        var interrupted=false;
        try{await EventAccessMigration.InitializeAsync(old,default,false,()=>throw new InvalidOperationException("Synthetic interruption"));}
        catch(InvalidOperationException){interrupted=true;}
        TestProgram.Check(interrupted&&original==await PrizeMigrationChecks.Snapshot(old),"6.5 interrupted event migration rolls back schema, data, index and activation marker");
        await old.InitializeAsync();var migrated=await PrizeMigrationChecks.Snapshot(old);await old.InitializeAsync();
        TestProgram.Check(migrated==await PrizeMigrationChecks.Snapshot(old),"6.5 historical migration is repeatable without data loss");
        TestProgram.Check(Convert.ToInt64(await old.ScalarAsync("SELECT Existing FROM PresenterInstallation",default))==1
            &&Convert.ToInt64(await old.ScalarAsync("SELECT COUNT(*) FROM PresenterEventScopes WHERE OwnerId IS NOT NULL",default))==0,"6.5 established installation skips first-run and historical owners remain explicitly unknown");
        TestProgram.Check((string?)await old.ScalarAsync("SELECT Name FROM Demos WHERE Id='visitor'",default)=="Visitor-KEEP"
            &&Convert.ToInt64(await old.ScalarAsync("SELECT COUNT(*) FROM MonitorGrants",default))==1,"6.5 migration retains synthetic visitor, passkey and existing grant rows");
        await old.ExecuteAsync("CREATE TABLE PresenterEventUnexpected(Id TEXT)",default);
        await PrizeMigrationChecks.ExpectReject(()=>old.InitializeAsync(),"6.5 schema contract rejects unknown tables rather than accepting a name prefix");
    }
    private static async Task SettingsAndWizard(DemoStore db,PresenterService accounts,ClaimsPrincipal admin,SoftwareAuthenticator key,TestClock clock)
    {
        var settings=new PresentationSettings(db,accounts,new ExperienceSettings(),clock);
        var onboarding=new PresenterOnboarding(db,accounts,clock);
        TestProgram.Check(!(await onboarding.StateAsync(default)).Existing,"6.5 a genuinely new database requires onboarding");
        await PresenterChecks.Reauth(accounts,admin,key);await settings.SaveAsync(admin,new("Synthetic Organization","Synthetic Demo","nl-NL","experience",7,"",0),default);
        await settings.LoadAsync(default);TestProgram.Check(settings.Current.Revision==1,"6.5 settings persist with optimistic revision");
        await PresenterChecks.Reauth(accounts,admin,key);
        await TestProgram.RejectAsync(()=>settings.SaveAsync(admin,settings.Current with{ProposedBaseUrl="http://unsafe.invalid"},default),"6.5 proposed hosting rejects non-HTTPS without changing effective WebAuthn");
        await TestProgram.RejectAsync(()=>onboarding.ChangeAsync(admin,"hosting","",false,true,default),"6.5 onboarding requires effective HTTPS");
        await onboarding.ChangeAsync(admin,"hosting","",true,true,default);
        await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>onboarding.ChangeAsync(admin,"event","Synthetic First",true,false,default))));
        var first=(await onboarding.StateAsync(default)).FirstEvent;await db.InitializeAsync();
        TestProgram.Check(first is not null&&(await onboarding.StateAsync(default)).FirstEvent==first,"6.5 concurrent first-event requests and restart resume one installation");
        TestProgram.Check(await onboarding.StateAsync(default) is {Ready:true,EventName:"Synthetic First"},"Restart preserves named event and all onboarding requirements");
        clock.Advance(TimeSpan.FromMinutes(6));
        await TestProgram.RejectAsync(()=>onboarding.ChangeAsync(admin,"finish","",true,false,default),"An expired passkey confirmation cannot complete onboarding");
        TestProgram.Check(!(await onboarding.StateAsync(default)).Completed,"Expired confirmation preserves onboarding state");
        await PresenterChecks.Reauth(accounts,admin,key);await onboarding.ChangeAsync(admin,"finish","",true,false,default);
        TestProgram.Check((await onboarding.StateAsync(default)).Completed,"6.5 validated installation completes the wizard");
        await db.InitializeAsync();
        TestProgram.Check(await onboarding.StateAsync(default) is {Completed:true,EventName:"Synthetic First"},"Completed onboarding is durable across startup initialization");
        var blocked=false;try{await db.ExecuteAsync("DELETE FROM PresenterInstallation",default);}catch(Microsoft.Data.Sqlite.SqliteException){blocked=true;}
        TestProgram.Check(blocked,"6.5 installation cannot be reopened by deleting its flag");
        await TestProgram.RejectAsync(()=>onboarding.ChangeAsync(admin,"event","Again",true,false,default),"6.5 completed onboarding cannot create a replacement installation");
        foreach(var bad in new[]{System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"),new byte[262145]})
            await TestProgram.RejectAsync(()=>Task.Run(()=>SafeLogo.Validate(bad)),"6.5 active content and oversized branding uploads rejected");
    }
}
