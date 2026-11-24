using System.Security.Claims;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Tests;

internal static class PrizeEventChecks
{
    static string Key()=>Guid.NewGuid().ToString("D");
    static async Task Reject(Func<Task> call,string reason)
    {
        var rejected=false;
        try { await call(); } catch(Exception e) when(e is DemoRequestException or PrizeConflictException or ArgumentException or SqliteException) { rejected=true; }
        TestProgram.Check(rejected,reason);
    }
    static async Task Denied(Func<Task> call,string reason)
    {
        var denied=false;
        try { await call(); } catch(DemoRequestException error) when(error.Message=="PresenterDenied") { denied=true; }
        TestProgram.Check(denied,reason);
    }
    internal static async Task RunAsync(string root)
    {
        using var db=new DemoStore(Path.Combine(root,"prize-events.db"));await db.InitializeAsync();
        using var peer=new DemoStore(db.DatabasePath);
        var clock=new TestClock();var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        async Task<ClaimsPrincipal> Actor(string id,string role,DemoStore? targetStore=null)
        {
            var actorStore=targetStore??db;
            var token=DemoIdentity.RandomToken();
            await actorStore.ExecuteAsync("""
                INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Pending,Activated)
                  VALUES($id,$id,'Synthetic',$role,$handle,1,1,0,1);
                INSERT INTO PresenterAuthSessions VALUES($hash,$id,$expires,NULL);
                INSERT INTO PresenterKeys(Id,Ref,AccountId,PublicKey,Counter,BackupEligible,BackedUp,Name,Registered) VALUES($key,$ref,$id,X'01',0,0,0,'Synthetic service fixture',1);
                """,default,("$id",id),("$role",role),("$handle",System.Text.Encoding.UTF8.GetBytes(id)),
                ("$hash",DemoIdentity.Hash(token)),("$expires",now+200L*86400000),("$key",id+"-fixture"),("$ref",Guid.NewGuid().ToString("D")));
            var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Role,"Presenter"),new("amr","webauthn"),new("sid",token)};
            if(role=="Administrator")claims.Add(new(ClaimTypes.Role,"Administrator"));
            return new(new ClaimsIdentity(claims,PresenterService.Scheme));
        }
        var admin=await Actor("admin","Administrator");var presenter=await Actor("presenter","Presenter");
        var accounts=PresenterChecks.Service(db,clock);
        TestProgram.Check(await accounts.ValidateAsync(admin,default)&&await accounts.ValidateAsync(presenter,default),"6.5 fixture has production-equivalent authenticated role claims and activated accounts");
        using(var legacyStore=new DemoStore(Path.Combine(root,"prize-existing-admin-upgrade.db"))){
            await legacyStore.InitializeLegacyAsync();
            var legacyAdmin=await Actor("legacy-admin","Administrator",legacyStore);
            await legacyStore.InitializeAsync();
            var legacyAccounts=PresenterChecks.Service(legacyStore,clock);var legacyAccess=new EventAccess(legacyStore,legacyAccounts,clock);
            var legacyEvent=(await legacyAccess.ListAsync(legacyAdmin,default)).First(x=>x.State=="Active");
            TestProgram.Check(await legacyAccounts.ValidateAsync(legacyAdmin,default)&&legacyEvent.Owner is null
                &&await legacyAccess.CanAsync(legacyAdmin,legacyEvent.Kind,legacyEvent.Id,true,default),"6.5 pre-upgrade administrator session and role retain access to historical events without inferred ownership");
        }
        PrizeEventService Service(DemoStore d)=>new(d,PresenterChecks.Service(d,clock),clock);
        var service=Service(db);var other=Service(peer);
        var config=new PrizeConfiguration();var anonymous=new ClaimsPrincipal();
        TestProgram.Check((await service.DefaultProfileAsync(admin)).Id==PrizeProfiles.Cybertruck,"5.2a first selection falls back to Cybertruck");
        TestProgram.Check((await service.ProfilesAsync(presenter)).Count==4,"5.2a presenter can read all built-in profiles");
        await Reject(()=>service.ListAsync(anonymous),"5.2a anonymous management denied");
        await Reject(()=>service.CreateAsync(anonymous,Key(),"Denied",PrizeProfiles.Cybertruck,config,true),"5.2a anonymous creation denied");
        var draft=await service.CreateAsync(admin,Key(),"Draft","builtin-instant",config,false);
        TestProgram.Check(draft.Status=="Draft" && (await service.DefaultProfileAsync(presenter)).Id==PrizeProfiles.Cybertruck,"5.2a draft does not update last-used profile");
        var key=Key();var started=await service.CreateAsync(admin,key,"Started","builtin-instant",config,true);
        TestProgram.Check(started.Status=="Open" && !started.Configuration.PrizeEnabled,"5.2a start opens management only and keeps prize admission disabled");
        TestProgram.Check((await other.DefaultProfileAsync(presenter)).Id=="builtin-instant","5.2a successful start remembers installation-wide preference");
        TestProgram.Check((await service.CreateAsync(admin,key,"Started","builtin-instant",config,true)).Id==started.Id,"5.2a identical creation retry returns same event");
        await Reject(()=>service.CreateAsync(admin,key,"Changed","builtin-instant",config,true),"5.2a differing name conflicts with creation intent");
        await Reject(()=>service.CreateAsync(admin,key,"Started","builtin-instant",config with {GroupSize=16},true),"5.2a differing configuration conflicts with creation intent");
        await Reject(()=>service.CreateAsync(admin,key,"Started","builtin-instant",config,false),"5.2a differing start flag conflicts with creation intent");
        var raceKey=Key();
        var race=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>
            (i%2==0?service:other).CreateAsync(admin,raceKey,"Race","builtin-draw",config,true))));
        TestProgram.Check(race.Select(x=>x.Id).Distinct().Count()==1,"5.2a concurrent creation on independent connections produces one event");
        TestProgram.Check((await service.DefaultProfileAsync(admin)).Id=="builtin-draw","5.2a latest committed first start wins preference");
        await service.CreateAsync(admin,key,"Started","builtin-instant",config,true);
        TestProgram.Check((await service.DefaultProfileAsync(admin)).Id=="builtin-draw","5.2a retry old creation never reverts newer preference");
        var before=await PrizeMigrationChecks.Snapshot(db);
        await db.ExecuteAsync("CREATE TRIGGER SyntheticStartFailure BEFORE INSERT ON PrizeOutbox BEGIN SELECT RAISE(ABORT,'synthetic'); END",default);
        await Reject(()=>service.CreateAsync(admin,Key(),"Rollback","builtin-custom",config,true),"5.2a interrupted start rejects atomically");
        await db.ExecuteAsync("DROP TRIGGER SyntheticStartFailure",default);
        TestProgram.Check(before==await PrizeMigrationChecks.Snapshot(db),"5.2a failed start rolls back event, stock, preference, audit and intent");
        var access=new EventAccess(db,PresenterChecks.Service(db,clock),clock);
        TestProgram.Check((string?)await db.ScalarAsync("SELECT OwnerId FROM PresenterEventScopes WHERE Kind='prize' AND SourceId=$id",default,("$id",draft.Id))=="admin","6.5 draft belongs to its actual administrator creator");
        await Denied(()=>service.GetAsync(presenter,draft.Id),"6.5 unrelated presenter cannot read an administrator event");
        await Denied(()=>access.InviteAsync(presenter,"prize",draft.Id,"presenter",default),"6.5 unrelated presenter cannot self-invite into another event");
        // Reproduce the formerly implicit DEFAULT Pending=1 explicitly as a negative case.
        await db.ExecuteAsync("UPDATE PresenterAccounts SET Pending=1 WHERE Id='presenter'",default);
        await Denied(()=>access.InviteAsync(admin,"prize",draft.Id,"presenter",default),"6.5 administrator cannot invite an incompletely enrolled target");
        await db.ExecuteAsync("UPDATE PresenterAccounts SET Pending=0 WHERE Id='presenter'",default);
        await access.InviteAsync(admin,"prize",draft.Id,"presenter",default);
        TestProgram.Check(!await access.CanAsync(presenter,"prize",draft.Id,false,default),"6.5 pending invitation grants no access before acceptance");
        var invitation=(await access.InvitationsAsync(presenter,default)).Single();
        await Denied(()=>access.AcceptAsync(admin,invitation.Id,true,default),"6.5 only intended recipient can accept invitation");
        await access.AcceptAsync(presenter,invitation.Id,true,default);
        await Denied(()=>access.AcceptAsync(presenter,invitation.Id,true,default),"6.5 invitation acceptance cannot be replayed");
        TestProgram.Check(await access.CanAsync(presenter,"prize",draft.Id,false,default)&&!await access.CanAsync(presenter,"prize",draft.Id,true,default),"6.5 accepted presenter can present but cannot manage invitations");
        await access.InviteAsync(presenter,"prize",draft.Id,"admin",default);
        await access.AcceptAsync(admin,(await access.InvitationsAsync(admin,default)).Single().Id,true,default);
        TestProgram.Check(Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM PresenterEventMembers WHERE Kind='prize' AND EventId=$event AND AccountId='admin'",default,("$event",draft.Id)))==1,
            "Event member can invite an existing presenter through the service");
        await Denied(()=>service.GetAsync(presenter,started.Id),"6.5 accepted event A never grants access to event B");
        var owner=await Actor("owner","Presenter");var guest=await Actor("guest","Presenter");
        var owned=await service.CreateAsync(owner,Key(),"Owned event","builtin-instant",config,false);
        await Denied(()=>access.InviteAsync(owner,"prize",owned.Id,"owner",default),"6.5 event owner cannot self-invite");
        await Denied(()=>access.InviteAsync(owner,"prize",started.Id,"guest",default),"6.5 owner of A cannot manage invitations for B");
        await access.InviteAsync(owner,"prize",owned.Id,"guest",default);
        await access.AcceptAsync(guest,(await access.InvitationsAsync(guest,default)).Single().Id,true,default);
        TestProgram.Check(await access.CanAsync(guest,"prize",owned.Id,false,default)&&!await access.CanAsync(guest,"prize",draft.Id,false,default),"6.5 non-administrator owner can invite and accepted guest remains event-isolated");
        var ownedDraft=(await service.GetAsync(owner,owned.Id))!;
        await Denied(()=>service.ConfigureAsync(admin,owned.Id,owned.Id,ownedDraft.Version,Key(),config with{GroupSize=16}),"Administrator without event ownership cannot configure Prize policy");
        await Denied(()=>service.ConfigureAsync(guest,owned.Id,owned.Id,ownedDraft.Version,Key(),config with{GroupSize=17}),"Shared event member cannot configure Prize policy");
        var ownerPolicy=await service.ConfigureAsync(owner,owned.Id,owned.Id,ownedDraft.Version,Key(),config with{GroupSize=18});
        TestProgram.Check(ownerPolicy.Configuration.GroupSize==18,"Only the event owner configures Prize policy at the service boundary");
        await Denied(()=>accounts.AccountsAsync(owner,default),"6.5 event ownership never grants global administrator rights");
        await Denied(()=>access.InviteNewAsync(owner,"prize",owned.Id,"scoped-owner-new","Synthetic scoped invite",default),"Event owner cannot create a new presenter account");
        await access.InviteNewAsync(admin,"prize",owned.Id,"scoped-new","Synthetic scoped invite",default);
        TestProgram.Check((string?)await db.ScalarAsync("SELECT Role FROM PresenterAccounts WHERE Login='scoped-new'",default)=="Presenter","Administrator-issued event enrollment grants only the global Presenter role");
        await db.InitializeAsync();
        var legacy=(await access.ListAsync(admin,default)).First(x=>x.Owner is null&&x.State=="Active");
        TestProgram.Check(await accounts.ValidateAsync(admin,default)&&await access.CanAsync(admin,legacy.Kind,legacy.Id,true,default)
            &&await access.CanAsync(admin,"prize",owned.Id,true,default),"6.5 existing global administrator retains management access after repeat migration, including unknown legacy ownership");
        draft=await service.ConfigureAsync(admin,draft.Id,draft.Id,draft.Version,Key(),config with {GroupSize=20,Stock=3,PrizeName="Synthetic prize"});
        TestProgram.Check(!draft.ConfigurationLocked && draft.Configuration.GroupSize==20,"Round capacity and prize policy can be configured while the event is a draft");
        var startKey=Key();var opened=await service.TransitionAsync(presenter,draft.Id,draft.Id,draft.Version,startKey,PrizeEventAction.Start,true);
        TestProgram.Check(opened.Status=="Open" && opened.Version==draft.Version+1,"5.2a second presenter can start a draft with revision");
        TestProgram.Check((await service.TransitionAsync(presenter,draft.Id,draft.Id,draft.Version,startKey,PrizeEventAction.Start,true)).Version==opened.Version,"5.2a transition retry is idempotent before stale revision check");
        await Reject(()=>service.TransitionAsync(admin,opened.Id,opened.Id,opened.Version,Key(),PrizeEventAction.Start,true),"5.2a repeated new start intent is invalid");
        await Reject(()=>service.TransitionAsync(admin,opened.Id,opened.Id,opened.Version,Key(),PrizeEventAction.Close,false),"5.2a lifecycle action requires explicit confirmation");
        await Reject(()=>service.RenameAsync(admin,opened.Id,started.Id,opened.Version,Key(),"Wrong"),"5.2a route/body event mismatch rejected");
        TestProgram.Check((await service.GetAsync(admin,started.Id))!.Name=="Started","5.2a wrong-event request changes neither target");
        var edits=await Task.WhenAll(Enumerable.Range(0,2).Select(i=>Task.Run(async()=>{
            try {await (i==0?service:other).RenameAsync(admin,opened.Id,opened.Id,opened.Version,Key(),"Concurrent edit");return true;}
            catch(PrizeConflictException){return false;}
        })));
        TestProgram.Check(edits.Count(x=>x)==1,"5.2a concurrent presenters commit exactly one matching revision");
        var current=(await service.GetAsync(admin,opened.Id))!;
        foreach(var lockedConfiguration in new[]{current.Configuration with {GroupSize=21},current.Configuration with {Stock=4},current.Configuration with {PrizeName="Changed prize"}})
        {
            var locked=false;
            try { await service.ConfigureAsync(admin,current.Id,current.Id,current.Version,Key(),lockedConfiguration); }
            catch(PrizeConflictException error) when(error.Message=="PrizeSettingsLocked") { locked=true; }
            TestProgram.Check(locked,"Starting the first round rejects capacity and prize policy changes with PrizeSettingsLocked");
        }
        TestProgram.Check((await service.GetAsync(admin,current.Id))!.Configuration==current.Configuration,"Rejected configuration changes preserve the complete prize policy");
        var edited=current;
        TestProgram.Check(edited.Configuration.GroupSize==20 && Convert.ToInt32(await db.ScalarAsync("SELECT GroupSize FROM PrizeEvents WHERE Id=$id",default,("$id",edited.Id)))==20,"5.2a settings snapshot and typed columns change together");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT Total FROM PrizeTypes WHERE EventId=$id",default,("$id",edited.Id)))==3,"5.2a prepared prize stock changes without issuing prizes");
        var profileKey=Key();var profile=await service.SaveProfileAsync(admin,edited.Id,edited.Id,edited.Version,profileKey,"Custom template");
        TestProgram.Check(await service.SaveProfileAsync(admin,edited.Id,edited.Id,edited.Version,profileKey,"Custom template")==profile,"5.2a save-as-new-profile is idempotent");
        var saved=(await service.ProfilesAsync(presenter)).Single(x=>x.Id==profile);
        TestProgram.Check(saved.Configuration.GroupSize==20 && (await service.DefaultProfileAsync(admin)).Id=="builtin-draw","5.2a explicit profile snapshot does not change remembered selection");
        current=(await service.GetAsync(admin,edited.Id))!;
        var closed=await service.TransitionAsync(admin,current.Id,current.Id,current.Version,Key(),PrizeEventAction.Close,true);
        TestProgram.Check(closed.Status=="Closed" && closed.PurgeAt==clock.GetUtcNow().AddDays(30).ToUnixTimeMilliseconds(),"5.2a first close fixes retention deadline");
        await Reject(()=>service.ConfigureAsync(admin,closed.Id,closed.Id,closed.Version,Key(),config),"5.2a closed settings are immutable");
        await Reject(()=>service.RenameAsync(presenter,closed.Id,closed.Id,closed.Version,Key(),"Denied edit"),"6.5 linked presenter cannot change owner settings");
        var renamed=await service.RenameAsync(admin,closed.Id,closed.Id,closed.Version,Key(),"Closed name");
        var reopened=await service.TransitionAsync(admin,renamed.Id,renamed.Id,renamed.Version,Key(),PrizeEventAction.Reopen,true);
        TestProgram.Check(reopened.Status=="Open" && reopened.PurgeAt==closed.PurgeAt && reopened.ClosedAt==closed.ClosedAt,"5.2a reopen preserves original retention and history");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM PrizeGroups WHERE EventId=$event AND Number=1 AND Capacity=20",default,("$event",reopened.Id)))==1,"Reopening preserves the initial round and its configured capacity");
        TestProgram.Check((await service.GetAsync(presenter,reopened.Id))!.ConfigurationLocked,"5.2a existing group locks configuration before participation");
        await Reject(()=>service.ConfigureAsync(admin,reopened.Id,reopened.Id,reopened.Version,Key(),config),"5.2a frozen settings rejected server-side");
        var closedAgain=await service.TransitionAsync(admin,reopened.Id,reopened.Id,reopened.Version,Key(),PrizeEventAction.Close,true);
        TestProgram.Check(closedAgain.PurgeAt==closed.PurgeAt,"5.2a repeated close cannot extend retention");
        foreach(var invalid in new[]{config with {GroupSize=0},config with {WinnersPerGroup=16},config with {Visibility="Public"},config with {RetentionDays=91},config with {ClaimFieldSchemaJson="{\"Unknown\":\"Required\"}"}})
            await Reject(()=>service.CreateAsync(admin,Key(),"Invalid",PrizeProfiles.Cybertruck,invalid,true),"5.2a invalid configuration rejected");
        var fromProfile=await service.CreateAsync(presenter,Key(),"Custom",profile,saved.Configuration,true);
        TestProgram.Check((await service.DefaultProfileAsync(admin)).Id=="builtin-draw","6.5 ordinary presenter start cannot change a global profile preference");
        await db.ExecuteAsync("DELETE FROM PrizeProfiles WHERE Id=$id",default,("$id",profile));
        TestProgram.Check((await service.DefaultProfileAsync(admin)).Id=="builtin-draw","6.5 deleting an unused profile leaves the global preference intact");
        TestProgram.Check((await service.GetAsync(admin,fromProfile.Id))!.Configuration.GroupSize==20,"5.2a profile deletion preserves event snapshot");
        clock.Advance(TimeSpan.FromDays(31));
        await Reject(()=>service.TransitionAsync(admin,closedAgain.Id,closedAgain.Id,closedAgain.Version,Key(),PrizeEventAction.Reopen,true),"5.2a expired retention prevents reopen");
        await Reject(()=>service.RenameAsync(admin,closedAgain.Id,closedAgain.Id,closedAgain.Version,Key(),"Expired"),"5.2a expired event cannot mutate");
        await db.ExecuteAsync("UPDATE PresenterAuthSessions SET Reauthenticated=$now WHERE AccountId='admin'",default,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        await access.TransferAsync(admin,"prize",fromProfile.Id,"admin","recover",default);
        await db.ExecuteAsync("UPDATE PresenterAccounts SET Active=0 WHERE Id='presenter'",default);
        await Reject(()=>service.ListAsync(presenter),"5.2a management revalidates deactivated account");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM PrizeEvents WHERE PrizeEnabled!=0",default))==0,"5.2a every event remains disabled for public prize participation");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT (SELECT COUNT(*) FROM PrizeParticipants)+(SELECT COUNT(*) FROM PrizeDraws)+(SELECT COUNT(*) FROM PrizeClaims)+(SELECT COUNT(*) FROM PrizeInventoryLedger)",default))==0,"5.2a management never registers, draws, claims or issues");
        TestProgram.Check(await db.ScalarAsync("PRAGMA foreign_key_check",default) is null && (string?)await db.ScalarAsync("PRAGMA integrity_check",default)=="ok","5.2a management preserves foreign keys and integrity");
        TestProgram.Check((await service.ListAsync(admin)).Events.Count>=4,"5.2a overview includes multiple independently managed events");
    }
}
