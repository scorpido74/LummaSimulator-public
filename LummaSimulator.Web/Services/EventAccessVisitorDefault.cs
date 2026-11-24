using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed partial class EventAccess
{
    // Same active owner, login routes and event admission as the existing visitor links.
    internal static async Task<string?> VisitorDefaultNameAsync(SqliteConnection c,SqliteTransaction tx,string kind,string id,CancellationToken ct)
    {
        if(kind is not ("experience" or "prize")||!Valid(kind,id)||id==ExperienceStatistics.UnassignedEvent)return null;
        if(Convert.ToInt64(await Scalar(c,tx,VisitorOwnerSql,ct,("$kind",kind),("$id",id)))!=1)return null;
        var sql=kind=="experience"
            ?"SELECT Name FROM ExperienceEvents WHERE Id=$id AND Status='Active'"
            :"SELECT Name FROM PrizeEvents WHERE Id=$id AND Status='Open' AND Mode='Group'";
        if(Convert.ToInt32(await Scalar(c,tx,"SELECT COALESCE((SELECT Mask FROM EventLoginRoutes WHERE Kind=$kind AND EventId=$id),15)",ct,("$kind",kind),("$id",id)))==0)return null;
        return await Scalar(c,tx,sql,ct,("$id",id)) as string;
    }

    public async Task<VisitorDefault?> DefaultVisitorEventAsync(CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:true);
        var json=await Scalar(c,tx,"SELECT json_extract(Json,'$.DefaultVisitorEvent') FROM PresenterPresentationSettings WHERE Id=1",ct) as string;
        if(json is not null)
        {
            var saved=System.Text.Json.JsonSerializer.Deserialize<VisitorDefault>(json)!;
            var name=await VisitorDefaultNameAsync(c,tx,saved.Kind,saved.Id,ct);
            return saved with{Name=name??saved.Name,Available=name is not null};
        }
        return await InitialVisitorDefaultAsync(c,tx,ct);
    }

    private static async Task<VisitorDefault?> InitialVisitorDefaultAsync(SqliteConnection c,SqliteTransaction tx,CancellationToken ct)
    {
        // Onboarding records the original Experience. Legacy installations can have no FirstEvent.
        var first=await Scalar(c,tx,"SELECT FirstEvent FROM PresenterInstallation WHERE Id=1",ct) as string;
        if(first is not null&&await VisitorDefaultNameAsync(c,tx,"experience",first,ct) is {} firstName)
            return new("experience",first,firstName,true);
        using var query=Command(c,tx,"SELECT Id FROM ExperienceEvents WHERE Status='Active' AND Id<>$unassigned ORDER BY StartedAt,Id",("$unassigned",ExperienceStatistics.UnassignedEvent));
        var ids=new List<string>();
        using(var reader=await query.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))ids.Add(reader.GetString(0));
        foreach(var id in ids)
            if(await VisitorDefaultNameAsync(c,tx,"experience",id,ct) is {} name)return new("experience",id,name,true);
        return null;
    }

    public async Task<IReadOnlyList<EventAccessRow>> VisitorDefaultChoicesAsync(System.Security.Claims.ClaimsPrincipal user,CancellationToken ct)
    {
        if(!user.IsInRole("Administrator"))return [];
        var events=await ListAsync(user,ct);
        await using var c=await store.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:true);
        var actor=await presenters.Actor(c,tx,user,true,false,ct);
        var first=await InitialVisitorDefaultAsync(c,tx,ct);
        var result=new List<EventAccessRow>();
        foreach(var item in events.OrderByDescending(e=>e.Kind=="experience"&&e.Id==first?.Id))
            if(await Allowed(c,tx,actor.Id,actor.Role,item.Kind,item.Id,true,ct)&&await VisitorDefaultNameAsync(c,tx,item.Kind,item.Id,ct) is not null)
                result.Add(item);
        return result;
    }
}
