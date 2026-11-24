using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

internal static class TelemetryChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task WaitAsync(MainViewModel vm)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (vm.IsRunning) await Task.Delay(20, timeout.Token);
    }

    public static async Task RunAsync()
    {
        var service = new TelemetryService(Guid.NewGuid());
        var logger = new JsonLogService();
        var vm = new MainViewModel(new SimulationService(), logger, service) { IsLiveTelemetry = true };
        vm.StartCommand.Execute(null);
        await WaitAsync(vm);
        Check(vm.Status == "Completed", "Live telemetry completion");
        Check(Guid.TryParse(vm.DemoRunId, out _), "DemoRunId is a GUID");
        var firstRunId = vm.DemoRunId;
        Check(vm.Entries.All(e => e.DemoRunId == firstRunId && e.Mode == "LIVE TELEMETRY"), "Run correlation on every entry");
        var start = vm.Entries.Single(e => e.Activity == "ProcessStart" && e.Outcome == "Succeeded");
        var exit = vm.Entries.Single(e => e.Activity == "ProcessExit");
        var startData = JsonSerializer.SerializeToElement(start.Details);
        var exitData = JsonSerializer.SerializeToElement(exit.Details);
        Check(startData.GetProperty("pid").GetInt32() > 0 && exitData.GetProperty("pid").GetInt32() == startData.GetProperty("pid").GetInt32(), "Actual process PID");
        Check(exitData.GetProperty("endTime").GetDateTimeOffset() >= startData.GetProperty("startTime").GetDateTimeOffset(), "Actual process times");
        Check(vm.Entries.Any(e => e.Activity == "HttpPost" && e.Outcome == "Skipped"), "HTTP disabled by default");
        var collectedPath = Path.Combine(service.DemoRoot, firstRunId, "collected-data.json");
        using (var collected = JsonDocument.Parse(await File.ReadAllTextAsync(collectedPath)))
        {
            Check(collected.RootElement.GetProperty("Password").GetString() == "FAKE-PASSWORD-ONLY", "Dummy records only");
        }
        var logPath = vm.LogStatus["Saved: ".Length..];
        using (var log = JsonDocument.Parse(await File.ReadAllTextAsync(logPath)))
        {
            Check(!log.RootElement.GetProperty("safeSimulation").GetBoolean(), "Live log cannot be mistaken for visual simulation");
            Check(!log.RootElement.GetProperty("edrDetectionConfirmed").GetBoolean(), "No EDR detection claim");
            Check(log.RootElement.GetProperty("demoRunId").GetString() == firstRunId, "JSON correlation");
        }

        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            vm.LocalPort = port.ToString(); vm.EnableLocalHttp = true;
            await vm.LocalHttpUpdate;
            Check(vm.LocalHttpReady, "Managed HTTP receiver is ready before simulation");
            vm.StartCommand.Execute(null);
            await WaitAsync(vm);
            Check(vm.Status == "Completed" && vm.DemoRunId != firstRunId, "Live HTTP completion and unique ID");
            Check(vm.Entries.Any(e => e.Activity == "HttpResponse" && e.Outcome == "Received"), "Actual identified loopback receiver accepts strictly synthetic payload and correlation");
            vm.EnableLocalHttp = false; await vm.LocalHttpUpdate;
        }

        var events = new List<TelemetryActivity>();
        var run = new TelemetryRun(Guid.NewGuid().ToString("D"), true, 8080);
        await service.ExecuteAsync(3, run, events.Add, CancellationToken.None);
        await service.ExecuteAsync(4, run, events.Add, CancellationToken.None);
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            var redirectRun = new TelemetryRun(run.DemoRunId, true, ((IPEndPoint)listener.LocalEndpoint).Port) { CollectedData = run.CollectedData };
            var received = ReceiveOnceAsync(listener, "302 Found", $"Location: http://127.0.0.1:{redirectRun.Port}/redirected\r\n");
            var refused = false;
            try { await service.ExecuteAsync(6, redirectRun, events.Add, CancellationToken.None); }
            catch (HttpRequestException) { refused = true; }
            await received;
            Check(refused && !listener.Pending(), "Redirect is rejected without a follow-up connection");
        }

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var refused = false;
            try { await service.ExecuteAsync(3, new(Guid.NewGuid().ToString("D"), false, 8080), events.Add, cancelled.Token); }
            catch (OperationCanceledException) { refused = true; }
            Check(refused, "Live cancellation before file creation");
        }
        vm.EnableLocalHttp = false;
        await vm.LocalHttpUpdate;
        vm.StartCommand.Execute(null);
        await vm.StopAndWaitAsync();
        Check(vm.Status == "Stopped", "Live run cancellation and child reaping");

        // Cleanup must preserve directories containing unexpected files, even inside its owned scope.
        var sentinelPath = Path.Combine(service.DemoRoot, run.DemoRunId, "retain-me.txt");
        await File.WriteAllTextAsync(sentinelPath, "verification sentinel");
        await service.CleanupAsync(events.Add, CancellationToken.None);
        Check(File.Exists(sentinelPath) && File.Exists(Path.Combine(service.DemoRoot, run.DemoRunId, "synthetic-browser.json")), "Unknown contents are retained");
        Check(!File.Exists(collectedPath) && File.Exists(logPath), "Cleanup deletes synthetic files and retains session logs");
        // Remove only our known sentinel so the service can clean the remainder of this test scope.
        File.Delete(sentinelPath);
        await service.CleanupAsync(events.Add, CancellationToken.None);
        Check(!Directory.EnumerateFileSystemEntries(service.DemoRoot).Any(), "Owned test artifacts cleaned");

        vm.CleanupCommand.Execute(null);
        await WaitAsync(vm);
        Check(vm.Status == "Cleanup completed" && vm.Entries.Any(e => e.Activity == "Cleanup"), "Cleanup command and audit log");
        await vm.ShutdownAsync();
        Console.WriteLine("PASS: LIVE process/PID/times, own dummy files, correlation, actual loopback HTTP, redirect refusal, cancellation, bounded cleanup and no detection claims.");
        Console.WriteLine($"Telemetry verification scope: {service.DemoRoot}");
    }

    private static async Task<string> ReceiveOnceAsync(TcpListener listener, string status, string extraHeaders = "")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using (var healthClient = await listener.AcceptTcpClientAsync(timeout.Token)) {
            await using var healthStream = healthClient.GetStream(); var healthBuffer = new byte[4096];
            var count = await healthStream.ReadAsync(healthBuffer, timeout.Token);
            var line = Encoding.ASCII.GetString(healthBuffer, 0, count).Split(' ')[1];
            var nonce = line.Split("nonce=")[1];
            var identity = new LummaSimulator.LocalTesting.LocalServerIdentity(LummaSimulator.LocalTesting.LocalTestServerProtocol.Product, 1, nonce, Guid.NewGuid().ToString("N"), Environment.ProcessId, ((IPEndPoint)listener.LocalEndpoint).Port);
            var body = JsonSerializer.SerializeToUtf8Bytes(identity);
            var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nX-Lumma-TestServer: {LummaSimulator.LocalTesting.LocalTestServerProtocol.Stamp(identity)}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await healthStream.WriteAsync(headers, timeout.Token); await healthStream.WriteAsync(body, timeout.Token);
        }
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = client.GetStream();
        var bytes = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, timeout.Token);
            if (count == 0) throw new IOException("Incomplete local HTTP request.");
            bytes.AddRange(buffer.Take(count));
            if (bytes.Count > 32768) throw new IOException("Unexpected HTTP request size.");
            var text = Encoding.UTF8.GetString(bytes.ToArray());
            var boundary = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (boundary < 0) continue;
            var lengthHeader = text[..boundary].Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var length = int.Parse(lengthHeader.Split(':')[1].Trim());
            if (bytes.Count < Encoding.UTF8.GetByteCount(text[..(boundary + 4)]) + length) continue;
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{extraHeaders}Content-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, timeout.Token);
            return text;
        }
    }
}
