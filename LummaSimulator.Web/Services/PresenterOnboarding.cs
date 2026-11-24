using System.Security.Claims;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Services;
public sealed record OnboardingState(bool Existing,bool Completed,bool HostingConfirmed,string? FirstEvent,
    string? EventName=null,bool EventReady=false,bool OrganizationReady=false,bool AdministratorReady=false)
{
    public bool Ready=>HostingConfirmed&&EventReady&&OrganizationReady&&AdministratorReady;
}
public sealed class PresenterOnboarding(DemoStore store,PresenterService presenters,TimeProvider clock)
{
    public async Task<OnboardingState> StateAsync(CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var q=c.CreateCommand();q.CommandText="""
            SELECT i.Existing,i.CompletedAt,i.HostingConfirmed,i.FirstEvent,x.Name,
              COALESCE(x.Status='Active' AND a.Active=1,0),
              EXISTS(SELECT 1 FROM PresenterPresentationSettings WHERE Id=1),
              EXISTS(SELECT 1 FROM PresenterAccounts p JOIN PresenterKeys k ON k.AccountId=p.Id WHERE p.Active=1 AND p.Role='Administrator')
            FROM PresenterInstallation i LEFT JOIN ExperienceEvents x ON x.Id=i.FirstEvent
            LEFT JOIN PresenterEventScopes e ON e.Kind='experience' AND e.SourceId=x.Id
            LEFT JOIN PresenterAccounts a ON a.Id=e.OwnerId WHERE i.Id=1
            """;
        using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new InvalidDataException("Installation state unavailable");
        return new(r.GetBoolean(0),!r.IsDBNull(1),r.GetBoolean(2),r.IsDBNull(3)?null:r.GetString(3),
            r.IsDBNull(4)?null:r.GetString(4),r.GetBoolean(5),r.GetBoolean(6),r.GetBoolean(7));
    }
    public async Task ChangeAsync(ClaimsPrincipal principal,string action,string name,bool https,bool certificateConfirmed,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,true,action=="finish",ct);
        if(Convert.ToInt64(await EventAccess.Scalar(c,tx,"SELECT COUNT(*) FROM PresenterInstallation WHERE Id=1 AND Existing=0 AND CompletedAt IS NULL",ct))!=1)throw new DemoRequestException("PresenterDenied");
        if(action=="hosting") {
            if(!https||!certificateConfirmed)throw new DemoRequestException("PresenterConfirm");
            // The host validates the effective RP ID and origins at startup using its actual environment.
            if(await EventAccess.Execute(c,tx,"UPDATE PresenterInstallation SET HostingConfirmed=1 WHERE Id=1 AND HostingConfirmed=0",ct)==0){tx.Commit();return;}
        } else if(action=="event") {
            if(string.IsNullOrWhiteSpace(name)||name.Length>100||name.Any(char.IsControl))throw new DemoRequestException("PresenterConfirm");
            if(await EventAccess.Scalar(c,tx,"SELECT FirstEvent FROM PresenterInstallation WHERE Id=1",ct) is string) {tx.Commit();return;}
            var id=Guid.NewGuid().ToString("D");
            await EventAccess.Execute(c,tx,"INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES($id,$name,$now,'Active'); INSERT INTO EventPrizePolicies(EventId) VALUES($id); UPDATE PresenterInstallation SET FirstEvent=$id WHERE Id=1",ct,("$id",id),("$name",name.Trim()),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
            await EventAccess.Register(c,tx,"experience",id,name.Trim(),actor.Id,ct);
        } else if(action=="finish") {
            if(!https||Convert.ToInt64(await EventAccess.Scalar(c,tx,"""
                SELECT COUNT(*) FROM PresenterInstallation i JOIN PresenterEventScopes e ON e.Kind='experience' AND e.SourceId=i.FirstEvent
                JOIN PresenterAccounts a ON a.Id=e.OwnerId AND a.Active=1
                JOIN ExperienceEvents x ON x.Id=e.SourceId AND x.Status='Active'
                WHERE i.Id=1 AND i.HostingConfirmed=1 AND EXISTS(SELECT 1 FROM PresenterPresentationSettings WHERE Id=1)
                  AND EXISTS(SELECT 1 FROM PresenterAccounts p JOIN PresenterKeys k ON k.AccountId=p.Id WHERE p.Active=1 AND p.Role='Administrator')
                """,ct))!=1)throw new DemoRequestException("PresenterConfirm");
            await EventAccess.Execute(c,tx,"UPDATE PresenterInstallation SET CompletedAt=$now WHERE Id=1",ct,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
        } else throw new DemoRequestException("PresenterDenied");
        await EventAccess.Execute(c,tx,"INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($now,$actor,$action,'installation')",ct,("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$actor",actor.Id),("$action","Onboarding"+action));tx.Commit();
    }
}
