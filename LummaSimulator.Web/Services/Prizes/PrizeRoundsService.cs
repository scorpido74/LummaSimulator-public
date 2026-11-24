using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using LummaSimulator.Web.Models;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services.Prizes;

public sealed class PrizeRoundsService(DemoStore store,PresenterService presenters,TimeProvider clock)
{
    private static readonly string[] FirstNames=["Bliksem","Koper","Maan","Ster","Koraal","Saffier","Zilver","Komet","Nova","Bos","Rivier","Vonk","Donder","Zon","Storm","Parel","Pixel","Goud","Amber","Kobalt","Limoen","Krijt","Fluister","Regen","Nevel","Noorder","Turbo","Wolk","Prisma","Echo","Vlam","Pool"];
    private static readonly string[] Animals=["otter","vos","das","uil","lynx","meeuw","marter","bever","wolf","kraanvogel","egel","valk","haas","mol","zwaan","hermelijn","roerdomp","dolfijn","hagedis","specht","ree","panda","kievit","zeehond","bunzing","raaf","vlinder","eekhoorn","papegaai","spreeuw","pinguin","geit"];
    private readonly SecurityAuditService audit=new(store,clock);

    public async Task<bool> HasPrizeDrawAsync(string? token, CancellationToken ct)
    {
        var account = await audit.OwnerAsync(token, ct);
        if (account is null) return false;
        return Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterDemoContexts x JOIN PrizeEvents e ON e.Id=x.EventId WHERE x.DemoId=$demo AND x.Kind='prize' AND e.PrizeEnabled=1 AND e.Mode='Group' AND (e.PurgeAt IS NULL OR e.PurgeAt>$now)", ct,
            ("$demo", account.Id), ("$now", clock.GetUtcNow().ToUnixTimeMilliseconds()))) > 0;
    }

    internal static string FixedJoinPath(string eventId)
    {
        if(!EventAccess.Valid("prize",eventId))throw new ArgumentException("Invalid prize event id.",nameof(eventId));
        return LummaSimulator.Shared.ParticipantLink.EventPath(eventId);
    }

    internal static async Task StartFirstGroupAsync(SqliteConnection connection,SqliteTransaction transaction,string eventId,int capacity,long now,CancellationToken token)
    {
        using var query=EventAccess.Command(connection,transaction,"SELECT COUNT(*) FROM PrizeGroups WHERE EventId=$event",("$event",eventId));
        if(Convert.ToInt32(await query.ExecuteScalarAsync(token))!=0)return;
        var id=Guid.NewGuid().ToString("D");
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeGroups(Id,EventId,Number,Status,Capacity,Occupied,FrozenAt,Version) VALUES($id,$event,1,'Open',$capacity,0,NULL,1)",token,
            ("$id",id),("$event",eventId),("$capacity",capacity));
        await EventAccess.Execute(connection,transaction,"INSERT OR IGNORE INTO PrizeRoundSettings(EventId,NextCapacity,Version) VALUES($event,NULL,1)",token,("$event",eventId));
    }

    public async Task<PrizeRoundSnapshot?> PublicJoinInfoAsync(string eventId,CancellationToken token=default)
    {
        if(!EventAccess.Valid("prize",eventId))return null;
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:true);
        using var query=EventAccess.Command(connection,transaction,"""
            SELECT e.Name,e.Status,e.Mode,e.GroupSize,g.Id,g.Number,g.Capacity,g.Occupied,
              (SELECT NextCapacity FROM PrizeRoundSettings WHERE EventId=e.Id)
            FROM PrizeEvents e LEFT JOIN PrizeGroups g ON g.EventId=e.Id AND g.Status='Open'
            WHERE e.Id=$event
            """,("$event",eventId));
        using var reader=await query.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)||reader.GetString(1)!="Open"||reader.GetString(2)!="Group")return null;
        var eventName=reader.GetString(0);var defaultCapacity=reader.GetInt32(3);var groupId=reader.IsDBNull(4)?null:reader.GetString(4);
        var number=groupId is null?1:reader.GetInt32(5);var capacity=groupId is null?defaultCapacity:reader.GetInt32(6);
        var occupied=groupId is null?0:reader.GetInt32(7);var nextCapacity=reader.IsDBNull(8)?(int?)null:reader.GetInt32(8);
        return new(eventId,eventName,groupId??"",number,capacity,occupied,occupied>=capacity?"Full":"Open",null,false,null,0,[],nextCapacity);
    }

    public async Task<PrizeRoundSnapshot?> JoinAuthenticatedAsync(string? secret,CancellationToken token=default)
    {
        var account=await audit.OwnerAsync(secret,token);if(account is null)return null;
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:false);
        var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        if(Convert.ToInt64(await EventAccess.Scalar(connection,transaction,"""
            SELECT COUNT(*) FROM Demos d JOIN IdentitySessions s ON s.UserId=d.Id AND s.BindingHash=d.SessionHash
            WHERE d.Id=$account AND d.ClosedAt IS NULL AND s.RevokedAt IS NULL AND s.ExpiresAt>$now AND s.LastActivity>$cutoff
              AND d.LastSeen>$cutoff AND d.LastAuthenticatedAt>$authCutoff AND d.LastAuthenticatedAt<=$now
            """,token,("$account",account.Id),("$now",now),("$cutoff",now-29*60000),("$authCutoff",now-30*60000)))!=1)return null;
        var eventId=await EventAccess.Scalar(connection,transaction,"SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$account AND Kind='prize'",token,("$account",account.Id)) as string;
        if(eventId is null)return null;
        var existing=await EventAccess.Scalar(connection,transaction,"SELECT GroupId FROM PrizeParticipants WHERE EventId=$event AND AccountId=$account",token,("$event",eventId),("$account",account.Id)) as string;
        if(existing is not null)
        {
            var snapshot=await ReadParticipant(connection,transaction,eventId,account.Id,token);transaction.Commit();return snapshot;
        }
        using var policy=EventAccess.Command(connection,transaction,"SELECT Name,Status,Mode,GroupSize,NextParticipantNumber FROM PrizeEvents WHERE Id=$event",("$event",eventId));
        string eventName;int groupSize;long participantNumber;
        using(var reader=await policy.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token)||reader.GetString(1)!="Open"||reader.GetString(2)!="Group")return null;
            eventName=reader.GetString(0);groupSize=reader.GetInt32(3);participantNumber=reader.GetInt64(4);
        }
        string? groupId;int roundNumber,capacity,occupied;
        using(var open=EventAccess.Command(connection,transaction,"""
            SELECT Id,Number,Capacity,Occupied FROM PrizeGroups WHERE EventId=$event AND Status='Open'
            """,("$event",eventId)))
        using(var reader=await open.ExecuteReaderAsync(token))
        {
            if(await reader.ReadAsync(token)){groupId=reader.GetString(0);roundNumber=reader.GetInt32(1);capacity=reader.GetInt32(2);occupied=reader.GetInt32(3);}
            else
            {
                groupId=null;roundNumber=1;capacity=groupSize;occupied=0;
                var configured=await EventAccess.Scalar(connection,transaction,"SELECT NextCapacity FROM PrizeRoundSettings WHERE EventId=$event",token,("$event",eventId));
                if(configured is not null and not DBNull)capacity=Convert.ToInt32(configured);
                roundNumber=Convert.ToInt32(await EventAccess.Scalar(connection,transaction,"SELECT COALESCE(MAX(Number),0)+1 FROM PrizeGroups WHERE EventId=$event",token,("$event",eventId)));
            }
        }
        if(groupId is null)
        {
            groupId=Guid.NewGuid().ToString("D");
            await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeGroups(Id,EventId,Number,Status,Capacity,Occupied,FrozenAt,Version) VALUES($id,$event,$number,'Open',$capacity,0,NULL,1)",token,
                ("$id",groupId),("$event",eventId),("$number",roundNumber),("$capacity",capacity));
            await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeRoundSettings(EventId,NextCapacity,Version) VALUES($event,NULL,1) ON CONFLICT(EventId) DO UPDATE SET NextCapacity=NULL,Version=PrizeRoundSettings.Version+1",token,("$event",eventId));
        }
        if(occupied>=capacity){transaction.Commit();return new(eventId,eventName,groupId,roundNumber,capacity,occupied,"Full",null,false,null,0,[],null);}
        if(await EventAccess.Execute(connection,transaction,"UPDATE PrizeGroups SET Occupied=Occupied+1,Version=Version+1 WHERE EventId=$event AND Id=$group AND Status='Open' AND Occupied<Capacity",token,("$event",eventId),("$group",groupId))!=1)
        {transaction.Commit();return new(eventId,eventName,groupId,roundNumber,capacity,occupied,"Full",null,false,null,0,[],null);}
        var alias=account.Name.Length<=40?account.Name:await UniqueAlias(connection,transaction,eventId,groupId,token);
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeParticipants(Id,EventId,AccountId,GroupId,Number,State,JoinedAt,CancelledAt,AuthenticationAt) VALUES($id,$event,$account,$group,$number,'Active',$now,NULL,$authenticated)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$account",account.Id),("$group",groupId),("$number",participantNumber),("$now",now),("$authenticated",account.LastAuthenticatedAt??now));
        var participantId=(string)(await EventAccess.Scalar(connection,transaction,"SELECT Id FROM PrizeParticipants WHERE EventId=$event AND AccountId=$account",token,("$event",eventId),("$account",account.Id)))!;
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeRoundParticipants(EventId,ParticipantId,GroupId,Alias) VALUES($event,$participant,$group,$alias)",token,
            ("$event",eventId),("$participant",participantId),("$group",groupId),("$alias",alias));
        var revision=Convert.ToInt64(await EventAccess.Scalar(connection,transaction,"UPDATE PrizeEvents SET NextParticipantNumber=NextParticipantNumber+1,Version=Version+1 WHERE Id=$event AND Status='Open' RETURNING Version",token,("$event",eventId)));
        var nowAgain=clock.GetUtcNow().ToUnixTimeMilliseconds();var operation=Guid.NewGuid().ToString("D");
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) VALUES($id,$event,$now,$actor,'RoundJoined',$group,$operation,$previous,$next)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$now",nowAgain),("$actor",account.Id),("$group",groupId),("$operation",operation),("$previous",revision-1),("$next",revision));
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeOutbox(Id,EventId,Revision,Kind,SafeReference,DueAt,PublishedAt) VALUES($id,$event,$revision,'RoundJoined',$group,$now,NULL)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$revision",revision),("$group",groupId),("$now",nowAgain));
        var result=await ReadParticipant(connection,transaction,eventId,account.Id,token);transaction.Commit();return result;
    }

    public async Task<PrizeRoundSnapshot?> ForDemoAsync(string? secret,CancellationToken token=default)
    {
        var account=await audit.OwnerAsync(secret,token);if(account is null)return null;
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:true);
        var eventId=await EventAccess.Scalar(connection,transaction,"SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$account AND Kind='prize'",token,("$account",account.Id)) as string;
        if(eventId is null)return null;
        var snapshot=await ReadParticipant(connection,transaction,eventId,account.Id,token);return snapshot;
    }

    public async Task<PrizeRoundBoard> BoardAsync(ClaimsPrincipal principal,string eventId,CancellationToken token=default)
    {
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:true);
        await EventAccess.Require(connection,transaction,presenters,principal,"prize",eventId,false,token);
        using var eventQuery=EventAccess.Command(connection,transaction,"""
            SELECT e.Name,e.GroupSize,(SELECT NextCapacity FROM PrizeRoundSettings WHERE EventId=e.Id)
            FROM PrizeEvents e WHERE e.Id=$event
            """,("$event",eventId));
        using var eventReader=await eventQuery.ExecuteReaderAsync(token);
        if(!await eventReader.ReadAsync(token))throw new DemoRequestException("PrizeNotFound");
        var eventName=eventReader.GetString(0);var defaultCapacity=eventReader.GetInt32(1);var nextCapacity=eventReader.IsDBNull(2)?(int?)null:eventReader.GetInt32(2);
        var groups=new List<(string Id,int Number,int Capacity,int Occupied,string Status)>();
        using(var query=EventAccess.Command(connection,transaction,"SELECT Id,Number,Capacity,Occupied,Status FROM PrizeGroups WHERE EventId=$event ORDER BY Number DESC LIMIT 25",("$event",eventId)))
        using(var reader=await query.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))groups.Add((reader.GetString(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetString(4)));
        var snapshots=new List<PrizeRoundSnapshot>();foreach(var group in groups)snapshots.Add(await ReadGroup(connection,transaction,eventId,eventName,group.Id,group.Number,group.Capacity,group.Occupied,group.Status,null,token));
        if(groups.Count==0)snapshots.Add(new(eventId,eventName,"",1,nextCapacity??defaultCapacity,0,"NotStarted",null,false,null,0,[],nextCapacity));
        return new(eventId,eventName,defaultCapacity,nextCapacity,snapshots);
    }

    public async Task SetNextCapacityAsync(ClaimsPrincipal principal,string eventId,string expectedOpenGroupId,int capacity,CancellationToken token=default)
    {
        if(capacity is <1 or >200)throw new DemoRequestException("PrizeInvalidSettings");
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:false);
        var actor=await EventAccess.Require(connection,transaction,presenters,principal,"prize",eventId,false,token);
        if(Convert.ToInt64(await EventAccess.Scalar(connection,transaction,"SELECT COUNT(*) FROM PrizeEvents WHERE Id=$event AND Status='Open'",token,("$event",eventId)))!=1)
            throw new DemoRequestException("PrizeEventChanged");
        var open=await EventAccess.Scalar(connection,transaction,"SELECT Id FROM PrizeGroups WHERE EventId=$event AND Status='Open'",token,("$event",eventId)) as string;
        if(open is not null&&open!=expectedOpenGroupId||open is null&&expectedOpenGroupId.Length!=0)throw new PrizeConflictException("PrizeEventChanged");
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeRoundSettings(EventId,NextCapacity,Version) VALUES($event,$capacity,1) ON CONFLICT(EventId) DO UPDATE SET NextCapacity=excluded.NextCapacity,Version=PrizeRoundSettings.Version+1",token,
            ("$event",eventId),("$capacity",capacity));
        var previous=Convert.ToInt64(await EventAccess.Scalar(connection,transaction,"SELECT Version FROM PrizeEvents WHERE Id=$event",token,("$event",eventId)));
        await EventAccess.Execute(connection,transaction,"UPDATE PrizeEvents SET Version=Version+1 WHERE Id=$event",token,("$event",eventId));
        var revision=previous+1;var operation=Guid.NewGuid().ToString("D");var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) VALUES($id,$event,$now,$actor,'NextRoundCapacitySet',$group,$operation,$previous,$next)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$now",now),("$actor",actor),("$group",open),("$operation",operation),("$previous",previous),("$next",revision));
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeOutbox(Id,EventId,Revision,Kind,SafeReference,DueAt,PublishedAt) VALUES($id,$event,$revision,'RoundCapacityChanged',$group,$now,NULL)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$revision",revision),("$group",open??eventId),("$now",now));
        transaction.Commit();
    }

    public async Task<PrizeRoundSnapshot> CloseAsync(ClaimsPrincipal principal,string eventId,string expectedGroupId,CancellationToken token=default)
    {
        if(!EventAccess.Valid("prize",eventId)||!Guid.TryParseExact(expectedGroupId,"D",out _))throw new DemoRequestException("PrizeRoundChanged");
        await using var connection=await store.OpenAsync(token);using var transaction=connection.BeginTransaction(deferred:false);
        var actor=await EventAccess.Require(connection,transaction,presenters,principal,"prize",eventId,false,token);
        using(var prior=EventAccess.Command(connection,transaction,"""
            SELECT g.Number,g.Capacity,g.Occupied,g.Status,e.Name FROM PrizeGroups g JOIN PrizeRoundResults r ON r.EventId=g.EventId AND r.GroupId=g.Id
              JOIN PrizeEvents e ON e.Id=g.EventId WHERE g.EventId=$event AND g.Id=$group
            """,("$event",eventId),("$group",expectedGroupId)))
        using(var reader=await prior.ExecuteReaderAsync(token))
        {
            if(await reader.ReadAsync(token))return await ReadGroup(connection,transaction,eventId,reader.GetString(4),expectedGroupId,reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3),null,token);
        }
        using var policy=EventAccess.Command(connection,transaction,"""
            SELECT e.Name,e.Status,e.Mode,e.PrizeEnabled,e.GroupSize,e.WinnersPerGroup,e.PrizeSelection,e.FixedPrizeTypeId,e.Version,
              g.Number,g.Capacity,g.Occupied,g.Version,s.NextCapacity
            FROM PrizeEvents e JOIN PrizeGroups g ON g.EventId=e.Id AND g.Id=$group AND g.Status='Open'
            LEFT JOIN PrizeRoundSettings s ON s.EventId=e.Id WHERE e.Id=$event
            """,("$event",eventId),("$group",expectedGroupId));
        string eventName,mode,selection,groupId;int defaultCapacity,winnersPerGroup,number,capacity,occupied;bool prizesEnabled;long eventVersion,groupVersion;string? fixedPrizeType;int? nextCapacity;
        using(var reader=await policy.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token))throw new PrizeConflictException("PrizeRoundChanged");
            eventName=reader.GetString(0);if(reader.GetString(1)!="Open")throw new PrizeConflictException("PrizeEventChanged");
            mode=reader.GetString(2);prizesEnabled=reader.GetBoolean(3);defaultCapacity=reader.GetInt32(4);winnersPerGroup=reader.GetInt32(5);selection=reader.GetString(6);fixedPrizeType=reader.IsDBNull(7)?null:reader.GetString(7);
            eventVersion=reader.GetInt64(8);number=reader.GetInt32(9);capacity=reader.GetInt32(10);occupied=reader.GetInt32(11);groupVersion=reader.GetInt64(12);groupId=expectedGroupId;
            nextCapacity=reader.IsDBNull(13)?null:reader.GetInt32(13);
        }
        var entries=await RoundEntries(connection,transaction,eventId,groupId,token);
        var participants=entries.Where(p=>p.Eligible).Select(p=>(Id:p.Id,Alias:p.Alias)).ToList();
        var selected=new List<(string ParticipantId,string Alias,string PrizeTypeId,string PrizeName)>();
        var available=await AvailableTypes(connection,transaction,eventId,selection,fixedPrizeType,token);
        if(prizesEnabled&&mode=="Group"&&participants.Count>0)
        {
            if(available.Count>0)
            {
                if(participants.Count<winnersPerGroup)throw new PrizeConflictException("PrizeRoundInsufficientParticipants");
                var target=(int)Math.Min(winnersPerGroup,available.Where(x=>x.Remaining is null or >0).Sum(x=>x.Remaining is null?(long)int.MaxValue:Math.Min(int.MaxValue,x.Remaining.Value)));
                var shuffled=participants.ToArray();
                for(var i=0;i<target;i++)
                {
                    var selectedIndex=RandomNumberGenerator.GetInt32(i,shuffled.Length);(shuffled[i],shuffled[selectedIndex])=(shuffled[selectedIndex],shuffled[i]);
                    var eligibleTypes=available.Where(x=>x.Remaining is null or >0).ToArray();if(eligibleTypes.Length==0)break;
                    var type=eligibleTypes[RandomNumberGenerator.GetInt32(eligibleTypes.Length)];
                    selected.Add((shuffled[i].Id,shuffled[i].Alias,type.Id,type.Name));
                    var typeIndex=available.FindIndex(x=>x.Id==type.Id);if(available[typeIndex].Remaining is {} remaining)available[typeIndex]=available[typeIndex] with{Remaining=remaining-1};
                }
            }
        }
        foreach(var winner in selected){
            await EventAccess.Execute(connection,transaction,"UPDATE PrizeRoundParticipants SET IsWinner=1,PrizeTypeId=$type,PrizeName=$name WHERE EventId=$event AND ParticipantId=$participant AND GroupId=$group AND IsWinner=0",token,
                ("$type",winner.PrizeTypeId),("$name",winner.PrizeName),("$event",eventId),("$participant",winner.ParticipantId),("$group",groupId));
            await EventAccess.Execute(connection,transaction,"UPDATE PrizeRoundHistory SET IsWinner=1,PrizeTypeId=$type,PrizeName=$name WHERE EventId=$event AND ParticipantId=$participant AND GroupId=$group AND IsWinner=0",token,
                ("$type",winner.PrizeTypeId),("$name",winner.PrizeName),("$event",eventId),("$participant",winner.ParticipantId),("$group",groupId));
        }
        var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        foreach(var entry in entries)await RoundFact(connection,transaction,eventId,entry.Id,entry.Eligible&&prizesEnabled&&available.Count>0?"RoundEnteredDraw":"RoundDidNotEnterDraw",actor,now,token);
        foreach(var entry in entries.Where(e=>!e.Eligible))await RoundFact(connection,transaction,eventId,entry.Id,!entry.Valid?"RoundWithdrawnAtDraw":"RoundIncompleteAtDraw",actor,now,token);
        if(prizesEnabled&&available.Count==0)await RoundFact(connection,transaction,eventId,groupId,"RoundNoPrizeAtDraw",actor,now,token);
        if(!prizesEnabled)await RoundFact(connection,transaction,eventId,groupId,"RoundPrizesDisabledAtDraw",actor,now,token);
        if(await EventAccess.Execute(connection,transaction,"UPDATE PrizeGroups SET Status='Closed',FrozenAt=$now,Version=Version+1 WHERE EventId=$event AND Id=$group AND Status='Open' AND Version=$version",token,
            ("$now",now),("$event",eventId),("$group",groupId),("$version",groupVersion))!=1)throw new PrizeConflictException("PrizeRoundChanged");
        var nextId=Guid.NewGuid().ToString("D");var nextNumber=number+1;var nextSize=nextCapacity??defaultCapacity;
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeGroups(Id,EventId,Number,Status,Capacity,Occupied,FrozenAt,Version) VALUES($id,$event,$number,'Open',$capacity,0,NULL,1)",token,
            ("$id",nextId),("$event",eventId),("$number",nextNumber),("$capacity",nextSize));
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeRoundSettings(EventId,NextCapacity,Version) VALUES($event,NULL,1) ON CONFLICT(EventId) DO UPDATE SET NextCapacity=NULL,Version=PrizeRoundSettings.Version+1",token,("$event",eventId));
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeRoundResults(EventId,GroupId,ClosedAt,ClosedBy,WinnerCount,PrizeTypeId,PrizeName,NextGroupId,NextCapacity) VALUES($event,$group,$now,$actor,$count,$type,$name,$next,$capacity)",token,
            ("$event",eventId),("$group",groupId),("$now",now),("$actor",actor),("$count",selected.Count),("$type",selected.Count==0?null:selected[0].PrizeTypeId),("$name",selected.Count==0?null:selected[0].PrizeName),("$next",nextId),("$capacity",nextSize));
        await EventAccess.Execute(connection,transaction,"UPDATE PrizeEvents SET Version=Version+1 WHERE Id=$event AND Version=$version",token,("$event",eventId),("$version",eventVersion));
        var operation=Guid.NewGuid().ToString("D");var revision=eventVersion+1;
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) VALUES($id,$event,$now,$actor,'RoundClosed',$group,$operation,$previous,$next)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$now",now),("$actor",actor),("$group",groupId),("$operation",operation),("$previous",eventVersion),("$next",revision));
        await EventAccess.Execute(connection,transaction,"INSERT INTO PrizeOutbox(Id,EventId,Revision,Kind,SafeReference,DueAt,PublishedAt) VALUES($id,$event,$revision,'RoundClosed',$group,$now,NULL)",token,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$revision",revision),("$group",groupId),("$now",now));
        var result=await ReadGroup(connection,transaction,eventId,eventName,groupId,number,capacity,occupied,"Closed",null,token);transaction.Commit();return result;
    }

    public async Task<IReadOnlyList<RoundPrizeDelivery>> DeliveriesAsync(ClaimsPrincipal principal,string eventId,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await EventAccess.Require(c,tx,presenters,principal,"prize",eventId,false,ct);
        using var q=EventAccess.Command(c,tx,"""
            SELECT p.ParticipantId,g.Number,p.Alias,p.PrizeName,
              EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=p.EventId AND a.Action='RoundPrizeDelivered' AND a.TargetId=p.ParticipantId)
            FROM (SELECT EventId,ParticipantId,GroupId,Alias,PrizeName FROM PrizeRoundParticipants WHERE IsWinner=1
              UNION ALL SELECT EventId,ParticipantId,GroupId,Alias,PrizeName FROM PrizeRoundHistory WHERE IsWinner=1) p
            JOIN PrizeGroups g ON g.EventId=p.EventId AND g.Id=p.GroupId AND g.Status='Closed'
            WHERE p.EventId=$event ORDER BY g.Number,p.Alias
            """,("$event",eventId));
        var result=new List<RoundPrizeDelivery>();using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))result.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetBoolean(4)));
        return result;
    }
    public async Task RegisterDeliveryAsync(ClaimsPrincipal principal,string eventId,string participantId,CancellationToken ct)
    {
        if(!Guid.TryParseExact(participantId,"D",out _))throw new DemoRequestException("PrizeRoundChanged");
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var actor=await EventAccess.Require(c,tx,presenters,principal,"prize",eventId,false,ct);var now=clock.GetUtcNow().ToUnixTimeMilliseconds();
        var winner=Convert.ToInt64(await EventAccess.Scalar(c,tx,"""
            SELECT COUNT(*) FROM (SELECT EventId,ParticipantId,GroupId FROM PrizeRoundParticipants WHERE IsWinner=1
              UNION ALL SELECT EventId,ParticipantId,GroupId FROM PrizeRoundHistory WHERE IsWinner=1) p
            JOIN PrizeGroups g ON g.EventId=p.EventId AND g.Id=p.GroupId AND g.Status='Closed'
            JOIN PrizeEvents e ON e.Id=p.EventId AND e.Status IN ('Open','Closed') AND (e.PurgeAt IS NULL OR e.PurgeAt>$now)
            WHERE p.EventId=$event AND p.ParticipantId=$participant
            """,ct,("$event",eventId),("$participant",participantId),("$now",now)))==1;
        if(!winner)throw new DemoRequestException("PrizeRoundChanged");
        if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND Action='RoundPrizeDelivered' AND TargetId=$participant",ct,("$event",eventId),("$participant",participantId)))==0)
            await EventAccess.Execute(c,tx,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) SELECT $id,Id,$now,$actor,'RoundPrizeDelivered',$participant,$operation,Version,Version FROM PrizeEvents WHERE Id=$event",ct,
                ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$now",now),("$actor",actor),("$participant",participantId),("$operation",Guid.NewGuid().ToString("D")));
        tx.Commit();
    }
    private async Task<PrizeRoundSnapshot?> ReadParticipant(SqliteConnection c,SqliteTransaction tx,string eventId,string accountId,CancellationToken token)
    {
        using var q=EventAccess.Command(c,tx,"""
            SELECT e.Name,g.Id,g.Number,g.Capacity,g.Occupied,g.Status,r.Alias,r.IsWinner,r.PrizeName,
              COALESCE(result.WinnerCount,0),(SELECT NextCapacity FROM PrizeRoundSettings WHERE EventId=e.Id),p.Id
            FROM PrizeEvents e JOIN PrizeParticipants p ON p.EventId=e.Id AND p.AccountId=$account
            JOIN PrizeGroups g ON g.EventId=p.EventId AND g.Id=p.GroupId
            JOIN PrizeRoundParticipants r ON r.EventId=p.EventId AND r.ParticipantId=p.Id
            LEFT JOIN PrizeRoundResults result ON result.EventId=g.EventId AND result.GroupId=g.Id
            WHERE e.Id=$event
            """,("$event",eventId),("$account",accountId));
        using var reader=await q.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;
        var eventName=reader.GetString(0);var groupId=reader.GetString(1);var number=reader.GetInt32(2);var capacity=reader.GetInt32(3);var occupied=reader.GetInt32(4);var status=reader.GetString(5);
        var alias=reader.GetString(6);var winner=reader.GetBoolean(7);var prize=reader.IsDBNull(8)?null:reader.GetString(8);var winnerCount=reader.GetInt32(9);var nextCapacity=reader.IsDBNull(10)?(int?)null:reader.GetInt32(10);
        var participant=reader.GetString(11);
        var result=await ReadGroup(c,tx,eventId,eventName,groupId,number,capacity,occupied,status,alias,token,winner,prize,winnerCount,nextCapacity);
        var drawKey=await SavedDrawStatus(c,tx,eventId,participant,winner,token);
        string message;
        if(status=="Closed"){
            var roundFact=await EventAccess.Scalar(c,tx,"SELECT Action FROM PrizeAudit WHERE EventId=$event AND TargetId=$group AND Action IN('RoundPrizesDisabledAtDraw','RoundNoPrizeAtDraw') LIMIT 1",token,("$event",eventId),("$group",groupId)) as string;
            message=roundFact=="RoundPrizesDisabledAtDraw"?"VisitorPrizesOff":roundFact=="RoundNoPrizeAtDraw"?"VisitorNoPrize":drawKey switch{"RoundRowWon"=>"VisitorPrizeWon","RoundRowNoPrize"=>"VisitorPrizeLost","RoundRowNotDrawn"=>"VisitorPrizeNotEntered",_=>"VisitorPrizeUnknown"};
        }else{
            var price=await PrizeState(c,tx,eventId,token);var entry=(await RoundEntries(c,tx,eventId,groupId,token)).SingleOrDefault(e=>e.Id==participant);
            message=!price.Enabled?"VisitorPrizesOff":!price.Available?"VisitorNoPrize":price.EventStatus!="Open"||entry?.Valid!=true?"VisitorPrizeNotEntered":entry.Completed?"VisitorPrizePending":"VisitorCompleteToEnter";
        }
        return result with{DrawResultKey=drawKey,PrizeMessageKey=message,DrawPerformed=status=="Closed"&&winnerCount>0};
    }

    private async Task<PrizeRoundSnapshot> ReadGroup(SqliteConnection c,SqliteTransaction tx,string eventId,string eventName,string groupId,
        int number,int capacity,int occupied,string status,string? accountAlias,CancellationToken token,bool isWinner=false,string? prizeName=null,int? knownWinnerCount=null,int? nextCapacity=null)
    {
        var winners=new List<PrizeRoundWinner>();
        using(var q=EventAccess.Command(c,tx,"SELECT Alias,PrizeName FROM PrizeRoundParticipants WHERE EventId=$event AND GroupId=$group AND IsWinner=1 UNION ALL SELECT Alias,PrizeName FROM PrizeRoundHistory WHERE EventId=$event AND GroupId=$group AND IsWinner=1 ORDER BY Alias",("$event",eventId),("$group",groupId)))
        using(var reader=await q.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))winners.Add(new(reader.GetString(0),reader.GetString(1)));
        if(knownWinnerCount is null)
            knownWinnerCount=Convert.ToInt32(await EventAccess.Scalar(c,tx,"SELECT COALESCE((SELECT WinnerCount FROM PrizeRoundResults WHERE EventId=$event AND GroupId=$group),0)",token,("$event",eventId),("$group",groupId)));
        return new(eventId,eventName,groupId,number,capacity,occupied,status,accountAlias,isWinner,prizeName,knownWinnerCount.Value,winners,nextCapacity);
    }

    internal static Task<int> RoundFact(SqliteConnection c,SqliteTransaction tx,string eventId,string target,string action,string actor,long now,CancellationToken ct) =>
        EventAccess.Execute(c,tx,"INSERT INTO PrizeAudit(Id,EventId,At,ActorId,Action,TargetId,OperationId,PreviousVersion,NewVersion) SELECT $id,Id,$now,$actor,$action,$target,$operation,Version,Version FROM PrizeEvents WHERE Id=$event AND NOT EXISTS(SELECT 1 FROM PrizeAudit WHERE EventId=$event AND TargetId=$target AND Action=$action)",ct,
            ("$id",Guid.NewGuid().ToString("D")),("$event",eventId),("$now",now),("$actor",actor),("$action",action),("$target",target),("$operation",Guid.NewGuid().ToString("D")));

    private static async Task<List<RoundEntry>> RoundEntries(SqliteConnection c,SqliteTransaction tx,string eventId,string groupId,CancellationToken ct)
    {
        var entries=new List<RoundEntry>();
        using var q=EventAccess.Command(c,tx,"""
            SELECT r.ParticipantId,r.Alias,
              EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=r.EventId AND a.TargetId=r.ParticipantId AND a.Action='RoundDemoCompleted')
                OR EXISTS(SELECT 1 FROM PrizeParticipants p JOIN VisitorExercises v ON v.DemoId=p.AccountId WHERE p.EventId=r.EventId AND p.Id=r.ParticipantId AND v.CompletedAt IS NOT NULL),
              NOT EXISTS(SELECT 1 FROM PrizeParticipants p WHERE p.EventId=r.EventId AND p.Id=r.ParticipantId AND p.State!='Active')
                AND NOT EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=r.EventId AND a.TargetId=r.ParticipantId AND a.Action='RoundEntryWithdrawn')
            FROM (SELECT EventId,ParticipantId,GroupId,Alias FROM PrizeRoundParticipants
              UNION ALL SELECT EventId,ParticipantId,GroupId,Alias FROM PrizeRoundHistory) r
            WHERE r.EventId=$event AND r.GroupId=$group ORDER BY r.ParticipantId
            """,("$event",eventId),("$group",groupId));
        using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))entries.Add(new(r.GetString(0),r.GetString(1),r.GetBoolean(2),r.GetBoolean(3)));
        return entries;
    }
    private sealed record RoundEntry(string Id,string Alias,bool Completed,bool Valid){public bool Eligible=>Completed&&Valid;}
    private static async Task<string> SavedDrawStatus(SqliteConnection c,SqliteTransaction tx,string eventId,string participant,bool winner,CancellationToken ct)
    {
        if(winner)return "RoundRowWon";
        return Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND TargetId=$participant AND Action='RoundEnteredDraw'",ct,("$event",eventId),("$participant",participant)))>0?"RoundRowNoPrize":Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND TargetId=$participant AND Action='RoundDidNotEnterDraw'",ct,("$event",eventId),("$participant",participant)))>0?"RoundRowNotDrawn":"RoundRowUnknown";
    }

    private static async Task<List<(string Id,string Name,long? Remaining)>> AvailableTypes(SqliteConnection c,SqliteTransaction tx,string eventId,string selection,string? fixedPrizeType,CancellationToken token)
    {
        var types=new List<(string Id,string Name,long? Total,long Reserved,long Issued)>();
        var typeSql=selection=="Fixed"
            ?"SELECT Id,Name,Total,Reserved,Issued FROM PrizeTypes WHERE EventId=$event AND Id=$fixed AND Active=1"
            :"SELECT Id,Name,Total,Reserved,Issued FROM PrizeTypes WHERE EventId=$event AND Active=1 ORDER BY Id";
        using(var query=EventAccess.Command(c,tx,typeSql,("$event",eventId),("$fixed",fixedPrizeType)))
        using(var reader=await query.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))types.Add((reader.GetString(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4)));
        var available=new List<(string Id,string Name,long? Remaining)>();
        foreach(var type in types)
        {
            var previouslyWon=Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT (SELECT COUNT(*) FROM PrizeRoundParticipants WHERE EventId=$event AND PrizeTypeId=$type AND IsWinner=1)+(SELECT COUNT(*) FROM PrizeRoundHistory WHERE EventId=$event AND PrizeTypeId=$type AND IsWinner=1)",token,("$event",eventId),("$type",type.Id)));
            long? remaining=type.Total is null?null:Math.Max(0,type.Total.Value-type.Reserved-type.Issued-previouslyWon);
            if(remaining is null or >0)available.Add((type.Id,type.Name,remaining));
        }
        return available;
    }
    private async Task<RoundPrizeState> PrizeState(SqliteConnection c,SqliteTransaction tx,string eventId,CancellationToken ct)
    {
        string status,mode,selection;bool enabled;int minimum;string? fixedId;
        using(var q=EventAccess.Command(c,tx,"SELECT Status,Mode,PrizeEnabled,WinnersPerGroup,PrizeSelection,FixedPrizeTypeId FROM PrizeEvents WHERE Id=$event AND (PurgeAt IS NULL OR PurgeAt>$now)",("$event",eventId),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds())))
        using(var r=await q.ExecuteReaderAsync(ct)){
            if(!await r.ReadAsync(ct))throw new DemoRequestException("PrizeNotFound");
            status=r.GetString(0);mode=r.GetString(1);enabled=r.GetBoolean(2);minimum=r.GetInt32(3);selection=r.GetString(4);fixedId=r.IsDBNull(5)?null:r.GetString(5);
        }
        var names=new List<string>();
        using(var q=EventAccess.Command(c,tx,selection=="Fixed"
            ?"SELECT Name FROM PrizeTypes WHERE EventId=$event AND Id=$fixed"
            :"SELECT Name FROM PrizeTypes WHERE EventId=$event ORDER BY Id",("$event",eventId),("$fixed",fixedId)))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))names.Add(r.GetString(0));
        var types=await AvailableTypes(c,tx,eventId,selection,fixedId,ct);
        return new(enabled,enabled&&mode=="Group"&&types.Count>0,minimum,status,
            !enabled?"RoundPrizesDisabled":mode!="Group"?"RoundNoGroupPrize":names.Count==0?"RoundNoPrizeConfigured":types.Count==0?"RoundNoStock":"RoundPrizeAvailable",selection,names);
    }
    public async Task<VisitorPrizeStatus?> VisitorStatusAsync(string? token,CancellationToken ct)
    {
        var round=await ForDemoAsync(token,ct);if(round?.Alias is null)return null;
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        RoundPrizeState price;try{price=await PrizeState(c,tx,round.EventId,ct);}catch(DemoRequestException){return null;}
        var yubi=round.Status=="Closed"&&round.IsWinner?round.PrizeName?.Contains("YubiKey",StringComparison.OrdinalIgnoreCase)==true:
            Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeTypes t JOIN PrizeEvents e ON e.Id=t.EventId WHERE e.Id=$event AND t.Active=1 AND (e.PrizeSelection='Random' OR t.Id=e.FixedPrizeTypeId) AND instr(lower(t.Name),'yubikey')>0",ct,("$event",round.EventId)))>0;
        if(round.Status=="Closed")yubi=round.IsWinner?round.PrizeName?.Contains("YubiKey",StringComparison.OrdinalIgnoreCase)==true:round.Winners.Any(w=>w.PrizeName.Contains("YubiKey",StringComparison.OrdinalIgnoreCase));
        return new(round,price.Enabled,round.Status=="Closed"?yubi:price.Available&&yubi);
    }
    public async Task<IReadOnlyList<RoundOption>> RoundOptionsAsync(ClaimsPrincipal user,string eventId,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await EventAccess.Require(c,tx,presenters,user,"prize",eventId,false,ct);await PrizeState(c,tx,eventId,ct);
        var list=new List<RoundOption>();using var q=EventAccess.Command(c,tx,"SELECT Id,Number,Status FROM PrizeGroups WHERE EventId=$event ORDER BY Number DESC",("$event",eventId));using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2)));return list;
    }
    internal static async Task<string> AllocateVisitorAliasAsync(SqliteConnection c,SqliteTransaction tx,CancellationToken ct)
    {
        for(var attempt=0;attempt<128;attempt++){
            var alias=FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)]+Animals[RandomNumberGenerator.GetInt32(Animals.Length)];
            if(attempt>=8)alias+=RandomNumberGenerator.GetInt32(1000,10000).ToString();
            if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT (SELECT COUNT(*) FROM Demos WHERE Name=$alias)+(SELECT COUNT(*) FROM PrizeRoundParticipants WHERE Alias=$alias)+(SELECT COUNT(*) FROM PrizeRoundHistory WHERE Alias=$alias)",ct,("$alias",alias)))==0)return alias;
        }
        throw new DemoRequestException("PrizeRoundAliasExhausted");
    }
    public async Task<RoundProgress> ProgressAsync(ClaimsPrincipal user,string eventId,string? groupId,CancellationToken ct)
    {
        var board=await BoardAsync(user,eventId,ct);
        var group=groupId is null?board.Rounds.FirstOrDefault(g=>g.Status=="Open"):board.Rounds.SingleOrDefault(g=>g.GroupId==groupId);

        if(groupId is null)group??=board.Rounds[0];
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        await EventAccess.Require(c,tx,presenters,user,"prize",eventId,false,ct);
        if(groupId is not null&&group is null){
            using var q=EventAccess.Command(c,tx,"SELECT Number,Capacity,Occupied,Status FROM PrizeGroups WHERE EventId=$event AND Id=$group",("$event",eventId),("$group",groupId));using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new DemoRequestException("PrizeRoundChanged");
            group=await ReadGroup(c,tx,eventId,board.EventName,groupId,r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),null,ct);
        }
        var price=await PrizeState(c,tx,eventId,ct);
        var entries=await RoundEntries(c,tx,eventId,group!.GroupId,ct);
        var eligible=entries.Count(e=>e.Eligible);
        var completed=Convert.ToInt32(await EventAccess.Scalar(c,tx,"SELECT COUNT(DISTINCT r.ParticipantId) FROM (SELECT EventId,GroupId,ParticipantId FROM PrizeRoundParticipants UNION SELECT EventId,GroupId,ParticipantId FROM PrizeRoundHistory) r WHERE r.EventId=$event AND r.GroupId=$group AND (EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=r.EventId AND a.TargetId=r.ParticipantId AND a.Action='RoundDemoCompleted') OR EXISTS(SELECT 1 FROM PrizeParticipants p JOIN VisitorExercises v ON v.DemoId=p.AccountId WHERE p.EventId=r.EventId AND p.Id=r.ParticipantId AND v.CompletedAt IS NOT NULL))",ct,("$event",eventId),("$group",group.GroupId)));
        var rows=new List<RoundParticipantRow>();
        using(var q=EventAccess.Command(c,tx,"""
            SELECT COALESCE(p.Id,d.Id),COALESCE(r.Alias,d.Name),
              EXISTS(SELECT 1 FROM Credentials cr WHERE cr.DemoId=d.Id),d.LastAuthenticatedAt IS NOT NULL,
              (v.CompletedAt IS NOT NULL OR EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=x.EventId AND a.TargetId=p.Id AND a.Action='RoundDemoCompleted')),
              p.State,r.IsWinner,r.PrizeName,p.Id
            FROM Demos d JOIN PresenterDemoContexts x ON x.DemoId=d.Id AND x.Kind='prize'
            LEFT JOIN PrizeParticipants p ON p.AccountId=d.Id AND p.EventId=x.EventId
            LEFT JOIN PrizeRoundParticipants r ON r.EventId=p.EventId AND r.ParticipantId=p.Id
            LEFT JOIN VisitorExercises v ON v.DemoId=d.Id
            WHERE x.EventId=$event AND (p.GroupId=$group OR (p.Id IS NULL AND $open=1 AND d.ClosedAt IS NULL))
            UNION ALL
            SELECT h.ParticipantId,h.Alias,1,1,EXISTS(SELECT 1 FROM PrizeAudit a WHERE a.EventId=h.EventId AND a.TargetId=h.ParticipantId AND a.Action='RoundDemoCompleted'),NULL,h.IsWinner,h.PrizeName,h.ParticipantId
            FROM PrizeRoundHistory h WHERE h.EventId=$event AND h.GroupId=$group AND NOT EXISTS(SELECT 1 FROM PrizeRoundParticipants live WHERE live.EventId=h.EventId AND live.ParticipantId=h.ParticipantId)
            ORDER BY 2
            """,("$event",eventId),("$group",group!.GroupId),("$open",group.Status=="Open")))
        using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct)){
            var registered=r.GetBoolean(2);var signed=r.GetBoolean(3);var included=!r.IsDBNull(5)&&r.GetString(5)=="Active";var winner=!r.IsDBNull(6)&&r.GetBoolean(6);
            var entry=entries.SingleOrDefault(e=>e.Id==r.GetString(0));
            var reason=group.Status=="Closed"?await SavedDrawStatus(c,tx,eventId,r.GetString(0),winner,ct):!price.Enabled?"RoundNoDraw":
                r.IsDBNull(8)?(!registered?"RoundRowNotRegistered":!signed?"RoundRowNotSignedIn":"RoundRowNotJoined"):
                entry?.Valid==false?"RoundRowEnded":entry?.Completed!=true?"RoundRowIncomplete":!price.Available?price.Reason:"RoundRowEligible";
            string? detail=null;
            if(group.Status=="Closed"&&reason=="RoundRowNotDrawn"){
                using var fact=EventAccess.Command(c,tx,"SELECT Action FROM PrizeAudit WHERE EventId=$event AND TargetId=$participant AND Action IN('RoundWithdrawnAtDraw','RoundIncompleteAtDraw') ORDER BY Action DESC LIMIT 1",("$event",eventId),("$participant",r.GetString(0)));
                detail=(await fact.ExecuteScalarAsync(ct)) as string;
            }
            rows.Add(new(r.GetString(1),registered,signed,r.GetBoolean(4),reason,r.IsDBNull(7)?null:r.GetString(7),detail));
        }
        if(group!.Status=="Closed")eligible=rows.Count(row=>row.Status is "RoundRowWon" or "RoundRowNoPrize");
        var canClose=group!.Status=="Open"&&price.EventStatus=="Open"&&(!price.Available||eligible==0||eligible>=price.Minimum);
        var reasonKey=price.EventStatus!="Open"?"RoundEventClosed":!price.Enabled?"RoundPrizesDisabled":!canClose&&group.Status=="Open"&&price.Available?"PrizeRoundInsufficientParticipants":eligible==0?"RoundNoEligible":price.Reason;
        var closedReason=group.WinnerCount>0?"RoundPrizeDrawn":Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND TargetId=$group AND Action='RoundNoPrizeAtDraw'",ct,("$event",eventId),("$group",group.GroupId)))>0?"RoundNoPriceAvailable":"RoundNoWinner";
        return new(group,completed,eligible,rows,price.Enabled,group.Status=="Closed"?group.WinnerCount>0:price.Available,canClose,reasonKey,group.Status=="Closed"?closedReason:price.Reason,price.Selection,price.PrizeNames);

    }
    private static async Task<string> UniqueAlias(SqliteConnection c,SqliteTransaction tx,string eventId,string groupId,CancellationToken token)
    {
        for(var attempt=0;attempt<128;attempt++)
        {
            var alias=FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)]+Animals[RandomNumberGenerator.GetInt32(Animals.Length)];
            if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT (SELECT COUNT(*) FROM PrizeRoundParticipants WHERE EventId=$event AND GroupId=$group AND Alias=$alias)+(SELECT COUNT(*) FROM PrizeRoundHistory WHERE EventId=$event AND GroupId=$group AND Alias=$alias)",token,
                ("$event",eventId),("$group",groupId),("$alias",alias)))==0)return alias;
        }
        throw new PrizeConflictException("PrizeRoundAliasExhausted");
    }
}
public sealed record RoundPrizeDelivery(string ParticipantId,int Round,string Alias,string PrizeName,bool Delivered);

public sealed record RoundProgress(PrizeRoundSnapshot Round,int Completed,int Eligible,IReadOnlyList<RoundParticipantRow> Participants,bool PrizesEnabled,bool PrizeAvailable,bool CanClose,string CloseReason,string PrizeReason,string PrizeSelection,IReadOnlyList<string> PrizeNames);
public sealed record RoundParticipantRow(string Alias,bool Registered,bool SignedIn,bool Completed,string Status,string? PrizeName,string? Reason=null);
public sealed record RoundOption(string Id,int Number,string Status);
public sealed record RoundPrizeState(bool Enabled,bool Available,int Minimum,string EventStatus,string Reason,string Selection,IReadOnlyList<string> PrizeNames);
public sealed record VisitorPrizeStatus(PrizeRoundSnapshot Round,bool PrizesEnabled,bool YubiKey)
{public string LinkKey=>YubiKey?(Round.Status=="Closed"?"YubiKeyResultLink":"YubiKeyParticipationLink"):(Round.Status=="Closed"?"RoundResultLink":"RoundParticipationLink");}
