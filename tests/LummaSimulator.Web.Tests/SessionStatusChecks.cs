using System.Net;
using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Configuration;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

internal static class SessionStatusChecks
{
    public static async Task RunAsync(string root)
    {
        using (var legacy = new DemoStore(Path.Combine(root, "legacy-v03.db")))
        {
            await legacy.ExecuteAsync("""
                CREATE TABLE Demos(Id TEXT PRIMARY KEY,DemoRunId TEXT NOT NULL UNIQUE,Name TEXT NOT NULL UNIQUE,UserHandle BLOB NOT NULL UNIQUE,
                  SessionHash TEXT NOT NULL UNIQUE,Route TEXT NOT NULL,Created INTEGER NOT NULL,LastSeen INTEGER NOT NULL,Successes INTEGER NOT NULL DEFAULT 0,Failures INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE Credentials(Id TEXT PRIMARY KEY,DemoId TEXT NOT NULL REFERENCES Demos(Id) ON DELETE CASCADE,PublicKey BLOB NOT NULL,
                  Counter INTEGER NOT NULL,BackupEligible INTEGER NOT NULL,BackedUp INTEGER NOT NULL);
                INSERT INTO Demos VALUES('legacy-demo',$run,'Visitor-MIGRATE',X'010203','legacy-hash','laptop',0,0,1,0);
                INSERT INTO Credentials VALUES('legacy-credential','legacy-demo',X'01020304',8,0,0);
                """, default, ("$run", Guid.NewGuid().ToString("D")));
            await legacy.InitializeAsync();
            var preserved = (await legacy.AccountsAsync("legacy-hash", default)).Single();
            var credential = (await legacy.CredentialsAsync(preserved.Id, default)).Single();
            TestProgram.Check(preserved.Successes == 1 && preserved.UserHandle.SequenceEqual(new byte[] { 1, 2, 3 }) && preserved.LastAuthenticatedAt is null, "Legacy account survives schema migration without inventing authentication time");
            TestProgram.Check(credential.Id == "legacy-credential" && credential.Counter == 8 && credential.PublicKey.SequenceEqual(new byte[] { 1, 2, 3, 4 }) && credential.Alias == "Passkey 1", "Existing credential key, counter and identity survive migration");
        }
        using var store = new DemoStore(Path.Combine(root, "session-status.db"));
        await store.InitializeAsync();
        var clock = new TestClock();
        var metrics = new DemoMetrics();
        var settings = new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] };
        var key = DemoIdentity.RandomToken();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Monitor:KeyHash"] = DemoIdentity.Hash(key) }).Build();
        var sessions = new SessionStatusService(store, clock, settings, config, metrics);
        var demos = new DemoService(store, new Fido2(settings.ToFidoConfiguration()), clock, NullLogger<DemoService>.Instance, metrics);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        TestProgram.Check(!sessions.Authorize(context), "Metadata API requires a separate integration key");
        context.Request.Headers["X-Lumma-Monitor-Key"] = key;
        TestProgram.Check(sessions.Authorize(context), "Correct local integration key accepted");
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        TestProgram.Check(!sessions.Authorize(context), "Even a correct key cannot query metadata from a non-loopback peer");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var unconfigured = new SessionStatusService(store, clock, settings, new ConfigurationBuilder().Build(), metrics);
        TestProgram.Check(!unconfigured.Authorize(context), "Metadata API fails closed without configuration");
        var demo = await demos.CreateAsync("phone", default);
        TestProgram.Check(await sessions.GetAsync(demo.Account.DemoRunId, default) is null, "No per-demo presenter grant means no metadata");
        await sessions.SetGrantAsync(demo.Account.Id, true, default);
        TestProgram.Check((await sessions.GrantedDemoIdsAsync(default)).Contains(demo.Account.Id), "Saved monitor grant is returned by dashboard grant query");
        var grantExpiry = Convert.ToInt64(await store.ScalarAsync("SELECT Expires FROM MonitorGrants WHERE DemoId=$id", default, ("$id", demo.Account.Id)));
        TestProgram.Check(grantExpiry == clock.GetUtcNow().AddMinutes(29).ToUnixTimeMilliseconds(), "Monitor grant remains bounded to exactly 29 minutes");
        var state = await sessions.GetAsync(demo.Account.DemoRunId, default);
        TestProgram.Check(state is null, "A visitor cookie alone cannot expose session metadata");
        using var authenticator = new SoftwareAuthenticator();
        var options = await demos.OptionsAsync(demo.Token, true, default);
        using (var names = JsonDocument.Parse(options))
        {
            TestProgram.Check(names.RootElement.GetProperty("rp").GetProperty("name").GetString() == WebAuthnSettings.DisplayName
                && names.RootElement.GetProperty("rp").GetProperty("id").GetString() == "localhost", "Visible RP name changes without changing RP ID");
            TestProgram.Check(names.RootElement.GetProperty("user").GetProperty("name").GetString() == demo.Account.Name
                && names.RootElement.GetProperty("user").GetProperty("displayName").GetString() == demo.Account.Name, "Recognizable random visitor display name");
        }
        var registered = await demos.VerifyAsync(demo.Token, true, authenticator.Register(options), default);
        TestProgram.Check(registered.CredentialAlias == "Passkey 1" && await sessions.GetAsync(demo.Account.DemoRunId, default) is null, "Registration names credential but does not authorize monitor metadata");
        var verified = await demos.VerifyAsync(demo.Token, false, authenticator.Authenticate(await demos.OptionsAsync(demo.Token, false, default)), default);
        state = await sessions.GetAsync(demo.Account.DemoRunId, default);
        TestProgram.Check((await sessions.GetAsync(demo.Account.DemoRunId.ToUpperInvariant(), default))?.DemoRunId == demo.Account.DemoRunId, "Copied demo GUID is case-insensitive and canonicalized");
        TestProgram.Check(state is { State: "ActiveWebAuthnSession", SessionActive: true, SessionTokenExposed: false, AuthenticationMethod: "WebAuthn" }
            && state.VerifiedAt?.ToUnixTimeMilliseconds() == verified.VerifiedAt.ToUnixTimeMilliseconds() && verified.CredentialAlias == "Passkey 1", "Only successful WebAuthn verification establishes active state and reliable credential alias");
        var json = JsonSerializer.Serialize(state);
        var managedKeys = new MonitorKeyStore(Path.Combine(root, "grant-key-test.state"), new EphemeralDataProtectionProvider(), config, clock);
        var managedSessions = new SessionStatusService(store, clock, settings, config, metrics, managedKeys);
        TestProgram.Check(managedSessions.Authorize(context) && await managedSessions.GetAsync(demo.Account.DemoRunId, default) is not null,
            "Managed authority verifies legacy key with existing valid run grant and authenticated session");
        await managedKeys.ChangeAsync("revoke", 0, _ => Task.FromResult(Guid.NewGuid().ToString("D")), (_, _) => Task.CompletedTask, default);
        TestProgram.Check(!managedSessions.Authorize(context), "Revoked monitor key cannot authorize even while demo grant and session remain valid");
        TestProgram.Check(!json.Contains(demo.Token) && !json.Contains(demo.Account.SessionHash) && !json.Contains(demo.Account.Name)
            && !json.Contains("PublicKey"), "Metadata never contains visitor cookies, hashes, names or credential material");
        var originalLastSeen = (await demos.GetAsync(demo.Token, false, default))!.LastSeen;
        clock.Advance(TimeSpan.FromMinutes(10));
        await sessions.GetAsync(demo.Account.DemoRunId, default);
        TestProgram.Check((await demos.GetAsync(demo.Token, false, default))!.LastSeen == originalLastSeen, "Monitoring does not extend visitor lifetime");
        await sessions.SetGrantAsync(demo.Account.Id, false, default);
        TestProgram.Check(!(await sessions.GrantedDemoIdsAsync(default)).Contains(demo.Account.Id), "Revoked grant disappears from dashboard readback");
        TestProgram.Check(await sessions.GetAsync(demo.Account.DemoRunId, default) is null, "Presenter revocation removes access");
        await sessions.SetGrantAsync(demo.Account.Id, true, default);
        clock.Advance(TimeSpan.FromMinutes(20));
        TestProgram.Check(await sessions.GetAsync(demo.Account.DemoRunId, default) is null && metrics.ExpiredDemonstrations == 1, "Idle expiry removes account and updates aggregate count only");
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM MonitorGrants", default)) == 0, "Idle deletion cascades to monitor grants");
        var another = await demos.CreateAsync("laptop", default);
        using var otherAuthenticator = new SoftwareAuthenticator();
        await demos.VerifyAsync(another.Token, true, otherAuthenticator.Register(await demos.OptionsAsync(another.Token, true, default)), default);
        await demos.VerifyAsync(another.Token, false, otherAuthenticator.Authenticate(await demos.OptionsAsync(another.Token, false, default)), default);
        clock.Advance(TimeSpan.FromMinutes(28));
        await demos.GetAsync(another.Token, true, default);
        clock.Advance(TimeSpan.FromMinutes(3));
        await sessions.SetGrantAsync(another.Account.Id, true, default);
        TestProgram.Check(await sessions.GetAsync(another.Account.DemoRunId, default) is null, "Expired authentication cannot expose session metadata");
        await demos.EndOwnAsync(another.Token, default);
        TestProgram.Check(await sessions.GetAsync(another.Account.DemoRunId, default) is null, "Ending a demo revokes metadata and authenticated state");

        var previousData = Environment.GetEnvironmentVariable("LUMMA_DATA_DIRECTORY");
        var previousLogs = Environment.GetEnvironmentVariable("LUMMA_LOG_DIRECTORY");
        var previousExport = Environment.GetEnvironmentVariable("LUMMA_EXPORT_DIRECTORY");
        var previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.SetEnvironmentVariable("LUMMA_DATA_DIRECTORY", "portable-data");
            Environment.SetEnvironmentVariable("LUMMA_LOG_DIRECTORY", "audit");
            Environment.SetEnvironmentVariable("LUMMA_EXPORT_DIRECTORY", "layers");
            Environment.CurrentDirectory = root;
            TestProgram.Check(RuntimePaths.DataRoot() == Path.GetFullPath("portable-data", AppContext.BaseDirectory), "Portable data path is independent of working directory");
            TestProgram.Check(RuntimePaths.Logs() == Path.Combine(RuntimePaths.DataRoot(), "audit") && RuntimePaths.Exports() == Path.Combine(RuntimePaths.DataRoot(), "layers"), "Relative logs and exports use configured data root");
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
            Environment.SetEnvironmentVariable("LUMMA_DATA_DIRECTORY", previousData);
            Environment.SetEnvironmentVariable("LUMMA_LOG_DIRECTORY", previousLogs);
            Environment.SetEnvironmentVariable("LUMMA_EXPORT_DIRECTORY", previousExport);
        }
    }
}
