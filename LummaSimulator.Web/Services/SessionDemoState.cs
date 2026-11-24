namespace LummaSimulator.Web.Services;

// Ephemeral grants are intentionally not restored after a portal restart.
public sealed class SessionDemoState(TimeProvider clock)
{
    internal sealed class AppGrant
    {
        internal required string Id,KeyHash;
        internal required DateTimeOffset Expires;
        internal string? Actor,ActorSession,EventId;
        internal bool RemoteHelper;
        internal long SelectionVersion;
    }
    internal sealed class ConsentGrant
    {
        internal required string Id,AppId,EventId,ParticipantId,WpfRunId,AccountId,BindingHash,IdentitySession;
        internal required DateTimeOffset Expires;
        internal required long SelectionVersion;
        internal string Status="Pending";
    }
    internal sealed class ViewGrant
    {
        internal required string Id,ConsentId,OpenHash;
        internal required DateTimeOffset OpenExpires;
        internal string? CookieHash;
    }
    internal readonly Dictionary<string,AppGrant> Apps=new(StringComparer.Ordinal);
    internal readonly Dictionary<string,ConsentGrant> Consents=new(StringComparer.Ordinal);
    internal readonly Dictionary<string,ViewGrant> Views=new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate=new(1,1);
    internal Task RevokeAccountAsync(string account,CancellationToken ct)=>Access(()=>{
        foreach(var consent in Consents.Values.Where(c=>c.AccountId==account))consent.Status="Revoked";
        return Task.FromResult(true);
    },ct);
    internal async Task<T> Access<T>(Func<Task<T>> action,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now=clock.GetUtcNow();
            foreach(var item in Apps.Values.Where(a=>a.Expires<=now).ToArray())Apps.Remove(item.Id);
            foreach(var item in Consents.Values.Where(c=>!Apps.ContainsKey(c.AppId)||c.Expires.AddMinutes(5)<=now).ToArray())Consents.Remove(item.ParticipantId);
            foreach(var item in Views.Values.Where(v=>!Consents.Values.Any(c=>c.Id==v.ConsentId)||(v.CookieHash is null&&v.OpenExpires<=now)).ToArray())Views.Remove(item.Id);
            return await action();
        }
        finally { _gate.Release(); }
    }
}
