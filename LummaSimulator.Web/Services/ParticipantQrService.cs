using System.Security.Claims;
using LummaSimulator.Shared;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Services;

public sealed record ParticipantQrEntry(string EventId,string EventName,string Url,bool LaptopOnly,string Kind="prize");

public sealed class ParticipantQrService(EventAccess access,PrizeRoundsService rounds,WebAuthnSettings settings)
{
    public async Task<ParticipantQrEntry?> ReadAsync(ClaimsPrincipal user,string id,string origin,string pathBase,CancellationToken ct,string kind="prize")
    {
        if(!await access.CanInviteVisitorsAsync(user,kind,id,ct))return null;
        string name;
        if(kind=="prize"){
            var info=await rounds.PublicJoinInfoAsync(id,ct);
            if(info is null)return null;
            name=info.EventName;
        }else{
            var info=(await access.ListAsync(user,ct)).SingleOrDefault(e=>e.Id==id&&e.Kind==kind);
            if(info is null)return null;
            name=info.Name;
        }
        // The configured visitor origin is authoritative; the management Host must never select it.
        var uri=settings.VisitorLink(id,pathBase,kind);
        return new(id,name,uri.AbsoluteUri,ParticipantLink.LaptopOnly(uri),kind);
    }
}
