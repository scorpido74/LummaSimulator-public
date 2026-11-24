using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Services.Prizes;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

// One application instance. The service layer holds Gate across account checks, challenge consumption,
// FIDO verification and updates so cleanup/end cannot race a ceremony or resurrect a deleted account.
public sealed class DemoStore(string databasePath) : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);
    internal async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, Pooling = false
        }.ToString());
        await connection.OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(token);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        var newInstallation=!File.Exists(DatabasePath);
        if(newInstallation){
            var root=Path.GetDirectoryName(DatabasePath)!;var keys=Path.Combine(root,"keys");
            if((Directory.Exists(keys)&&Directory.EnumerateFiles(keys,"key-*.xml").Any())||File.Exists(Path.Combine(root,"monitor-integration.state"))||File.Exists(Path.Combine(root,"event-edition.manifest")))
                throw new InvalidDataException("Missing database with existing protected runtime state; explicit consistent recovery required.");
        }
        if (!await PrizeMigration.PreflightAsync(this, token))
            await InitializeLegacyAsync(token);
        await PrizeMigration.InitializeAsync(this, token);
        await IdentityMigration.InitializeAsync(this, token);
        await EventAccessMigration.InitializeAsync(this,token,newInstallation);
        await PrizeRoundsMigration.InitializeAsync(this,token);
        await PrizeRoundHistoryMigration.InitializeAsync(this,token);
        await VisitorFlowMigration.InitializeAsync(this,token);
        await ExecuteAsync(HelperInstallationSchema.CreateIfMissing,token);
    }

    // Internal fixture/upgrade seam; production always uses the complete initialization above.
    internal async Task InitializeLegacyAsync(CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await ExecuteAsync("""
            PRAGMA journal_mode=DELETE;
            CREATE TABLE IF NOT EXISTS Demos (
              Id TEXT PRIMARY KEY, DemoRunId TEXT NOT NULL UNIQUE, Name TEXT NOT NULL UNIQUE,
              UserHandle BLOB NOT NULL UNIQUE, SessionHash TEXT NOT NULL UNIQUE, Route TEXT NOT NULL,
              Created INTEGER NOT NULL, LastSeen INTEGER NOT NULL, Successes INTEGER NOT NULL DEFAULT 0,
              Failures INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Credentials (
              Id TEXT PRIMARY KEY, DemoId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE CASCADE,
              PublicKey BLOB NOT NULL, Counter INTEGER NOT NULL, BackupEligible INTEGER NOT NULL, BackedUp INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS Challenges (
              DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE,
              SessionHash TEXT NOT NULL, Kind TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL,
              OperationId TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS PresenterSessions (Hash TEXT PRIMARY KEY, Expires INTEGER NOT NULL);
            """, token);
        if (Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Challenges') WHERE name='OperationId'", token)) == 0)
            await ExecuteAsync("ALTER TABLE Challenges ADD COLUMN OperationId TEXT NOT NULL DEFAULT ''", token);
        await ExecuteAsync("DELETE FROM Challenges WHERE OperationId='';", token);
        if (Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Demos') WHERE name='LastAuthenticatedAt'", token)) == 0)
            await ExecuteAsync("ALTER TABLE Demos ADD COLUMN LastAuthenticatedAt INTEGER NULL", token);
        if (Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Demos') WHERE name='ClosedAt'", token)) == 0)
            await ExecuteAsync("ALTER TABLE Demos ADD COLUMN ClosedAt INTEGER NULL", token);
        if (Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Credentials') WHERE name='Alias'", token)) == 0)
            await ExecuteAsync("ALTER TABLE Credentials ADD COLUMN Alias TEXT NOT NULL DEFAULT ''", token);
        await ExecuteAsync("CREATE TABLE IF NOT EXISTS MonitorGrants(DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE, Expires INTEGER NOT NULL)", token);
        await new ExperienceStatistics(this).InitializeAsync(token);
        await PresenterService.InitializeAsync(this, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken token, params (string Name, object? Value)[] values)
    {
        await using var connection = await OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(token);
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken token, params (string Name, object? Value)[] values)
    {
        await using var connection = await OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteScalarAsync(token);
    }

    public async Task<List<DemoAccount>> AccountsAsync(string? hash, CancellationToken token, bool includeClosed = false)
    {
        await using var connection = await OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id,d.DemoRunId,d.Name,d.UserHandle,d.SessionHash,d.Route,d.Created,d.LastSeen,d.Successes,d.Failures,
              (SELECT COUNT(*) FROM Credentials c WHERE c.DemoId=d.Id) AS CredentialCount, d.LastAuthenticatedAt
            FROM Demos d WHERE ($includeClosed=1 OR d.ClosedAt IS NULL) AND ($hash IS NULL OR d.SessionHash=$hash) ORDER BY d.Created;
            """;
        command.Parameters.AddWithValue("$hash", (object?)hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$includeClosed",includeClosed?1:0);
        using var reader = await command.ExecuteReaderAsync(token);
        var accounts = new List<DemoAccount>();
        while (await reader.ReadAsync(token))
            accounts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3],
                reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        return accounts;
    }

    public async Task<List<Credential>> CredentialsAsync(string demoId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DemoId, PublicKey, Counter, BackupEligible, BackedUp, Alias FROM Credentials WHERE DemoId=$id ORDER BY rowid";
        command.Parameters.AddWithValue("$id", demoId);
        using var reader = await command.ExecuteReaderAsync(token);
        var credentials = new List<Credential>();
        while (await reader.ReadAsync(token))
            credentials.Add(new(reader.GetString(0), reader.GetString(1), (byte[])reader[2], (uint)reader.GetInt64(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetString(6) is { Length: > 0 } alias ? alias : "Passkey " + (credentials.Count + 1)));
        return credentials;
    }

    public void Dispose() => Gate.Dispose();
}
