using System.Globalization;
using System.Security.Claims;
using LummaSimulator.Web.Models;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed record VisitorReceipt(long Number, string EventId, string EventName);
public sealed record EventSummary(string Id, string Name, long StartedAt, long? EndedAt, string Status, long Visitors, long Authentications);
public sealed record ExperienceSummary(long TotalVisitors, long VisitorsToday, long EventVisitors, long Authentications, long ActiveAccounts,
    EventSummary ActiveEvent, IReadOnlyList<EventSummary> Events);

// SQLite is the authority across independent service instances. All write decisions
// use BEGIN IMMEDIATE; the process-local DemoStore gate is not the statistics lock.
public sealed class ExperienceStatistics(DemoStore store)
{
    internal const string UnassignedEvent = "00000000-0000-0000-0000-000000000004";
    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static async Task<int> Execute(SqliteConnection c, SqliteTransaction tx, string sql, CancellationToken ct, params (string, object?)[] args)
    { using var cmd = Command(c, tx, sql, args); return await cmd.ExecuteNonQueryAsync(ct); }
    private static async Task<object?> Scalar(SqliteConnection c, SqliteTransaction tx, string sql, CancellationToken ct, params (string, object?)[] args)
    { using var cmd = Command(c, tx, sql, args); return await cmd.ExecuteScalarAsync(ct); }

    internal async Task InitializeAsync(CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct);
        using var tx = c.BeginTransaction(deferred: false);
        await Execute(c, tx, """
            CREATE TABLE IF NOT EXISTS ExperienceMigrations (Version INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS ExperienceEvents (
              Id TEXT PRIMARY KEY, Name TEXT NOT NULL, StartedAt INTEGER NOT NULL, EndedAt INTEGER,
              Status TEXT NOT NULL CHECK(Status IN ('Active','Closed','Imported')),
              Visitors INTEGER NOT NULL DEFAULT 0 CHECK(Visitors>=0), Authentications INTEGER NOT NULL DEFAULT 0 CHECK(Authentications>=0));
            CREATE TABLE IF NOT EXISTS VisitorCounts (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, DemoId TEXT UNIQUE REFERENCES Demos(Id) ON DELETE SET NULL,
              EventId TEXT NOT NULL REFERENCES ExperienceEvents(Id), VisitorNumber INTEGER NOT NULL,
              UNIQUE(EventId,VisitorNumber));
            CREATE TABLE IF NOT EXISTS DailyExperienceStats (
              Day TEXT PRIMARY KEY, Visitors INTEGER NOT NULL DEFAULT 0, Authentications INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS EventPrizePolicies (
              EventId TEXT PRIMARY KEY REFERENCES ExperienceEvents(Id), Enabled INTEGER NOT NULL DEFAULT 0 CHECK(Enabled=0),
              PrizeLimit INTEGER NOT NULL DEFAULT 0 CHECK(PrizeLimit>=0), Awarded INTEGER NOT NULL DEFAULT 0 CHECK(Awarded=0));
            """, ct);
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PresenterEventMigrations'",ct))==0)
            await Execute(c,tx,"CREATE UNIQUE INDEX IF NOT EXISTS OneActiveExperienceEvent ON ExperienceEvents((1)) WHERE Status='Active'",ct);
        var firstMigration = Convert.ToInt32(await Scalar(c, tx, "SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=3", ct)) == 0;
        if (firstMigration)
        {
            // Retained legacy successes are imported once into a separate historical
            // event. Their first-authentication date is unknown: do not invent a day.
            var legacy = Guid.NewGuid().ToString("D"); var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await Execute(c, tx, """
                INSERT INTO ExperienceEvents(Id,Name,StartedAt,EndedAt,Status,Visitors,Authentications)
                SELECT $id,'Imported v0.4.1',MIN(Created),$now,'Imported',COUNT(*),COALESCE(SUM(Successes),0)
                FROM Demos WHERE Successes>0 HAVING COUNT(*)>0;
                INSERT INTO VisitorCounts(DemoId,EventId,VisitorNumber)
                SELECT Id,$id,ROW_NUMBER() OVER (ORDER BY Created,Id) FROM Demos WHERE Successes>0;
                INSERT INTO ExperienceMigrations VALUES(3);
                """, ct, ("$id", legacy), ("$now", now));
        }
        if (firstMigration && Convert.ToInt32(await Scalar(c, tx, "SELECT COUNT(*) FROM ExperienceEvents WHERE Status='Active'", ct)) == 0)
        {
            var id = Guid.NewGuid().ToString("D");
            await Execute(c, tx, "INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES($id,'Passkey Experience',$now,'Active')", ct,
                ("$id", id), ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        await Execute(c, tx, "INSERT OR IGNORE INTO EventPrizePolicies(EventId) SELECT Id FROM ExperienceEvents", ct);
        await Execute(c,tx,"INSERT OR IGNORE INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES($id,'Open demo',0,'Imported')",ct,("$id",UnassignedEvent));
        tx.Commit();
    }

    internal async Task<VisitorReceipt> CommitAuthenticationAsync(string demoId, string sessionHash, string credentialId, uint expectedCounter, uint counter, bool backedUp, long now, long cutoff, CancellationToken ct, SecurityAuditService? audit = null, DemoAccount? account = null)
    {
        await using var c = await store.OpenAsync(ct);
        using var tx = c.BeginTransaction(deferred: false);
        if (await Execute(c, tx, "UPDATE Credentials SET Counter=$counter,BackedUp=$backup WHERE Id=$id AND DemoId=$demo AND Counter=$expected", ct,
            ("$counter", (long)counter), ("$expected", (long)expectedCounter), ("$backup", backedUp), ("$id", credentialId), ("$demo", demoId)) != 1)
            throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
        if (await Execute(c, tx, "UPDATE Demos SET Successes=Successes+1,LastAuthenticatedAt=$now WHERE Id=$demo AND SessionHash=$session AND LastSeen>$cutoff AND ClosedAt IS NULL", ct,
            ("$now", now), ("$demo", demoId), ("$session", sessionHash), ("$cutoff", cutoff)) != 1) throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
        var active = await Scalar(c, tx, "SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$demo AND Kind='experience'",ct,("$demo",demoId)) as string ?? UnassignedEvent;
        var day = DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var first = Convert.ToInt64(await Scalar(c, tx, "SELECT COUNT(*) FROM VisitorCounts WHERE DemoId=$demo", ct, ("$demo", demoId))) == 0;
        if (first)
        {
            var number = Convert.ToInt64(await Scalar(c, tx, "UPDATE ExperienceEvents SET Visitors=Visitors+1 WHERE Id=$id RETURNING Visitors", ct, ("$id", active)));
            await Execute(c, tx, "INSERT INTO VisitorCounts(DemoId,EventId,VisitorNumber) VALUES($demo,$event,$number)", ct,
                ("$demo", demoId), ("$event", active), ("$number", number));
        }
        await Execute(c, tx, """
            UPDATE ExperienceEvents SET Authentications=Authentications+1 WHERE Id=$event;
            INSERT INTO DailyExperienceStats(Day,Visitors,Authentications) VALUES($day,$visitors,1)
            ON CONFLICT(Day) DO UPDATE SET Visitors=Visitors+$visitors,Authentications=Authentications+1;
            """, ct, ("$event", active), ("$day", day), ("$visitors", first ? 1 : 0));
        using var cmd = Command(c, tx, "SELECT v.VisitorNumber,e.Id,e.Name FROM VisitorCounts v JOIN ExperienceEvents e ON e.Id=v.EventId WHERE v.DemoId=$demo", ("$demo", demoId));
        VisitorReceipt receipt;
        using (var reader = await cmd.ExecuteReaderAsync(ct)) { await reader.ReadAsync(ct); receipt = new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)); }
        if(audit is not null && account is not null) await audit.AuthenticatedAsync(c,tx,account,credentialId,now,ct);
        tx.Commit(); return receipt;
    }

    // Legacy phase-3 test harness. Production management goes through PresenterService,
    // which revalidates the actual database session before using the shared transaction helpers.
    internal async Task<string> StartEventAsync(ClaimsPrincipal presenter, string expectedActiveId, string name, bool confirmed, long now, CancellationToken ct)
    {
        if (presenter.Identity?.IsAuthenticated != true || !presenter.IsInRole("Presenter")
            || presenter.FindFirst("amr")?.Value != "webauthn" || !confirmed)
            throw new UnauthorizedAccessException("Presenter WebAuthn and explicit confirmation required.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Event name must contain 1–100 characters.");
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var id=await StartEventCoreAsync(c,tx,expectedActiveId,name,now,ct);
        tx.Commit(); return id;
    }
    internal static async Task CloseEventCoreAsync(SqliteConnection c,SqliteTransaction tx,string expected,long now,CancellationToken ct)
    {
        if (string.IsNullOrEmpty(expected) || await Execute(c,tx,"UPDATE ExperienceEvents SET Status='Closed',EndedAt=$now WHERE Id=$id AND Status='Active'",ct,("$now",now),("$id",expected))!=1)
            throw new DemoRequestException("PresenterEventChanged");
    }
    internal static async Task<string> StartEventCoreAsync(SqliteConnection c,SqliteTransaction tx,string expectedActiveId,string name,long now,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Length>100)throw new DemoRequestException("PresenterConfirm");
        if(string.IsNullOrEmpty(expectedActiveId)) {
            if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM ExperienceEvents WHERE Status='Active'",ct))!=0)throw new DemoRequestException("PresenterEventChanged");
        } else await CloseEventCoreAsync(c,tx,expectedActiveId,now,ct);
        var id = Guid.NewGuid().ToString("D");
        await Execute(c, tx, "INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES($id,$name,$now,'Active'); INSERT INTO EventPrizePolicies(EventId) VALUES($id)", ct,
            ("$id", id), ("$name", name.Trim()), ("$now", now));
        return id;
    }

    public async Task<ExperienceSummary> SummaryAsync(long now, int idleMinutes, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        var events = new List<EventSummary>();
        using (var cmd = Command(c, tx, "SELECT Id,Name,StartedAt,EndedAt,Status,Visitors,Authentications FROM ExperienceEvents WHERE Id<>$open OR Visitors>0 OR Authentications>0 ORDER BY StartedAt,Id",("$open",UnassignedEvent)))
        using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) events.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6)));
        var active = events.FirstOrDefault(e => e.Status == "Active") ?? new EventSummary("","NoActiveEvent",0,null,"None",0,0);
        var day = DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = Convert.ToInt64(await Scalar(c, tx, "SELECT COALESCE((SELECT Visitors FROM DailyExperienceStats WHERE Day=$day),0)", ct, ("$day", day)));
        var accounts = Convert.ToInt64(await Scalar(c, tx, "SELECT COUNT(*) FROM Demos WHERE LastSeen>$cutoff AND ClosedAt IS NULL", ct,
            ("$cutoff", now - (long)TimeSpan.FromMinutes(idleMinutes).TotalMilliseconds)));
        tx.Commit(); return new(events.Sum(e => e.Visitors), today, active.Visitors, events.Sum(e => e.Authentications), accounts, active, events);
    }
}
