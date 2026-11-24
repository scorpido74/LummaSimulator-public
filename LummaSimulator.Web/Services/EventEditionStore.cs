using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace LummaSimulator.Web.Services;

public sealed record EditionAction(string DemoRunId, string ActionType, long Sequence, DateTimeOffset OccurredAtUtc);
public sealed record EditionSnapshot(string WpfRunId, string PortalRunId, string EventId, string EventName,
    string Status, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, EditionAction? Action);
public sealed class EditionEvent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public string PresenterId { get; set; } = "";
    public string Status { get; set; } = "Scheduled";
    public List<EditionRun> Runs { get; set; } = [];
}
public sealed class EditionRun
{
    public string PortalRunId { get; set; } = "";
    public string WpfRunId { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string SessionHash { get; set; } = "";
    public DateTimeOffset Expires { get; set; }
    public string Generation { get; set; } = "";
    public EditionAction? Action { get; set; }
}
public sealed class EditionManifest
{
    public int Version { get; set; } = 1;
    public List<EditionEvent> Events { get; set; } = [];
}

// Dedicated new metadata, never a copy of SQLite. No startup read/write or automatic recovery.
public sealed class EventEditionStore(string path, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Lumma.EventEdition.Manifest.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private EditionManifest Read(string file)
    {
        if (new FileInfo(file).Length > 4_000_000) throw new InvalidDataException("Event manifest too large.");
        var value = JsonSerializer.Deserialize<EditionManifest>(_protector.Unprotect(File.ReadAllBytes(file)), Json)
            ?? throw new InvalidDataException("Invalid event manifest.");
        if (value.Version != 1 || value.Events.Count > 100 || value.Events.Select(e => e.Id).Distinct().Count() != value.Events.Count
            || value.Events.Any(e => !Guid.TryParseExact(e.Id, "D", out _) || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 100
                || e.PlannedEnd <= e.PlannedStart || e.Status is not ("Scheduled" or "Active" or "Closed") || e.Runs.Count > 1000)
            || value.Events.Count(e => e.Status == "Active") > 1) throw new InvalidDataException("Invalid event manifest.");
        return value;
    }
    public async Task<T> AccessAsync<T>(bool write, Func<EditionManifest, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // An OS file lock also serializes independent service instances/processes; fail closed on contention.
            await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!File.Exists(path) && File.Exists(path + ".previous")) throw new InvalidDataException("Explicit manifest recovery required.");
            var manifest = File.Exists(path) ? Read(path) : new EditionManifest();
            var result = await action(manifest);
            if (write) Save(manifest);
            return result;
        }
        finally { _gate.Release(); }
    }
    private void Save(EditionManifest manifest)
    {
        var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        if (bytes.Length > 4_000_000) throw new InvalidDataException("Event manifest too large.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        // Leave evidence on failure; never repair or delete data automatically.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".previous");
        else File.Move(temporary, path);
    }
    public async Task RecoverAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var recovered = Read(path + ".previous");
            File.Copy(path + ".previous", path + ".recovery-source-" + Guid.NewGuid().ToString("N"));
            // Recovery never silently re-enables old codes or reuses historical run sequences.
            foreach (var item in recovered.Events) { item.Status = "Closed"; foreach (var run in item.Runs) run.CodeHash = ""; }
            if (File.Exists(path)) File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
            Save(recovered);
        }
        finally { _gate.Release(); }
    }
}
