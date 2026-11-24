using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services.Prizes;

// Additive history for explicitly ended demo accounts. The v6 and round-v1 contracts stay intact.
internal static class PrizeRoundHistoryMigration
{
    internal static readonly string[] Statements = [
        "CREATE TABLE PrizeRoundHistory (EventId TEXT NOT NULL, ParticipantId TEXT NOT NULL, GroupId TEXT NOT NULL, Alias TEXT NOT NULL CHECK(length(Alias) BETWEEN 1 AND 40), IsWinner INTEGER NOT NULL CHECK(IsWinner IN(0,1)), PrizeTypeId TEXT, PrizeName TEXT, PRIMARY KEY(EventId,ParticipantId), UNIQUE(EventId,GroupId,Alias), FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id) ON DELETE CASCADE, FOREIGN KEY(EventId,PrizeTypeId) REFERENCES PrizeTypes(EventId,Id), CHECK((IsWinner=0 AND PrizeTypeId IS NULL AND PrizeName IS NULL) OR (IsWinner=1 AND PrizeTypeId IS NOT NULL AND PrizeName IS NOT NULL)))",
        "CREATE INDEX PrizeRoundHistoryGroup ON PrizeRoundHistory(EventId,GroupId)",
        "CREATE TABLE PrizeRoundHistoryContract (Version INTEGER PRIMARY KEY CHECK(Version=1), Fingerprint TEXT NOT NULL)"
    ];
    internal static Dictionary<string,string> Expected => Statements.ToDictionary(
        sql=>sql.Split(' ')[1].ToLowerInvariant()+":"+sql.Split(' ')[2],PrizeMigration.Normalize,StringComparer.Ordinal);
    private static string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Statements.Select(PrizeMigration.Normalize)))));
    internal static async Task ValidateAsync(SqliteConnection c, SqliteTransaction? tx, CancellationToken ct)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT name,sql FROM sqlite_schema WHERE name IN ('PrizeRoundHistory','PrizeRoundHistoryGroup','PrizeRoundHistoryContract')";
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var reader = await q.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) actual.Add(reader.GetString(0), PrizeMigration.Normalize(reader.GetString(1)));
        if (actual.Count == 0) return;
        if (actual.Count != Statements.Length) throw new InvalidOperationException("Incomplete round history schema.");
        foreach (var statement in Statements)
        {
            var name = statement.Split(' ')[2];
            if (!actual.TryGetValue(name, out var sql) || sql != PrizeMigration.Normalize(statement)) throw new InvalidOperationException("Conflicting round history schema: " + name);
        }
        q.CommandText = "SELECT Fingerprint FROM PrizeRoundHistoryContract WHERE Version=1";
        if ((string?)await q.ExecuteScalarAsync(ct) != Fingerprint) throw new InvalidOperationException("Conflicting round history schema identity.");
    }
    internal static async Task InitializeAsync(DemoStore store, CancellationToken ct, Action? beforeCommit = null)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        await ValidateAsync(c, tx, ct);
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name='PrizeRoundHistoryContract'";
        if (Convert.ToInt32(await q.ExecuteScalarAsync(ct)) != 0) { tx.Commit(); return; }
        foreach (var statement in Statements) { q.CommandText = statement; await q.ExecuteNonQueryAsync(ct); }
        q.CommandText = "INSERT INTO PrizeRoundHistoryContract VALUES(1,$fingerprint)"; q.Parameters.AddWithValue("$fingerprint", Fingerprint); await q.ExecuteNonQueryAsync(ct);
        beforeCommit?.Invoke(); await ValidateAsync(c, tx, ct); tx.Commit();
    }
    internal static async Task DetachAccountAsync(SqliteConnection c, SqliteTransaction tx, string account, CancellationToken ct)
    {
        var facts=new List<(string Event,string Participant,bool Completed,bool Withdrawn,long At)>();
        using(var q=EventAccess.Command(c,tx,"SELECT p.EventId,p.Id,v.CompletedAt IS NOT NULL,p.State!='Active',COALESCE(p.CancelledAt,v.CompletedAt,p.JoinedAt) FROM PrizeParticipants p JOIN PrizeRoundParticipants r ON r.EventId=p.EventId AND r.ParticipantId=p.Id LEFT JOIN VisitorExercises v ON v.DemoId=p.AccountId WHERE p.AccountId=$account",("$account",account)))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))facts.Add((r.GetString(0),r.GetString(1),r.GetBoolean(2),r.GetBoolean(3),r.GetInt64(4)));
        foreach(var fact in facts){
            if(fact.Completed)await PrizeRoundsService.RoundFact(c,tx,fact.Event,fact.Participant,"RoundDemoCompleted",account,fact.At,ct);
            if(fact.Withdrawn)await PrizeRoundsService.RoundFact(c,tx,fact.Event,fact.Participant,"RoundEntryWithdrawn",account,fact.At,ct);
        }
        // Keep only event/round/alias/result. No account binding, passkey, timestamp or session secret is copied.
        await EventAccess.Execute(c, tx, """
            INSERT INTO PrizeRoundHistory(EventId,ParticipantId,GroupId,Alias,IsWinner,PrizeTypeId,PrizeName)
            SELECT r.EventId,r.ParticipantId,r.GroupId,r.Alias,r.IsWinner,r.PrizeTypeId,r.PrizeName
            FROM PrizeRoundParticipants r JOIN PrizeParticipants p ON p.EventId=r.EventId AND p.Id=r.ParticipantId
            WHERE p.AccountId=$account;
            DELETE FROM PrizeParticipants WHERE AccountId=$account AND EXISTS(
              SELECT 1 FROM PrizeRoundParticipants r WHERE r.EventId=PrizeParticipants.EventId AND r.ParticipantId=PrizeParticipants.Id);
            DELETE FROM PrizeVisitorSessions WHERE AccountId=$account;
            """, ct, ("$account", account));
        // Any unrelated historical Prize FK still blocks the surrounding transaction; never disable FK enforcement.
    }
}
