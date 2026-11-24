using System.Net;
using System.Security.Cryptography;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed class DemoMetrics
{
    private long _expired;
    public long ExpiredDemonstrations => Interlocked.Read(ref _expired);
    public void RecordExpired(int count) => Interlocked.Add(ref _expired, count);
}
public sealed record SessionMetadata(string DemoRunId, string Target, bool SessionFound,
    string AuthenticationMethod, bool SessionActive, bool SessionTokenExposed,
    string State, DateTimeOffset? VerifiedAt);

public sealed class SessionStatusService(DemoStore store, TimeProvider clock, WebAuthnSettings settings,
    IConfiguration configuration, DemoMetrics metrics, MonitorKeyStore? monitorKeys = null, SessionDemoState? sessionDemos = null)
{
    public bool IsConfigured => monitorKeys?.Snapshot().Configured ?? (configuration["Monitor:KeyHash"] is { Length: 64 } hash && hash.All(Uri.IsHexDigit));
    public bool Authorize(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return false;
        var key = context.Request.Headers["X-Lumma-Monitor-Key"].ToString();
        if (monitorKeys is not null) return monitorKeys.Verify(key);
        if (!IsConfigured || !DemoIdentity.IsToken(key)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(DemoIdentity.Hash(key)), Convert.FromHexString(configuration["Monitor:KeyHash"]!));
    }

    // Invoked only by CSRF-protected, presenter-authorized handlers; never by anonymous API parameters.
    public async Task SetGrantAsync(string demoId, bool enabled, CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try
        {
            if (!enabled) { await store.ExecuteAsync("DELETE FROM MonitorGrants WHERE DemoId=$id", token, ("$id", demoId)); return; }
            if (!IsConfigured) throw new DemoRequestException("Configureer eerst het aparte monitorgeheim.");
            var expires = clock.GetUtcNow().Add(DemoService.IdleLifetime).ToUnixTimeMilliseconds();
            await store.ExecuteAsync("""
                INSERT INTO MonitorGrants(DemoId,Expires) SELECT Id,$expires FROM Demos WHERE Id=$id
                ON CONFLICT(DemoId) DO UPDATE SET Expires=excluded.Expires
                """, token, ("$id", demoId), ("$expires", expires));
        }
        finally { store.Gate.Release(); }
    }
    public async Task<HashSet<string>> GrantedDemoIdsAsync(CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var accounts = await store.AccountsAsync(null, token);
            var result = new HashSet<string>();
            foreach (var account in accounts)
                if (Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM MonitorGrants WHERE DemoId=$id AND Expires>$now", token,
                        ("$id", account.Id), ("$now", now))) == 1) result.Add(account.Id);
            return result;
        }
        finally { store.Gate.Release(); }
    }
    public async Task<SessionMetadata?> GetAsync(string runId, CancellationToken token)
    {
        if (!Guid.TryParseExact(runId, "D", out var parsedRun)) return null;
        runId = parsedRun.ToString("D");
        await store.Gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            var account = (await store.AccountsAsync(null, token)).SingleOrDefault(a => a.DemoRunId == runId);
            if (account is null) return null;
            if (account.LastSeen <= now.Subtract(TimeSpan.FromMinutes(configuration.GetValue("Experience:AccountIdleMinutes", 29))).ToUnixTimeMilliseconds())
            {
                await DemoAccountRemoval.DeleteAsync(store,new SecurityAuditService(store,clock,configuration:configuration),account,false,sessionDemos,token);
                metrics.RecordExpired(1);
                return null;
            }
            if (Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM MonitorGrants WHERE DemoId=$id AND Expires>$now", token,
                ("$id", account.Id), ("$now", now.ToUnixTimeMilliseconds()))) == 0) return null;
            var verifiedAt = account.LastAuthenticatedAt is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : (DateTimeOffset?)null;
            var active = verifiedAt.HasValue && now - verifiedAt.Value < TimeSpan.FromMinutes(30)
                && await new SecurityAuditService(store,clock,configuration:configuration).IsAuthenticatedAsync(account,token);
            if (!active) return null;
            return new(account.DemoRunId, settings.RpId, true, verifiedAt.HasValue ? "WebAuthn" : "None", active, false,
                active ? "ActiveWebAuthnSession" : verifiedAt.HasValue ? "InvalidExpired" : "NoAuthenticatedSession", verifiedAt);
        }
        finally { store.Gate.Release(); }
    }
}
