using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace LummaSimulator.Helper;

public sealed record HelperUpdateJournal(string Transaction, string Phase, string Version, string Stage, string Backup);

[SupportedOSPlatform("windows")]
public sealed class HelperUpdateTransaction
{
    private readonly string _root;
    public string Current => Path.Combine(_root, "current");
    private string JournalPath => Path.Combine(_root, "helper-update-journal.json");
    public HelperUpdateTransaction(string root) { _root = Path.GetFullPath(root); HelperUserStore.RejectReparse(_root); }
    private void Save(HelperUpdateJournal journal) => HelperUserStore.Atomic(JournalPath, JsonSerializer.SerializeToUtf8Bytes(journal, HelperProtocol.Json));
    private string Child(string name) {
        if (Path.GetFileName(name) != name || !HelperReleaseVerifier.SafePath(name)) throw new InvalidDataException();
        var path = Path.Combine(_root, name); HelperUserStore.RejectReparse(path); return path;
    }
    public HelperUpdateJournal? Load() => File.Exists(JournalPath) ? JsonSerializer.Deserialize<HelperUpdateJournal>(File.ReadAllBytes(JournalPath), HelperProtocol.Json) ?? throw new InvalidDataException() : null;
    public void Recover() {
        var journal = Load(); if (journal is null || journal.Phase is "Committed" or "RolledBack") return;
        var backup = Child(journal.Backup); Child(journal.Stage);
        if (Directory.Exists(backup)) {
            if (Directory.Exists(Current)) Directory.Move(Current, Child("failed-" + journal.Transaction));
            Directory.Move(backup, Current);
        }
        HelperUserStore.Atomic(Path.Combine(_root, "failed-version.txt"), System.Text.Encoding.UTF8.GetBytes(journal.Version));
        // Keep all interrupted staging/evidence; do not recursively delete an installation.
        Save(journal with { Phase = "RolledBack" });
    }
    public HelperUpdateJournal Prepare(string package, HelperRelease release, SignedHelperRelease envelope) {
        Recover(); Directory.CreateDirectory(_root);
        var id = Guid.NewGuid().ToString("N"); var journal = new HelperUpdateJournal(id, "Preparing", release.Version, "stage-" + id, "previous-" + id);
        var stage = Child(journal.Stage); Save(journal); Directory.CreateDirectory(stage);
        using var zip = ZipFile.OpenRead(package); var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries) {
            if (!HelperReleaseVerifier.SafePath(entry.FullName) || !release.Files.TryGetValue(entry.FullName, out var expected) || !actual.Add(entry.FullName)
                || entry.Length is < 1 or > 268435456 || (expanded += entry.Length) > 1073741824 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Unsafe package entry.");
            var destination = Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = entry.Open()) using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough)) { input.CopyTo(output); output.Flush(true); }
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(destination)), Convert.FromHexString(expected))) throw new CryptographicException("Extracted file hash mismatch.");
        }
        if (actual.Count != release.Files.Count) throw new InvalidDataException("Package files missing.");
        HelperUserStore.Atomic(Path.Combine(stage, "installed-release.json"), JsonSerializer.SerializeToUtf8Bytes(envelope, HelperProtocol.Json));
        Save(journal with { Phase = "Prepared" }); return journal with { Phase = "Prepared" };
    }
    public void Activate(HelperUpdateJournal journal) {
        if (Load() != journal || journal.Phase != "Prepared") throw new InvalidDataException();
        // Record intent before every move, including the power-loss window between directory renames.
        Save(journal with { Phase = "Activating" });
        if (Directory.Exists(Current)) Directory.Move(Current, Child(journal.Backup));
        Directory.Move(Child(journal.Stage), Current); Save(journal with { Phase = "AwaitingHealth" });
    }
    public void Commit(HelperUpdateJournal journal) {
        var loaded = Load(); if (loaded?.Transaction != journal.Transaction || loaded.Phase != "AwaitingHealth") throw new InvalidDataException();
        Save(loaded with { Phase = "Committed" });
        HelperUserStore.Atomic(Path.Combine(_root, "highest-version.txt"), System.Text.Encoding.UTF8.GetBytes(journal.Version));
    }
    public Version? HighestVersion() {
        var path = Path.Combine(_root, "highest-version.txt"); return File.Exists(path) ? Version.Parse(File.ReadAllText(path)) : null;
    }
}
