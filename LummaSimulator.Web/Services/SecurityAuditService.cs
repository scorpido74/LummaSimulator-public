using System.Text.Json;
using LummaSimulator.Web.Models;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public enum IdentityEvent
{
    IDENTITY_CREATED=6101, PASSKEY_REGISTRATION_STARTED=6102, PASSKEY_REGISTERED=6103,
    PASSKEY_REGISTRATION_FAILED=6104, AUTHENTICATION_STARTED=6105, AUTHENTICATION_SUCCEEDED=6106,
    AUTHENTICATION_FAILED=6107, SESSION_CREATED=6108, SESSION_ACTIVITY=6109, SESSION_REVOKED=6110, USER_LOGGED_OUT=6111
}
public sealed record IdentityAuditEvent(int SchemaVersion,DateTimeOffset At,int EventId,string EventName,
    string? DemoRunId,string? UserId,string? CredentialHash,string? SessionId,string AuthenticationMethod,
    string Result,string SourceComponent,string CorrelationId,string? IpAddress,bool Simulation);
public sealed record IdentitySession(string Id,string UserId,string DemoRunId,DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,DateTimeOffset ExpiresAt,string AuthenticationMethod,string? CredentialHash,
    string Status,string SessionType,bool Current);
public sealed record AuditEntry(long Sequence,IdentityAuditEvent Event);

public sealed record IdentityAuditExportBatch(Guid StreamId,IReadOnlyList<AuditEntry> Entries);
// Independent sinks consume committed SQLite records, never an authentication callback.
public interface IIdentityAuditExporter
{
    string Name { get; }
    Task CheckAvailabilityAsync(CancellationToken ct) => Task.CompletedTask;
    Task ExportAsync(IdentityAuditExportBatch batch,CancellationToken ct);
}

public sealed class SecurityAuditService(DemoStore store,TimeProvider clock,IHttpContextAccessor? accessor=null,IConfiguration? configuration=null)
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private long Cutoff => Now-(long)TimeSpan.FromMinutes(configuration?.GetValue("Experience:AccountIdleMinutes",29) ?? 29).TotalMilliseconds;
    internal static SqliteCommand Command(SqliteConnection c,SqliteTransaction tx,string sql,params (string,object?)[] args)
    {
        var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;
        foreach(var (name,value) in args) q.Parameters.AddWithValue(name,value ?? DBNull.Value);
        return q;
    }
    public static string Correlation(string? value) => Guid.TryParseExact(value,"D",out var id) ? id.ToString("D") : Guid.NewGuid().ToString("D");
    private string RequestCorrelation(string? operation)
    {
        var http=accessor?.HttpContext;
        if(http is null) return Correlation(operation);
        const string key="Lumma.IdentityCorrelation";
        if(http.Items[key] is not string value) {
            var header=http.Request.Headers["X-Lumma-Correlation"].ToString();
            value=Guid.TryParseExact(header,"D",out _) ? Correlation(header)
                : Correlation(operation ?? http.Request.Headers["X-Lumma-Operation"].ToString());
            http.Items[key]=value;
        }
        return value;
    }
    internal async Task WriteAsync(SqliteConnection c,SqliteTransaction tx,IdentityEvent kind,DemoAccount? account,
        string result,CancellationToken ct,string? credentialId=null,string? sessionId=null,string? operation=null)
    {
        if(sessionId is null && account is not null) {
            using var lookup=Command(c,tx,"SELECT Id FROM IdentitySessions WHERE BindingHash=$hash",("$hash",account.SessionHash));
            sessionId=await lookup.ExecuteScalarAsync(ct) as string;
        }
        var item=new IdentityAuditEvent(1,clock.GetUtcNow(),(int)kind,kind.ToString(),account?.DemoRunId,account?.Id,
            credentialId is null ? null : DemoIdentity.Hash(credentialId),sessionId,
            kind==IdentityEvent.IDENTITY_CREATED ? "None" : "WebAuthn",result,"LummaSimulator.Web",RequestCorrelation(operation),
            configuration?.GetValue("SecurityAudit:IncludeIpAddress",false)==true ? accessor?.HttpContext?.Connection.RemoteIpAddress?.ToString() : null,false);
        using var q=Command(c,tx,"INSERT INTO IdentityAudit(At,UserId,DemoRunId,EventName,Payload) VALUES($at,$user,$run,$name,$json)",
            ("$at",item.At.ToUnixTimeMilliseconds()),("$user",item.UserId),("$run",item.DemoRunId),("$name",item.EventName),("$json",JsonSerializer.Serialize(item,JsonOptions)));
        await q.ExecuteNonQueryAsync(ct);
    }
    internal async Task RecordAsync(IdentityEvent kind,DemoAccount? account,string result,CancellationToken ct,string? credentialId=null,string? operation=null)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await WriteAsync(c,tx,kind,account,result,ct,credentialId,operation:operation);tx.Commit();
    }
    internal async Task AuthenticatedAsync(SqliteConnection c,SqliteTransaction tx,DemoAccount account,string credentialId,long now,CancellationToken ct)
    {
        string? session;
        using(var q=Command(c,tx,"SELECT Id FROM IdentitySessions WHERE BindingHash=$hash",("$hash",account.SessionHash))) session=await q.ExecuteScalarAsync(ct) as string;
        var created=session is null;session ??=Guid.NewGuid().ToString("D");
        using(var q=Command(c,tx,"""
            INSERT INTO IdentitySessions(Id,UserId,DemoRunId,BindingHash,CreatedAt,LastActivity,ExpiresAt,CredentialHash,Method,SessionType)
            VALUES($id,$user,$run,$hash,$now,$now,$expires,$credential,'WebAuthn','Normal')
            ON CONFLICT(BindingHash) DO UPDATE SET LastActivity=$now,ExpiresAt=$expires,CredentialHash=$credential,RevokedAt=NULL
            """,("$id",session),("$user",account.Id),("$run",account.DemoRunId),("$hash",account.SessionHash),
            ("$now",now),("$expires",now+1800000),("$credential",DemoIdentity.Hash(credentialId)))) await q.ExecuteNonQueryAsync(ct);
        await WriteAsync(c,tx,IdentityEvent.AUTHENTICATION_SUCCEEDED,account,"Success",ct,credentialId,session);
        if(created) await WriteAsync(c,tx,IdentityEvent.SESSION_CREATED,account,"Success",ct,credentialId,session);
    }
    // Reads never extend authentication. Registry entries alone never confer access.
    internal async Task<bool> IsAuthenticatedAsync(DemoAccount account,CancellationToken ct)
    {
        if(account.LastAuthenticatedAt is not { } at || at>Now || at<=Now-1800000 || account.LastSeen<=Cutoff) return false;
        return Convert.ToInt64(await store.ScalarAsync("""
            SELECT COUNT(*) FROM IdentitySessions WHERE UserId=$user AND BindingHash=$hash
            AND RevokedAt IS NULL AND ExpiresAt>$now AND LastActivity>$cutoff
            """,ct,("$user",account.Id),("$hash",account.SessionHash),("$now",Now),("$cutoff",Cutoff)))==1;
    }
    internal async Task<DemoAccount?> OwnerAsync(string? secret,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(secret)) return null;
        var account=(await store.AccountsAsync(DemoIdentity.Hash(secret!),ct)).SingleOrDefault();
        return account is not null && await IsAuthenticatedAsync(account,ct) ? account : null;
    }
    internal async Task TouchAsync(DemoAccount account,CancellationToken ct)
    {
        if(!await IsAuthenticatedAsync(account,ct)) return;
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        string? id;long last;
        using(var q=Command(c,tx,"SELECT Id,LastActivity FROM IdentitySessions WHERE BindingHash=$hash",("$hash",account.SessionHash)))
        using(var r=await q.ExecuteReaderAsync(ct)) { if(!await r.ReadAsync(ct)) return;id=r.GetString(0);last=r.GetInt64(1); }
        using(var q=Command(c,tx,"UPDATE IdentitySessions SET LastActivity=$now WHERE Id=$id AND RevokedAt IS NULL AND ExpiresAt>$now AND EXISTS(SELECT 1 FROM Demos WHERE Id=$user AND SessionHash=$hash AND ClosedAt IS NULL AND LastAuthenticatedAt>$authCutoff)",("$now",Now),("$id",id),("$user",account.Id),("$hash",account.SessionHash),("$authCutoff",Now-1800000)))
            if(await q.ExecuteNonQueryAsync(ct)!=1) return;
        using(var q=Command(c,tx,"SELECT COALESCE(MAX(At),0) FROM IdentityAudit WHERE UserId=$user AND EventName='SESSION_ACTIVITY' AND json_extract(Payload,'$.sessionId')=$id",("$user",account.Id),("$id",id)))
            last=Convert.ToInt64(await q.ExecuteScalarAsync(ct));
        if(Now-last>=60000) await WriteAsync(c,tx,IdentityEvent.SESSION_ACTIVITY,account,"Success",ct,sessionId:id);
        tx.Commit();
    }
    // Caller holds the existing DemoStore gate. No user/tenant identifier comes from a request parameter.
    internal async Task<List<IdentitySession>> SessionsAsync(DemoAccount owner,CancellationToken ct,int offset=0,int limit=50,long? from=null,long? until=null,string? run=null)
    {
        var result=new List<IdentitySession>();await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        using var q=Command(c,tx,"""
            SELECT s.Id,s.UserId,s.DemoRunId,s.CreatedAt,s.LastActivity,s.ExpiresAt,s.Method,s.CredentialHash,
              CASE WHEN s.RevokedAt IS NOT NULL THEN 'Revoked'
              WHEN s.ExpiresAt<=$now OR s.LastActivity<=$cutoff OR d.Id IS NULL OR d.ClosedAt IS NOT NULL
                OR d.LastSeen<=$cutoff OR d.LastAuthenticatedAt IS NULL OR d.LastAuthenticatedAt<=$authCutoff
                OR d.LastAuthenticatedAt>$now OR d.SessionHash<>s.BindingHash THEN 'Expired' ELSE 'Active' END,
              s.SessionType,s.BindingHash=$hash
            FROM IdentitySessions s LEFT JOIN Demos d ON d.Id=s.UserId
            WHERE s.UserId=$user AND ($from IS NULL OR s.CreatedAt>=$from) AND ($until IS NULL OR s.CreatedAt<=$until)
            AND ($run IS NULL OR s.DemoRunId=$run) ORDER BY s.CreatedAt DESC,s.Id LIMIT $limit OFFSET $offset
            """,("$user",owner.Id),("$hash",owner.SessionHash),("$now",Now),("$cutoff",Cutoff),("$authCutoff",Now-1800000),
            ("$limit",Math.Clamp(limit,1,100)),("$offset",Math.Clamp(offset,0,100000)),("$from",from),("$until",until),("$run",run));
        using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)),
            DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetString(8),r.GetString(9),r.GetBoolean(10)));
        return result;
    }
    internal async Task<List<AuditEntry>> AuditAsync(DemoAccount owner,long? from,long? until,string? eventName,string? run,int offset,int limit,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        using var q=Command(c,tx,"""
            SELECT Sequence,Payload FROM IdentityAudit WHERE UserId=$user AND ($from IS NULL OR At>=$from)
            AND ($until IS NULL OR At<=$until) AND ($event IS NULL OR EventName=$event) AND ($run IS NULL OR DemoRunId=$run)
            ORDER BY Sequence DESC LIMIT $limit OFFSET $offset
            """,("$user",owner.Id),("$from",from),("$until",until),("$event",eventName),("$run",run),
            ("$limit",Math.Clamp(limit,1,100)),("$offset",Math.Clamp(offset,0,100000)));
        var result=new List<AuditEntry>();using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) result.Add(new(r.GetInt64(0),JsonSerializer.Deserialize<IdentityAuditEvent>(r.GetString(1),JsonOptions)!));return result;
    }
    internal async Task RevokeAsync(DemoAccount account,string? target,bool logout,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await RevokeWithinAsync(c,tx,account,target,logout,ct);tx.Commit();
    }
    internal async Task RevokeWithinAsync(SqliteConnection c,SqliteTransaction tx,DemoAccount account,string? target,bool logout,CancellationToken ct)
    {
        using(var q=Command(c,tx,"""
            UPDATE IdentitySessions SET RevokedAt=$now WHERE UserId=$user AND RevokedAt IS NULL AND ($id IS NULL OR Id=$id)
            RETURNING Id,BindingHash
            """,("$now",Now),("$user",account.Id),("$id",target))) {
            var revoked=new List<(string,string)>();
            using(var r=await q.ExecuteReaderAsync(ct)) while(await r.ReadAsync(ct)) revoked.Add((r.GetString(0),r.GetString(1)));
            foreach(var (id,hash) in revoked) {
                // Rotate the actual ownership binding as well. A registry flag alone must
                // not leave an old cookie/challenge usable by other portal entrypoints.
                using var update=Command(c,tx,"UPDATE Demos SET SessionHash=$revoked WHERE Id=$user AND SessionHash=$hash; DELETE FROM Challenges WHERE DemoId=$user AND SessionHash=$hash",("$user",account.Id),("$hash",hash),("$revoked",DemoIdentity.Hash(DemoIdentity.RandomToken())));await update.ExecuteNonQueryAsync(ct);
                await WriteAsync(c,tx,IdentityEvent.SESSION_REVOKED,account,"Success",ct,sessionId:id);
            }
        }
        if(logout) await WriteAsync(c,tx,IdentityEvent.USER_LOGGED_OUT,account,"Success",ct);
    }
    internal async Task<bool?> IsCurrentSessionAsync(DemoAccount owner,string id,CancellationToken ct)
    {
        var value=await store.ScalarAsync("SELECT BindingHash=$hash FROM IdentitySessions WHERE UserId=$user AND Id=$id",ct,
            ("$hash",owner.SessionHash),("$user",owner.Id),("$id",id));
        return value is null or DBNull ? null : Convert.ToInt64(value)==1;
    }
    internal async Task<IReadOnlyList<AuditEntry>> ReadExportBatchAsync(long after,int limit,CancellationToken ct)
    {
        // A short, read-only connection: no Gate held during sink I/O, no schema/PRAGMA writes.
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=store.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1 }.ToString());
        await c.OpenAsync(ct);using var q=c.CreateCommand();
        q.CommandText="SELECT COALESCE(MAX(Sequence),0) FROM IdentityAudit";
        if(Convert.ToInt64(await q.ExecuteScalarAsync(ct))<after) throw new InvalidOperationException("Audit source precedes export checkpoint.");
        q.CommandText="SELECT Sequence,Payload FROM IdentityAudit WHERE Sequence>$after ORDER BY Sequence LIMIT $limit";
        q.Parameters.AddWithValue("$after",after);q.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,200));
        var result=new List<AuditEntry>();using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) result.Add(new(r.GetInt64(0),JsonSerializer.Deserialize<IdentityAuditEvent>(r.GetString(1),JsonOptions)
            ?? throw new JsonException("Invalid audit envelope.")));
        return result;
    }
}
