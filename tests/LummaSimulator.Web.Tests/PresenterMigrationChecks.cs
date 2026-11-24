using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;

namespace LummaSimulator.Web.Tests;

internal static class PresenterMigrationChecks
{
    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
    private static async Task<string> Snapshot(DemoStore store)
    {
        await using var c=await store.OpenAsync(default);using var q=c.CreateCommand();
        q.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT IN ('ExperienceMigrations','sqlite_sequence') ORDER BY name";
        var names=new List<string>();using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())names.Add(r.GetString(0));
        var rows=new List<object>();foreach(var name in names) {
            q.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\" ORDER BY rowid";
            using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync()){var values=new object[r.FieldCount];r.GetValues(values);rows.Add(new{name,values});}
        }
        return JsonSerializer.Serialize(rows);
    }
    internal static async Task RunAsync(string root,DemoStore source,TestClock clock,ClaimsPrincipal admin,
        SoftwareAuthenticator primary,SoftwareAuthenticator reserve,SoftwareAuthenticator personKey)
    {
        // Construct a v4 snapshot ONLY in an isolated synthetic test database. Never open live data.
        using var target=new DemoStore(Path.Combine(root,"presenter-v4-upgrade.db"));
        await using(var original=await source.OpenAsync(default))
        await using(var copy=await target.OpenAsync(default))original.BackupDatabase(copy);
        var s=PresenterChecks.Service(target,clock);var id=admin.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var reBrowser=DemoIdentity.RandomToken();var reOp=Guid.NewGuid().ToString("D");
        var reOptions=await s.OptionsAsync(reBrowser,reOp,"reauth","",null,admin,true,default);
        // A pending legacy login retains its original allowlist and account binding on upgrade.
        var loginBrowser=DemoIdentity.RandomToken();var loginOp=Guid.NewGuid().ToString("D");
        var node=JsonNode.Parse(await s.OptionsAsync(loginBrowser,loginOp,"login","",null,Anonymous,true,default))!;
        node["allowCredentials"]=JsonNode.Parse(reOptions)!["allowCredentials"]!.DeepClone();var loginOptions=node.ToJsonString();
        await target.ExecuteAsync("UPDATE PresenterCeremonies SET AccountId=$id,Options=$options WHERE BrowserHash=$browser",default,("$id",id),("$options",loginOptions),("$browser",DemoIdentity.Hash(loginBrowser)));
        var wrongBrowser=DemoIdentity.RandomToken();var wrongOp=Guid.NewGuid().ToString("D");
        var wrongOptions=await s.OptionsAsync(wrongBrowser,wrongOp,"login","",null,Anonymous,true,default);
        await target.ExecuteAsync("UPDATE PresenterCeremonies SET AccountId=$id WHERE BrowserHash=$browser",default,("$id",id),("$browser",DemoIdentity.Hash(wrongBrowser)));
        // Mint an existing account-bound enrollment after fresh reauthentication, but leave it pending.
        var freshBrowser=DemoIdentity.RandomToken();var freshOp=Guid.NewGuid().ToString("D");
        var fresh=await s.OptionsAsync(freshBrowser,freshOp,"reauth","",null,admin,true,default);
        await s.VerifyAsync(freshBrowser,freshOp,"reauth",primary.Authenticate(fresh),"",null,admin,true,default);
        var enBrowser=DemoIdentity.RandomToken();var enOp=Guid.NewGuid().ToString("D");
        var enOptions=await s.OptionsAsync(enBrowser,enOp,"enroll","",null,admin,true,default);
        await target.ExecuteAsync("""
            BEGIN IMMEDIATE;
            CREATE TABLE PresenterCeremonies_v4 (
              BrowserHash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Kind TEXT NOT NULL,
              OperationId TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, Binding TEXT NOT NULL);
            INSERT INTO PresenterCeremonies_v4 SELECT * FROM PresenterCeremonies;
            DROP TABLE PresenterCeremonies;
            ALTER TABLE PresenterCeremonies_v4 RENAME TO PresenterCeremonies;
            DELETE FROM ExperienceMigrations WHERE Version=5;
            COMMIT;
            """,default);
        var before=await Snapshot(target);
        await target.ExecuteAsync("CREATE TRIGGER FailDiscoverableMarker BEFORE INSERT ON ExperienceMigrations WHEN NEW.Version=5 BEGIN SELECT RAISE(ABORT,'Synthetic migration failure'); END",default);
        var rolledBack=false;try {await PresenterService.InitializeAsync(target,default);}catch(Microsoft.Data.Sqlite.SqliteException){rolledBack=true;}
        TestProgram.Check(rolledBack && before==await Snapshot(target)
            && Convert.ToInt32(await target.ScalarAsync("SELECT \"notnull\" FROM pragma_table_info('PresenterCeremonies') WHERE name='AccountId'",default))==1
            && Convert.ToInt32(await target.ScalarAsync("SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=5",default))==0,
            "Failure after table replacement rolls back schema, every active ceremony and the migration marker together");
        await target.ExecuteAsync("DROP TRIGGER FailDiscoverableMarker",default);
        TestProgram.Check(Convert.ToInt32(await target.ScalarAsync("SELECT \"notnull\" FROM pragma_table_info('PresenterCeremonies') WHERE name='AccountId'",default))==1,
            "Upgrade fixture starts with the old NOT NULL account binding and active ceremonies");
        await PresenterService.InitializeAsync(target,default);
        TestProgram.Check(before==await Snapshot(target),"Upgrade preserves all existing accounts, keys, sessions, active ceremonies, audit and visitor/statistical rows byte-for-byte");
        TestProgram.Check(Convert.ToInt32(await target.ScalarAsync("SELECT COUNT(*) FROM pragma_foreign_key_list('PresenterCeremonies') WHERE \"table\"='PresenterAccounts' AND \"from\"='AccountId'",default))==1
            && await target.ScalarAsync("PRAGMA foreign_key_check",default) is null,"Nullable migration retains the account foreign key and all foreign keys remain valid");
        await PresenterService.InitializeAsync(target,default);
        TestProgram.Check(before==await Snapshot(target) && Convert.ToInt32(await target.ScalarAsync("SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=5",default))==1,"Discoverable login migration is repeatable with exactly one new marker");
        var signed=await s.VerifyAsync(loginBrowser,loginOp,"login",primary.Authenticate(loginOptions),"",null,Anonymous,true,default);
        TestProgram.Check(await s.ValidateAsync(signed!,default),"Pending pre-upgrade account-bound login still verifies after migration");
        await TestProgram.RejectAsync(()=>s.VerifyAsync(wrongBrowser,wrongOp,"login",personKey.Authenticate(wrongOptions),"",null,Anonymous,true,default),"Legacy account binding rejects a credential belonging to another presenter");
        await s.VerifyAsync(reBrowser,reOp,"reauth",reserve.Authenticate(reOptions),"",null,admin,true,default);
        using var extra=new SoftwareAuthenticator();
        await s.VerifyAsync(enBrowser,enOp,"enroll",extra.Register(enOptions),"Upgrade test key",null,admin,true,default);
        TestProgram.Check((await s.KeysAsync(admin,null,default)).Count==3,"Pending pre-upgrade reauthentication and enrollment retain their original session binding");
        foreach(var kind in new[]{"reauth","enroll"}) {
            var blocked=false;try {await target.ExecuteAsync("INSERT INTO PresenterCeremonies VALUES('synthetic',NULL,$kind,'op','{}',0,'binding')",default,("$kind",kind));}catch(Microsoft.Data.Sqlite.SqliteException){blocked=true;}
            TestProgram.Check(blocked,"Database constraint forbids account-less "+kind);
        }

        // Rollback on an unexpected dependent FK: no temporary schema or marker may escape.
        using var failed=new DemoStore(Path.Combine(root,"presenter-migration-blocked.db"));
        await using(var original=await source.OpenAsync(default))
        await using(var copy=await failed.OpenAsync(default))original.BackupDatabase(copy);
        await failed.ExecuteAsync("DELETE FROM ExperienceMigrations WHERE Version=5; CREATE TABLE UnexpectedCeremonyReference(Id TEXT REFERENCES PresenterCeremonies(BrowserHash));",default);
        var snapshot=await Snapshot(failed);var rejected=false;
        try {await PresenterService.InitializeAsync(failed,default);}catch(InvalidOperationException){rejected=true;}
        TestProgram.Check(rejected && snapshot==await Snapshot(failed)
            && Convert.ToInt32(await failed.ScalarAsync("SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=5",default))==0,
            "Unexpected incoming foreign key aborts migration transactionally without losing data or adding a marker");
        using var extended=new DemoStore(Path.Combine(root,"presenter-migration-extended.db"));
        await using(var original=await source.OpenAsync(default))
        await using(var copy=await extended.OpenAsync(default))original.BackupDatabase(copy);
        await extended.ExecuteAsync("DELETE FROM ExperienceMigrations WHERE Version=5; ALTER TABLE PresenterCeremonies ADD COLUMN Extension TEXT DEFAULT 'retain-me';",default);
        var extensionSnapshot=await Snapshot(extended);rejected=false;
        try {await PresenterService.InitializeAsync(extended,default);}catch(InvalidOperationException){rejected=true;}
        TestProgram.Check(rejected && extensionSnapshot==await Snapshot(extended)
            && Convert.ToInt32(await extended.ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('PresenterCeremonies') WHERE name='Extension'",default))==1,
            "Unexpected extra ceremony columns stop migration rather than silently dropping data");
    }
}
