using System.Net;
using System.Net.Http;
using System.Text;
using LummaSimulator.App.Services;
using LummaSimulator.Configuration;

internal static class MonitorIntegrationChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
    private static async Task Reject<T>(Func<Task> action, string message) where T : Exception
    { bool rejected = false; try { await action(); } catch (T) { rejected = true; } Check(rejected, message); }
    public static async Task RunAsync()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using (var client = new SessionMetadataClient("https://localhost:7243", key, new Handler(request => {
            Check(request.Method == HttpMethod.Head && request.RequestUri!.Query == "" && request.Headers.GetValues("X-Lumma-Monitor-Key").Single() == key,
                "Connection test authenticates via header on existing HEAD route without URL secrets");
            var response = new HttpResponseMessage(HttpStatusCode.NoContent); response.Headers.Add("X-Lumma-Monitor-Protocol", "1"); return response;
        }))) await client.TestAsync(default);
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }) {
            using var client = new SessionMetadataClient("https://localhost:7243", key, new Handler(_ => new(code)));
            await Reject<MonitorKeyRejectedException>(() => client.TestAsync(default), "Invalid or revoked key is distinguished from successful connection");
        }
        using (var client = new SessionMetadataClient("https://localhost:7243", key, new Handler(_ => new(HttpStatusCode.OK))))
            await Reject<InvalidDataException>(() => client.TestAsync(default), "Generic HTTP success cannot impersonate monitor protocol confirmation");
        using (var client = new SessionMetadataClient("https://localhost:7243", key, new Handler(_ => throw new HttpRequestException())))
            await Reject<HttpRequestException>(() => client.TestAsync(default), "Unavailable portal cannot become connected");
        var grant=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
        var scopedRun=Guid.NewGuid().ToString("D");var otherRun=Guid.NewGuid().ToString("D");string? sentGrant=null;
        using(var scoped=new MonitorIntegrationSettings(new DesktopSettings(),createClient:(origin,secret)=>new(origin,secret,new Handler(request=>{
            sentGrant=request.Headers.TryGetValues("X-Lumma-Monitor-Grant",out var values)?values.Single():null;
            return new(HttpStatusCode.NotFound);
        })),environmentKey:()=>key)){
            scoped.SetGrant(scopedRun,grant);
            await Reject<SessionAccessException>(()=>scoped.FetchAsync(scopedRun,default),"Synthetic scoped monitor request handles denied metadata");
            Check(sentGrant==grant,"WPF sends the memory-only capability for its exact portal run");
            await Reject<SessionAccessException>(()=>scoped.FetchAsync(otherRun,default),"Another portal run remains denied");
            Check(sentGrant is null,"WPF never forwards a capability to a different run");
            scoped.ClearGrant();await Reject<SessionAccessException>(()=>scoped.FetchAsync(scopedRun,default),"Cleared grant cannot be reused");
            Check(sentGrant is null,"Cleared capability is absent from headers");
        }
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: DPAPI/ACL require Windows"); return; }
        var root = Path.Combine(Path.GetTempPath(), "LummaMonitorCredentials-" + Guid.NewGuid().ToString("N"));
        var store = new MonitorCredentialStore(root);
        Check(store.Load() is null && !Directory.Exists(root), "Credential read does not enable or create unconfigured integration");
        store.Save("https://localhost:7243", key);
        var bytes = File.ReadAllBytes(Path.Combine(root, "monitor-credential.dpapi"));
        Check(!Encoding.UTF8.GetString(bytes).Contains(key), "Credential file does not contain plaintext key");
        UserPrivateFiles.Verify(root, true); UserPrivateFiles.Verify(Path.Combine(root, "monitor-credential.dpapi"), false);
        Check(new MonitorCredentialStore(root).Load() is { } credential && credential.Key == key && credential.Origin == "https://localhost:7243", "DPAPI CurrentUser credential reload preserves origin binding");
        store.Save("https://localhost:7243", key);
        await Reject<IOException>(() => { store.Save("https://localhost:7243", "invalid"); return Task.CompletedTask; }, "Invalid key cannot replace saved credential");
        Check(store.Load()!.Key == key, "Rejected replacement preserves existing credential");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using (var settings = new MonitorIntegrationSettings(new DesktopSettings(), store, (origin, secret) => new(origin, secret,
            new AsyncHandler(() => { requests++; return pending.Task; })), () => null)) {
            settings.TestCommand.Execute(null); settings.TestCommand.Execute(null);
            Check(requests == 1 && settings.IsBusy, "Repeated pairing tests cannot issue concurrent authenticated requests");
            var response = new HttpResponseMessage(HttpStatusCode.NoContent); response.Headers.Add("X-Lumma-Monitor-Protocol", "1"); pending.SetResult(response);
            for (var i = 0; settings.IsBusy && i < 200; i++) await Task.Delay(10);
            await settings.StopAsync();
            Check(settings.Status == "Connected; monitor key verified" && settings.LastSuccess != "—", "WPF becomes connected only after authenticated protocol confirmation");
        }
        using (var settings = new MonitorIntegrationSettings(new DesktopSettings(), store, (_, _) => throw new InvalidOperationException("No transport expected"), () => "invalid"))
            Check(!settings.IsConfigured && !settings.CanEdit, "Invalid explicit environment override never falls back to stored DPAPI credential");
        store.Remove(); store.Remove();
        Check(store.Load() is null, "Credential removal is idempotent and removes only synthetic credential");
        // Retain the private, empty test directory; never clean unrelated runtime material.
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
    private sealed class AsyncHandler(Func<Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply();
    }
}
