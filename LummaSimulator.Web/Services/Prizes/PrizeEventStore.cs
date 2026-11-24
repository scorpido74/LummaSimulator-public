using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services.Prizes;

internal sealed partial class PrizeStore
{
    private static async Task<object?> Scalar(SqliteConnection c,SqliteTransaction tx,string sql,CancellationToken ct,params (string,object?)[] args)
    { using var q=Command(c,tx,sql,args);return await q.ExecuteScalarAsync(ct); }
    private static async Task Exec(SqliteConnection c,SqliteTransaction tx,string sql,CancellationToken ct,params (string,object?)[] args)
    { using var q=Command(c,tx,sql,args);await q.ExecuteNonQueryAsync(ct); }
    private static string Digest(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string IntentKey(string key)
    {
        if(!Guid.TryParseExact(key,"D",out var parsed))throw new DemoRequestException("PrizeInvalidRequest");
        return parsed.ToString("D");
    }
    private static string EventName(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Trim().Length>100||value.Any(char.IsControl))
            throw new DemoRequestException("PrizeInvalidName");
        return value.Trim();
    }
    private static void BoundTarget(string id,string expectedId)
    {
        if(id!=expectedId || !Guid.TryParseExact(id,"D",out _))throw new DemoRequestException("PrizeWrongEvent");
    }
    private static async Task<PrizeManagedEvent?> ReadEvent(SqliteConnection c,SqliteTransaction tx,string id,CancellationToken ct)
    {
        using var q=Command(c,tx,"""
            SELECT e.Id,e.Name,e.ProfileId,e.ProfileSnapshotJson,e.Status,e.Version,e.CreatedAt,e.ClosedAt,e.PurgeAt,
              (EXISTS(SELECT 1 FROM PrizeParticipants p WHERE p.EventId=e.Id)
                OR EXISTS(SELECT 1 FROM PrizeGroups g WHERE g.EventId=e.Id)
                OR EXISTS(SELECT 1 FROM PrizeDraws d WHERE d.EventId=e.Id)
                OR EXISTS(SELECT 1 FROM PrizeTypes t WHERE t.EventId=e.Id AND (t.Reserved>0 OR t.Issued>0)))
            FROM PrizeEvents e WHERE e.Id=$id
            """,("$id",id));
        using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
        var config=JsonSerializer.Deserialize<PrizeConfiguration>(r.GetString(3))??throw new InvalidOperationException("Invalid stored event.");
        return new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),config,r.GetString(4),r.GetInt64(5),
            r.GetInt64(6),r.IsDBNull(7)?null:r.GetInt64(7),r.IsDBNull(8)?null:r.GetInt64(8),
            r.GetInt64(9)!=0 || r.GetString(4) is not ("Draft" or "Open"));
    }
    internal async Task<IReadOnlyList<PrizeProfile>> ManagementProfilesAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await authorization.RequireActorAsync(c,tx,principal,ct);
        using var q=Command(c,tx,"SELECT Id,Name,ConfigJson,Version FROM PrizeProfiles ORDER BY BuiltIn DESC,CreatedAt,Id");
        using var r=await q.ExecuteReaderAsync(ct);var profiles=new List<PrizeProfile>();
        while(await r.ReadAsync(ct))profiles.Add(new(r.GetString(0),r.GetString(1),
            JsonSerializer.Deserialize<PrizeConfiguration>(r.GetString(2))??throw new InvalidOperationException(),r.GetInt64(3)));
        return profiles;
    }
    internal async Task<PrizeProfile> ManagementDefaultProfileAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await authorization.RequireActorAsync(c,tx,principal,ct);
        using var q=Command(c,tx,"""
            SELECT p.Id,p.Name,p.ConfigJson,p.Version FROM PrizeProfiles p
            WHERE p.Id=COALESCE((SELECT LastProfileId FROM PrizeInstallationSettings WHERE Id=1),$default)
            """,("$default",PrizeProfiles.Cybertruck));
        using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new InvalidOperationException("Missing default profile.");
        return new(r.GetString(0),r.GetString(1),
            JsonSerializer.Deserialize<PrizeConfiguration>(r.GetString(2))??throw new InvalidOperationException(),r.GetInt64(3));
    }
    internal async Task<PrizeEventPage> ManagementListAsync(ClaimsPrincipal principal,int page,CancellationToken ct)
    {
        if(page is <0 or >1000000)throw new DemoRequestException("PrizeInvalidRequest");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await authorization.RequireActorAsync(c,tx,principal,ct);
        using var q=Command(c,tx,"SELECT Id FROM PrizeEvents ORDER BY CreatedAt DESC,Id");
        var ids=new List<string>();using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))ids.Add(r.GetString(0));
        var events=new List<PrizeManagedEvent>();
        foreach(var id in ids)if(await authorization.CanEventAsync(c,tx,principal,id,ct))events.Add((await ReadEvent(c,tx,id,ct))!);
        return new(events.Skip(page*25).Take(25).ToList(),page,events.Count>(page+1)*25);
    }
    internal async Task<PrizeManagedEvent?> ManagementGetAsync(ClaimsPrincipal principal,string id,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await authorization.RequireEventAsync(c,tx,principal,id,false,ct);
        return await ReadEvent(c,tx,id,ct);
    }
    private static async Task<string?> Prior(SqliteConnection c,SqliteTransaction tx,string id,string actor,string kind,
        string key,string payload,CancellationToken ct)
    {
        using var q=Command(c,tx,"""
            SELECT RequestHash,ResultReference FROM PrizeOperations
            WHERE EventId=$event AND ActorScope=$actor AND OperationKind=$kind AND IdempotencyKey=$key
            """,("$event",id),("$actor",actor),("$kind",kind),("$key",key));
        using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
        if(r.GetString(0)!=Digest(payload))throw new PrizeConflictException("PrizeIntentChanged");
        return r.GetString(1);
    }
    private static async Task Record(SqliteConnection c,SqliteTransaction tx,string id,string actor,string kind,string key,
        string payload,string result,long oldVersion,long revision,long now,CancellationToken ct)
    {
        await Exec(c,tx,"INSERT INTO PrizeOperations VALUES($row,$event,$actor,$kind,$key,$hash,$result,$now)",ct,
            ("$row",Guid.NewGuid().ToString("D")),("$event",id),("$actor",actor),("$kind",kind),("$key",key),
            ("$hash",Digest(payload)),("$result",result),("$now",now));
        await Exec(c,tx,"""
            INSERT INTO PrizeAudit VALUES($row,$event,$now,$actor,$kind,$event,$key,$old,$new);
            INSERT INTO PrizeOutbox VALUES($message,$event,$new,'EventChanged',$event,$now,NULL)
            """,ct,("$row",Guid.NewGuid().ToString("D")),("$event",id),("$now",now),("$actor",actor),("$kind",kind),
            ("$key",key),("$old",oldVersion),("$new",revision),("$message",Guid.NewGuid().ToString("D")));
    }
    private static async Task ConfigurePrice(SqliteConnection c,SqliteTransaction tx,string id,PrizeConfiguration config,CancellationToken ct)
    {
        var type=await Scalar(c,tx,"SELECT FixedPrizeTypeId FROM PrizeEvents WHERE Id=$id",ct,("$id",id)) as string;
        if(type is null)
        {
            // Only the prepared price configuration, never a reservation, award or inventory movement.
            if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PrizeTypes WHERE EventId=$id",ct,("$id",id)))!=0)
                throw new PrizeConflictException("PrizeSettingsLocked");
            type=Guid.NewGuid().ToString("D");
            await Exec(c,tx,"INSERT INTO PrizeTypes VALUES($type,$event,$name,$stock,0,0,1,1)",ct,
                ("$type",type),("$event",id),("$name",config.PrizeName),("$stock",config.Stock));
            await Exec(c,tx,"UPDATE PrizeEvents SET FixedPrizeTypeId=$type WHERE Id=$id",ct,("$type",type),("$id",id));
        }
        else
        {
            using var q=Command(c,tx,"""
                UPDATE PrizeTypes SET Name=$name,Total=$stock,Version=Version+1
                WHERE Id=$type AND EventId=$event AND Reserved=0 AND Issued=0
                """,("$name",config.PrizeName),("$stock",config.Stock),("$type",type),("$event",id));
            if(await q.ExecuteNonQueryAsync(ct)!=1)throw new PrizeConflictException("PrizeSettingsLocked");
        }
    }
    private static Task RememberProfile(SqliteConnection c,SqliteTransaction tx,string? profile,CancellationToken ct) =>
        Exec(c,tx,"UPDATE PrizeInstallationSettings SET LastProfileId=$profile,Version=Version+1 WHERE Id=1",ct,("$profile",profile));

    internal async Task<PrizeManagedEvent> ManagementChangeAsync(ClaimsPrincipal principal,string id,string expectedId,long version,
        string key,string kind,bool confirmed,string? name,PrizeConfiguration? configuration,CancellationToken ct)
    {
        BoundTarget(id,expectedId);key=IntentKey(key);
        if(!confirmed)throw new DemoRequestException("PrizeConfirmRequired");
        if(kind is not ("Start" or "Close" or "Reopen" or "Rename" or "Configure"))throw new DemoRequestException("PrizeInvalidRequest");
        if(name is not null)name=EventName(name);
        var payload=JsonSerializer.Serialize(new{Id=id,Version=version,Kind=kind,Name=name,Configuration=configuration});
        try
        {
            await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
            var actor=kind=="Configure"
                ?await authorization.RequireOwnerAsync(c,tx,principal,id,ct)
                :await authorization.RequireEventAsync(c,tx,principal,id,kind=="Rename",ct);
            var previous=await Prior(c,tx,id,actor,kind,key,payload,ct);
            var current=await ReadEvent(c,tx,id,ct)??throw new DemoRequestException("PrizeNotFound");
            if(previous is not null)return current;
            var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
            if(current.Version!=version)throw new PrizeConflictException("PrizeEventChanged");
            if(current.Status=="Purging" || current.PurgeAt is { } purge && purge<=now)
                throw new PrizeConflictException("PrizeEventExpired");
            var config=current.Configuration;var status=current.Status;var eventName=current.Name;
            long? closedAt=current.ClosedAt,purgeAt=current.PurgeAt;
            switch(kind)
            {
                case "Start":
                    if(status!="Draft")throw new PrizeConflictException("PrizeEventChanged");
                    config=PrizeEventService.Validate(config);
                    await ConfigurePrice(c,tx,id,config,ct);
                    if(await authorization.IsAdminAsync(c,tx,principal,ct))await RememberProfile(c,tx,current.ProfileId,ct);status="Open";break;
                case "Close":
                    if(status!="Open")throw new PrizeConflictException("PrizeEventChanged");
                    status="Closed";closedAt=now;
                    purgeAt ??=checked(now+(long)config.RetentionDays*86400000);
                    break;
                case "Reopen":
                    if(status!="Closed" || purgeAt is null)throw new PrizeConflictException("PrizeEventChanged");
                    status="Open";break;
                case "Rename":eventName=name??throw new DemoRequestException("PrizeInvalidName");break;
                case "Configure":
                    if(current.ConfigurationLocked)throw new PrizeConflictException("PrizeSettingsLocked");
                    config=configuration??throw new DemoRequestException("PrizeInvalidSettings");
                    if(status=="Open")await ConfigurePrice(c,tx,id,config,ct);
                    break;
            }
            using var update=ConfigurationUpdate(c,tx,id,version,eventName,config,status,closedAt,purgeAt);
            if(await update.ExecuteNonQueryAsync(ct)!=1)throw new PrizeConflictException("PrizeEventChanged");
            if(kind=="Start"&&config.Mode=="Group")await PrizeRoundsService.StartFirstGroupAsync(c,tx,id,config.GroupSize,now,ct);
            await Record(c,tx,id,actor,kind,key,payload,id,version,version+1,now,ct);
            var result=(await ReadEvent(c,tx,id,ct))!;tx.Commit();return result;
        }
        catch(SqliteException e) when(e.SqliteErrorCode is 5 or 6) {throw new PrizeConflictException("PrizeBusy");}
    }
    private static SqliteCommand ConfigurationUpdate(SqliteConnection c,SqliteTransaction tx,string id,long version,string name,
        PrizeConfiguration config,string status,long? closedAt,long? purgeAt) => Command(c,tx,"""
        UPDATE PrizeEvents SET Name=$name,ProfileSnapshotJson=$snapshot,PrizeEnabled=$enabled,Visibility=$visibility,Mode=$mode,
          GroupSize=$size,WinnersPerGroup=$winners,AssignmentMode=$assignment,NextGroupMode=$next,DrawMethod=$method,
          RevealMode=$reveal,AnimationSeconds=$animation,StartCodeRequired=$codes,PrizeSelection=$selection,
          ChanceNumerator=$numerator,ChanceDenominator=$denominator,MaxInstantWins=$max,ReactionSeconds=$reaction,
          RetentionDays=$retention,PostCloseClaimSeconds=$postclose,ClaimFieldSchemaJson=$fields,
          Status=$status,ClosedAt=$closed,PurgeAt=$purge,Version=Version+1 WHERE Id=$id AND Version=$version
        """,("$name",name),("$snapshot",JsonSerializer.Serialize(config)),("$enabled",config.PrizeEnabled),("$visibility",config.Visibility),("$mode",config.Mode),
        ("$size",config.GroupSize),("$winners",config.WinnersPerGroup),("$assignment",config.AssignmentMode),
        ("$next",config.NextGroupMode),("$method",config.DrawMethod),("$reveal",config.RevealMode),("$animation",config.AnimationSeconds),
        ("$codes",config.StartCodeRequired),("$selection",config.PrizeSelection),("$numerator",config.ChanceNumerator),
        ("$denominator",config.ChanceDenominator),("$max",config.MaxInstantWins),("$reaction",config.ReactionSeconds),
        ("$retention",config.RetentionDays),("$postclose",config.PostCloseClaimSeconds),("$fields",config.ClaimFieldSchemaJson),
        ("$status",status),("$closed",closedAt),("$purge",purgeAt),("$id",id),("$version",version));

    internal async Task<string> ManagementSaveProfileAsync(ClaimsPrincipal principal,string id,string expectedId,long version,
        string key,string name,CancellationToken ct)
    {
        BoundTarget(id,expectedId);key=IntentKey(key);name=EventName(name);
        var payload=JsonSerializer.Serialize(new{Id=id,Version=version,Name=name});
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var actor=await authorization.RequireEventAsync(c,tx,principal,id,true,ct);
        await authorization.RequireAdminAsync(c,tx,principal,ct);
        var previous=await Prior(c,tx,id,actor,"SaveProfile",key,payload,ct);
        if(previous is not null)return previous;
        var current=await ReadEvent(c,tx,id,ct)??throw new DemoRequestException("PrizeNotFound");
        var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        if(current.Version!=version)throw new PrizeConflictException("PrizeEventChanged");
        if(current.Status=="Purging" || current.PurgeAt is {} deadline && deadline<=now)
            throw new PrizeConflictException("PrizeEventExpired");
        var config=PrizeEventService.Validate(current.Configuration);var profileId=Guid.NewGuid().ToString("D");
        await Exec(c,tx,"INSERT INTO PrizeProfiles VALUES($id,$name,0,$json,1,1,$now,$now)",ct,
            ("$id",profileId),("$name",name),("$json",JsonSerializer.Serialize(config)),("$now",now));
        // Profile creation is not an event start and never rewrites the event's snapshot or last-used preference.
        using var revise=Command(c,tx,"UPDATE PrizeEvents SET Version=Version+1 WHERE Id=$id AND Version=$version",
            ("$id",id),("$version",version));
        if(await revise.ExecuteNonQueryAsync(ct)!=1)throw new PrizeConflictException("PrizeEventChanged");
        await Record(c,tx,id,actor,"SaveProfile",key,payload,profileId,version,version+1,now,ct);
        tx.Commit();return profileId;
    }
}
