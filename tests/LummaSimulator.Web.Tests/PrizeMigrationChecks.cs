using System.Text.Json;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Tests;

internal static class PrizeMigrationChecks
{
    internal static async Task<string> Snapshot(DemoStore store,bool legacyOnly=false)
    {
        await using var c=await store.OpenAsync(default);using var q=c.CreateCommand();
        q.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' "+
            (legacyOnly?"AND name NOT LIKE 'Prize%' AND name NOT LIKE 'Identity%' AND name!='ExperienceMigrations' ":"")+"ORDER BY name";
        var names=new List<string>();using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())names.Add(r.GetString(0));
        // These additive, separately validated tables are not pre-upgrade records.
        // Keep the full snapshot (including schema) unchanged for rollback/idempotency checks.
        if(legacyOnly)names.RemoveAll(name=>EventAccessMigration.Expected.ContainsKey("table:"+name));
        var result=new List<object>();
        foreach(var name in names) {
            q.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\" ORDER BY rowid";
            using var r=await q.ExecuteReaderAsync();
            while(await r.ReadAsync()){var row=new object[r.FieldCount];r.GetValues(row);result.Add(new{name,row});}
        }
        if(!legacyOnly) {
            q.CommandText="SELECT type,name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
            using var r=await q.ExecuteReaderAsync();
            while(await r.ReadAsync())result.Add(new { schema=r.GetString(0),name=r.GetString(1),sql=r.GetString(2) });
        }
        return JsonSerializer.Serialize(result);
    }
    private sealed record TableState(string[] Columns,string[][] Rows);
    private static async Task<Dictionary<string,TableState>> LegacyTables(DemoStore store,IEnumerable<string>? tables=null)
    {
        await using var c=await store.OpenAsync(default);using var tx=c.BeginTransaction(deferred:true);using var q=c.CreateCommand();q.Transaction=tx;
        var names=new List<string>();
        if(tables is not null)names.AddRange(tables);
        else {
            q.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'Prize%' AND name NOT LIKE 'Identity%' AND name!='ExperienceMigrations' ORDER BY name";
            using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync())names.Add(r.GetString(0));
            names.RemoveAll(name=>EventAccessMigration.Expected.ContainsKey("table:"+name));
        }
        var result=new Dictionary<string,TableState>(StringComparer.Ordinal);
        foreach(var name in names){
            q.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\" ORDER BY rowid";
            using var r=await q.ExecuteReaderAsync();var columns=Enumerable.Range(0,r.FieldCount).Select(r.GetName).ToArray();var rows=new List<string[]>();
            while(await r.ReadAsync())rows.Add(Enumerable.Range(0,r.FieldCount).Select(i=>JsonSerializer.Serialize(r.GetValue(i))).ToArray());
            result.Add(name,new(columns,rows.ToArray()));
        }
        return result;
    }
    private static string Category(string table)=>table switch {
        "Demos"=>"visitor accounts, handles and session bindings",
        "PresenterAccounts"=>"presenter accounts, roles and activation",
        "Credentials" or "PresenterKeys"=>"WebAuthn credential bytes, signature counters and backup flags",
        "Challenges" or "PresenterCeremonies"=>"pending ceremonies and operation bindings",
        "PresenterSessions" or "PresenterAuthSessions" or "PresenterInvitations" or "PresenterEnrollmentGrants" or "MonitorGrants"=>"sessions, enrollment authorizations and monitor grants",
        "ExperienceEvents" or "VisitorCounts" or "DailyExperienceStats" or "EventPrizePolicies"=>"statistics, visitor numbering and event policies",
        _=>"existing records"
    };
    private static async Task Preserved(DemoStore store,Dictionary<string,TableState> before,string stage)
    {
        // Compare the exact pre-upgrade tables, all columns and cells, including empty tables.
        // Diagnostics name only the table/columns/counts, never session or credential values.
        var after=await LegacyTables(store,before.Keys);
        foreach(var (name,expected) in before){
            var actual=after[name];
            TestProgram.Check(expected.Columns.SequenceEqual(actual.Columns),$"{stage}: {name} column contract preserved ({Category(name)})");
            var changed=Enumerable.Range(0,expected.Columns.Length).Where(i=>!expected.Rows.Select(row=>row[i]).SequenceEqual(actual.Rows.Select(row=>row[i])))
                .Select(i=>expected.Columns[i]).ToArray();
            TestProgram.Check(expected.Rows.Length==actual.Rows.Length&&changed.Length==0,
                $"{stage}: {name} preserved ({Category(name)}); rows {expected.Rows.Length}->{actual.Rows.Length}; changed columns: {(changed.Length==0?"none":string.Join(", ",changed))}");
        }
    }
    internal static async Task ExpectReject(Func<Task> action,string description)
    {
        var rejected=false;
        try{await action();}catch(InvalidOperationException){rejected=true;}
        TestProgram.Check(rejected,description);
    }
    internal static async Task SeedLegacy(DemoStore store)
    {
        await store.InitializeLegacyAsync();
        await store.ExecuteAsync("""
            INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen,Successes,Failures,LastAuthenticatedAt)
              VALUES('visitor','synthetic-run','Visitor-KEEP',X'01020304','synthetic-session','yubikey',1,2,3,4,2);
            INSERT INTO Credentials VALUES('synthetic-key','visitor',X'05060708',8,1,0,'Reserve');
            INSERT INTO Challenges VALUES('visitor','synthetic-session','authenticate','{}',9999999999999,'valid-operation');
            INSERT INTO MonitorGrants VALUES('visitor',9999999999999);
            INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Activated)
              VALUES('actor','synthetic-presenter','Synthetic Presenter','Presenter',X'09080706',1,1,1);
            INSERT INTO PresenterKeys VALUES('presenter-key','presenter-ref','actor',X'0102',7,0,0,'Primary',1,2);
            INSERT INTO PresenterAuthSessions VALUES('synthetic-presenter-session','actor',9999999999999,NULL);
            INSERT INTO PresenterCeremonies VALUES('browser',NULL,'login','presenter-operation','{}',9999999999999,'binding');
            UPDATE ExperienceEvents SET Visitors=9,Authentications=12 WHERE Status='Active';
            INSERT INTO VisitorCounts(DemoId,EventId,VisitorNumber) SELECT 'visitor',Id,9 FROM ExperienceEvents WHERE Status='Active';
            INSERT INTO DailyExperienceStats VALUES('2026-10-30',9,12);
            """,default);
    }
    internal static async Task RunAsync(string root)
    {
        using(var empty=new DemoStore(Path.Combine(root,"prize-empty.db")))
        {
            await empty.InitializeAsync();
            TestProgram.Check(Convert.ToInt32(await empty.ScalarAsync("SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=6",default))==1,
                "Empty database initializes through fixed migration 6");
            var first=await Snapshot(empty);await empty.InitializeAsync();
            TestProgram.Check(first==await Snapshot(empty),"Repeated initialization preserves all rows and schema exactly");
            TestProgram.Check(await empty.ScalarAsync("PRAGMA foreign_key_check",default) is null
                && (string?)await empty.ScalarAsync("PRAGMA integrity_check",default)=="ok","Prize schema passes SQLite integrity and FK checks");
        }
        using(var v5=new DemoStore(Path.Combine(root,"prize-v5.db")))
        {
            await SeedLegacy(v5);
            await v5.ExecuteAsync("""
                INSERT INTO PresenterSessions VALUES('synthetic-legacy-presenter-session',9999999999999);
                INSERT INTO PresenterInvitations VALUES('synthetic-invitation','actor',9999999999999,0);
                INSERT INTO PresenterEnrollmentGrants VALUES('synthetic-enrollment','actor',9999999999999,0);
                INSERT INTO PresenterCeremonies VALUES('synthetic-enroll-browser','actor','enroll','synthetic-enroll-operation','{}',9999999999999,'synthetic-enroll-binding');
                """,default);
            var original=await Snapshot(v5,true);var records=await LegacyTables(v5);
            TestProgram.Check(Convert.ToInt32(await v5.ScalarAsync("SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=6",default))==0,
                "Synthetic source is a genuine version-5 fixture");
            var entire=await Snapshot(v5);
            await ExpectReject(()=>PrizeMigration.InitializeAsync(v5,default,()=>throw new InvalidOperationException("Synthetic interruption")),
                "Interrupted migration is rejected");
            TestProgram.Check(entire==await Snapshot(v5),"Interrupted DDL/seeds/marker transaction leaves no partial migration");
            await ExpectReject(()=>PrizeMigration.InitializeAsync(v5,default,beforeMarker:()=>throw new InvalidOperationException("Synthetic marker failure")),
                "Failure after seeds and schema contract aborts before marker commit");
            TestProgram.Check(entire==await Snapshot(v5),"Seed data and schema identity also roll back with the DDL");
            // Exercise the separate phase-6.5 transaction, not only Prize's transaction.
            await PrizeMigration.InitializeAsync(v5,default);await IdentityMigration.InitializeAsync(v5,default);
            await Preserved(v5,records,"Prize and Identity upgrade");var beforeEventAccess=await Snapshot(v5);
            await ExpectReject(()=>EventAccessMigration.InitializeAsync(v5,default,beforeCommit:()=>throw new InvalidOperationException("Synthetic event migration interruption")),
                "Interrupted phase-6.5 migration is rejected");
            TestProgram.Check(beforeEventAccess==await Snapshot(v5),"Phase-6.5 rollback preserves all rows, schema, index and markers");
            await Preserved(v5,records,"Interrupted phase-6.5 upgrade");
            await v5.InitializeAsync();
            await Preserved(v5,records,"Completed upgrade");
            TestProgram.Check(original==await Snapshot(v5,true),
                "Migration preserves visitor/presenter accounts, credential bytes/counters, sessions, ceremonies and statistics");
            TestProgram.Check(Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM PresenterEventMigrations WHERE Version=1",default))==1
                &&Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM PresenterInstallation WHERE Id=1 AND Existing=1 AND HostingConfirmed=0 AND FirstEvent IS NULL AND CompletedAt IS NULL",default))==1,
                "Phase-6.5 adds exactly the expected migration and existing-installation state");
            TestProgram.Check(Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM PresenterEventScopes",default))==Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM ExperienceEvents",default))
                &&Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM PresenterEventScopes WHERE Kind!='experience' OR OwnerId IS NOT NULL",default))==0
                &&Convert.ToInt64(await v5.ScalarAsync("SELECT COUNT(*) FROM PresenterDemoContexts x JOIN VisitorCounts v ON v.DemoId=x.DemoId WHERE x.Kind='experience' AND x.EventId=v.EventId",default))==1,
                "Phase-6.5 additions map the existing demo and events without inferring an owner");
            var once=await Snapshot(v5);await v5.InitializeAsync();
            TestProgram.Check(once==await Snapshot(v5),"Version-6 reopening does not reseed profiles or change legacy state");
            await Preserved(v5,records,"Repeated upgrade");
            TestProgram.Check(await v5.ScalarAsync("PRAGMA foreign_key_check",default) is null
                &&(string?)await v5.ScalarAsync("PRAGMA integrity_check",default)=="ok","Upgraded historical fixture passes SQLite integrity and FK checks");
        }
        foreach(var (name,sql) in new (string,string)[] {
            ("column","ALTER TABLE Demos ADD COLUMN Unexpected TEXT"),
            ("index","CREATE INDEX UnexpectedIndex ON Credentials(Counter)"),
            ("trigger","CREATE TRIGGER UnexpectedTrigger AFTER UPDATE ON Demos BEGIN SELECT 1; END"),
            ("table","CREATE TABLE UnexpectedTable(Id TEXT)"),
            ("future","INSERT INTO ExperienceMigrations VALUES(7)"),
            ("marker","INSERT INTO ExperienceMigrations VALUES(6)"),
            ("orphan","CREATE TABLE PrizeOrphan(Id TEXT)") })
        {
            using var bad=new DemoStore(Path.Combine(root,"prize-bad-"+name+".db"));await SeedLegacy(bad);
            await bad.ExecuteAsync(sql,default);var before=await Snapshot(bad);
            await ExpectReject(()=>bad.InitializeAsync(),"Preflight refuses unexpected "+name);
            TestProgram.Check(before==await Snapshot(bad),"Rejected "+name+" performs no repair or data changes");
        }
        using(var historic=new DemoStore(Path.Combine(root,"prize-v3-missing-index.db")))
        {
            await historic.InitializeLegacyAsync();
            await historic.ExecuteAsync("DELETE FROM ExperienceMigrations WHERE Version IN(4,5); DROP INDEX OneActiveExperienceEvent",default);
            var before=await Snapshot(historic);
            await ExpectReject(()=>historic.InitializeAsync(),"Historical marker 3 cannot hide a missing required index");
            TestProgram.Check(before==await Snapshot(historic),"Historical preflight failure does not repair or mutate data");
        }
        using(var conflict=new DemoStore(Path.Combine(root,"prize-identity.db")))
        {
            await conflict.InitializeAsync();
            await conflict.ExecuteAsync("UPDATE PrizeSchemaContract SET Fingerprint='conflicting-build'",default);
            await ExpectReject(()=>conflict.InitializeAsync(),"Version 6 with a conflicting fingerprint is refused");
        }
        using(var changed=new DemoStore(Path.Combine(root,"prize-modified-schema.db")))
        {
            await changed.InitializeAsync();
            await changed.ExecuteAsync("ALTER TABLE PrizeGroups ADD COLUMN Unexpected INTEGER",default);
            await ExpectReject(()=>changed.InitializeAsync(),"Modified version-6 table is refused despite the existing marker");
        }
        using(var missing=new DemoStore(Path.Combine(root,"prize-missing-schema.db")))
        {
            await missing.InitializeAsync();await missing.ExecuteAsync("DROP INDEX PrizeOneOpenGroup",default);
            await ExpectReject(()=>missing.InitializeAsync(),"Missing version-6 index is refused");
        }
        using(var concurrent=new DemoStore(Path.Combine(root,"prize-init-race.db")))
        {
            await concurrent.InitializeLegacyAsync();
            using var peer=new DemoStore(concurrent.DatabasePath);
            await Task.WhenAll(Task.Run(()=>concurrent.InitializeAsync()),Task.Run(()=>peer.InitializeAsync()));
            TestProgram.Check(Convert.ToInt32(await concurrent.ScalarAsync("SELECT COUNT(*) FROM PrizeProfiles",default))==4,
                "Independent initializers create one foundation and four profiles");
        }
    }
}
