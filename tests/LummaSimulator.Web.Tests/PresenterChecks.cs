using System.Security.Claims;
using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

internal static class PresenterChecks
{
    internal static PresenterService Service(DemoStore store,TimeProvider clock,string origin="https://localhost:7243") => new(store,new ConfigurationBuilder().Build(),clock,
        new Fido2(new WebAuthnSettings{RpId="localhost",Origins=[origin]}.ToFidoConfiguration()));
    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
    internal static async Task Enroll(PresenterService s,string code,SoftwareAuthenticator key,bool local=true)
    {
        var grant=await s.OpenEnrollmentAsync(code,local,default);var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");
        var options=await s.OptionsAsync(browser,op,"enroll","",grant,Anonymous,local,default);
        await s.VerifyAsync(browser,op,"enroll",key.Register(options),"Primary",grant,Anonymous,local,default);
    }
    internal static async Task<ClaimsPrincipal> Login(PresenterService s,string login,SoftwareAuthenticator key,uint? counter=null)
    {
        var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");var options=await s.OptionsAsync(browser,op,"login","",null,Anonymous,true,default);
        return (await s.VerifyAsync(browser,op,"login",key.Authenticate(options,counter:counter),"",null,Anonymous,true,default))!;
    }
    internal static async Task Reauth(PresenterService s,ClaimsPrincipal principal,SoftwareAuthenticator key)
    {
        var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");var options=await s.OptionsAsync(browser,op,"reauth","",null,principal,true,default);
        await s.VerifyAsync(browser,op,"reauth",key.Authenticate(options),"",null,principal,true,default);
    }
    public static async Task RunAsync(string root)
    {
        // Two local maintenance requests must leave only one usable bootstrap capability.
        using(var raceStore=new DemoStore(Path.Combine(root,"setup-race.db"))) {
            await raceStore.InitializeAsync();
            var a=Service(raceStore,new TestClock());var b=Service(raceStore,new TestClock());
            var issued=await Task.WhenAll(
                Task.Run(()=>a.IssueBootstrapAsync("race-one","Synthetic One",default)),
                Task.Run(()=>b.IssueBootstrapAsync("race-two","Synthetic Two",default)));
            var accepted=0;
            foreach(var candidate in issued) {
                try {
                    var raceGrant=await a.OpenEnrollmentAsync(candidate.Code,true,default,bootstrapOnly:true);
                    using var raceKey=new SoftwareAuthenticator();
                    var raceBrowser=DemoIdentity.RandomToken();var raceOp=Guid.NewGuid().ToString("D");
                    var raceOptions=await a.OptionsAsync(raceBrowser,raceOp,"enroll","",raceGrant,Anonymous,true,default);
                    await a.VerifyAsync(raceBrowser,raceOp,"enroll",raceKey.Register(raceOptions),"Synthetic key",raceGrant,Anonymous,true,default);
                    accepted++;
                } catch(DemoRequestException) { }
            }
            TestProgram.Check(accepted==1 && !await a.IsSetupRequiredAsync(default),"Concurrent bootstrap issuance permits only one first administrator activation");
        }
        using var store=new DemoStore(Path.Combine(root,"presenter.db"));await store.InitializeAsync();
        var clock=new TestClock();var s=Service(store,clock);
        TestProgram.Check(!typeof(PresenterService).GetMethods().Any(m=>m.Name=="SignInAsync"),"Presenter password sign-in has no fallback API");
        TestProgram.Check(await s.IsSetupRequiredAsync(default),"Empty installation requires setup");
        var invitation=await s.IssueBootstrapAsync("admin-one","Administrator One",default);
        await TestProgram.RejectAsync(()=>s.OpenEnrollmentAsync(invitation.Code,false,default),"First-administrator bootstrap refuses non-local enrollment");
        TestProgram.Check(await s.IsSetupRequiredAsync(default),"Pending bootstrap does not close setup");
        var grant=await s.OpenEnrollmentAsync(invitation.Code,true,default,bootstrapOnly:true);
        await TestProgram.RejectAsync(()=>s.OpenEnrollmentAsync(invitation.Code,true,default),"Bootstrap authorization is consumed exactly once");
        using var primary=new SoftwareAuthenticator();using var reserve=new SoftwareAuthenticator();
        var browser=DemoIdentity.RandomToken();var operation=Guid.NewGuid().ToString("D");
        var options=await s.OptionsAsync(browser,operation,"enroll","",grant,Anonymous,true,default);
        var registered=primary.Register(options);
        await s.VerifyAsync(browser,operation,"enroll",registered,"Primary",grant,Anonymous,true,default);
        await TestProgram.RejectAsync(()=>s.VerifyAsync(browser,operation,"enroll",registered,"Primary",grant,Anonymous,true,default),"Presenter registration challenge cannot be replayed");
        await TestProgram.RejectAsync(()=>s.IssueBootstrapAsync("other-admin","Other Admin",default),"Bootstrap permanently closes after the first successful enrollment");
        TestProgram.Check(!await s.IsSetupRequiredAsync(default),"Successful registration closes setup");
        await store.ExecuteAsync("UPDATE PresenterAccounts SET Active=0 WHERE Login='admin-one'",default);
        TestProgram.Check(!await s.IsSetupRequiredAsync(default),"Disabled previously activated administrator never reopens setup");
        await TestProgram.RejectAsync(()=>s.IssueBootstrapAsync("other-admin","Other Admin",default),"Disabled administrator still blocks bootstrap issuance");
        await store.ExecuteAsync("UPDATE PresenterAccounts SET Active=1 WHERE Login='admin-one'",default);
        var principal=await Login(s,"admin-one",primary);
        await TestProgram.RejectAsync(() => s.MonitorAdministratorAsync(Anonymous, false, default), "Anonymous user cannot manage monitor keys");
        await TestProgram.RejectAsync(() => s.MonitorAdministratorAsync(principal, true, default), "Monitor mutation requires fresh administrator proof");
        await Reauth(s,principal,primary);
        TestProgram.Check(!string.IsNullOrEmpty(await s.MonitorAdministratorAsync(principal, true, default)), "Fresh administrator can authorize one monitor mutation");
        await TestProgram.RejectAsync(() => s.MonitorAdministratorAsync(principal, true, default), "Monitor management consumes fresh proof once");
        TestProgram.Check(await s.ValidateAsync(principal,default) && principal.IsInRole("Administrator") && principal.FindFirstValue("amr")=="webauthn",
            "Personal administrator session is established only after verified WebAuthn");
        await TestProgram.RejectAsync(()=>s.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",null,principal,true,default),"Extra own key requires explicit reauthentication");
        await Reauth(s,principal,primary);
        var extraBrowser=DemoIdentity.RandomToken();var extraOp=Guid.NewGuid().ToString("D");
        var extra=await s.OptionsAsync(extraBrowser,extraOp,"enroll","",null,principal,true,default);
        await s.CancelAsync(extraBrowser,Guid.NewGuid().ToString("D"),default);
        await s.VerifyAsync(extraBrowser,extraOp,"enroll",reserve.Register(extra),"Reserve",null,principal,true,default);
        TestProgram.Check((await s.KeysAsync(principal,null,default)).Count==2,"Primary and reserve credentials belong to one persistent presenter");
        await TestProgram.RejectAsync(()=>s.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",null,principal,true,default),"Reauthentication authorization permits only one security change");
        var zeroOne=await Login(s,"admin-one",reserve,0);var zeroTwo=await Login(s,"admin-one",reserve,0);
        TestProgram.Check(await s.ValidateAsync(zeroOne,default) && await s.ValidateAsync(zeroTwo,default),"Authenticators that report a constant zero signature counter are supported");
        var badBrowser=DemoIdentity.RandomToken();var badOp=Guid.NewGuid().ToString("D");
        var badOptions=await s.OptionsAsync(badBrowser,badOp,"login","admin-one",null,Anonymous,true,default);
        var bad=primary.Authenticate(badOptions,origin:"https://other.invalid");
        await TestProgram.RejectAsync(()=>s.VerifyAsync(badBrowser,badOp,"login",bad,"",null,Anonymous,true,default),"Presenter assertion with invalid origin is rejected");
        await TestProgram.RejectAsync(()=>s.VerifyAsync(badBrowser,badOp,"login",primary.Authenticate(badOptions),"",null,Anonymous,true,default),"Rejected presenter assertion burns its challenge");
        var visitors=new DemoService(store,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance);
        var visitor=await visitors.CreateAsync("yubikey",default);using var visitorKey=new SoftwareAuthenticator();
        await visitors.VerifyAsync(visitor.Token,true,visitorKey.Register(await visitors.OptionsAsync(visitor.Token,true,default)),default);
        var crossBrowser=DemoIdentity.RandomToken();var crossOp=Guid.NewGuid().ToString("D");var crossOptions=await s.OptionsAsync(crossBrowser,crossOp,"login","admin-one",null,Anonymous,true,default);
        await TestProgram.RejectAsync(()=>s.VerifyAsync(crossBrowser,crossOp,"login",visitorKey.Authenticate(crossOptions),"",null,Anonymous,true,default),"Visitor credential never authenticates a presenter account");
        await Reauth(s,principal,primary);
        var person=await s.CreateAccountAsync(principal,"presenter-two","Presenter Two","Presenter",default);
        await TestProgram.RejectAsync(()=>s.OpenEnrollmentAsync(person.Code,true,default,bootstrapOnly:true),"Closed setup rejects regular invitations without consuming them");
        TestProgram.Check(!await s.IsSetupRequiredAsync(default) && (await s.KeysAsync(principal,null,default)).Count==2,"Setup status preserves existing administrator credentials");
        using var personKey=new SoftwareAuthenticator();await Enroll(s,person.Code,personKey,false);
        var personSession=await Login(s,"presenter-two",personKey);
        await DiscoverablePresenterChecks.RunAsync(root,store,clock,s,principal,primary,reserve,personSession,personKey);
        await TestProgram.RejectAsync(()=>s.AccountsAsync(personSession,default),"Presenter cannot list or manage other presenter accounts");
        await TestProgram.RejectAsync(()=>s.AuditAsync(personSession,0,default),"Presenter cannot view administrator audit records");
        await TestProgram.RejectAsync(()=>s.SetAccountAsync(personSession,invitation.AccountId,"Presenter",false,default),"Presenter cannot change another account role through service calls");
        await Reauth(s,principal,primary);
        await TestProgram.RejectAsync(()=>s.SetAccountAsync(principal,invitation.AccountId,"Presenter",false,default),"Last active administrator cannot be disabled or demoted");
        var secondPrincipal=await Login(s,"admin-one",primary);
        await s.SignOutAsync(secondPrincipal,default);
        TestProgram.Check(!await s.ValidateAsync(secondPrincipal,default) && await s.ValidateAsync(principal,default) && await s.ValidateAsync(personSession,default),"Logout revokes only the selected session, not other presenters");
        await Reauth(s,principal,primary);
        await s.SetAccountAsync(principal,person.AccountId,"Presenter",false,default);
        TestProgram.Check(!await s.ValidateAsync(personSession,default) && await s.ValidateAsync(principal,default),"Disabling an account immediately invalidates its existing sessions only");
        await Reauth(s,principal,primary);
        await TestProgram.RejectAsync(()=>s.InviteAsync(principal,person.AccountId,default),"Enrollment invitations cannot replace an established identity, including disabled accounts");
        await Reauth(s,principal,primary);await s.SetAccountAsync(principal,person.AccountId,"Presenter",true,default);
        var recoveredSession=await Login(s,"presenter-two",personKey);
        TestProgram.Check(await s.ValidateAsync(recoveredSession,default),"Reactivation retains the account's own existing passkey");
        var ownKeys=await s.KeysAsync(principal,null,default);
        await Reauth(s,principal,primary);
        await s.RemoveKeyAsync(principal,invitation.AccountId,ownKeys.Single(k=>k.Name=="Reserve").Id,default);
        TestProgram.Check(!await s.ValidateAsync(principal,default),"Removing a credential revokes existing target sessions");
        principal=await Login(s,"admin-one",primary);
        await Reauth(s,principal,primary);
        await TestProgram.RejectAsync(async()=>await s.RemoveKeyAsync(principal,invitation.AccountId,(await s.KeysAsync(principal,null,default)).Single().Id,default),"Last credential cannot be removed");
        var audit=await s.AuditAsync(principal,0,default);var auditJson=JsonSerializer.Serialize(audit);
        TestProgram.Check(audit.Any(e=>e.Action=="SignedIn") && audit.Any(e=>e.Action=="CredentialRegistered") && !auditJson.Contains(invitation.Code)
            && !auditJson.Contains(principal.FindFirstValue("sid")!),"Audit contains actions and account references, never capabilities or session secrets");
        var appendOnly=false;try{await store.ExecuteAsync("DELETE FROM PresenterAudit",default);}catch(Microsoft.Data.Sqlite.SqliteException){appendOnly=true;}
        TestProgram.Check(appendOnly,"Audit rows cannot be deleted through normal SQLite statements");
        var stats=new ExperienceStatistics(store);var before=await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),29,default);
        await s.EventAsync(principal,before.ActiveEvent.Id,"",true,true,default);
        var closed=await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),29,default);
        TestProgram.Check(closed.ActiveEvent.Status=="None","Authenticated presenter can close the active event without creating a replacement");
        await visitors.VerifyAsync(visitor.Token,false,visitorKey.Authenticate(await visitors.OptionsAsync(visitor.Token,false,default)),default);
        var open=await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),29,default);
        TestProgram.Check(open.TotalVisitors==1 && open.ActiveEvent.Status=="None","Public visitor authentication still works when no event is active");
        await store.InitializeAsync();TestProgram.Check((await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),29,default)).ActiveEvent.Status=="None","Restart does not silently reopen a closed event");
        await s.EventAsync(principal,"","New event",false,true,default);
        var renewed=await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(),29,default);
        TestProgram.Check(renewed.EventVisitors==0 && renewed.TotalVisitors==1,"Starting a new event preserves totals and starts a fresh event counter");
        clock.Advance(TimeSpan.FromMinutes(31));await visitors.CleanupAsync(default);await s.PurgeExpiredAsync(default);
        TestProgram.Check(!await s.ValidateAsync(principal,default)
            && Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterAccounts",default))==2
            && Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterKeys",default))==2,"Presenter sessions expire but visitor cleanup never removes presenter accounts or keys");
        TestProgram.Check(Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterAuthSessions",default))==0,"Presenter housekeeping removes expired sessions without touching persistent accounts or audit");
        await ConcurrencyAndExpiry(root);
        await OwnKeyRemoval(root);
    }
    private static async Task OwnKeyRemoval(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"own-key-removal.db"));await store.InitializeAsync();
        var s=Service(store,new TestClock());using var adminKey=new SoftwareAuthenticator();
        var bootstrap=await s.IssueBootstrapAsync("own-admin","Synthetic Admin",default);await Enroll(s,bootstrap.Code,adminKey);
        var admin=await Login(s,"own-admin",adminKey);await Reauth(s,admin,adminKey);
        var invitation=await s.CreateAccountAsync(admin,"own-presenter","Synthetic Presenter","Presenter",default);
        using var key=new SoftwareAuthenticator();using var backup=new SoftwareAuthenticator();await Enroll(s,invitation.Code,key);
        var person=await Login(s,"own-presenter",key);
        await Reauth(s,person,key);var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");
        var options=await s.OptionsAsync(browser,op,"enroll","",null,person,true,default);
        await s.VerifyAsync(browser,op,"enroll",backup.Register(options),"Backup",null,person,true,default);
        var backupRef=(await s.KeysAsync(person,null,default)).Single(k=>k.Name=="Backup").Id;
        await TestProgram.RejectAsync(()=>s.RemoveOwnKeyAsync(person,backupRef,default),"Own key removal requires fresh passkey confirmation");
        await TestProgram.RejectAsync(()=>s.RemoveOwnKeyAsync(Anonymous,backupRef,default),"Anonymous own-key removal is refused");
        var otherSession=await Login(s,"own-presenter",key);await Reauth(s,person,key);
        var foreign=(await s.KeysAsync(admin,null,default)).Single().Id;
        await TestProgram.RejectAsync(()=>s.RemoveOwnKeyAsync(person,foreign,default),"Own-key removal cannot target another account even with fresh proof");
        TestProgram.Check((await s.KeysAsync(person,null,default)).Count==2,"Rejected foreign-key removal preserves own credentials");
        await s.RemoveOwnKeyAsync(person,backupRef,default);
        TestProgram.Check(!await s.ValidateAsync(person,default) && !await s.ValidateAsync(otherSession,default) && await s.ValidateAsync(admin,default),"Ordinary presenter removes own key and revokes only own sessions");
        var renewed=await Login(s,"own-presenter",key);await Reauth(s,renewed,key);
        var last=(await s.KeysAsync(renewed,null,default)).Single().Id;
        await TestProgram.RejectAsync(()=>s.RemoveOwnKeyAsync(renewed,last,default),"Last own passkey cannot be removed");
        TestProgram.Check(await s.ValidateAsync(renewed,default) && (await s.KeysAsync(renewed,null,default)).Count==1,"Rejected last-key removal preserves access and credential");
    }
    private static async Task ConcurrencyAndExpiry(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"presenter-concurrency.db"));await store.InitializeAsync();
        using var peer=new DemoStore(store.DatabasePath);await peer.InitializeAsync();
        var clock=new TestClock();var a=Service(store,clock);var b=Service(peer,clock);
        await TestProgram.RejectAsync(()=>a.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",null,Anonymous,true,default),"Anonymous presenter registration without a capability is refused");
        var expired=await a.IssueBootstrapAsync("admin-a","A",default);clock.Advance(TimeSpan.FromMinutes(11));
        await TestProgram.RejectAsync(()=>a.OpenEnrollmentAsync(expired.Code,true,default),"Bootstrap capabilities expire after their short lifetime");
        var slow=await a.IssueBootstrapAsync("admin-a","A",default);clock.Advance(TimeSpan.FromMinutes(9));
        var slowGrant=await a.OpenEnrollmentAsync(slow.Code,true,default);clock.Advance(TimeSpan.FromMinutes(2));
        await TestProgram.RejectAsync(()=>a.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",slowGrant,Anonymous,true,default),"Redeeming an invitation does not extend its original authorization deadline");
        var invitation=await a.IssueBootstrapAsync("admin-a","A",default);using var keyA=new SoftwareAuthenticator();await Enroll(a,invitation.Code,keyA);
        var principalA=await Login(a,"admin-a",keyA);await Reauth(a,principalA,keyA);
        var accountB=await a.CreateAccountAsync(principalA,"admin-b","B","Administrator",default);using var keyB=new SoftwareAuthenticator();await Enroll(b,accountB.Code,keyB,false);
        var principalB=await Login(b,"admin-b",keyB);await Reauth(a,principalA,keyA);await Reauth(b,principalB,keyB);
        async Task<bool> Demote(PresenterService service,ClaimsPrincipal principal,string id) {
            try { await service.SetAccountAsync(principal,id,"Presenter",true,default);return true; }catch(DemoRequestException){return false;}
        }
        var attempts=await Task.WhenAll(Task.Run(()=>Demote(a,principalA,invitation.AccountId)),Task.Run(()=>Demote(b,principalB,accountB.AccountId)));
        TestProgram.Check(attempts.Count(x=>x)==1 && Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterAccounts WHERE Active=1 AND Role='Administrator'",default))==1,
            "Concurrent administrator demotions across service instances always preserve one active administrator");
        var survivor=await a.ValidateAsync(principalA,default)?principalA:principalB;var key=ReferenceEquals(survivor,principalA)?keyA:keyB;
        await Reauth(a,survivor,key);clock.Advance(TimeSpan.FromMinutes(6));
        await TestProgram.RejectAsync(()=>a.CreateAccountAsync(survivor,"too-late","Too late","Presenter",default),"Fresh reauthentication authorization expires after five minutes");
        var login=ReferenceEquals(survivor,principalA)?"admin-a":"admin-b";
        var browser=DemoIdentity.RandomToken();var operation=Guid.NewGuid().ToString("D");var options=await a.OptionsAsync(browser,operation,"login",login,null,Anonymous,true,default);
        clock.Advance(TimeSpan.FromMinutes(3));
        await TestProgram.RejectAsync(()=>a.VerifyAsync(browser,operation,"login",key.Authenticate(options),"",null,Anonymous,true,default),"Presenter challenges expire independently of the authenticated session");
    }
}
