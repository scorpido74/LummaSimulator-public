using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Services;

// Additive contract: no existing table, credential or historical migration is changed.
internal static class VisitorFlowMigration
{
    internal static readonly string[] Statements = [
        "CREATE TABLE EventLoginRoutes (Kind TEXT NOT NULL, EventId TEXT NOT NULL, Mask INTEGER NOT NULL CHECK(Mask BETWEEN 0 AND 15), PRIMARY KEY(Kind,EventId), FOREIGN KEY(Kind,EventId) REFERENCES PresenterEventScopes(Kind,SourceId) ON DELETE CASCADE)",
        "CREATE TABLE VisitorExercises (DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE, StartedAt INTEGER NOT NULL, CompletedAt INTEGER CHECK(CompletedAt IS NULL OR CompletedAt>=StartedAt))"
    ];
    internal static Dictionary<string,string> Expected => Statements.ToDictionary(s => "table:" + s.Split(' ')[2], PrizeMigration.Normalize);
    internal static void Validate(Dictionary<string,string> schema)
    {
        var present = Expected.Keys.Count(schema.ContainsKey);
        if (present == 0) return;
        if (present != Expected.Count || Expected.Any(p => schema[p.Key] != p.Value)
            || !schema.ContainsKey("table:PresenterEventScopes")) throw new InvalidOperationException("Conflicting visitor flow schema.");
    }
    internal static async Task InitializeAsync(DemoStore store, CancellationToken ct, Action? beforeCommit = null)
    {
        await using var c = await store.OpenAsync(ct);
        using var tx = c.BeginTransaction(deferred: false);
        var schema = new Dictionary<string,string>();
        using (var q = c.CreateCommand()) {
            q.Transaction = tx; q.CommandText = "SELECT type||':'||name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%'";
            using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) schema.Add(r.GetString(0), PrizeMigration.Normalize(r.GetString(1)));
        }
        Validate(schema);
        if (!schema.ContainsKey("table:EventLoginRoutes"))
            foreach (var sql in Statements) await EventAccess.Execute(c, tx, sql, ct);
        beforeCommit?.Invoke(); // Synthetic migration rollback fixture only.
        tx.Commit();
    }
}
