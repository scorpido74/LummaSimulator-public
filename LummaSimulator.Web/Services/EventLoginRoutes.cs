using System.Security.Claims;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed record LoginRoutes(int Mask = 15)
{
    public static readonly string[] Routes = ["yubikey", "laptop", "phone", "ipad"];
    public bool Allows(string route) { var index = Array.IndexOf(Routes, route); return index >= 0 && (Mask & (1 << index)) != 0; }
}
public sealed record LoginRoutesEditor(string Kind,string Id,LoginRoutes Routes,bool PrizeDetails=false);

public sealed partial class EventAccess
{
    public async Task<LoginRoutes> LoginRoutesAsync(string? kind, string? id, CancellationToken ct)
    {
        if (kind is null && id is null) return new();
        if (kind is null || id is null || !Valid(kind,id) || !await LiveAsync(kind,id,ct)) throw new DemoRequestException("PresenterDenied");
        return new(Convert.ToInt32(await store.ScalarAsync("SELECT COALESCE((SELECT Mask FROM EventLoginRoutes WHERE Kind=$kind AND EventId=$id),15)", ct, ("$kind",kind),("$id",id))));
    }
    public async Task<LoginRoutes> ManagedLoginRoutesAsync(ClaimsPrincipal user, string kind, string id, CancellationToken ct)
    {
        await RequireAsync(user,kind,id,false,ct);
        return new(Convert.ToInt32(await store.ScalarAsync("SELECT COALESCE((SELECT Mask FROM EventLoginRoutes WHERE Kind=$kind AND EventId=$id),15)", ct, ("$kind",kind),("$id",id))));
    }
    public async Task SetLoginRoutesAsync(ClaimsPrincipal user, string kind, string id, int mask, CancellationToken ct)
    {
        if (mask is < 0 or > 15) throw new DemoRequestException("PresenterConfirm");
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred:false);
        var actor = await Require(c,tx,presenters,user,kind,id,true,ct);
        if(mask==0&&kind is ("experience" or "prize"))throw new DemoRequestException("LoginOptionsRequired");
        await Execute(c,tx,"INSERT INTO EventLoginRoutes VALUES($kind,$id,$mask) ON CONFLICT(Kind,EventId) DO UPDATE SET Mask=excluded.Mask",ct,("$kind",kind),("$id",id),("$mask",mask));
        await Audit(c,tx,actor,"EventLoginRoutesChanged",kind+":"+id+":"+mask,ct); tx.Commit();
    }
}
