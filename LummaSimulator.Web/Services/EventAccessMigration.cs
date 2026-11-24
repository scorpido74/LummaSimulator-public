namespace LummaSimulator.Web.Services;

internal static class EventAccessMigration
{
    internal const string SchemaSql="""
            CREATE TABLE IF NOT EXISTS PresenterEventMigrations(Version INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PresenterEventScopes(Kind TEXT NOT NULL CHECK(Kind IN ('experience','prize','edition')),SourceId TEXT NOT NULL,Name TEXT NOT NULL,OwnerId TEXT REFERENCES PresenterAccounts(Id),PRIMARY KEY(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterEventMembers(Kind TEXT NOT NULL,EventId TEXT NOT NULL,AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id),PRIMARY KEY(Kind,EventId,AccountId),FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterEventInvites(Id TEXT PRIMARY KEY,Kind TEXT NOT NULL,EventId TEXT NOT NULL,TargetId TEXT NOT NULL REFERENCES PresenterAccounts(Id),IssuerId TEXT NOT NULL REFERENCES PresenterAccounts(Id),Expires INTEGER NOT NULL,EnrollmentHash TEXT,UNIQUE(Kind,EventId,TargetId),FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterEventTransfers(Kind TEXT NOT NULL,EventId TEXT NOT NULL,OwnerId TEXT NOT NULL REFERENCES PresenterAccounts(Id),TargetId TEXT NOT NULL REFERENCES PresenterAccounts(Id),Expires INTEGER NOT NULL,PRIMARY KEY(Kind,EventId),FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterDemoContexts(DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE,Kind TEXT NOT NULL,EventId TEXT NOT NULL,FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterMonitorCapabilities(Hash TEXT PRIMARY KEY,DemoId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE CASCADE,Kind TEXT NOT NULL,EventId TEXT NOT NULL,ActorId TEXT NOT NULL REFERENCES PresenterAccounts(Id),SessionHash TEXT NOT NULL,Expires INTEGER NOT NULL,FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId));
            CREATE TABLE IF NOT EXISTS PresenterPresentationSettings(Id INTEGER PRIMARY KEY CHECK(Id=1),Json TEXT NOT NULL CHECK(json_valid(Json)));
            CREATE TABLE IF NOT EXISTS PresenterLogos(Slot TEXT PRIMARY KEY CHECK(Slot IN ('organization','presenter','visitor')),Image BLOB NOT NULL CHECK(length(Image)<=262144));
            CREATE TABLE IF NOT EXISTS PresenterInstallation(Id INTEGER PRIMARY KEY CHECK(Id=1),Existing INTEGER NOT NULL CHECK(Existing IN (0,1)),HostingConfirmed INTEGER NOT NULL DEFAULT 0,FirstEvent TEXT,CompletedAt INTEGER);
            CREATE TRIGGER IF NOT EXISTS PresenterInstallationNoDelete BEFORE DELETE ON PresenterInstallation BEGIN SELECT RAISE(ABORT,'Installation state cannot be reset'); END;
            CREATE TRIGGER IF NOT EXISTS PresenterInstallationNoReset BEFORE UPDATE ON PresenterInstallation WHEN NEW.Existing<>OLD.Existing OR (OLD.CompletedAt IS NOT NULL AND NEW.CompletedAt IS NULL) BEGIN SELECT RAISE(ABORT,'Installation state cannot be reset'); END;
            CREATE TABLE IF NOT EXISTS PresenterSessionRefs(Hash TEXT PRIMARY KEY REFERENCES PresenterAuthSessions(Hash) ON DELETE CASCADE,Ref TEXT NOT NULL UNIQUE);
            CREATE TRIGGER IF NOT EXISTS PresenterEventOwnerDisable BEFORE UPDATE OF Active ON PresenterAccounts
              WHEN NEW.Active=0 AND EXISTS(SELECT 1 FROM PresenterEventScopes WHERE OwnerId=OLD.Id)
              BEGIN SELECT RAISE(ABORT,'EventOwnerTransferRequired'); END;
            """;
    internal static Dictionary<string,string> Expected => System.Text.RegularExpressions.Regex.Matches(SchemaSql,@"CREATE (TABLE|TRIGGER) IF NOT EXISTS ([A-Za-z0-9_]+).*?;(?=\s*CREATE|\s*$)",System.Text.RegularExpressions.RegexOptions.Singleline)
        .Cast<System.Text.RegularExpressions.Match>().ToDictionary(m=>m.Groups[1].Value.ToLowerInvariant()+":"+m.Groups[2].Value,m=>Prizes.PrizeMigration.Normalize(m.Value.Replace(" IF NOT EXISTS","",StringComparison.Ordinal).Trim().TrimEnd(';')),StringComparer.Ordinal);
    internal static async Task ValidateAsync(Microsoft.Data.Sqlite.SqliteConnection c,Microsoft.Data.Sqlite.SqliteTransaction? tx,Dictionary<string,string> schema,CancellationToken ct)
    {
        var expected=Expected;if(expected.Count!=14)throw new InvalidOperationException("Event schema definition is incomplete");
        if(!schema.Keys.Any(expected.ContainsKey))return;
        foreach(var entry in expected)if(!schema.TryGetValue(entry.Key,out var actual)||actual!=entry.Value)throw new InvalidOperationException("Conflicting event access schema: "+entry.Key);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT Version FROM PresenterEventMigrations ORDER BY Version";
        using(var r=await q.ExecuteReaderAsync(ct)){var versions=new List<long>();while(await r.ReadAsync(ct))versions.Add(r.GetInt64(0));if(!versions.SequenceEqual(new long[]{1})&&!versions.SequenceEqual(new long[]{1,2}))throw new InvalidOperationException("Conflicting event access migration history");}
        q.CommandText="SELECT COUNT(*) FROM PresenterInstallation WHERE Id=1";if(Convert.ToInt64(await q.ExecuteScalarAsync(ct))!=1)throw new InvalidOperationException("Installation state requires controlled recovery");
    }
    internal static async Task InitializeAsync(DemoStore store,CancellationToken ct,bool newInstallation=false,Action? beforeCommit=null)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await EventAccess.Execute(c,tx,SchemaSql,ct);
        if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventMigrations WHERE Version=1",ct))==0) {
            var established=Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Activated IS NOT NULL",ct))!=0;
            await EventAccess.Execute(c,tx,"INSERT INTO PresenterInstallation(Id,Existing) VALUES(1,$existing)",ct,("$existing",!newInstallation||established));
            await EventAccess.Execute(c,tx,"""
                INSERT INTO PresenterEventScopes SELECT 'experience',Id,Name,NULL FROM ExperienceEvents;
                INSERT INTO PresenterEventScopes SELECT 'prize',Id,Name,NULL FROM PrizeEvents;
                INSERT INTO PresenterDemoContexts SELECT d.Id,'experience',COALESCE(v.EventId,'00000000-0000-0000-0000-000000000004') FROM Demos d LEFT JOIN VisitorCounts v ON v.DemoId=d.Id;
                INSERT INTO PresenterEventMigrations VALUES(1);
                """,ct);
        }
        if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PresenterInstallation WHERE Id=1",ct))!=1)throw new InvalidDataException("Installation state requires controlled recovery");
        await EventAccess.Execute(c,tx,"DROP INDEX IF EXISTS OneActiveExperienceEvent",ct);
        beforeCommit?.Invoke(); // Synthetic upgrade fault-injection; production never supplies it.
        tx.Commit();
    }
}
