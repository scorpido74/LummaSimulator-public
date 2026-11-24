using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

// Additive contract independent from the immutable Prize v6 contract/history.
internal static class IdentityMigration
{
    internal static readonly string[] Statements = [
        "CREATE TABLE IdentitySchemaVersion (Version INTEGER PRIMARY KEY CHECK(Version=1))",
        "CREATE TABLE IdentitySessions (Id TEXT PRIMARY KEY NOT NULL, UserId TEXT NOT NULL, DemoRunId TEXT NOT NULL, BindingHash TEXT NOT NULL UNIQUE, CreatedAt INTEGER NOT NULL, LastActivity INTEGER NOT NULL, ExpiresAt INTEGER NOT NULL, CredentialHash TEXT, Method TEXT NOT NULL CHECK(Method='WebAuthn'), SessionType TEXT NOT NULL CHECK(SessionType='Normal'), RevokedAt INTEGER)",
        "CREATE TABLE IdentityAudit (Sequence INTEGER PRIMARY KEY AUTOINCREMENT, At INTEGER NOT NULL, UserId TEXT, DemoRunId TEXT, EventName TEXT NOT NULL, Payload TEXT NOT NULL CHECK(json_valid(Payload)))",
        "CREATE INDEX IdentitySessionsUser ON IdentitySessions(UserId,CreatedAt)",
        "CREATE INDEX IdentityAuditFilter ON IdentityAudit(UserId,At,Sequence)",
        "CREATE TRIGGER IdentityAuditNoUpdate BEFORE UPDATE ON IdentityAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END",
        "CREATE TRIGGER IdentityAuditNoDelete BEFORE DELETE ON IdentityAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END"
    ];
    internal static Dictionary<string,string> Expected => Statements.ToDictionary(s => {
        var m=Regex.Match(s,"^CREATE (TABLE|INDEX|TRIGGER) ([A-Za-z0-9_]+)");
        return m.Groups[1].Value.ToLowerInvariant()+":"+m.Groups[2].Value;
    }, Prizes.PrizeMigration.Normalize, StringComparer.Ordinal);

    internal static async Task ValidateAsync(SqliteConnection c, SqliteTransaction? tx, Dictionary<string,string> schema, CancellationToken ct)
    {
        var expected=Expected;
        if(!schema.Keys.Any(expected.ContainsKey)) return;
        foreach(var (key,value) in expected)
            if(!schema.TryGetValue(key,out var actual) || actual!=value) throw new InvalidOperationException("Conflicting identity schema: "+key);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT Version FROM IdentitySchemaVersion";
        using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct) || r.GetInt32(0)!=1 || await r.ReadAsync(ct)) throw new InvalidOperationException("Conflicting identity migration history.");
    }
    internal static async Task InitializeAsync(DemoStore store,CancellationToken ct,Action? beforeCommit=null)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT COUNT(*) FROM sqlite_schema WHERE name='IdentitySchemaVersion'";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0) { tx.Commit();return; }
        foreach(var sql in Statements) { q.CommandText=sql;await q.ExecuteNonQueryAsync(ct); }
        // Preserve previously authenticated browser bindings, even when already expired.
        // Do not fabricate historic authentication audit events or credential evidence.
        q.CommandText="""
            INSERT INTO IdentitySessions(Id,UserId,DemoRunId,BindingHash,CreatedAt,LastActivity,ExpiresAt,Method,SessionType)
            SELECT lower(hex(randomblob(16))),Id,DemoRunId,SessionHash,LastAuthenticatedAt,LastSeen,LastAuthenticatedAt+1800000,'WebAuthn','Normal'
            FROM Demos WHERE LastAuthenticatedAt IS NOT NULL;
            INSERT INTO IdentitySchemaVersion VALUES(1);
            """;
        await q.ExecuteNonQueryAsync(ct);beforeCommit?.Invoke();tx.Commit();
    }
}
