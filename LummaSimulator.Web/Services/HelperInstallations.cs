using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed record HelperHeartbeat(string Id,string Identity,string Device,string Version,string Pairing,string Health);
public sealed record HelperInstallation(string Id,string Device,string Version,string Pairing,string Health,DateTimeOffset LastSeen,string Presence,bool Approved);

internal static class HelperInstallationSchema
{
    private const string Definition="HelperInstallations (Id TEXT PRIMARY KEY NOT NULL, IdentityHash TEXT NOT NULL, ConnectionId TEXT NOT NULL, Device TEXT NOT NULL, Version TEXT NOT NULL, Pairing TEXT NOT NULL, Health TEXT NOT NULL, LastSeen INTEGER NOT NULL)";
    internal const string CreateIfMissing="CREATE TABLE IF NOT EXISTS "+Definition;
    internal static readonly string Expected=Prizes.PrizeMigration.Normalize("CREATE TABLE "+Definition);
}

public sealed partial class SessionDemoService
{
    public const int HelperOnlineSeconds=45,HelperStaleSeconds=120;

    internal Task HelperHeartbeatAsync(string connectionId,string? key,HelperHeartbeat heartbeat,CancellationToken ct)=>state.Access(async()=>{
        var grant=App(connectionId,key);
        if(!grant.RemoteHelper||!Id(heartbeat.Id)||!DemoIdentity.IsToken(heartbeat.Identity)
            ||heartbeat.Device is not {Length:>0 and <=64}||heartbeat.Device.Any(char.IsControl)
            ||heartbeat.Version is not {Length:>0 and <=32}||!Version.TryParse(heartbeat.Version,out _)
            ||heartbeat.Pairing is not ("Unknown" or "Unconfigured" or "Confirmed" or "Rejected")
            ||heartbeat.Health is not ("Unknown" or "Healthy" or "Unreachable" or "Incompatible" or "Rejected"))throw Denied();
        await using var c=await store.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:false);
        await Actor(c,tx,grant,null,ct);
        var count=await EventAccess.Execute(c,tx,"""
            INSERT INTO HelperInstallations VALUES($id,$hash,$connection,$device,$version,$pairing,$health,$now)
            ON CONFLICT(Id) DO UPDATE SET ConnectionId=$connection,Device=$device,Version=$version,Pairing=$pairing,Health=$health,LastSeen=$now
            WHERE HelperInstallations.IdentityHash=$hash
            """,ct,("$id",heartbeat.Id),("$hash",DemoIdentity.Hash(heartbeat.Identity)),("$connection",connectionId),
            ("$device",heartbeat.Device),("$version",heartbeat.Version),("$pairing",heartbeat.Pairing),("$health",heartbeat.Health),("$now",Now.ToUnixTimeMilliseconds()));
        if(count!=1)throw Denied();
        tx.Commit();
        return true;
    },ct);

    internal Task<IReadOnlyList<HelperInstallation>> HelperInstallationsAsync(CancellationToken ct)=>state.Access<IReadOnlyList<HelperInstallation>>(async()=>{
        await using var c=await store.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:true);
        using var query=EventAccess.Command(c,tx,"SELECT Id,Device,Version,Pairing,Health,LastSeen,ConnectionId FROM HelperInstallations ORDER BY LastSeen DESC,Id");
        var rows=new List<(HelperInstallation Installation,string Connection)>();
        using(var reader=await query.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))
        {
            var lastSeen=DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5));
            var age=(Now-lastSeen).TotalSeconds;
            var presence=age<0?"Onbekend":age<=HelperOnlineSeconds?"Online":age<=HelperStaleSeconds?"Verouderd":"Offline";
            rows.Add((new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),lastSeen,presence,false),reader.GetString(6)));
        }
        var result=new List<HelperInstallation>();
        foreach(var (installation,connection) in rows)
        {
            var approved=false;
            if(state.Apps.TryGetValue(connection,out var grant)&&grant.RemoteHelper&&grant.Actor is not null)
            {
                try{await Actor(c,tx,grant,null,ct);approved=true;}
                catch(LummaSimulator.Web.Models.DemoRequestException){}
            }
            result.Add(installation with{Approved=approved});
        }
        return result;
    },ct);
}
