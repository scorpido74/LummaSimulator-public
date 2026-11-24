using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LummaSimulator.Helper;

// Fixed, signed helper payload only; no remote commands or arbitrary executable arguments.
try {
    if (args.Length == 4 && args[0] == "--sign") {
        var folder = Path.GetFullPath(args[1]); var thumbprint = args[2]; var output = Path.GetFullPath(args[3]);
        using var certificateStore = new X509Store(StoreName.My, StoreLocation.CurrentUser); certificateStore.Open(OpenFlags.ReadOnly);
        using var certificate = certificateStore.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, true).OfType<X509Certificate2>().Single();
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("RSA signing key required.");
        Directory.CreateDirectory(output); var zip = Path.Combine(output, "helper-package.zip");
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(folder, p).Replace('\\', '/'), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        if (files.Keys.Any(p => !HelperReleaseVerifier.SafePath(p) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unexpected release configuration.");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var file in files.Keys) System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)), file);
        var hash = SHA256.HashData(File.ReadAllBytes(zip));
        var release = new HelperRelease(HelperProtocol.HelperVersion, [1], new FileInfo(zip).Length, Convert.ToHexString(hash), "/lumma-helper/updates/v1/package.zip",
            Convert.ToBase64String(rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), files);
        var payload = JsonSerializer.SerializeToUtf8Bytes(release, HelperProtocol.Json);
        var envelope = new SignedHelperRelease(1, Convert.ToBase64String(payload), Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), Convert.ToBase64String(certificate.RawData));
        File.WriteAllBytes(Path.Combine(output, "helper-release.json"), JsonSerializer.SerializeToUtf8Bytes(envelope, HelperProtocol.Json));
        Console.WriteLine("SIGNED_HELPER_RELEASE=True");
    }
    else if (args.Length == 1 && args[0] == "--launch") {
        var store = new HelperUserStore(); store.EnsureRoot();
        using var lockFile = new FileStream(Path.Combine(store.Root, "helper-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var install = Path.Combine(store.Root, "install"); new HelperUpdateTransaction(install).Recover(); using var child = StartCurrent(install);
    }
    else if (args.Length == 2 && args[0] is "--apply" or "--recover") {
        var store = new HelperUserStore(); store.EnsureRoot(); var install = Path.Combine(store.Root, "install");
        using var lockFile = new FileStream(Path.Combine(store.Root, "helper-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var transaction = new HelperUpdateTransaction(install);
        if (args[0] == "--recover") { transaction.Recover(); StartCurrent(install); }
        else {
            var requestPath = Path.GetFullPath(args[1]); HelperUserStore.RejectReparse(requestPath);
            if (Path.GetDirectoryName(requestPath) != Path.Combine(store.Root, "updates")) throw new InvalidDataException();
            var request = JsonSerializer.Deserialize<HelperUpdateRequest>(File.ReadAllBytes(requestPath), HelperProtocol.Json) ?? throw new InvalidDataException();
            if (Path.GetFileName(request.Package) != request.Package) throw new InvalidDataException();
            try { using var parent = Process.GetProcessById(request.ParentPid); if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks) throw new InvalidDataException(); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (ArgumentException) { }
            var profile = store.LoadProfile(); var token = store.LoadPairing() ?? throw new InvalidDataException();
            using var receiver = new AppReceiverClient(profile.AppOrigin, profile.CertificateSha256, token);
            if ((await receiver.InfoAsync(default)).DemoActive) throw new InvalidOperationException("Demo remains active.");
            var verifier = new HelperReleaseVerifier(profile.PublisherSha256);
            var installedVersion = Version.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(transaction.Current, "LummaSimulator.PresenterHelper.dll")).FileVersion ?? throw new InvalidDataException());
            var release = verifier.Verify(request.Envelope, installedVersion, transaction.HighestVersion());
            var package = Path.Combine(store.Root, "updates", request.Package); verifier.VerifyPackage(request.Envelope, release, package);
            var journal = transaction.Prepare(package, release, request.Envelope);
            Process? child = null; bool held = false;
            try {
                if ((await receiver.InfoAsync(default)).DemoActive) throw new InvalidOperationException("Demo became active.");
                held = (await receiver.SendAsync<LeaseReply>(HttpMethod.Post, "/lumma-helper/v1/update-lease", new { }, default)).Granted;
                if (!held) throw new InvalidOperationException("Demo update lease unavailable.");
                transaction.Activate(journal);
                var health = Guid.NewGuid().ToString("N");
                child = StartCurrent(install, health);
                var healthFile = Path.Combine(install, "health-" + health + ".json"); var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                while (!File.Exists(healthFile) && !child.HasExited && DateTimeOffset.UtcNow < deadline) await Task.Delay(200);
                if (!File.Exists(healthFile) || File.ReadAllText(healthFile) != release.Version || child.HasExited) throw new IOException("Updated helper did not pass startup check.");
                transaction.Commit(journal);
            }
            catch {
                if (child is not null && !child.HasExited) { child.CloseMainWindow(); try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { child.Kill(); await child.WaitForExitAsync(); } }
                transaction.Recover(); throw;
            }
            finally {
                child?.Dispose();
                if (held) { try { await receiver.SendAsync<LeaseReply>(HttpMethod.Post, "/lumma-helper/v1/update-release", new { }, default); } catch { } }
            }
        }
    } else throw new ArgumentException("Use the installed helper or release tooling.");
    Environment.ExitCode = 0;
}
catch (Exception error) when (error is not OutOfMemoryException) {
    Console.Error.WriteLine("Helper update failed; retain installation and recovery journal. No credential values are logged.");
    if (args.Length == 2 && args[0] == "--apply") {
        try { var store = new HelperUserStore(); StartCurrent(Path.Combine(store.Root, "install")); } catch { }
    }
    Environment.ExitCode = 1;
}

static Process StartCurrent(string install, string? health = null) {
    var current = Path.Combine(install, "current");
    var start = new ProcessStartInfo(Path.Combine(current, "LummaSimulator.PresenterHelper.exe")) { UseShellExecute = false, WorkingDirectory = current };
    if (health is not null) { start.ArgumentList.Add("--health"); start.ArgumentList.Add(health); }
    return Process.Start(start) ?? throw new IOException();
}
internal sealed record LeaseReply(bool Granted);
