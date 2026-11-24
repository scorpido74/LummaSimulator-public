using System.Security.Claims;
using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Tests;

internal static class PrizeStoreChecks
{
    private static async Task SqlReject(DemoStore db,string sql,string message)
    {
        var rejected=false;try{await db.ExecuteAsync(sql,default);}catch(SqliteException){rejected=true;}
        TestProgram.Check(rejected,message);
    }
    private static async Task Conflict(Func<Task> action,string message)
    {
        var rejected=false;try{await action();}catch(PrizeConflictException){rejected=true;}
        TestProgram.Check(rejected,message);
    }
    internal static async Task RunAsync(string root)
    {
        using var db=new DemoStore(Path.Combine(root,"prize-store.db"));await db.InitializeAsync();
        var clock=new TestClock();var secret=DemoIdentity.RandomToken();var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        await db.ExecuteAsync("""
            INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Activated)
            VALUES('actor','prize-test','Synthetic Presenter','Presenter',X'012345',1,1,1);
            INSERT INTO PresenterAuthSessions VALUES($hash,'actor',$expires,NULL);
            INSERT INTO PresenterKeys(Id,Ref,AccountId,PublicKey,Counter,BackupEligible,BackedUp,Name,Registered) VALUES('synthetic-store-key','synthetic-store-ref','actor',X'01',0,0,0,'Synthetic service fixture',1);
            """,default,("$hash",DemoIdentity.Hash(secret)),("$expires",now+600000));
        var identity=new ClaimsIdentity(new[]{
            new Claim(ClaimTypes.NameIdentifier,"actor"),new Claim(ClaimTypes.Role,"Presenter"),
            new Claim("amr","webauthn"),new Claim("sid",secret)},PresenterService.Scheme);
        var principal=new ClaimsPrincipal(identity);
        PrizeStore Store(DemoStore d)=>new(d,new PrizeAuthorization(PresenterChecks.Service(d,clock)),clock);
        var store=Store(db);var profile=await store.DefaultProfileAsync();
        TestProgram.Check(profile.Id==PrizeProfiles.Cybertruck && profile.Configuration.GroupSize==15
            && profile.Configuration.WinnersPerGroup==1 && profile.Configuration.AnimationSeconds==10
            && profile.Configuration.ReactionSeconds==120 && profile.Configuration.RetentionDays==30
            && profile.Configuration.PostCloseClaimSeconds==86400 && !profile.Configuration.StartCodeRequired
            && profile.Configuration.NextGroupMode=="Manual","First default profile is Cybertruck with requested defaults");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM PrizeProfiles WHERE BuiltIn=1",default))==4,
            "All four built-in profiles are initialized exactly once");
        await db.ExecuteAsync("UPDATE PrizeInstallationSettings SET LastProfileId='builtin-instant',Version=Version+1",default);
        TestProgram.Check((await store.DefaultProfileAsync()).Id=="builtin-instant","Last profile preference is installation-wide");
        await db.InitializeAsync();
        TestProgram.Check((await store.DefaultProfileAsync()).Id=="builtin-instant","Reinitialization preserves last profile");
        var key=Guid.NewGuid().ToString("D");
        var a=await store.CreateDraftAsync(principal,key,"  Event A  ",PrizeProfiles.Cybertruck);
        var again=await store.CreateDraftAsync(principal,key,"Event A",PrizeProfiles.Cybertruck);
        TestProgram.Check(a.Id==again.Id,"Same key and canonical payload return the same draft");
        await Conflict(()=>store.CreateDraftAsync(principal,key,"Other",PrizeProfiles.Cybertruck),"Same key with different payload conflicts");
        using var peer=new DemoStore(db.DatabasePath);var other=Store(peer);
        var raceKey=Guid.NewGuid().ToString("D");
        var racers=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>
            (i%2==0?store:other).CreateDraftAsync(principal,raceKey,"Concurrent",PrizeProfiles.Cybertruck))));
        TestProgram.Check(racers.Select(r=>r.Id).Distinct().Count()==1
            && Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM PrizeCreationOperations WHERE IdempotencyKey=$key",default,("$key",raceKey)))==1,
            "Independent concurrent connections cannot duplicate one creation intent");
        var failedKey=Guid.NewGuid().ToString("D");var before=await PrizeMigrationChecks.Snapshot(db);
        await PrizeMigrationChecks.ExpectReject(()=>store.CreateDraftAsync(principal,failedKey,"Interrupted",PrizeProfiles.Cybertruck,
            afterInsert:()=>throw new InvalidOperationException("Synthetic interruption")),"Interrupted draft transaction fails");
        TestProgram.Check(before==await PrizeMigrationChecks.Snapshot(db),"Interrupted creation leaves no event, audit or operation");
        var recovered=await store.CreateDraftAsync(principal,failedKey,"Interrupted",PrizeProfiles.Cybertruck);
        TestProgram.Check(!string.IsNullOrEmpty(recovered.Id),"Same intent can retry after a full rollback");
        await db.ExecuteAsync("UPDATE PrizeProfiles SET ConfigJson=json_set(ConfigJson,'$.GroupSize',20),Version=Version+1 WHERE Id='builtin-cybertruck'",default);
        var unchanged=await store.CreateDraftAsync(principal,key,"Event A",PrizeProfiles.Cybertruck);
        TestProgram.Check(unchanged.Snapshot==a.Snapshot,"Retry retains original snapshot after profile modification");
        await db.InitializeAsync();
        TestProgram.Check((await store.DefaultProfileAsync()).Id=="builtin-instant","Draft storage never counts as successful event start");
        var b=await other.CreateDraftAsync(principal,Guid.NewGuid().ToString("D"),"Event B",PrizeProfiles.Cybertruck);
        await db.ExecuteAsync("""
            INSERT INTO PrizeGroups VALUES('group-a',$a,1,'Open',15,0,NULL,1);
            INSERT INTO PrizeGroups VALUES('group-b',$b,1,'Open',15,0,NULL,1);
            INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen)
              VALUES('visitor','prize-synthetic-run','Visitor-SYNTH',X'0102','visitor-session','yubikey',1,1);
            """,default,("$a",a.Id),("$b",b.Id));
        // Values below are server-generated UUIDs, never user input.
        await SqlReject(db,$"INSERT INTO PrizeParticipants VALUES('cross','{a.Id}','visitor','group-b',1,'Active',1,NULL,1)",
            "Composite participant/group FK rejects cross-event assignment");
        await SqlReject(db,$"INSERT INTO PrizeGroups VALUES('too-full','{a.Id}',2,'Closed',15,16,NULL,1)",
            "Database capacity cannot exceed the configured maximum");
        await SqlReject(db,"UPDATE PrizeGroups SET Status='Unknown' WHERE Id='group-a'","Unknown group status rejected");
        await SqlReject(db,$"INSERT INTO PrizeGroups VALUES('second-open','{a.Id}',2,'Open',15,0,NULL,1)",
            "Only one open group per event");
        await db.ExecuteAsync("INSERT INTO PrizeTypes VALUES('last',$a,'Last prize',1,0,0,1,1)",default,("$a",a.Id));
        await SqlReject(db,"UPDATE PrizeTypes SET Reserved=2 WHERE Id='last'","Stock constraint refuses oversubscription");
        await SqlReject(db,$"UPDATE PrizeEvents SET FixedPrizeTypeId='last' WHERE Id='{b.Id}'",
            "Fixed prize reference cannot cross event boundaries");
        await db.ExecuteAsync("INSERT INTO PrizeParticipants VALUES('participant-a',$a,'visitor','group-a',1,'Active',1,NULL,1); INSERT INTO PrizeParticipants VALUES('participant-b',$b,'visitor','group-b',1,'Active',1,NULL,1)",default,("$a",a.Id),("$b",b.Id));
        await SqlReject(db,$"INSERT INTO PrizeParticipants VALUES('duplicate','{a.Id}','visitor','group-a',2,'Cancelled',1,2,1)",
            "One account may join different events but never twice in one event");

        async Task<int> Reserve(DemoStore d) {
            await using var c=await d.OpenAsync(default);using var tx=c.BeginTransaction(deferred:false);
            using var q=PrizeStore.Command(c,tx,"UPDATE PrizeTypes SET Reserved=Reserved+1,Version=Version+1 WHERE Id='last' AND Reserved+Issued<Total");
            var count=await q.ExecuteNonQueryAsync();tx.Commit();return count;
        }
        var reservations=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>Reserve(i%2==0?db:peer))));
        TestProgram.Check(reservations.Sum()==1,"Independent writers reserve a final stock unit exactly once (storage primitive)");
        var edits=await Task.WhenAll(Enumerable.Range(0,2).Select(i=>Task.Run(async()=>{
            try{await (i==0?store:other).RenameDraftAsync(principal,a.Id,1,"Edited");return true;}
            catch(PrizeConflictException){return false;}
        })));
        TestProgram.Check(edits.Count(x=>x)==1,"Optimistic concurrency commits one edit and rejects stale version");
        await db.ExecuteAsync("INSERT INTO PrizePresentationSessions VALUES('screen-session',1,9999999999999,NULL); INSERT INTO PrizePresentationGrants VALUES('grant-a',$a,'grant-hash',9999999999999,NULL,1)",default,("$a",a.Id));
        await SqlReject(db,$"INSERT INTO PrizePresentationSessionGrants VALUES('{b.Id}','screen-session','grant-a')",
            "Presentation session cannot bind a grant to a different event");
        TestProgram.Check(Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM PrizeEvents WHERE PrizeEnabled!=0 OR Status!='Draft'",default))==0,
            "All foundation-created events stay disabled Drafts");
        await db.ExecuteAsync("DELETE FROM PresenterAuthSessions WHERE AccountId='actor'",default);
        await TestProgram.RejectAsync(()=>store.CreateDraftAsync(principal,Guid.NewGuid().ToString("D"),"Denied",PrizeProfiles.Cybertruck),
            "Storage revalidates revoked presenter session inside the transaction");
        TestProgram.Check(await db.ScalarAsync("PRAGMA foreign_key_check",default) is null,"Storage concurrency and rejection tests preserve all FKs");
        TestProgram.Check(!PrizeTransitions.Event(PrizeEventStatus.Purging,PrizeEventStatus.Open)
            && !PrizeTransitions.Claim(PrizeClaimState.Redeemed,PrizeClaimState.Forfeited)
            && !PrizeTransitions.Group(PrizeGroupStatus.Frozen,PrizeGroupStatus.Open),"Terminal transition contracts reject reopening");
    }
}
