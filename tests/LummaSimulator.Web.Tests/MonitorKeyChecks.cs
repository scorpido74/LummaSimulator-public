using System.Text;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace LummaSimulator.Web.Tests;
internal static class MonitorKeyChecks
{
    public static async Task RunAsync(string root)
    {
        var directory = Path.Combine(root, "monitor-management"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "monitor-integration.state");
        var protection = new EphemeralDataProtectionProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var actor = Guid.NewGuid().ToString("D"); var audit = new List<MonitorKeyAudit>();
        Task<string> Authorize(CancellationToken _) => Task.FromResult(actor);
        Task Audit(MonitorKeyAudit item, CancellationToken _) { audit.Add(item); return Task.CompletedTask; }
        MonitorKeyStore Open() => new(path, protection, config, TimeProvider.System);
        var keys = Open();
        TestProgram.Check(!keys.Snapshot().Configured && !File.Exists(path), "Unconfigured monitor stays disabled without creating state");
        await TestProgram.RejectAsync(() => keys.ChangeAsync("generate", 0, _ => throw new DemoRequestException("Denied"), Audit, default), "Failed authorization cannot generate monitor state");
        TestProgram.Check(!File.Exists(path), "Denied management leaves state absent");
        var first = (await keys.ChangeAsync("generate", 0, Authorize, Audit, default))!;
        TestProgram.Check(DemoIdentity.IsToken(first) && keys.Verify(first) && keys.Snapshot().CreatedAt is not null, "Generated 32-byte Base64URL key verifies after audited hash storage");
        var clear = Encoding.UTF8.GetString(protection.CreateProtector("Lumma.MonitorKeyState.v1").Unprotect(File.ReadAllBytes(path)));
        TestProgram.Check(clear.Contains(DemoIdentity.Hash(first)) && !clear.Contains(first), "Protected server state contains hash and never original key");
        TestProgram.Check(!keys.Verify(null) && !keys.Verify("invalid") && !keys.Verify(new string('!', 43)) && !keys.Verify(DemoIdentity.RandomToken()), "Missing malformed and wrong monitor keys are rejected");
        TestProgram.Check(Open().Verify(first), "New store instance with same protection context retains monitor key");
        await TestProgram.RejectAsync(() => keys.ChangeAsync("rotate", 0, Authorize, Audit, default), "Stale management revision cannot rotate");
        var second = (await keys.ChangeAsync("rotate", 1, Authorize, Audit, default))!;
        TestProgram.Check(!keys.Verify(first) && keys.Verify(second) && keys.Snapshot().RotatedAt is not null, "Rotation immediately replaces old key without overlap");
        await keys.ChangeAsync("revoke", 2, Authorize, Audit, default);
        config["Monitor:KeyHash"] = DemoIdentity.Hash(first);
        TestProgram.Check(!Open().Verify(first) && !Open().Verify(second) && !Open().Snapshot().Configured, "Revocation survives restart and cannot revive legacy configuration");
        TestProgram.Check(audit.Select(a => a.Action).SequenceEqual(new[] { "MonitorKeyGenerated", "MonitorKeyRotated", "MonitorKeyRevoked" }) && audit.All(a => !a.ToString().Contains(first) && !a.ToString().Contains(second)), "Management audits contain action references without secret values");
        try { await keys.ChangeAsync("generate", 3, Authorize, (_, _) => throw new IOException("Synthetic audit outage"), default); }
        catch (IOException) { }
        TestProgram.Check(keys.Snapshot().AuditPending && !keys.Snapshot().Configured, "Audit outage persists pending state and denies monitor access");
        await keys.ReconcileAsync(Audit, default); var count = audit.Count; await keys.ReconcileAsync(Audit, default);
        TestProgram.Check(!keys.Snapshot().AuditPending && audit.Count == count, "Pending audit reconciliation is repeatable without duplicate delivery");
        File.Delete(path); // Only this test's new synthetic state; managed marker remains.
        TestProgram.Check(Open().Snapshot().StorageError && !Open().Verify(first), "Missing managed state fails closed instead of legacy fallback");
        File.WriteAllText(path, "synthetic corruption");
        TestProgram.Check(Open().Snapshot().StorageError && !Open().Verify(first), "Corrupt managed state fails closed");
        var legacyPath = Path.Combine(directory, "legacy.state");
        var legacy = new MonitorKeyStore(legacyPath, protection, config, TimeProvider.System);
        TestProgram.Check(legacy.Verify(first) && legacy.Snapshot().Source == "Legacy", "Existing legacy hash remains compatible before adoption");
        await legacy.ChangeAsync("adopt", 0, Authorize, Audit, default);
        config["Monitor:KeyHash"] = DemoIdentity.Hash(second);
        TestProgram.Check(legacy.Verify(first) && !legacy.Verify(second) && legacy.Snapshot().CreatedAt is null, "Explicit adoption freezes authority without inventing legacy creation date");
    }
}
