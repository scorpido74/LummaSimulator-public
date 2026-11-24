using System.Security.Claims;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services.Prizes;

// Same transactional account/session verification as existing presenter operations.
// No new authentication scheme, role or public route is introduced by 5.1.
internal sealed class PrizeAuthorization(PresenterService presenters)
{
    internal async Task RequireAdminAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,CancellationToken ct)
    {await presenters.Actor(c,tx,principal,true,false,ct);}
    internal async Task<bool> IsAdminAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,CancellationToken ct)=>(await presenters.Actor(c,tx,principal,false,false,ct)).Role=="Administrator";
    internal Task<string> RequireEventAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,string id,bool manage,CancellationToken ct)=>
        EventAccess.Require(c,tx,presenters,principal,"prize",id,manage,ct);
    internal Task<string> RequireOwnerAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,string id,CancellationToken ct)=>
        EventAccess.RequireOwner(c,tx,presenters,principal,"prize",id,ct);
    internal async Task<bool> CanEventAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,string id,CancellationToken ct)
    {var actor=await presenters.Actor(c,tx,principal,false,false,ct);return await EventAccess.Allowed(c,tx,actor.Id,actor.Role,"prize",id,false,ct);}
    internal async Task<string> RequireActorAsync(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,CancellationToken ct)
    {
        var actor=await presenters.Actor(c,tx,principal,false,false,ct);
        if(actor.Role is not ("Administrator" or "Presenter"))throw new UnauthorizedAccessException();
        return actor.Id;
    }
}
