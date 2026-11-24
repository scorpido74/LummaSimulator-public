using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed record ExerciseBegin(bool Completed,string? Attempt,bool NotificationUnavailable);
public sealed class VisitorExerciseService(DemoStore store,SecurityAuditService audit,TimeProvider clock,EventEditionService? events=null)
{
    public async Task<ExerciseBegin> BeginAsync(string? token,CancellationToken ct)
    {
        var account=await audit.OwnerAsync(token,ct)??throw new DemoRequestException("ClickFixSignInRequired");
        if(account.CredentialCount==0)throw new DemoRequestException("ClickFixSignInRequired");
        // A completed exercise may be replayed visually; its durable completion and identity stay unchanged.
        await store.Gate.WaitAsync(ct);
        try{
            if(await audit.OwnerAsync(token,ct) is null)throw new DemoRequestException("ClickFixSignInRequired");
            if(await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",ct,("$id",account.Id)) is long)
                return new(true,null,false);
        }finally{store.Gate.Release();}
        string? attempt=null;bool unavailable=false;
        try{if(events is not null)attempt=await events.TicketAsync(token,ct);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){unavailable=true;}
        await store.Gate.WaitAsync(ct);
        try{
            if(await audit.OwnerAsync(token,ct) is null)throw new DemoRequestException("ClickFixSignInRequired");
            await store.ExecuteAsync("INSERT INTO VisitorExercises(DemoId,StartedAt) VALUES($id,$now) ON CONFLICT(DemoId) DO NOTHING",ct,("$id",account.Id),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()));
            var completed=await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",ct,("$id",account.Id)) is long;
            return new(completed,attempt,unavailable);
        }finally{store.Gate.Release();}
    }
}
