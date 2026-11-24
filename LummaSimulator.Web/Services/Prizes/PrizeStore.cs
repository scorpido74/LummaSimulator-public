using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services.Prizes;

// Shared storage. Management is presenter-only; no visitor admission or drawing.
internal sealed partial class PrizeStore(DemoStore store,PrizeAuthorization authorization,TimeProvider clock)
{
    internal static SqliteCommand Command(SqliteConnection c,SqliteTransaction tx,string sql,params (string,object?)[] values)
    {
        var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;
        foreach(var (name,value) in values)q.Parameters.AddWithValue(name,value??DBNull.Value);
        return q;
    }
    internal async Task<PrizeProfile> DefaultProfileAsync(CancellationToken ct=default)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        using var q=Command(c,tx,"""
            SELECT p.Id,p.Name,p.ConfigJson,p.Version FROM PrizeProfiles p
            WHERE p.Id=COALESCE((SELECT LastProfileId FROM PrizeInstallationSettings WHERE Id=1),$default)
            """,("$default",PrizeProfiles.Cybertruck));
        using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new InvalidOperationException("Missing default profile.");
        var config=JsonSerializer.Deserialize<PrizeConfiguration>(r.GetString(2))??throw new InvalidOperationException("Invalid profile.");
        config.Validate();return new(r.GetString(0),r.GetString(1),config,r.GetInt64(3));
    }
    internal async Task<PrizeDraft> CreateDraftAsync(ClaimsPrincipal principal,string idempotencyKey,string name,string profileId,
        CancellationToken ct=default,Action? afterInsert=null,PrizeConfiguration? configuration=null,bool start=false)
    {
        if(!Guid.TryParseExact(idempotencyKey,"D",out var key))throw new ArgumentException("A UUID idempotency key is required.");
        if(string.IsNullOrWhiteSpace(name) || name.Trim().Length>100 || string.IsNullOrWhiteSpace(profileId) || profileId.Length>100)
            throw new ArgumentException("Invalid draft request.");
        name=name.Trim();idempotencyKey=key.ToString("D");
        var canonical=configuration is null && !start
            ?JsonSerializer.Serialize(new { Name=name,ProfileId=profileId })
            :JsonSerializer.Serialize(new { ManagementVersion=1,Name=name,ProfileId=profileId,Configuration=configuration,Start=start });
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        try
        {
            await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
            var actor=await authorization.RequireActorAsync(c,tx,principal,ct);
            using(var prior=Command(c,tx,"""
                SELECT o.CanonicalPayload,o.RequestHash,e.Id,e.Name,e.ProfileId,e.ProfileSnapshotJson,e.Version
                FROM PrizeCreationOperations o JOIN PrizeEvents e ON e.Id=o.EventId
                WHERE o.ActorId=$actor AND o.OperationKind='CreateDraft' AND o.IdempotencyKey=$key
                """,("$actor",actor),("$key",idempotencyKey)))
            using(var r=await prior.ExecuteReaderAsync(ct))
            {
                if(await r.ReadAsync(ct))
                {
                    await authorization.RequireEventAsync(c,tx,principal,r.GetString(2),false,ct);
                    if(r.GetString(0)!=canonical || r.GetString(1)!=hash)throw new PrizeConflictException("PrizeIntentChanged");
                    return new(r.GetString(2),r.GetString(3),r.IsDBNull(4)?profileId:r.GetString(4),r.GetString(5),r.GetInt64(6));
                }
            }
            string snapshot;PrizeConfiguration config;
            using(var profile=Command(c,tx,"SELECT ConfigJson FROM PrizeProfiles WHERE Id=$id",("$id",profileId)))
            {
                snapshot=await profile.ExecuteScalarAsync(ct) as string??throw new ArgumentException("Unknown profile.");
                config=configuration??JsonSerializer.Deserialize<PrizeConfiguration>(snapshot)??throw new ArgumentException("Invalid profile.");
                config.Validate();
                snapshot=JsonSerializer.Serialize(config);
            }
            var id=Guid.NewGuid().ToString("D");var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
            using(var insert=Command(c,tx,"""
                INSERT INTO PrizeEvents(Id,Name,ProfileId,ProfileSnapshotJson,ConfigVersion,PrizeEnabled,Visibility,Mode,
                  GroupSize,WinnersPerGroup,AssignmentMode,NextGroupMode,DrawMethod,RevealMode,AnimationSeconds,StartCodeRequired,
                  PrizeSelection,ChanceNumerator,ChanceDenominator,MaxInstantWins,ReactionSeconds,RetentionDays,
                  PostCloseClaimSeconds,ClaimFieldSchemaJson,Status,CreatedAt,Version)
                VALUES($id,$name,$profile,$snapshot,1,$enabled,$visibility,$mode,$size,$winners,$assignment,$next,$method,$reveal,
                  $animation,$codes,$selection,$numerator,$denominator,$maximum,$reaction,$retention,$postclose,$fields,'Draft',$now,1)
                """,("$id",id),("$name",name),("$profile",profileId),("$snapshot",snapshot),
                ("$enabled",config.PrizeEnabled),("$visibility",config.Visibility),("$mode",config.Mode),("$size",config.GroupSize),("$winners",config.WinnersPerGroup),
                ("$assignment",config.AssignmentMode),("$next",config.NextGroupMode),("$method",config.DrawMethod),
                ("$reveal",config.RevealMode),("$animation",config.AnimationSeconds),("$codes",config.StartCodeRequired),
                ("$selection",config.PrizeSelection),("$numerator",config.ChanceNumerator),("$denominator",config.ChanceDenominator),
                ("$maximum",config.MaxInstantWins),("$reaction",config.ReactionSeconds),("$retention",config.RetentionDays),
                ("$postclose",config.PostCloseClaimSeconds),("$fields",config.ClaimFieldSchemaJson),("$now",now)))
                await insert.ExecuteNonQueryAsync(ct);
            afterInsert?.Invoke();
            await EventAccess.Register(c,tx,"prize",id,name,actor,ct);
            if(start)
            {
                await ConfigurePrice(c,tx,id,config,ct);
                await Exec(c,tx,"UPDATE PrizeEvents SET Status='Open' WHERE Id=$id",ct,("$id",id));
                if(config.Mode=="Group")await PrizeRoundsService.StartFirstGroupAsync(c,tx,id,config.GroupSize,now,ct);
                if(await authorization.IsAdminAsync(c,tx,principal,ct))await RememberProfile(c,tx,profileId,ct);
                await Exec(c,tx,"INSERT INTO PrizeOutbox VALUES($message,$id,1,'EventChanged',$id,$now,NULL)",ct,
                    ("$message",Guid.NewGuid().ToString("D")),("$id",id),("$now",now));
            }
            using(var operation=Command(c,tx,"INSERT INTO PrizeCreationOperations VALUES($actor,'CreateDraft',$key,$payload,$hash,$event,$now)",
                ("$actor",actor),("$key",idempotencyKey),("$payload",canonical),("$hash",hash),("$event",id),("$now",now)))
                await operation.ExecuteNonQueryAsync(ct);
            using(var audit=Command(c,tx,"""
                INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,NewVersion)
                VALUES($id,$event,$now,$actor,$action,$event,$operation,1)
                """,("$id",Guid.NewGuid().ToString("D")),("$event",id),("$now",now),("$actor",actor),("$operation",idempotencyKey),("$action",start?"Start":"DraftStored")))
                await audit.ExecuteNonQueryAsync(ct);
            tx.Commit();return new(id,name,profileId,snapshot,1);
        }
        catch(SqliteException e) when(e.SqliteErrorCode is 5 or 6) { throw new PrizeConflictException("PrizeBusy"); }
    }
    internal async Task<long> RenameDraftAsync(ClaimsPrincipal principal,string id,long expectedVersion,string name,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Trim().Length>100)throw new ArgumentException("Invalid name.");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var actor=await authorization.RequireEventAsync(c,tx,principal,id,true,ct);
        using var q=Command(c,tx,"""
            UPDATE PrizeEvents SET Name=$name,Version=Version+1
            WHERE Id=$id AND Status='Draft' AND Version=$expected RETURNING Version
            """,("$name",name.Trim()),("$id",id),("$expected",expectedVersion));
        var value=await q.ExecuteScalarAsync(ct);
        if(value is null)throw new PrizeConflictException("Draft revision conflict.");
        using var audit=Command(c,tx,"""
            INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion)
            VALUES($audit,$id,$now,$actor,'DraftRenamed',$id,$operation,$old,$new)
            """,("$audit",Guid.NewGuid().ToString("D")),("$id",id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),
            ("$actor",actor),("$operation",Guid.NewGuid().ToString("D")),("$old",expectedVersion),("$new",value));
        await audit.ExecuteNonQueryAsync(ct);tx.Commit();return Convert.ToInt64(value);
    }
}
