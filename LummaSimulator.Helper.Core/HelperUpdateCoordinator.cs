using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace LummaSimulator.Helper;

public sealed record HelperUpdateRequest(string Package, SignedHelperRelease Envelope, int ParentPid, long ParentStartTicks, string CurrentVersion);

[SupportedOSPlatform("windows")]
public sealed class HelperUpdateCoordinator(HelperUserStore store)
{
    private SignedHelperRelease? _envelope;
    private HelperRelease? _release;
    private string? _package;
    private string _status = "Nog niet gecontroleerd.";
    public event Action<string>? StatusChanged;
    public string Status { get => _status; private set { _status = value; StatusChanged?.Invoke(value); } }
    public async Task CheckAsync(AppReceiverClient client, string publisher, CancellationToken ct)
    {
        try {
            store.EnsureRoot();
            using var lease = new FileStream(Path.Combine(store.Root, "helper-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var verifier = new HelperReleaseVerifier(publisher);
            var envelope = await client.SendAsync<SignedHelperRelease>(HttpMethod.Get, "/lumma-helper/updates/v1/manifest", null, ct);
            var transaction = new HelperUpdateTransaction(Path.Combine(store.Root, "install"));
            var release = verifier.Verify(envelope, Version.Parse(HelperProtocol.HelperVersion), transaction.HighestVersion());
            var failed = Path.Combine(store.Root, "install", "failed-version.txt");
            if (File.Exists(failed) && File.ReadAllText(failed) == release.Version) throw new InvalidDataException("Deze versie is eerder teruggedraaid; een nieuwe release is nodig.");
            var downloads = Path.Combine(store.Root, "updates"); Directory.CreateDirectory(downloads);
            var package = Path.Combine(downloads, Guid.NewGuid().ToString("N") + ".zip");
            Status = "Update wordt op de achtergrond gedownload.";
            await client.DownloadAsync(release.UpdatePath, package, release.Size, ct);
            verifier.VerifyPackage(envelope, release, package);
            _envelope = envelope; _release = release; _package = package; Status = "Update " + release.Version + " gecontroleerd; wacht tot de demo inactief is.";
        }
        catch (Exception error) when (error is not OutOfMemoryException) {
            Status = error is System.Security.Cryptography.CryptographicException ? "Update geweigerd: handtekening, hash of vertrouwde uitgever ongeldig."
                : error is InvalidDataException ? "Geen installeerbare update: configuratie, versie of protocol niet geschikt."
                : "Updatecontrole niet beschikbaar. De helper blijft bruikbaar; controleer de verbinding.";
        }
    }
    public async Task<bool> InstallIfIdleAsync(AppReceiverClient client, CancellationToken ct)
    {
        if (_package is null || _release is null || _envelope is null) return false;
        try {
            if ((await client.InfoAsync(ct)).DemoActive) { Status = "Update gereed; installatie wacht tot de demo is beëindigd."; return false; }
            var install = Path.Combine(store.Root, "install");
            if (!string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(install, "current"), StringComparison.OrdinalIgnoreCase)) {
                Status = "Deze helper draait portable. Automatische updates vereisen een afzonderlijk ingerichte beheerde gebruikersinstallatie; die installer is niet bij dit pakket inbegrepen."; return false;
            }
            var runner = Path.Combine(store.Root, "updater-runs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runner);
            foreach (var input in Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(AppContext.BaseDirectory, input); HelperUserStore.RejectReparse(input);
                var output = Path.Combine(runner, relative); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(input, output, false);
            }
            var requestFile = Path.Combine(store.Root, "updates", Guid.NewGuid().ToString("N") + ".json");
            var parent = Process.GetCurrentProcess();
            HelperUserStore.Atomic(requestFile, JsonSerializer.SerializeToUtf8Bytes(new HelperUpdateRequest(Path.GetFileName(_package), _envelope, parent.Id, parent.StartTime.ToUniversalTime().Ticks, HelperProtocol.HelperVersion), HelperProtocol.Json));
            var start = new ProcessStartInfo(Path.Combine(runner, "LummaSimulator.HelperUpdater.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runner };
            start.ArgumentList.Add("--apply"); start.ArgumentList.Add(requestFile);
            using var updater = Process.Start(start) ?? throw new IOException();
            Status = "Gecontroleerde updater gestart; de helper wordt opnieuw geopend."; return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Status = "Update niet geïnstalleerd; huidige versie behouden. Controleer verbinding en gebruikersinstallatie."; return false; }
    }
}
