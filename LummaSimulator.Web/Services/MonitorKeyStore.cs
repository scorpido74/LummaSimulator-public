using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services;

public sealed record MonitorKeyStatus(string Source, long Revision, bool Configured, string? KeyId,
    DateTimeOffset? CreatedAt, DateTimeOffset? RotatedAt, bool AuditPending = false, bool StorageError = false);
public sealed record MonitorKeyAudit(string OperationId, string ActorId, string Action, DateTimeOffset At);
internal sealed class MonitorKeyState
{
    public MonitorKeyState() { }
    public int Version { get; set; } = 1;
    public long Revision { get; set; }
    public string Hash { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string Status { get; set; } = "Revoked";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public MonitorKeyAudit? Pending { get; set; }
}

// One managed authority; legacy configuration is consulted ONLY before explicit adoption.
public sealed class MonitorKeyStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public MonitorKeyStore(string path, IDataProtectionProvider protection, IConfiguration configuration, TimeProvider clock)
    { _path = path; _protector = protection.CreateProtector("Lumma.MonitorKeyState.v1"); _configuration = configuration; _clock = clock; }
    private string? LegacyHash { get { var hash = _configuration["Monitor:KeyHash"]; return ValidHash(hash) ? hash : null; } }
    private static bool Exists(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("MonitorKeyStorage"); return true; }
        catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; }
    }
    private bool Managed => Exists(_path) || Exists(_path + ".managed") || Exists(_path + ".previous");
    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private FileStream Lease() => new(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private MonitorKeyState Read()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length > 65536) throw new IOException("MonitorKeyStorage");
        var state = JsonSerializer.Deserialize<MonitorKeyState>(_protector.Unprotect(File.ReadAllBytes(_path)), Json) ?? throw new IOException("MonitorKeyStorage");
        if (state.Version != 1 || state.Revision < 1 || state.Hash is null || !Guid.TryParseExact(state.KeyId, "D", out _)
            || state.Status is not ("Active" or "Revoked") || (state.Status == "Active" ? !ValidHash(state.Hash) : state.Hash.Length != 0)
            || (state.Pending is { } pending && (!Guid.TryParseExact(pending.OperationId, "D", out _) || !Guid.TryParseExact(pending.ActorId, "D", out _)
                || pending.Action is not ("MonitorKeyGenerated" or "MonitorKeyRotated" or "MonitorKeyRevoked" or "MonitorKeyAdopted")))) throw new IOException("MonitorKeyStorage");
        return state;
    }
    private void Save(MonitorKeyState state)
    {
        // Latch first. A crash can cause an outage, never legacy-key resurrection.
        if (!File.Exists(_path + ".managed")) {
            using var marker = new FileStream(_path + ".managed", FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            marker.WriteByte(1); marker.Flush(true);
        }
        var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state, Json));
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { output.Write(bytes); output.Flush(true); }
        if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".previous"); else File.Move(temporary, _path);
    }
    private static MonitorKeyStatus Status(MonitorKeyState state) => new(state.Status, state.Revision,
        state.Status == "Active" && state.Pending is null, state.KeyId, state.CreatedAt, state.RotatedAt, state.Pending is not null);
    public MonitorKeyStatus Snapshot()
    {
        try {
            if (!Managed && LegacyHash is null) return new("Disabled", 0, false, null, null, null);
            using var lease = Lease();
            return Managed ? Status(Read()) : new("Legacy", 0, LegacyHash is not null, null, null, null);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return new("StorageError", 0, false, null, null, null, StorageError: true); }
    }
    public bool Verify(string? key)
    {
        if (!DemoIdentity.IsToken(key)) return false;
        try {
            if (!Managed && LegacyHash is null) return false;
            using var lease = Lease();
            var state = Managed ? Read() : null;
            var hash = state is null ? LegacyHash : state.Status == "Active" && state.Pending is null ? state.Hash : null;
            return hash is not null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(DemoIdentity.Hash(key!)));
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return false; }
    }
    private async Task Flush(MonitorKeyState state, Func<MonitorKeyAudit, CancellationToken, Task> audit, CancellationToken ct)
    {
        if (state.Pending is not { } pending) return;
        await audit(pending, ct); // Existing PresenterAudit, idempotent on operation reference.
        state.Pending = null; Save(state);
    }
    public async Task ReconcileAsync(Func<MonitorKeyAudit, CancellationToken, Task> audit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { if (!Managed) return; using var lease = Lease(); var state = Read(); await Flush(state, audit, ct); }
        finally { _gate.Release(); }
    }
    public async Task<string?> ChangeAsync(string operation, long expectedRevision, Func<CancellationToken, Task<string>> authorize,
        Func<MonitorKeyAudit, CancellationToken, Task> audit, CancellationToken ct)
    {
        if (operation is not ("generate" or "rotate" or "revoke" or "adopt")) throw new DemoRequestException("MonitorManageDenied");
        await _gate.WaitAsync(ct);
        try {
            using var lease = Lease();
            var state = Managed ? Read() : new MonitorKeyState();
            if (state.Revision != expectedRevision || state.Pending is not null) throw new DemoRequestException("MonitorManageChanged");
            var legacyHash = !Managed ? LegacyHash : null;
            var legacy = legacyHash is not null;
            var active = state.Status == "Active" || legacy;
            if ((operation == "generate" && active) || (operation is "rotate" or "revoke" && !active) || (operation == "adopt" && !legacy)) throw new DemoRequestException("MonitorManageDenied");
            var actor = await authorize(ct); // Fresh admin proof is consumed before a mutation.
            var now = _clock.GetUtcNow(); string? issued = null;
            state.Revision++; state.KeyId = Guid.NewGuid().ToString("D");
            if (operation == "revoke") { state.Hash = ""; state.Status = "Revoked"; }
            else if (operation == "adopt") { state.Hash = legacyHash!; state.Status = "Active"; }
            else { issued = DemoIdentity.RandomToken(); state.Hash = DemoIdentity.Hash(issued); state.Status = "Active"; state.CreatedAt = now; if (operation == "rotate") state.RotatedAt = now; }
            state.Pending = new(Guid.NewGuid().ToString("D"), actor, operation switch { "generate" => "MonitorKeyGenerated", "rotate" => "MonitorKeyRotated", "revoke" => "MonitorKeyRevoked", _ => "MonitorKeyAdopted" }, now);
            Save(state); await Flush(state, audit, ct);
            return issued; // Never persisted. A lost response requires explicit rotation.
        } finally { _gate.Release(); }
    }
}
