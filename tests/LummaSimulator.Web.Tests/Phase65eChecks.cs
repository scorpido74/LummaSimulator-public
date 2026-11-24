using System.Security.Claims;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace LummaSimulator.Web.Tests;
internal static class Phase65eChecks
{
    internal static async Task Denied(Func<Task> action,string label){
        var denied=false;try{await action();}catch(DemoRequestException e) when(e.Message=="PresenterDenied"||e.Message=="Je demonstratie is verlopen. Start opnieuw."){denied=true;}
        TestProgram.Check(denied,label);
    }
    internal static async Task RunAsync(string root)
    {
        using var db=new DemoStore(Path.Combine(root,"phase65e.db"));await db.InitializeAsync();
        var clock=new TestClock();var accounts=PresenterChecks.Service(db,clock);using var adminKey=new SoftwareAuthenticator();using var bKey=new SoftwareAuthenticator();
        var boot=await accounts.IssueBootstrapAsync("p1-admin","Synthetic Admin",default);await PresenterChecks.Enroll(accounts,boot.Code,adminKey);
        var admin=await PresenterChecks.Login(accounts,"p1-admin",adminKey);
        await PresenterChecks.Reauth(accounts,admin,adminKey);var bInvite=await accounts.CreateAccountAsync(admin,"p1-owner","Synthetic Owner","Presenter",default);
        await PresenterChecks.Enroll(accounts,bInvite.Code,bKey);var b=await PresenterChecks.Login(accounts,"p1-owner",bKey);
        var access=new EventAccess(db,accounts,clock);var bId=b.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var networkPlan=new DemoNetworkPlan("https://portal.demo.example","https://app.demo.example","192.0.2.10","192.0.2.20","192.0.2.0/24","192.0.2.50/32","Private");
        TestProgram.Check(networkPlan.IsValid(),"Network plan accepts explicit hosts, local IPs and restricted demo CIDRs");
        TestProgram.Check(!(networkPlan with{HelperSubnet="0.0.0.0/0"}).IsValid()
            &&!(networkPlan with{AppOrigin="https://app.demo.example:7243"}).IsValid()
            &&!(networkPlan with{Profile="Public"}).IsValid(),"Network plan rejects Any, receiver port 7243 and Public profile");
        var networkSettings=new PresentationSettings(db,accounts,new ExperienceSettings(),clock);
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        await networkSettings.SaveAsync(admin,new("Synthetic","Synthetic demo","nl-NL","experience",7,"",0){Network=networkPlan},default);
        TestProgram.Check(networkSettings.Current.Network==networkPlan,"Administrator network plan round-trips in existing presentation JSON without schema changes");
        var changedProfile=new PresentationProfile("Synthetic changed","Synthetic demo","nl-NL","experience",7,"",networkSettings.Current.Revision);
        var reauthRequired=false;
        try{await networkSettings.SaveAsync(admin,changedProfile,default);}
        catch(DemoRequestException e) when(e.Message=="PresenterReauthenticate"){reauthRequired=true;}
        TestProgram.Check(reauthRequired&&networkSettings.Current.Revision==changedProfile.Revision
            &&networkSettings.Current.Network==networkPlan,"A second configuration save requires fresh reauthentication and preserves settings when rejected");
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        await networkSettings.SaveAsync(admin,changedProfile,default);
        TestProgram.Check(networkSettings.Current.Network==networkPlan,"Ordinary configuration edits preserve the stored network plan");
        await Denied(()=>networkSettings.SaveAsync(b,networkSettings.Current,default),"Presenter cannot change the administrator network plan");
        var demos=new DemoService(db,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance);
        var aEvent=await access.CreateExperienceAsync(admin,"Rotation A",default);var bEvent=await access.CreateExperienceAsync(b,"Rotation B",default);
        TestProgram.Check((await access.ActivePresentationEventsAsync(admin,default)).Any(e=>e.Id==aEvent)
            &&!(await access.ActivePresentationEventsAsync(admin,default)).Any(e=>e.Id==bEvent),"Administrator selector requires an explicit active presenter link, without losing management access");
        TestProgram.Check((await access.ListAsync(admin,default)).Any(e=>e.Id==bEvent),"Admin overview retains separately owned events for management and recovery");
        TestProgram.Check((await access.ActivePresentationEventsAsync(b,default)).All(e=>e.Id==bEvent),"Presenter selector excludes unrelated events");
        var existingOpen=await demos.CreateAsync("laptop",default);
        TestProgram.Check(!await access.MatchesVisitorEventAsync(existingOpen.Token,"experience",aEvent,default),"An open demo is not silently treated as an event QR participant");
        await access.BindVisitorAsync(existingOpen.Token,"experience",aEvent,default);
        TestProgram.Check(await access.MatchesVisitorEventAsync(existingOpen.Token,"experience",aEvent,default)
            &&!await access.MatchesVisitorEventAsync(existingOpen.Token,"experience",bEvent,default)
            &&!await access.MatchesVisitorEventAsync(existingOpen.Token,"prize",aEvent,default),"QR resume compares both persisted event kind and event ID for the owned visitor");
        TestProgram.Check((await access.LoginRoutesAsync("experience",aEvent,default)).Mask==15,"Existing events offer all four login routes by default");
        await access.SetLoginRoutesAsync(admin,"experience",aEvent,5,default);
        var routes=await access.LoginRoutesAsync("experience",aEvent,default);
        TestProgram.Check(routes.Allows("yubikey")&&routes.Allows("phone")&&!routes.Allows("laptop")&&!routes.Allows("ipad"),"Per-event routes preserve independent switches");
        await Denied(()=>access.SetLoginRoutesAsync(b,"experience",aEvent,15,default),"An unrelated presenter cannot alter event login routes");
        TestProgram.Check((await access.LoginRoutesAsync("experience",bEvent,default)).Mask==15,"Route settings do not affect another event");
        var blocked=await demos.CreateAsync("laptop",default);
        await TestProgram.RejectAsync(()=>access.BindVisitorAsync(blocked.Token,"experience",aEvent,default),"Direct admission cannot bind a disabled login route");
        var resumable=await demos.CreateAsync("phone",default);await access.BindVisitorAsync(resumable.Token,"experience",aEvent,default);
        await TestProgram.RejectAsync(()=>access.SetLoginRoutesAsync(admin,"experience",aEvent,0,default),"Experience cannot save a configuration with all routes off");
        await db.ExecuteAsync("UPDATE EventLoginRoutes SET Mask=0 WHERE Kind='experience' AND EventId=$id",default,("$id",aEvent));
        TestProgram.Check((await access.LoginRoutesAsync("experience",aEvent,default)).Mask==0&&await demos.GetAsync(resumable.Token,false,default) is not null,"All routes off preserves existing demo resumption");
        await TestProgram.RejectAsync(()=>demos.CreateAsync("phone",default,resumable.Token),"All routes off rejects a new visitor rotation");
        TestProgram.Check(await demos.GetAsync(resumable.Token,false,default) is not null,"Rejected rotation preserves the existing visitor");
        await db.InitializeAsync();
        TestProgram.Check((await access.LoginRoutesAsync("experience",aEvent,default)).Mask==0,"Additive visitor schema supports repeat initialization and preserves settings");
        await access.SetLoginRoutesAsync(admin,"experience",aEvent,15,default);
        async Task<string?> Context(string id)=>(string?)await db.ScalarAsync("SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$id",default,("$id",id));
        async Task<VerificationReceipt> Authenticate(NewDemo demo,SoftwareAuthenticator key){
            await demos.VerifyAsync(demo.Token,true,key.Register(await demos.OptionsAsync(demo.Token,true,default)),default);
            return await demos.VerifyAsync(demo.Token,false,key.Authenticate(await demos.OptionsAsync(demo.Token,false,default)),default);
        }
        var publicDemo=await demos.CreateAsync("laptop",default);
        TestProgram.Check(await Context(publicDemo.Account.Id)==ExperienceStatistics.UnassignedEvent,"6.5e public start never implicitly selects an active event");
        var visitor=await demos.CreateAsync("laptop",default);await access.BindVisitorAsync(visitor.Token,"experience",aEvent,default);
        using var visitorKey=new SoftwareAuthenticator();await Authenticate(visitor,visitorKey);
        var capability=await access.SetMonitorAsync(admin,visitor.Account.Id,true,default);
        var credentialBefore=(await db.CredentialsAsync(visitor.Account.Id,default)).Single();
        var next=await demos.CreateAsync("laptop",default,visitor.Token);
        TestProgram.Check(await Context(next.Account.Id)==aEvent&&next.Account.DemoRunId!=visitor.Account.DemoRunId&&next.Account.CredentialCount==0,"6.5e rotation inherits only server-side A context and creates a fresh run/identity");
        TestProgram.Check(await demos.GetAsync(visitor.Token,false,default) is null&&!await access.VerifyMonitorAsync(visitor.Account.DemoRunId,capability,default)&&!await access.VerifyMonitorAsync(next.Account.DemoRunId,capability,default),"6.5e rotation revokes old visitor and monitor authority; no grant transfers to new run");
        var retained=(await db.CredentialsAsync(visitor.Account.Id,default)).Single();
        TestProgram.Check(retained.Counter==credentialBefore.Counter&&retained.PublicKey.SequenceEqual(credentialBefore.PublicKey),"6.5e rotation retains old credential bytes and counters until normal cleanup");
        using var nextKey=new SoftwareAuthenticator();var aReceipt=await Authenticate(next,nextKey);
        var other=await demos.CreateAsync("phone",default);await access.BindVisitorAsync(other.Token,"experience",bEvent,default);
        using var otherKey=new SoftwareAuthenticator();var bReceipt=await Authenticate(other,otherKey);
        var nextB=await demos.CreateAsync("phone",default,other.Token);
        TestProgram.Check(await Context(nextB.Account.Id)==bEvent&&aReceipt.VisitorNumber==2&&bReceipt.VisitorNumber==1,"6.5e A and B rotations preserve independent visitor sequences");
        var publicNext=await demos.CreateAsync("laptop",default,publicDemo.Token);
        TestProgram.Check(await Context(publicNext.Account.Id)==ExperienceStatistics.UnassignedEvent,"6.5e public rotation remains Open demo while events are active");
        await Denied(()=>demos.CreateAsync("laptop",default,DemoIdentity.RandomToken()),"6.5e fabricated prior visitor token cannot choose a context");
        await access.ChangeExperienceAsync(admin,aEvent,true,default);
        await Denied(()=>demos.CreateAsync("laptop",default,next.Token),"6.5e closed event cannot admit a rotated visitor");
        TestProgram.Check(await demos.GetAsync(next.Token,false,default) is not null,"6.5e failed rotation rolls back without closing the existing visitor");
        var protection=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"phase65e-edition-keys")));
        var manifest=new EventEditionStore(Path.Combine(root,"phase65e-edition.manifest"),protection);
        var editionService=new EventEditionService(manifest,db,accounts,clock,protection,new ExperienceSettings());
        var editionId=await editionService.CreateAsync(admin,"Rotation edition",clock.GetUtcNow(),clock.GetUtcNow().AddHours(1),default);
        await editionService.StatusAsync(admin,editionId,false,default);
        var editionDemos=new DemoService(db,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance,edition:manifest);
        var editionVisitor=await editionDemos.CreateAsync("laptop",default);
        await new EventAccess(db,accounts,clock,manifest).BindVisitorAsync(editionVisitor.Token,"edition",editionId,default);
        var editionNext=await editionDemos.CreateAsync("laptop",default,editionVisitor.Token);
        TestProgram.Check(await Context(editionNext.Account.Id)==editionId,"6.5e Edition rotation validates and retains manifest-backed context without lock inversion");
        await editionService.StatusAsync(admin,editionId,true,default);
        await Denied(()=>editionDemos.CreateAsync("laptop",default,editionNext.Token),"6.5e closed Edition manifest refuses rotation");

        async Task<string> Event()=>await access.CreateExperienceAsync(admin,"Enrollment lifecycle",default);
        async Task Move(string id){
            await access.InviteAsync(admin,"experience",id,bId,default);await access.AcceptAsync(b,(await access.InvitationsAsync(b,default)).Single(i=>i.EventId==id).Id,true,default);
            await PresenterChecks.Reauth(accounts,admin,adminKey);await access.TransferAsync(admin,"experience",id,bId,"offer",default);
            await PresenterChecks.Reauth(accounts,b,bKey);await access.TransferAsync(b,"experience",id,"","accept",default);
        }
        async Task<long> PendingKeys(string login)=>Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM PresenterKeys k JOIN PresenterAccounts a ON a.Id=k.AccountId WHERE a.Login=$login",default,("$login",login)));
        var serial=0;
        foreach(var stage in new[]{"raw","grant","options"}){
            var id=await Event();var login="pending-transfer-"+(++serial);var code=await access.InviteNewAsync(admin,"experience",id,login,"Pending synthetic",default);
            string? grant=null,options=null;var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");
            if(stage!="raw")grant=await accounts.OpenEnrollmentAsync(code,true,default);
            if(stage=="options")options=await accounts.OptionsAsync(browser,op,"enroll","",grant,new ClaimsPrincipal(),true,default);
            await Move(id);
            await Denied(()=>accounts.OpenEnrollmentAsync(code,true,default),"6.5e transfer revokes raw/replayed enrollment at stage "+stage);
            if(grant is not null)await Denied(()=>accounts.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",grant,new ClaimsPrincipal(),true,default),"6.5e transfer revokes redeemed grant at stage "+stage);
            if(options is not null){using var key=new SoftwareAuthenticator();await Denied(()=>accounts.VerifyAsync(browser,op,"enroll",key.Register(options),"Denied",grant,new ClaimsPrincipal(),true,default),"6.5e transfer invalidates an already issued ceremony");}
            TestProgram.Check(await PendingKeys(login)==0,"6.5e revoked pending enrollment inserts no credential");
        }
        foreach(var stage in new[]{"raw","grant","options"}){
            var id=await Event();var code=await access.InviteNewAsync(admin,"experience",id,"pending-expiry-"+(++serial),"Expiring",default);
            string? grant=null,options=null;var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");
            if(stage!="raw")grant=await accounts.OpenEnrollmentAsync(code,true,default);
            if(stage=="options")options=await accounts.OptionsAsync(browser,op,"enroll","",grant,new ClaimsPrincipal(),true,default);
            await db.ExecuteAsync("UPDATE PresenterEventInvites SET Expires=$now WHERE EventId=$id",default,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$id",id));
            if(stage=="raw")await Denied(()=>accounts.OpenEnrollmentAsync(code,true,default),"6.5e raw redemption rechecks original event invitation expiry");
            else if(stage=="grant")await Denied(()=>accounts.OptionsAsync(browser,op,"enroll","",grant,new ClaimsPrincipal(),true,default),"6.5e options recheck original invitation expiry");
            else{using var key=new SoftwareAuthenticator();await Denied(()=>accounts.VerifyAsync(browser,op,"enroll",key.Register(options!),"Expired",grant,new ClaimsPrincipal(),true,default),"6.5e credential commit rechecks original invitation expiry");}
        }
        var stale=await access.CreateExperienceAsync(b,"Stale issuer",default);
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        var stalePending=await accounts.CreateAccountAsync(admin,"stale-issuer","Pending","Presenter",default);
        await db.ExecuteAsync("INSERT INTO PresenterEventInvites(Id,Kind,EventId,TargetId,IssuerId,Expires,EnrollmentHash) VALUES($id,'experience',$event,$target,$issuer,$expires,$hash)",default,
            ("$id",Guid.NewGuid().ToString("D")),("$event",stale),("$target",stalePending.AccountId),("$issuer",bId),("$expires",clock.GetUtcNow().AddMinutes(10).ToUnixTimeMilliseconds()),("$hash",DemoIdentity.Hash(stalePending.Code)));
        // A legacy non-admin event enrollment must not remain redeemable after the role boundary changes.
        await db.ExecuteAsync("UPDATE PresenterEventScopes SET OwnerId=$admin WHERE SourceId=$id AND Kind='experience'",default,("$admin",admin.FindFirstValue(ClaimTypes.NameIdentifier)),("$id",stale));
        await Denied(()=>accounts.OpenEnrollmentAsync(stalePending.Code,true,default),"Non-administrator event enrollment cannot be redeemed");
        var raceEvent=await Event();var raceCode=await access.InviteNewAsync(admin,"experience",raceEvent,"race-redemption","Race",default);string? raceGrant=null;
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        await Task.WhenAll(Task.Run(()=>access.TransferAsync(admin,"experience",raceEvent,bId,"recover",default)),Task.Run(async()=>{try{raceGrant=await accounts.OpenEnrollmentAsync(raceCode,true,default);}catch(DemoRequestException e) when(e.Message=="PresenterDenied"){} }));
        if(raceGrant is not null)await Denied(()=>accounts.OptionsAsync(DemoIdentity.RandomToken(),Guid.NewGuid().ToString("D"),"enroll","",raceGrant,new ClaimsPrincipal(),true,default),"6.5e serialized redemption/transfer leaves no usable grant");
        await Denied(()=>accounts.OpenEnrollmentAsync(raceCode,true,default),"6.5e concurrent transfer permanently rejects raw redemption");
        var commitEvent=await Event();var commitCode=await access.InviteNewAsync(admin,"experience",commitEvent,"race-commit","Commit race",default);
        var commitGrant=await accounts.OpenEnrollmentAsync(commitCode,true,default);var commitBrowser=DemoIdentity.RandomToken();var commitOp=Guid.NewGuid().ToString("D");
        var commitOptions=await accounts.OptionsAsync(commitBrowser,commitOp,"enroll","",commitGrant,new ClaimsPrincipal(),true,default);using var commitKey=new SoftwareAuthenticator();var response=commitKey.Register(commitOptions);var registered=false;
        await PresenterChecks.Reauth(accounts,admin,adminKey);
        await Task.WhenAll(Task.Run(()=>access.TransferAsync(admin,"experience",commitEvent,bId,"recover",default)),Task.Run(async()=>{try{await accounts.VerifyAsync(commitBrowser,commitOp,"enroll",response,"Race",commitGrant,new ClaimsPrincipal(),true,default);registered=true;}catch(DemoRequestException e) when(e.Message=="PresenterDenied"){} }));
        TestProgram.Check(await PendingKeys("race-commit")== (registered?1:0),"6.5e credential commit versus transfer is serialized: completed activation survives, otherwise no key");
        if(registered){var person=await PresenterChecks.Login(accounts,"race-commit",commitKey);TestProgram.Check(!person.IsInRole("Administrator")&&!await access.CanAsync(person,"experience",commitEvent,false,default),"6.5e completed account survives transfer without event or administrator escalation");}
        // Independent administrator-issued enrollment and established identities remain usable.
        await PresenterChecks.Reauth(accounts,admin,adminKey);var generic=await accounts.CreateAccountAsync(admin,"generic-after-transfer","Generic","Presenter",default);
        using var genericKey=new SoftwareAuthenticator();await PresenterChecks.Enroll(accounts,generic.Code,genericKey);
        TestProgram.Check(await accounts.ValidateAsync(admin,default)&&await accounts.ValidateAsync(b,default)&&(await accounts.KeysAsync(admin,null,default)).Count==1&&(await accounts.KeysAsync(b,null,default)).Count==1,"6.5e existing accounts/keys/sessions and independent administrator enrollment are preserved");
        TestProgram.Check(await db.ScalarAsync("PRAGMA foreign_key_check",default) is null&&(string?)await db.ScalarAsync("PRAGMA integrity_check",default)=="ok","6.5e workflows preserve SQLite integrity and foreign keys");
    }
}
