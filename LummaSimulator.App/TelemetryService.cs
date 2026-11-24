using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LummaSimulator.LocalTesting;

namespace LummaSimulator.App.Services;

public sealed record TelemetryActivity(string Activity, string Outcome, string Message, object? Details = null);
public sealed record DummyBrowserData(string DemoRunId, string Browser, string Profile, string Url,
    string Username, string Password, string Cookie, string Token);

public sealed class TelemetryRun(string demoRunId, bool enableHttp, int port)
{
    public string DemoRunId { get; } = Guid.ParseExact(demoRunId, "D").ToString("D");
    public bool EnableHttp { get; } = enableHttp;
    public int Port { get; } = port is >= 1 and <= 65535 ? port : throw new ArgumentOutOfRangeException(nameof(port));
    internal DummyBrowserData? CollectedData { get; set; }
}

public interface ITelemetryService
{
    Task ExecuteAsync(int phase, TelemetryRun run, Action<TelemetryActivity> report, CancellationToken token);
    Task CleanupAsync(Action<TelemetryActivity> report, CancellationToken token);
}

/// <summary>Only runs whoami, handles owned synthetic files, and optionally posts to IPv4 loopback.</summary>
public sealed class TelemetryService : ITelemetryService
{
    private readonly string _appRoot = LummaSimulator.Configuration.RuntimePaths.DataRoot(DesktopSettings.Load().DataDirectory);
    public string DemoRoot { get; }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] OwnedFiles = ["synthetic-browser.json", "collected-data.json", "demo-owner.txt"];

    public TelemetryService(Guid? verificationScope = null)
    {
        var root = Path.Combine(_appRoot, "DemoRuns");
        // Verification never cleans unrelated demo runs and cannot select an arbitrary filesystem root.
        DemoRoot = verificationScope is { } scope ? Path.Combine(root, $"Verification-{scope:D}") : root;
    }

    private static DummyBrowserData CreateDummy(string id) => new(id, "Fictional Browser", "Demo profile",
        "https://example.invalid/demo", "demo@example.invalid", "FAKE-PASSWORD-ONLY", "FAKE-COOKIE-ONLY", "FAKE-TOKEN-ONLY");

    public async Task ExecuteAsync(int phase, TelemetryRun run, Action<TelemetryActivity> report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(DemoRoot, run.DemoRunId);
        switch (phase)
        {
            case 1:
                await RunProcessAsync(run, report, token);
                break;
            case 2:
                report(new("SystemDiscovery", "Simulated", "SIMULATED: fixed inventory TRAINING-PC / demo-user; no host inventory read."));
                await Task.Delay(350, token);
                break;
            case 3:
                EnsureSafePath(directory);
                Directory.CreateDirectory(directory);
                report(new("DirectoryCreate", "Succeeded", "ACTUAL: created owned demo run directory.", new { path = directory }));
                report(new("FileCreate", "Attempted", "ACTUAL ATTEMPT: creating demo ownership marker.", new { path = Path.Combine(directory, "demo-owner.txt") }));
                // Finish the tiny ownership marker so cancellation cannot leave an unidentifiable run.
                await WriteNewAsync(Path.Combine(directory, "demo-owner.txt"), run.DemoRunId, CancellationToken.None);
                report(new("FileCreate", "Succeeded", "ACTUAL: created demo ownership marker.", new { path = Path.Combine(directory, "demo-owner.txt") }));
                token.ThrowIfCancellationRequested();
                var dummy = CreateDummy(run.DemoRunId);
                var browserPath = Path.Combine(directory, "synthetic-browser.json");
                report(new("FileCreate", "Attempted", "ACTUAL ATTEMPT: writing synthetic browser JSON.", new { path = browserPath }));
                await WriteNewAsync(browserPath, JsonSerializer.Serialize(dummy, JsonOptions), token);
                report(new("FileCreate", "Succeeded", "ACTUAL: wrote synthetic browser JSON in the owned demo directory.", new { path = browserPath, run.DemoRunId }));
                break;
            case 4:
                var source = Path.Combine(directory, "synthetic-browser.json");
                EnsureSafePath(source);
                report(new("FileRead", "Attempted", "ACTUAL ATTEMPT: reading this run's synthetic browser JSON.", new { path = source }));
                var content = await File.ReadAllTextAsync(source, token);
                var collected = JsonSerializer.Deserialize<DummyBrowserData>(content)
                    ?? throw new InvalidDataException("Synthetic browser JSON is empty.");
                if (collected != CreateDummy(run.DemoRunId))
                    throw new InvalidDataException("Synthetic data was modified or does not belong to this DemoRunId.");
                run.CollectedData = collected;
                report(new("FileRead", "Succeeded", "ACTUAL: read only the synthetic browser JSON created by this run.", new { path = source }));
                break;
            case 5:
                var output = Path.Combine(directory, "collected-data.json");
                var data = run.CollectedData ?? throw new InvalidOperationException("No synthetic data collected.");
                report(new("FileCreate", "Attempted", "ACTUAL ATTEMPT: writing collected dummy JSON.", new { path = output }));
                await WriteNewAsync(output, JsonSerializer.Serialize(data, JsonOptions), token);
                report(new("FileCreate", "Succeeded", "ACTUAL: wrote collected-data.json containing dummy data only.", new { path = output }));
                break;
            case 6:
                if (!run.EnableHttp)
                {
                    report(new("HttpPost", "Skipped", "SIMULATED: delivery only; local HTTP is disabled. No connection made."));
                    await Task.Delay(350, token);
                    break;
                }
                await SendLoopbackAsync(run, report, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase));
        }
    }

    private static async Task RunProcessAsync(TelemetryRun run, Action<TelemetryActivity> report, CancellationToken token)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        // A visible console is allowed; output (the actual account name) is deliberately not captured.
        startInfo.Environment["LUMMA_DEMO_RUN_ID"] = run.DemoRunId;
        report(new("ProcessStart", "Attempted", "ACTUAL ATTEMPT: starting visible whoami.exe; output will not be collected.", new { executable, run.DemoRunId }));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("whoami.exe did not start.");
        var pid = process.Id;
        var startTime = new DateTimeOffset(process.StartTime.ToUniversalTime());
        report(new("ProcessStart", "Succeeded", $"ACTUAL: whoami.exe started, PID {pid}.", new { processName = "whoami.exe", pid, startTime, run.DemoRunId }));
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            // Reap this child and record its end time even when the run is cancelled.
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (process.HasExited)
            {
                var endTime = new DateTimeOffset(process.ExitTime.ToUniversalTime());
                report(new("ProcessExit", "Succeeded", $"ACTUAL: whoami.exe exited, PID {pid}, code {process.ExitCode}.",
                    new { processName = "whoami.exe", pid, startTime, endTime, exitCode = process.ExitCode, run.DemoRunId }));
            }
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"whoami.exe returned {process.ExitCode}.");
    }

    private static async Task SendLoopbackAsync(TelemetryRun run, Action<TelemetryActivity> report, CancellationToken token)
    {
        var data = run.CollectedData ?? throw new InvalidOperationException("No synthetic data collected.");
        var endpoint = new Uri($"http://127.0.0.1:{run.Port}/lumma-demo");
        // No proxies, redirects, cookies, credentials or DNS names: requests stay on IPv4 loopback.
        using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Lumma-DemoRunId", run.DemoRunId);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        report(new("HttpPost", "Attempted", "ACTUAL ATTEMPT: POST dummy JSON to local test server only.",
            new { endpoint = endpoint.ToString(), payloadBytes = Encoding.UTF8.GetByteCount(json), run.DemoRunId }));
        try
        {
            var identity = await LocalTestServerProtocol.ProbeAsync(client, run.Port, token)
                ?? throw new HttpRequestException("Local endpoint is not an identified Lumma test server.");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            report(new("HttpResponse", "Received", $"ACTUAL: local HTTP status {(int)response.StatusCode}; redirects are not followed.",
                new { endpoint = endpoint.ToString(), statusCode = (int)response.StatusCode }));
            response.EnsureSuccessStatusCode();
            if (!response.Headers.TryGetValues(LocalTestServerProtocol.IdentityHeader, out var identityHeaders)
                || identityHeaders.SingleOrDefault() != LocalTestServerProtocol.Stamp(identity))
                throw new HttpRequestException("Local test server identity changed during POST.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            report(new("HttpPost", token.IsCancellationRequested ? "Cancelled" : "Failed", $"Local HTTP did not complete successfully: {exception.Message}"));
            throw;
        }
    }

    private async Task WriteNewAsync(string path, string text, CancellationToken token)
    {
        EnsureSafePath(path);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, token);
    }

    // Resolve and validate every component before file access. Never follow demo junctions or symlinks.
    private void EnsureSafePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(DemoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !fullPath.Equals(DemoRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Path lies outside the dedicated demo root.");
        for (var component = fullPath; component is not null; component = Path.GetDirectoryName(component))
        {
            if ((File.Exists(component) || Directory.Exists(component))
                && File.GetAttributes(component).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Reparse point refused: {component}");
            if (component.Equals(_appRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    public async Task CleanupAsync(Action<TelemetryActivity> report, CancellationToken token)
    {
        EnsureSafePath(DemoRoot);
        if (!Directory.Exists(DemoRoot))
        {
            report(new("Cleanup", "Skipped", "No owned demo directory exists; logs retained."));
            return;
        }
        foreach (var directory in Directory.EnumerateDirectories(DemoRoot))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "D", out var id)) continue;
            EnsureSafePath(directory);
            var marker = Path.Combine(directory, "demo-owner.txt");
            EnsureSafePath(marker);
            if (!File.Exists(marker) || await File.ReadAllTextAsync(marker, token) != id.ToString("D"))
            {
                report(new("Cleanup", "Skipped", "Unrecognized demo directory retained.", new { path = directory }));
                continue;
            }
            var files = Directory.GetFileSystemEntries(directory);
            if (files.Any(file => !OwnedFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal) || Directory.Exists(file)))
            {
                report(new("Cleanup", "Skipped", "Directory with unknown contents retained.", new { path = directory }));
                continue;
            }
            foreach (var file in files) EnsureSafePath(file);
            // Delete only these known files; no recursive deletion or external paths.
            foreach (var file in files.Where(file => Path.GetFileName(file) != "demo-owner.txt"))
            {
                token.ThrowIfCancellationRequested();
                File.Delete(file);
                report(new("FileDelete", "Succeeded", "ACTUAL: removed owned synthetic file.", new { path = file, demoRunId = id }));
            }
            token.ThrowIfCancellationRequested();
            File.Delete(marker);
            report(new("FileDelete", "Succeeded", "ACTUAL: removed demo ownership marker.", new { path = marker, demoRunId = id }));
            Directory.Delete(directory, recursive: false);
            report(new("DirectoryDelete", "Succeeded", "ACTUAL: removed empty demo run directory; session logs retained.", new { path = directory, demoRunId = id }));
        }
        report(new("Cleanup", "Completed", "Owned demo cleanup complete. JSON logs retained."));
    }
}
