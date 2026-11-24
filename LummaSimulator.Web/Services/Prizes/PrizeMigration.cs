using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services.Prizes;

// Fixed schema identity. Never derive the version from data or accept a marker alone.
internal static class PrizeMigration
{
    internal const int Version = 6;
    internal static readonly string[] Statements =
    [
        "CREATE TABLE PrizeProfiles (Id TEXT PRIMARY KEY NOT NULL, Name TEXT NOT NULL, BuiltIn INTEGER NOT NULL CHECK(BuiltIn IN(0,1)), ConfigJson TEXT NOT NULL CHECK(json_valid(ConfigJson)), ConfigVersion INTEGER NOT NULL CHECK(ConfigVersion=1), Version INTEGER NOT NULL CHECK(Version>=1), CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL)",
        "CREATE TABLE PrizeInstallationSettings (Id INTEGER PRIMARY KEY CHECK(Id=1), LastProfileId TEXT REFERENCES PrizeProfiles(Id) ON DELETE SET NULL, Version INTEGER NOT NULL CHECK(Version>=1))",
        "CREATE TABLE PrizeEvents (Id TEXT PRIMARY KEY NOT NULL, Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100), ProfileId TEXT REFERENCES PrizeProfiles(Id) ON DELETE SET NULL, ProfileSnapshotJson TEXT NOT NULL CHECK(json_valid(ProfileSnapshotJson)), ConfigVersion INTEGER NOT NULL CHECK(ConfigVersion=1), PrizeEnabled INTEGER NOT NULL DEFAULT 0 CHECK(PrizeEnabled IN(0,1)), Visibility TEXT NOT NULL CHECK(Visibility IN('Public','Private')), Mode TEXT NOT NULL CHECK(Mode IN('Group','Instant')), GroupSize INTEGER NOT NULL CHECK(GroupSize BETWEEN 1 AND 200), WinnersPerGroup INTEGER NOT NULL CHECK(WinnersPerGroup BETWEEN 1 AND GroupSize), AssignmentMode TEXT NOT NULL CHECK(AssignmentMode IN('Automatic','Code')), NextGroupMode TEXT NOT NULL CHECK(NextGroupMode IN('Manual','Automatic')), DrawMethod TEXT NOT NULL CHECK(DrawMethod='Manual'), RevealMode TEXT NOT NULL CHECK(RevealMode IN('Together','Sequential')), AnimationSeconds INTEGER NOT NULL CHECK(AnimationSeconds IN(5,10,15)), StartCodeRequired INTEGER NOT NULL CHECK(StartCodeRequired IN(0,1)), PrizeSelection TEXT NOT NULL CHECK(PrizeSelection IN('Fixed','Random')), FixedPrizeTypeId TEXT, ChanceNumerator INTEGER NOT NULL, ChanceDenominator INTEGER NOT NULL CHECK(ChanceDenominator>0), MaxInstantWins INTEGER CHECK(MaxInstantWins>=0), InstantWins INTEGER NOT NULL DEFAULT 0 CHECK(InstantWins>=0 AND (MaxInstantWins IS NULL OR InstantWins<=MaxInstantWins)), ReactionSeconds INTEGER NOT NULL CHECK(ReactionSeconds>0), RetentionDays INTEGER NOT NULL CHECK(RetentionDays BETWEEN 1 AND 90), PostCloseClaimSeconds INTEGER NOT NULL CHECK(PostCloseClaimSeconds>=0), ClaimFieldSchemaJson TEXT NOT NULL CHECK(json_valid(ClaimFieldSchemaJson)), Status TEXT NOT NULL CHECK(Status IN('Draft','Open','Closed','Purging')), CreatedAt INTEGER NOT NULL, ClosedAt INTEGER, PurgeAt INTEGER, Version INTEGER NOT NULL CHECK(Version>=1), NextParticipantNumber INTEGER NOT NULL DEFAULT 1 CHECK(NextParticipantNumber>=1), CHECK(Visibility!='Public' OR StartCodeRequired=1), CHECK(ChanceNumerator BETWEEN 0 AND ChanceDenominator), CHECK(PurgeAt IS NULL OR (ClosedAt IS NOT NULL AND PurgeAt>=ClosedAt)), FOREIGN KEY(Id,FixedPrizeTypeId) REFERENCES PrizeTypes(EventId,Id))",
        "CREATE TABLE PrizeGroups (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), Number INTEGER NOT NULL CHECK(Number>=1), Status TEXT NOT NULL CHECK(Status IN('Open','Closed','Frozen')), Capacity INTEGER NOT NULL CHECK(Capacity BETWEEN 1 AND 200), Occupied INTEGER NOT NULL DEFAULT 0 CHECK(Occupied BETWEEN 0 AND Capacity), FrozenAt INTEGER, Version INTEGER NOT NULL CHECK(Version>=1), UNIQUE(EventId,Number), UNIQUE(EventId,Id))",
        "CREATE TABLE PrizeParticipants (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), AccountId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE RESTRICT, GroupId TEXT, Number INTEGER NOT NULL CHECK(Number>=1), State TEXT NOT NULL CHECK(State IN('Active','Cancelled')), JoinedAt INTEGER NOT NULL, CancelledAt INTEGER, AuthenticationAt INTEGER NOT NULL, UNIQUE(EventId,AccountId), UNIQUE(EventId,Number), UNIQUE(EventId,Id), FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id))",
        "CREATE TABLE PrizeStartCodes (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), TokenHash TEXT NOT NULL UNIQUE, ExpiresAt INTEGER NOT NULL, State TEXT NOT NULL CHECK(State IN('Available','Consumed','Revoked')), ConsumedByParticipantId TEXT UNIQUE, ConsumedAt INTEGER, CHECK((State='Consumed' AND ConsumedByParticipantId IS NOT NULL AND ConsumedAt IS NOT NULL) OR (State!='Consumed' AND ConsumedByParticipantId IS NULL AND ConsumedAt IS NULL)), UNIQUE(EventId,Id), FOREIGN KEY(EventId,ConsumedByParticipantId) REFERENCES PrizeParticipants(EventId,Id))",
        "CREATE TABLE PrizeGroupCodes (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), GroupId TEXT NOT NULL, TokenHash TEXT NOT NULL UNIQUE, ExpiresAt INTEGER NOT NULL, RevokedAt INTEGER, UNIQUE(EventId,Id), FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id))",
        "CREATE TABLE PrizeTypes (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), Name TEXT NOT NULL, Total INTEGER CHECK(Total>=0), Reserved INTEGER NOT NULL DEFAULT 0 CHECK(Reserved>=0), Issued INTEGER NOT NULL DEFAULT 0 CHECK(Issued>=0), Active INTEGER NOT NULL CHECK(Active IN(0,1)), Version INTEGER NOT NULL CHECK(Version>=1), CHECK(Total IS NULL OR Total>=Reserved+Issued), UNIQUE(EventId,Id))",
        "CREATE TABLE PrizeDraws (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), GroupId TEXT, Kind TEXT NOT NULL CHECK(Kind IN('Initial','Replacement','Instant')), Sequence INTEGER NOT NULL CHECK(Sequence>=1), Status TEXT NOT NULL CHECK(Status='Committed'), StartedAt INTEGER NOT NULL, CompletedRevealAt INTEGER NOT NULL CHECK(CompletedRevealAt>StartedAt), AnimationSeconds INTEGER NOT NULL CHECK(AnimationSeconds IN(5,10,15)), CandidateHash TEXT NOT NULL, AlgorithmVersion TEXT NOT NULL, ConfigurationSnapshotJson TEXT NOT NULL CHECK(json_valid(ConfigurationSnapshotJson)), Version INTEGER NOT NULL CHECK(Version>=1), CHECK((Kind='Instant' AND GroupId IS NULL) OR (Kind!='Instant' AND GroupId IS NOT NULL)), UNIQUE(EventId,GroupId,Sequence), UNIQUE(EventId,Id), FOREIGN KEY(EventId,GroupId) REFERENCES PrizeGroups(EventId,Id))",
        "CREATE TABLE PrizeDrawCandidates (EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), DrawId TEXT NOT NULL, ParticipantId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK(Ordinal>=0), PRIMARY KEY(DrawId,ParticipantId), UNIQUE(DrawId,Ordinal), FOREIGN KEY(EventId,DrawId) REFERENCES PrizeDraws(EventId,Id), FOREIGN KEY(EventId,ParticipantId) REFERENCES PrizeParticipants(EventId,Id))",
        "CREATE TABLE PrizeInstantDecisions (EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), ParticipantId TEXT PRIMARY KEY NOT NULL, IsWinner INTEGER NOT NULL CHECK(IsWinner IN(0,1)), Reason TEXT NOT NULL, AlgorithmVersion TEXT NOT NULL, DrawId TEXT, FOREIGN KEY(EventId,ParticipantId) REFERENCES PrizeParticipants(EventId,Id), FOREIGN KEY(EventId,DrawId) REFERENCES PrizeDraws(EventId,Id))",
        "CREATE TABLE PrizeAwards (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), DrawId TEXT NOT NULL, ParticipantId TEXT NOT NULL, PrizeTypeId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK(Ordinal>=0), AnimatesFrom INTEGER NOT NULL, RevealAt INTEGER NOT NULL CHECK(RevealAt>AnimatesFrom), ReplacesAwardId TEXT UNIQUE, UNIQUE(EventId,ParticipantId), UNIQUE(DrawId,Ordinal), UNIQUE(EventId,Id), FOREIGN KEY(EventId,DrawId) REFERENCES PrizeDraws(EventId,Id), FOREIGN KEY(EventId,ParticipantId) REFERENCES PrizeParticipants(EventId,Id), FOREIGN KEY(EventId,PrizeTypeId) REFERENCES PrizeTypes(EventId,Id), FOREIGN KEY(EventId,ReplacesAwardId) REFERENCES PrizeAwards(EventId,Id))",
        "CREATE TABLE PrizeClaims (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), AwardId TEXT NOT NULL UNIQUE, State TEXT NOT NULL CHECK(State IN('Pending','Redeemed','Forfeited','Expired')), ReactionDueAt INTEGER NOT NULL, RedeemedAt INTEGER, ForfeitedAt INTEGER, ExpiredAt INTEGER, Version INTEGER NOT NULL CHECK(Version>=1), FieldSchemaJson TEXT NOT NULL CHECK(json_valid(FieldSchemaJson)), UNIQUE(EventId,Id), FOREIGN KEY(EventId,AwardId) REFERENCES PrizeAwards(EventId,Id))",
        "CREATE TABLE PrizeClaimSecrets (EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), ClaimId TEXT PRIMARY KEY NOT NULL, TokenHash TEXT NOT NULL UNIQUE, ProtectedToken TEXT NOT NULL, KeyVersion TEXT NOT NULL, UNIQUE(EventId,ClaimId), FOREIGN KEY(EventId,ClaimId) REFERENCES PrizeClaims(EventId,Id))",
        "CREATE TABLE PrizeClaimData (EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), ClaimId TEXT PRIMARY KEY NOT NULL, ProtectedPayload TEXT NOT NULL, KeyVersion TEXT NOT NULL, UpdatedAt INTEGER NOT NULL, UNIQUE(EventId,ClaimId), FOREIGN KEY(EventId,ClaimId) REFERENCES PrizeClaims(EventId,Id))",
        "CREATE TABLE PrizeInventoryLedger (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), PrizeTypeId TEXT NOT NULL, AwardId TEXT, Kind TEXT NOT NULL CHECK(Kind IN('Reserve','Release','Issue','Adjust')), Quantity INTEGER NOT NULL CHECK(Quantity!=0), At INTEGER NOT NULL, ActorId TEXT, UNIQUE(EventId,Id), FOREIGN KEY(EventId,PrizeTypeId) REFERENCES PrizeTypes(EventId,Id), FOREIGN KEY(EventId,AwardId) REFERENCES PrizeAwards(EventId,Id))",
        "CREATE TABLE PrizePresentationGrants (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), TokenHash TEXT NOT NULL UNIQUE, ExpiresAt INTEGER NOT NULL, RevokedAt INTEGER, Version INTEGER NOT NULL CHECK(Version>=1), UNIQUE(EventId,Id))",
        "CREATE TABLE PrizePresentationSessions (Hash TEXT PRIMARY KEY NOT NULL, CreatedAt INTEGER NOT NULL, ExpiresAt INTEGER NOT NULL CHECK(ExpiresAt>CreatedAt), RevokedAt INTEGER)",
        "CREATE TABLE PrizePresentationSessionGrants (EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), SessionHash TEXT NOT NULL REFERENCES PrizePresentationSessions(Hash) ON DELETE CASCADE, GrantId TEXT NOT NULL, PRIMARY KEY(SessionHash,EventId,GrantId), FOREIGN KEY(EventId,GrantId) REFERENCES PrizePresentationGrants(EventId,Id) ON DELETE CASCADE)",
        "CREATE TABLE PrizeVisitorSessions (Hash TEXT PRIMARY KEY NOT NULL, AccountId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE RESTRICT, CreatedAt INTEGER NOT NULL, ExpiresAt INTEGER NOT NULL CHECK(ExpiresAt>CreatedAt), AuthenticatedAt INTEGER NOT NULL, RevokedAt INTEGER)",
        "CREATE TABLE PrizeVisitorCeremonies (Id TEXT PRIMARY KEY NOT NULL, BrowserHash TEXT NOT NULL, OperationId TEXT NOT NULL UNIQUE, Options TEXT NOT NULL, ExpiresAt INTEGER NOT NULL, ConsumedAt INTEGER)",
        "CREATE TABLE PrizeOperations (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), ActorScope TEXT NOT NULL, OperationKind TEXT NOT NULL, IdempotencyKey TEXT NOT NULL, RequestHash TEXT NOT NULL, ResultReference TEXT NOT NULL, CompletedAt INTEGER NOT NULL, UNIQUE(EventId,ActorScope,OperationKind,IdempotencyKey), UNIQUE(EventId,Id))",
        "CREATE TABLE PrizeCreationOperations (ActorId TEXT NOT NULL, OperationKind TEXT NOT NULL CHECK(OperationKind='CreateDraft'), IdempotencyKey TEXT NOT NULL, CanonicalPayload TEXT NOT NULL, RequestHash TEXT NOT NULL, EventId TEXT NOT NULL UNIQUE REFERENCES PrizeEvents(Id), CompletedAt INTEGER NOT NULL, PRIMARY KEY(ActorId,OperationKind,IdempotencyKey))",
        "CREATE TABLE PrizeAudit (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), At INTEGER NOT NULL, ActorId TEXT, Action TEXT NOT NULL, TargetId TEXT, OperationId TEXT NOT NULL, PreviousVersion INTEGER, NewVersion INTEGER, UNIQUE(EventId,Id))",
        "CREATE TABLE PrizeOutbox (Id TEXT PRIMARY KEY NOT NULL, EventId TEXT NOT NULL REFERENCES PrizeEvents(Id), Revision INTEGER NOT NULL CHECK(Revision>=1), Kind TEXT NOT NULL, SafeReference TEXT NOT NULL, DueAt INTEGER NOT NULL, PublishedAt INTEGER, UNIQUE(EventId,Revision,Kind,SafeReference), UNIQUE(EventId,Id))",
        "CREATE TABLE PrizeSchemaContract (Version INTEGER PRIMARY KEY CHECK(Version=6), Fingerprint TEXT NOT NULL)",
        "CREATE UNIQUE INDEX PrizeOneOpenGroup ON PrizeGroups(EventId) WHERE Status='Open'",
        "CREATE UNIQUE INDEX PrizeOneInitialDraw ON PrizeDraws(EventId,GroupId) WHERE Kind='Initial'",
        "CREATE UNIQUE INDEX PrizeLedgerOnce ON PrizeInventoryLedger(AwardId,Kind) WHERE AwardId IS NOT NULL AND Kind IN('Reserve','Release','Issue')",
        "CREATE INDEX PrizePurgeDue ON PrizeEvents(PurgeAt)",
        "CREATE INDEX PrizeClaimsDue ON PrizeClaims(EventId,State,ReactionDueAt)",
        "CREATE INDEX PrizeOutboxDue ON PrizeOutbox(PublishedAt,DueAt)",
        "CREATE INDEX PrizeParticipantsAccount ON PrizeParticipants(AccountId)",
        "CREATE INDEX PrizePresentationExpiry ON PrizePresentationSessions(ExpiresAt)"
    ];
    private static readonly Dictionary<string,string> Legacy = new()
    {
        ["index:OneActiveExperienceEvent"] = "CREATE UNIQUE INDEX OneActiveExperienceEvent ON ExperienceEvents((1)) WHERE Status='Active'",
        ["table:Challenges"] = "CREATE TABLE Challenges (\n  DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE,\n  SessionHash TEXT NOT NULL, Kind TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, OperationId TEXT NOT NULL DEFAULT '')",
        ["table:Credentials"] = "CREATE TABLE Credentials (\n  Id TEXT PRIMARY KEY, DemoId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE CASCADE,\n  PublicKey BLOB NOT NULL, Counter INTEGER NOT NULL, BackupEligible INTEGER NOT NULL, BackedUp INTEGER NOT NULL, Alias TEXT NOT NULL DEFAULT '')",
        ["table:DailyExperienceStats"] = "CREATE TABLE DailyExperienceStats (\n  Day TEXT PRIMARY KEY, Visitors INTEGER NOT NULL DEFAULT 0, Authentications INTEGER NOT NULL DEFAULT 0)",
        ["table:Demos"] = "CREATE TABLE Demos (\n  Id TEXT PRIMARY KEY, DemoRunId TEXT NOT NULL UNIQUE, Name TEXT NOT NULL UNIQUE,\n  UserHandle BLOB NOT NULL UNIQUE, SessionHash TEXT NOT NULL UNIQUE, Route TEXT NOT NULL,\n  Created INTEGER NOT NULL, LastSeen INTEGER NOT NULL, Successes INTEGER NOT NULL DEFAULT 0,\n  Failures INTEGER NOT NULL DEFAULT 0, LastAuthenticatedAt INTEGER NULL, ClosedAt INTEGER NULL)",
        ["table:EventPrizePolicies"] = "CREATE TABLE EventPrizePolicies (\n  EventId TEXT PRIMARY KEY REFERENCES ExperienceEvents(Id), Enabled INTEGER NOT NULL DEFAULT 0 CHECK(Enabled=0),\n  PrizeLimit INTEGER NOT NULL DEFAULT 0 CHECK(PrizeLimit>=0), Awarded INTEGER NOT NULL DEFAULT 0 CHECK(Awarded=0))",
        ["table:ExperienceEvents"] = "CREATE TABLE ExperienceEvents (\n  Id TEXT PRIMARY KEY, Name TEXT NOT NULL, StartedAt INTEGER NOT NULL, EndedAt INTEGER,\n  Status TEXT NOT NULL CHECK(Status IN ('Active','Closed','Imported')),\n  Visitors INTEGER NOT NULL DEFAULT 0 CHECK(Visitors>=0), Authentications INTEGER NOT NULL DEFAULT 0 CHECK(Authentications>=0))",
        ["table:ExperienceMigrations"] = "CREATE TABLE ExperienceMigrations (Version INTEGER PRIMARY KEY)",
        ["table:MonitorGrants"] = "CREATE TABLE MonitorGrants(DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE, Expires INTEGER NOT NULL)",
        ["table:PresenterAccounts"] = "CREATE TABLE PresenterAccounts (\r\n  Id TEXT PRIMARY KEY, Login TEXT NOT NULL UNIQUE COLLATE NOCASE, Name TEXT NOT NULL,\r\n  Role TEXT NOT NULL CHECK(Role IN ('Administrator','Presenter')), Active INTEGER NOT NULL DEFAULT 0,\r\n  Pending INTEGER NOT NULL DEFAULT 1, Handle BLOB NOT NULL UNIQUE, Created INTEGER NOT NULL, Activated INTEGER, LastLogin INTEGER)",
        ["table:PresenterAudit"] = "CREATE TABLE PresenterAudit (\r\n  Id INTEGER PRIMARY KEY AUTOINCREMENT, At INTEGER NOT NULL, Actor TEXT, Action TEXT NOT NULL, Target TEXT)",
        ["table:PresenterAuthSessions"] = "CREATE TABLE PresenterAuthSessions (\r\n  Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL,\r\n  Reauthenticated INTEGER)",
        ["table:PresenterCeremonies"] = "CREATE TABLE \"PresenterCeremonies\" (\n  BrowserHash TEXT PRIMARY KEY, AccountId TEXT REFERENCES PresenterAccounts(Id), Kind TEXT NOT NULL,\n  OperationId TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, Binding TEXT NOT NULL,\n  CHECK(AccountId IS NOT NULL OR Kind='login'))",
        ["table:PresenterEnrollmentGrants"] = "CREATE TABLE PresenterEnrollmentGrants (\r\n  Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL, Bootstrap INTEGER NOT NULL)",
        ["table:PresenterInvitations"] = "CREATE TABLE PresenterInvitations (\r\n  Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL, Bootstrap INTEGER NOT NULL)",
        ["table:PresenterKeys"] = "CREATE TABLE PresenterKeys (\r\n  Id TEXT PRIMARY KEY, Ref TEXT NOT NULL UNIQUE, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), PublicKey BLOB NOT NULL,\r\n  Counter INTEGER NOT NULL, BackupEligible INTEGER NOT NULL, BackedUp INTEGER NOT NULL,\r\n  Name TEXT NOT NULL, Registered INTEGER NOT NULL, Used INTEGER)",
        ["table:PresenterSessions"] = "CREATE TABLE PresenterSessions (Hash TEXT PRIMARY KEY, Expires INTEGER NOT NULL)",
        ["table:VisitorCounts"] = "CREATE TABLE VisitorCounts (\n  Id INTEGER PRIMARY KEY AUTOINCREMENT, DemoId TEXT UNIQUE REFERENCES Demos(Id) ON DELETE SET NULL,\n  EventId TEXT NOT NULL REFERENCES ExperienceEvents(Id), VisitorNumber INTEGER NOT NULL,\n  UNIQUE(EventId,VisitorNumber))",
        ["trigger:PresenterAuditNoDelete"] = "CREATE TRIGGER PresenterAuditNoDelete BEFORE DELETE ON PresenterAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END",
        ["trigger:PresenterAuditNoUpdate"] = "CREATE TRIGGER PresenterAuditNoUpdate BEFORE UPDATE ON PresenterAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END"
    };
    internal static string Normalize(string sql)
    {
        // Ignore layout/identifier quoting, never whitespace inside SQL string literals.
        var output=new StringBuilder();var literal=false;
        for(var i=0;i<sql.Length;i++)
        {
            var ch=sql[i];
            if(ch=='\'')
            {
                output.Append(ch);
                if(literal && i+1<sql.Length && sql[i+1]=='\'') { output.Append(sql[++i]);continue; }
                literal=!literal;continue;
            }
            if(!literal && (char.IsWhiteSpace(ch) || ch=='"'))continue;
            output.Append(ch);
        }
        return output.ToString();
    }
    private static Dictionary<string,string> Expected => Statements.ToDictionary(
        s => { var m=Regex.Match(s, "^CREATE (?:UNIQUE )?(TABLE|INDEX) ([A-Za-z0-9_]+)"); return m.Groups[1].Value.ToLowerInvariant()+":"+m.Groups[2].Value; },
        Normalize, StringComparer.Ordinal);
    private static string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Expected.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value)))));

    private static async Task<Dictionary<string,string>> Schema(SqliteConnection c, SqliteTransaction? tx, CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT type,name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
        using var r=await q.ExecuteReaderAsync(ct);var result=new Dictionary<string,string>(StringComparer.Ordinal);
        while(await r.ReadAsync(ct)) result.Add(r.GetString(0)+":"+r.GetString(1),Normalize(r.GetString(2)));
        return result;
    }
    private static async Task<int[]> Versions(SqliteConnection c,SqliteTransaction? tx,Dictionary<string,string> schema,CancellationToken ct)
    {
        if(!schema.ContainsKey("table:ExperienceMigrations")) return [];
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT Version FROM ExperienceMigrations ORDER BY Version";
        using var r=await q.ExecuteReaderAsync(ct);var result=new List<int>();
        while(await r.ReadAsync(ct))result.Add(r.GetInt32(0));
        return result.ToArray();
    }
    private static bool LegacyMatches(string key,string actual,string expected)
    {
        if(actual==Normalize(expected))return true;
        // Explicit historical shapes only; arbitrary extra columns/constraints are never ignored.
        var n=Normalize(expected);
        if(key=="table:Demos")return actual==n.Replace(",LastAuthenticatedAtINTEGERNULL,ClosedAtINTEGERNULL","")
            || actual==n.Replace(",ClosedAtINTEGERNULL","");
        if(key=="table:Credentials")return actual==n.Replace(",AliasTEXTNOTNULLDEFAULT''","");
        if(key=="table:Challenges")return actual==n.Replace(",OperationIdTEXTNOTNULLDEFAULT''","");
        if(key=="table:PresenterCeremonies")return actual==Normalize(
            "CREATE TABLE PresenterCeremonies (BrowserHash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Kind TEXT NOT NULL, OperationId TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, Binding TEXT NOT NULL)");
        return false;
    }
    private static async Task Validate(SqliteConnection c,SqliteTransaction? tx,bool requireCurrent,CancellationToken ct)
    {
        var schema=await Schema(c,tx,ct);var versions=await Versions(c,tx,schema,ct);
        var known = new[]{ Array.Empty<int>(),new[]{3},new[]{3,4},new[]{3,4,5},new[]{3,4,5,6} };
        if(!known.Any(v=>v.SequenceEqual(versions)))throw new InvalidOperationException("Unsupported migration history.");
        var migrated=versions.Contains(Version);var expected=Expected;
        var identity=IdentityMigration.Expected;
        var eventAccess=EventAccessMigration.Expected;
        VisitorFlowMigration.Validate(schema);
        if(!migrated && schema.Keys.Any(identity.ContainsKey)) throw new InvalidOperationException("Identity schema requires Prize migration 6.");
        await IdentityMigration.ValidateAsync(c,tx,schema,ct);
        await EventAccessMigration.ValidateAsync(c,tx,schema,ct);
        await PrizeRoundsMigration.ValidateAsync(c,tx,ct);
        await PrizeRoundHistoryMigration.ValidateAsync(c,tx,ct);
        var rounds=PrizeRoundsMigration.Expected;
        var roundHistory=PrizeRoundHistoryMigration.Expected;
        if(schema.Keys.Any(roundHistory.ContainsKey)&&(!migrated||!schema.ContainsKey("table:PrizeRoundsContract")))throw new InvalidOperationException("Round history requires the existing Prize and rounds contracts.");
        var scoped=schema.ContainsKey("table:PresenterEventMigrations");
        foreach(var (key,sql) in schema)
        {
            if(migrated&&scoped&&key=="table:HelperInstallations"&&sql==HelperInstallationSchema.Expected)continue;
            if(VisitorFlowMigration.Expected.TryGetValue(key,out var flowSql)&&sql==flowSql)continue;
            if(identity.TryGetValue(key,out var identitySql) && sql==identitySql) continue;
            if(eventAccess.TryGetValue(key,out var eventSql)&&sql==eventSql)continue;
            if(rounds.TryGetValue(key,out var roundsSql)&&sql==roundsSql)continue;
            if(roundHistory.TryGetValue(key,out var historySql)&&sql==historySql)continue;
            if(scoped&&key=="index:OneActiveExperienceEvent")throw new InvalidOperationException("Retired global event index is present");
            if(Legacy.TryGetValue(key,out var original)) {
                if((requireCurrent || versions.Contains(5)) ? sql!=Normalize(original) : !LegacyMatches(key,sql,original))throw new InvalidOperationException("Unexpected legacy schema: "+key);
            } else if(!migrated || !expected.TryGetValue(key,out var wanted) || wanted!=sql)
                throw new InvalidOperationException("Unexpected schema object: "+key);
        }
        if(versions.Contains(3) || requireCurrent)
        {
            var required=Legacy.Keys.Where(k=>versions.Contains(4) || requireCurrent
                || !k.Contains(":Presenter",StringComparison.Ordinal) || k=="table:PresenterSessions");
            foreach(var key in required)
            {
                if(scoped&&key=="index:OneActiveExperienceEvent")continue;
                if(!schema.ContainsKey(key))throw new InvalidOperationException("Missing legacy object: "+key);
            }
        }
        if(migrated)
        {
            foreach(var key in expected.Keys)if(!schema.ContainsKey(key))throw new InvalidOperationException("Incomplete prize schema: "+key);
            using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT Fingerprint FROM PrizeSchemaContract WHERE Version=6";
            if((string?)await q.ExecuteScalarAsync(ct)!=Fingerprint)throw new InvalidOperationException("Conflicting prize schema identity.");
        }
        await Integrity(c,tx,ct);
    }
    internal static async Task Integrity(SqliteConnection c,SqliteTransaction? tx,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="PRAGMA integrity_check";
        if((string?)await q.ExecuteScalarAsync(ct)!="ok")throw new InvalidOperationException("Database integrity failure.");
        q.CommandText="PRAGMA foreign_key_check";using var r=await q.ExecuteReaderAsync(ct);
        if(await r.ReadAsync(ct))throw new InvalidOperationException("Database foreign key failure.");
    }
    // Runs BEFORE legacy initialization can repair/mutate an unexpected existing database.
    internal static async Task<bool> PreflightAsync(DemoStore store,CancellationToken ct)
    {
        if(!File.Exists(store.DatabasePath) || new FileInfo(store.DatabasePath).Length==0)return false;
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=store.DatabasePath,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false }.ToString());
        await c.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await Validate(c,tx,false,ct);
        return (await Versions(c,tx,await Schema(c,tx,ct),ct)).Contains(5);
    }
    internal static async Task InitializeAsync(DemoStore store,CancellationToken ct,Action? afterSchema=null,Action? beforeMarker=null)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await Validate(c,tx,true,ct);
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=6";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0) { tx.Commit();return; }
        foreach(var statement in Statements){q.CommandText=statement;await q.ExecuteNonQueryAsync(ct);}
        afterSchema?.Invoke(); // Internal test fault injection, no configuration or HTTP access.
        foreach(var p in PrizeProfiles.BuiltIns)
        {
            p.Configuration.Validate();q.Parameters.Clear();
            q.CommandText="INSERT INTO PrizeProfiles VALUES($id,$name,1,$json,1,1,0,0)";
            q.Parameters.AddWithValue("$id",p.Id);q.Parameters.AddWithValue("$name",p.Name);
            q.Parameters.AddWithValue("$json",System.Text.Json.JsonSerializer.Serialize(p.Configuration));
            await q.ExecuteNonQueryAsync(ct);
        }
        q.Parameters.Clear();q.CommandText="INSERT INTO PrizeInstallationSettings VALUES(1,NULL,1);";
        await q.ExecuteNonQueryAsync(ct);
        q.CommandText="INSERT INTO PrizeSchemaContract VALUES(6,$hash)";q.Parameters.AddWithValue("$hash",Fingerprint);
        await q.ExecuteNonQueryAsync(ct);q.Parameters.Clear();
        beforeMarker?.Invoke();
        q.CommandText="INSERT INTO ExperienceMigrations(Version) VALUES(6)";await q.ExecuteNonQueryAsync(ct);
        await Validate(c,tx,true,ct);tx.Commit();
    }
}
