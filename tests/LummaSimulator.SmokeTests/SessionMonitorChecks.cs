using System.Net;
using System.Net.Http;
using System.Text.Json;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

internal static class SessionMonitorChecks
{
    public static async Task RunAsync()
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
        static async Task Wait(SessionMonitorViewModel monitor) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (monitor.IsBusy) await Task.Delay(10, timeout.Token); }
        var root = Path.Combine(Path.GetTempPath(), "LummaSessionMonitorTests", Guid.NewGuid().ToString("N"));
        var run = Guid.NewGuid().ToString("D");
        var fake = new FakeSessionClient(run);
        using var monitor = new SessionMonitorViewModel(fake, root) { PortalDemoRunId = run };
        Check(monitor.State == MonitorSessionState.NotEvaluated, "Monitor begins without an authentication claim");
        monitor.RefreshCommand.Execute(null); await Wait(monitor);
        Check(monitor.State == MonitorSessionState.NoAuthenticatedSession && !monitor.SimulateExposureCommand.CanExecute(null), "Visitor ownership without WebAuthn is not active authentication");
        fake.Metadata = fake.Metadata with { State = "ActiveWebAuthnSession", AuthenticationMethod = "WebAuthn", SessionActive = true, VerifiedAt = DateTimeOffset.UtcNow };
        monitor.RefreshCommand.Execute(null); await Wait(monitor);
        Check(monitor.State == MonitorSessionState.ActiveWebAuthnSession && monitor.Techniques.All(t => t.Activity == ActivityState.NotEvaluated), "Active session does not automatically simulate theft or mark a technique executed");
        monitor.SimulateExposureCommand.Execute(null); await Wait(monitor);
        Check(monitor.State == MonitorSessionState.FictitiousExposure && monitor.Techniques[0].TechniqueActivity == ActivityState.Simulated, "Exposure is explicitly fictitious");
        monitor.SimulateReplayCommand.Execute(null); await Wait(monitor);
        Check(monitor.ReplayText.Contains("fictief object") && monitor.Techniques[1].TechniqueActivity == ActivityState.Simulated && fake.Calls == 4, "Replay uses only sandbox memory and rechecks metadata");
        Check(monitor.Techniques.All(t => t.Assessments.All(a => a.Detection == DetectionState.NotEvaluated && a.Observation == ObservationState.NotEvaluated)), "Metadata is never endpoint-product observation or detection");
        var audit = await File.ReadAllTextAsync(Path.Combine(root, "session-monitor-" + run + ".json"));
        Check(!audit.Contains("PrivateKey") && !audit.Contains("Cookie") && !audit.Contains("SandboxId") && audit.Contains("\"sessionTokenExposed\": false"), "Monitor audit contains no tokens or reusable fictitious credentials");
        fake.Deny = true;
        monitor.RefreshCommand.Execute(null); await Wait(monitor);
        Check(monitor.State == MonitorSessionState.InvalidExpired && !monitor.SimulateReplayCommand.CanExecute(null), "Revocation clears active state and prevents further replay");
        fake.Deny = false;
        fake.Metadata = fake.Metadata with { State = "InvalidExpired", SessionActive = false };
        monitor.RefreshCommand.Execute(null); await Wait(monitor);
        Check(monitor.State == MonitorSessionState.InvalidExpired, "Explicit expired server status is distinct from no authenticated session");
        fake.Metadata = fake.Metadata with { State = "ActiveWebAuthnSession", SessionActive = true };
        monitor.RefreshCommand.Execute(null); await Wait(monitor);
        fake.Delay = true;
        monitor.RefreshCommand.Execute(null);
        await monitor.StopAndWaitAsync();
        Check(monitor.State == MonitorSessionState.NotEvaluated && !monitor.IsBusy, "Cancellation restores monitor controls without a stale active claim");

        var key = new string('a', 43);
        var payload = new SessionMetadata(run, "localhost", true, "WebAuthn", true, false, "ActiveWebAuthnSession", DateTimeOffset.UtcNow);
        var transport = new FakeTransport(JsonSerializer.Serialize(payload));
        using (var client = new SessionMetadataClient("https://localhost:7243", key, transport))
        {
            var result = await client.FetchAsync(run, default);
            Check(result.SessionActive && transport.Method == HttpMethod.Get && !transport.HadCookie && transport.HadIntegrationKey, "Metadata client sends only GET and a separate integration credential, never visitor cookies");
        }
        foreach (var badOrigin in new[] { "http://localhost:7243", "https://example.invalid", "https://localhost:7243/path", "https://localhost:7243?key=secret" })
        {
            var refused = false; try { using var client = new SessionMetadataClient(badOrigin, key); } catch (ArgumentException) { refused = true; }
            Check(refused, "Monitor target origin is strictly allowlisted");
        }
        using (var client = new SessionMetadataClient("https://localhost:7243", key, new FakeTransport(JsonSerializer.Serialize(payload with { SessionTokenExposed = true }))))
        {
            var refused = false; try { await client.FetchAsync(run, default); } catch (InvalidDataException) { refused = true; }
            Check(refused, "Client rejects an endpoint claiming to expose a real token");
        }
        using (var client = new SessionMetadataClient("https://localhost:7243", key, new FakeTransport("{}", HttpStatusCode.Redirect)))
        {
            var refused = false; try { await client.FetchAsync(run, default); } catch (HttpRequestException) { refused = true; }
            Check(refused, "Metadata client rejects redirects");
        }
    }
    private sealed class FakeSessionClient(string run) : ISessionMetadataClient
    {
        public SessionMetadata Metadata { get; set; } = new(run, "localhost", true, "None", false, false, "NoAuthenticatedSession", null);
        public bool Deny { get; set; }
        public bool Delay { get; set; }
        public int Calls { get; private set; }
        public bool IsConfigured => true;
        public string Origin => "https://localhost:7243";
        public async Task<SessionMetadata> FetchAsync(string id, CancellationToken token)
        {
            Calls++;
            if (Delay) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (Deny) throw new SessionAccessException();
            if (id != Metadata.DemoRunId) throw new InvalidOperationException("Wrong demo queried");
            return Metadata;
        }
        public void Dispose() { }
    }
    private sealed class FakeTransport(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public bool HadCookie { get; private set; }
        public bool HadIntegrationKey { get; private set; }
        public HttpMethod? Method { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            HadCookie = request.Headers.Contains("Cookie"); HadIntegrationKey = request.Headers.Contains("X-Lumma-Monitor-Key"); Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
