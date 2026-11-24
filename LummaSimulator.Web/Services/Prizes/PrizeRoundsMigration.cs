using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services.Prizes;

internal static class PrizeRoundsMigration
{
    internal const int Version=1;
    internal static readonly string[] Statements=
    [
        "CREATE TABLE PrizeRoundSettings (EventId TEXT PRIMARY KEY NOT NULL REFERENCES PrizeEvents(Id) ON DELETE CASCADE, NextCapacity INTEGER CHECK(NextCapacity BETWEEN 1 AND 200), Version INTEGER NOT NULL CHECK(Version>=1))",
        "CREATE TABLE PrizeRoundParticipants (EventId TEXT NOT NULL, ParticipantId TEXT NOT NULL, GroupId TEXT NOT NULL, Alias TEXT NOT NULL CHECK(length(Alias) BETWEEN 1 AND 40), IsWinner INTEGER NOT NULL DEFAULT 0 CHECK(IsWinner IN(0,1)), PrizeTypeId TEXT, PrizeName TEXT, PRIMARY KEY(EventId,ParticipantId), UNIQUE(EventId,GroupId,Alias), FOREIGN KEY(EventId,ParticipantId) REFERENCES PrizeParticipants(EventId,Id) ON DELETE CASCADE, FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id) ON DELETE CASCADE, FOREIGN KEY(EventId,PrizeTypeId) REFERENCES PrizeTypes(EventId,Id), CHECK((IsWinner=0 AND PrizeTypeId IS NULL AND PrizeName IS NULL) OR (IsWinner=1 AND PrizeTypeId IS NOT NULL AND PrizeName IS NOT NULL)))",
        "CREATE TABLE PrizeRoundResults (EventId TEXT NOT NULL, GroupId TEXT NOT NULL, ClosedAt INTEGER NOT NULL, ClosedBy TEXT NOT NULL REFERENCES PresenterAccounts(Id), WinnerCount INTEGER NOT NULL CHECK(WinnerCount>=0), PrizeTypeId TEXT, PrizeName TEXT, NextGroupId TEXT NOT NULL, NextCapacity INTEGER NOT NULL CHECK(NextCapacity BETWEEN 1 AND 200), PRIMARY KEY(EventId,GroupId), UNIQUE(EventId,NextGroupId), FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id), FOREIGN KEY(EventId,NextGroupId) REFERENCES PrizeGroups(EventId,Id), FOREIGN KEY(EventId,PrizeTypeId) REFERENCES PrizeTypes(EventId,Id), CHECK((WinnerCount=0 AND PrizeTypeId IS NULL AND PrizeName IS NULL) OR (WinnerCount>0 AND PrizeTypeId IS NOT NULL AND PrizeName IS NOT NULL)))",
        "CREATE TABLE PrizeRoundsContract (Version INTEGER PRIMARY KEY CHECK(Version=1), Fingerprint TEXT NOT NULL)",
        "CREATE INDEX PrizeRoundParticipantsGroup ON PrizeRoundParticipants(EventId,GroupId,ParticipantId)"
    ];
    internal static Dictionary<string,string> Expected=>Statements.ToDictionary(
        sql=>{var match=Regex.Match(sql,"^CREATE (?:UNIQUE )?(TABLE|INDEX) ([A-Za-z0-9_]+)");return match.Groups[1].Value.ToLowerInvariant()+":"+match.Groups[2].Value;},
        PrizeMigration.Normalize,StringComparer.Ordinal);
    private static string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",Expected
        .Where(pair=>pair.Key!="table:PrizeRoundsContract").OrderBy(pair=>pair.Key,StringComparer.Ordinal)
        .Select(pair=>pair.Key+"="+pair.Value)))));

    internal static async Task ValidateAsync(SqliteConnection connection,SqliteTransaction? transaction,CancellationToken token)
    {
        var schema=new Dictionary<string,string>(StringComparer.Ordinal);
        using(var query=connection.CreateCommand())
        {
            query.Transaction=transaction;query.CommandText="SELECT type,name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
            using var reader=await query.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))schema.Add(reader.GetString(0)+":"+reader.GetString(1),PrizeMigration.Normalize(reader.GetString(2)));
        }
        var expected=Expected;var present=expected.Keys.Count(schema.ContainsKey);
        if(present==0)return;
        if(present!=expected.Count)throw new InvalidOperationException("Incomplete prize rounds schema.");
        foreach(var item in expected)if(!schema.TryGetValue(item.Key,out var actual)||actual!=item.Value)
            throw new InvalidOperationException("Conflicting prize rounds schema: "+item.Key);
        using var contract=connection.CreateCommand();contract.Transaction=transaction;
        contract.CommandText="SELECT Fingerprint FROM PrizeRoundsContract WHERE Version=$version";
        contract.Parameters.AddWithValue("$version",Version);
        if((string?)await contract.ExecuteScalarAsync(token)!=Fingerprint)throw new InvalidOperationException("Conflicting prize rounds schema identity.");
    }

    internal static async Task InitializeAsync(DemoStore store,CancellationToken token)
    {
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:false);
        await ValidateAsync(connection,transaction,token);
        using(var check=connection.CreateCommand())
        {
            check.Transaction=transaction;check.CommandText="SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='PrizeRoundsContract'";
            if(Convert.ToInt32(await check.ExecuteScalarAsync(token))!=0){transaction.Commit();return;}
        }
        foreach(var statement in Statements)
        {
            using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=statement.Replace("CREATE TABLE ","CREATE TABLE IF NOT EXISTS ",StringComparison.Ordinal).Replace("CREATE INDEX ","CREATE INDEX IF NOT EXISTS ",StringComparison.Ordinal);
            await command.ExecuteNonQueryAsync(token);
        }
        using(var marker=connection.CreateCommand())
        {
            marker.Transaction=transaction;marker.CommandText="INSERT INTO PrizeRoundsContract VALUES($version,$fingerprint)";
            marker.Parameters.AddWithValue("$version",Version);marker.Parameters.AddWithValue("$fingerprint",Fingerprint);
            await marker.ExecuteNonQueryAsync(token);
        }
        await ValidateAsync(connection,transaction,token);transaction.Commit();
    }
}