using System.Security.Cryptography;
using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

internal static class TestProgram
{
    private static int _checks;
    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS: {message}");
        _checks++;
    }
    public static async Task RejectAsync(Func<Task> action, string message)
    {
        var rejected = false;
        try { await action(); } catch (DemoRequestException) { rejected = true; }
        Check(rejected, message);
    }
    public static async Task Main(string[] args)
    {
        string[] selections=["--session-demo-checks","--session-demo-http-checks","--round-account-end-checks","--visitor-display-checks","--round-visitor-baseline","--prize-http-checks","--prize-configure-http-diagnostic",
            "--prize-round-checks","--clickfix-browser-checks","--phase65e-checks","--experience-checks","--prize-event-checks","--prize-migration-checks",
            "--http-rate-limit-fixture-checks","--windows-eventlog-standard-user-smoke","--onboarding-http-checks"];
        if(args.SequenceEqual(new[]{"--onboarding-http-checks"})) {
            var onboardingRoot=Path.Combine(Path.GetTempPath(),"LummaOnboardingHttpTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(onboardingRoot);
            await HttpChecks.RunAsync(onboardingRoot,onboardingOnly:true);
            return;
        }
        if(args.SequenceEqual(new[]{"--runner-info"})) {
            static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var runner=typeof(TestProgram).Assembly.Location;
            var dependency=typeof(LummaSimulator.Web.Pages.Presenter.Prizes.EventModel).Assembly.Location;
            var target=Environment.GetEnvironmentVariable("LUMMA_TEST_WEB_DLL");
            var roundId=typeof(LummaSimulator.Web.Pages.Presenter.Prizes.EventModel).GetProperty("RoundId")!;
            var targetHash=target is not null&&File.Exists(target)?Hash(target):null;
            Console.WriteLine(JsonSerializer.Serialize(new{
                Runner=runner,RunnerSha256=Hash(runner),SupportedSelections=selections,
                WebDependency=dependency,WebDependencySha256=Hash(dependency),HttpTarget=target,HttpTargetSha256=targetHash,
                HttpTargetMatchesDependency=targetHash==Hash(dependency),
                CompiledRoundIdNullable=new System.Reflection.NullabilityInfoContext().Create(roundId).ReadState==System.Reflection.NullabilityState.Nullable,
                HttpFixtureParameterCount=typeof(HttpChecks).GetMethod("RunAsync")!.GetParameters().Length
            }));return;
        }
        if(args.Length>0) {
            if(args.Length!=1||!selections.Contains(args[0],StringComparer.Ordinal)) {
                Console.Error.WriteLine("Unknown test selection. Use --runner-info; the general suite was not started.");Environment.ExitCode=2;return;
            }
            Console.WriteLine("TEST_SELECTION="+args[0]);
            Console.WriteLine("TEST_RUNNER="+typeof(TestProgram).Assembly.Location);
            Console.WriteLine("TEST_WEB_DEPENDENCY="+typeof(LummaSimulator.Web.Pages.Presenter.Prizes.EventModel).Assembly.Location);
            Console.WriteLine("TEST_HTTP_PORTAL_DLL="+(Environment.GetEnvironmentVariable("LUMMA_TEST_WEB_DLL")??"configuration default"));
        }
        if(args.SequenceEqual(new[]{"--session-demo-checks"})) {
            var sessionRoot=Path.Combine(Path.GetTempPath(),"LummaSessionDemoTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sessionRoot);
            await SessionDemoChecks.RunAsync(sessionRoot);await RoundAccountEndChecks.RunAsync(sessionRoot);return;
        }
        if(args.SequenceEqual(new[]{"--round-account-end-checks"})) {
            var endRoot=Path.Combine(Path.GetTempPath(),"LummaRoundEndTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(endRoot);
            await RoundAccountEndChecks.RunAsync(endRoot);return;
        }
        if(args.SequenceEqual(new[]{"--session-demo-http-checks"})) {
            var sessionRoot=Path.Combine(Path.GetTempPath(),"LummaSessionDemoHttpTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sessionRoot);
            await HttpChecks.RunAsync(sessionRoot,sessionDemoOnly:true);return;
        }
        if(args.SequenceEqual(new[]{"--visitor-display-checks"})) {
            var displayRoot=Path.Combine(Path.GetTempPath(),"LummaVisitorDisplayTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(displayRoot);
            await HttpChecks.RunAsync(displayRoot,visitorDisplayChecks:true);return;
        }
        if(args.SequenceEqual(new[]{"--round-visitor-baseline"})) {
            var baselineRoot=Path.Combine(Path.GetTempPath(),"LummaRoundVisitorBaseline",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(baselineRoot);
            await HttpChecks.RunAsync(baselineRoot,roundVisitorBaseline:true);return;
        }
        if(args.SequenceEqual(new[]{"--prize-http-checks"})) {
            var httpRoot=Path.Combine(Path.GetTempPath(),"LummaPrizeHttpTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(httpRoot);
            await HttpChecks.RunAsync(httpRoot,prizeOnly:true);return;
        }
        if(args.SequenceEqual(new[]{"--prize-configure-http-diagnostic"})) {
            var httpRoot=Path.Combine(Path.GetTempPath(),"LummaPrizeConfigureHttpTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(httpRoot);
            await HttpChecks.RunAsync(httpRoot,prizeConfigureDiagnostic:true);return;
        }
        if(args.SequenceEqual(new[]{"--prize-round-checks"})) {
            var roundRoot=Path.Combine(Path.GetTempPath(),"LummaPrizeRoundsTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(roundRoot);
            await PrizeRoundsChecks.RunAsync(roundRoot);return;
        }
        if(args.SequenceEqual(new[]{"--clickfix-browser-checks"})) {
            var clickFixRoot=Path.Combine(Path.GetTempPath(),"LummaClickFixTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(clickFixRoot);
            await HttpChecks.RunAsync(clickFixRoot,true);return;
        }
        if(args.SequenceEqual(new[]{"--phase65e-checks"})) {
            var p1Root=Path.Combine(Path.GetTempPath(),"LummaPhase65eTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p1Root);
            await Phase65eChecks.RunAsync(p1Root);return;
        }
        if(args.SequenceEqual(new[]{"--experience-checks"})) {
            var experienceRoot=Path.Combine(Path.GetTempPath(),"LummaExperienceTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(experienceRoot);
            await ExperienceChecks.RunAsync(experienceRoot);return;
        }
        if(args.SequenceEqual(new[]{"--prize-event-checks"})) {
            var eventRoot=Path.Combine(Path.GetTempPath(),"LummaPrizeEventTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(eventRoot);
            await PrizeEventChecks.RunAsync(eventRoot);return;
        }
        if(args.SequenceEqual(new[]{"--prize-migration-checks"})) {
            var migrationRoot=Path.Combine(Path.GetTempPath(),"LummaMigrationTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(migrationRoot);
            await PrizeMigrationChecks.RunAsync(migrationRoot);return;
        }
        if(args.SequenceEqual(new[]{"--http-rate-limit-fixture-checks"})) {
            await HttpRateLimitFixture.RunChecksAsync();return;
        }
        if(args.SequenceEqual(new[]{"--windows-eventlog-standard-user-smoke"})) {
            await AuditExportChecks.WindowsStandardUserSmokeAsync();return;
        }
        if(args.Length!=0){Console.Error.WriteLine("Test selection has no handler; the general suite was not started.");Environment.ExitCode=2;return;}
        DomainModelChecks.Run();
        var settings = new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] };
        settings.Validate(true);
        new WebAuthnSettings { RpId = "portal.example.test", Origins = ["https://portal.example.test"] }.Validate(false);
        foreach (var origin in new[] { "http://localhost:7243", "https://*.localhost:7243", "https://localhost:7243/path", "https://localhost:7243/", "https://example.invalid", "https://localhost:7243?x=1", "https://u:p@localhost:7243" })
        {
            var rejected = false;
            try { new WebAuthnSettings { RpId = "localhost", Origins = [origin] }.Validate(true); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Strict origin rejection: " + origin);
        }
        var productionRejected = false;
        try { settings.Validate(false); } catch (InvalidOperationException) { productionRejected = true; }
        Check(productionRejected, "Production cannot fall back to localhost");
        var root = Path.Combine(Path.GetTempPath(), "LummaPortalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var store = new DemoStore(Path.Combine(root, "test.db"));
        await store.ExecuteAsync("CREATE TABLE Challenges (DemoId TEXT PRIMARY KEY REFERENCES Demos(Id) ON DELETE CASCADE, SessionHash TEXT NOT NULL, Kind TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL)", default);
        await store.InitializeAsync();
        Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Challenges') WHERE name='OperationId'", default)) == 1, "v0.3 challenge schema migrates without resetting accounts");
        var clock = new TestClock();
        var service = new DemoService(store, new Fido2(settings.ToFidoConfiguration()), clock, NullLogger<DemoService>.Instance);
        var demo = await service.CreateAsync("laptop", default);
        var other = await service.CreateAsync("ipad", default);
        Check(System.Text.RegularExpressions.Regex.IsMatch(demo.Account.Name,"^[A-Z][a-z]+[0-9]*$") && demo.Account.Name != other.Account.Name, "Random visitor usernames");
        Check(demo.Account.Id != other.Account.Id && demo.Account.DemoRunId != other.Account.DemoRunId
            && !demo.Account.UserHandle.SequenceEqual(other.Account.UserHandle) && demo.Token != other.Token, "Separate random IDs, handles and sessions");
        Check(await service.GetAsync(demo.Account.Name, false, default) is null && await service.GetAsync(demo.Account.DemoRunId, false, default) is null, "Public identifiers cannot own an account");
        foreach (var invalidRegistration in new[] { "origin", "rp", "uv", "challenge" })
        {
            using var rejectedAuthenticator = new SoftwareAuthenticator();
            var options = await service.OptionsAsync(demo.Token, true, default);
            var response = rejectedAuthenticator.Register(options,
                origin: invalidRegistration == "origin" ? "https://other.invalid" : "https://localhost:7243",
                rpId: invalidRegistration == "rp" ? "other.invalid" : "localhost", verified: invalidRegistration != "uv",
                challenge: invalidRegistration == "challenge" ? "wrong-challenge" : null);
            await RejectAsync(() => service.VerifyAsync(demo.Token, true, response, default), "Registration rejects invalid " + invalidRegistration);
        }
        using var authenticator = new SoftwareAuthenticator();
        var registration = await service.OptionsAsync(demo.Token, true, default);
        var rawRegistration = authenticator.Register(registration);
        var registrationReceipt = await service.VerifyAsync(demo.Token, true, rawRegistration, default);
        Check(registrationReceipt.Operation == "register" && registrationReceipt.SuccessfulAuthentications == 0, "Registration is not authentication and never increments success count");
        Check((await service.GetAsync(demo.Token, false, default))!.CredentialCount == 1, "Real library verifies a signed-data-format WebAuthn registration");
        await RejectAsync(() => service.VerifyAsync(demo.Token, true, rawRegistration, default), "Registration challenge cannot be reused");
        var assertionOptions = await service.OptionsAsync(demo.Token, false, default);
        var assertion = authenticator.Authenticate(assertionOptions);
        var authenticationReceipt = await service.VerifyAsync(demo.Token, false, assertion, default);
        Check(authenticationReceipt.Operation == "authenticate" && authenticationReceipt.VerificationStatus == "Verified"
            && authenticationReceipt.Username == demo.Account.Name && authenticationReceipt.VerifiedAt == clock.GetUtcNow()
            && authenticationReceipt.SuccessfulAuthentications == 1, "Authentication receipt is authoritative and contains username, server time and verified status");
        Check((await service.GetAsync(demo.Token, false, default))!.Successes == 1, "Real library verifies ECDSA WebAuthn authentication");
        await RejectAsync(() => service.VerifyAsync(demo.Token, false, assertion, default), "Authentication challenge cannot be reused");
        var concurrentOptions = await service.OptionsAsync(demo.Token, false, default);
        var concurrentAssertion = authenticator.Authenticate(concurrentOptions);
        async Task<bool> AttemptAsync()
        {
            try { await service.VerifyAsync(demo.Token, false, concurrentAssertion, default); return true; }
            catch (DemoRequestException) { return false; }
        }
        var attempts = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Check(attempts.Count(success => success) == 1, "Concurrent replay succeeds exactly once");
        var operationA = Guid.NewGuid().ToString("D");
        var leasedOptions = await service.OptionsAsync(demo.Token, false, default, operationA);
        var beforeCancel = (await service.GetAsync(demo.Token, false, default))!;
        await RejectAsync(async () => { await service.OptionsAsync(demo.Token, false, default, Guid.NewGuid().ToString("D")); }, "Concurrent options request cannot replace active challenge");
        await service.CancelAsync(other.Token, false, operationA, default);
        await service.CancelAsync(demo.Token, false, Guid.NewGuid().ToString("D"), default);
        await service.VerifyAsync(demo.Token, false, authenticator.Authenticate(leasedOptions), default, operationA);
        Check((await service.GetAsync(demo.Token, false, default))!.Successes == beforeCancel.Successes + 1, "Other session and stale cancel cannot destroy current operation");
        var cancelId = Guid.NewGuid().ToString("D");
        await service.OptionsAsync(demo.Token, false, default, cancelId);
        var before = (await service.GetAsync(demo.Token, false, default))!;
        await service.CancelAsync(demo.Token, false, cancelId, default);
        var after = (await service.GetAsync(demo.Token, false, default))!;
        Check(after.Successes == before.Successes && after.Failures == before.Failures, "Cancellation does not count as successful or failed verification");
        var replacementId = Guid.NewGuid().ToString("D");
        var replacementOptions = await service.OptionsAsync(demo.Token, false, default, replacementId);
        await service.CancelAsync(demo.Token, false, cancelId, default);
        await service.VerifyAsync(demo.Token, false, authenticator.Authenticate(replacementOptions), default, replacementId);
        Check((await service.GetAsync(demo.Token, false, default))!.Successes == after.Successes + 1, "Retry works after cancellation and late cancel leaves replacement intact");
        foreach (var invalid in new[] { "origin", "rp", "uv", "signature", "challenge", "user", "backup" })
        {
            var options = await service.OptionsAsync(demo.Token, false, default);
            var response = authenticator.Authenticate(options,
                origin: invalid == "origin" ? "https://other.invalid" : "https://localhost:7243",
                rpId: invalid == "rp" ? "other.invalid" : "localhost", verified: invalid != "uv", badSignature: invalid == "signature",
                challenge: invalid == "challenge" ? "wrong-challenge" : null,
                userHandle: invalid == "user" ? other.Account.UserHandle : null, backupEligible: invalid == "backup");
            await RejectAsync(() => service.VerifyAsync(demo.Token, false, response, default), "Server rejects invalid " + invalid);
            await RejectAsync(() => service.VerifyAsync(demo.Token, false, response, default), "Failed challenge consumed: " + invalid);
        }
        using var otherAuthenticator = new SoftwareAuthenticator();
        await service.VerifyAsync(other.Token, true, otherAuthenticator.Register(await service.OptionsAsync(other.Token, true, default)), default);
        var otherOptions = await service.OptionsAsync(other.Token, false, default);
        await RejectAsync(() => service.VerifyAsync(other.Token, false, authenticator.Authenticate(otherOptions), default), "Credential cannot cross demo ownership boundary");
        var expiredOptions = await service.OptionsAsync(demo.Token, false, default);
        clock.Advance(TimeSpan.FromMinutes(3));
        await RejectAsync(() => service.VerifyAsync(demo.Token, false, authenticator.Authenticate(expiredOptions), default), "Challenge expires independently of session");
        await service.OptionsAsync(demo.Token, false, default);
        Check(await service.EndOwnAsync(demo.Token, default), "Owner can end their demo");
        Check(await service.GetAsync(demo.Token, false, default) is null && (await store.CredentialsAsync(demo.Account.Id, default)).Count == 0,
            "Ending revokes session and deletes credentials");
        Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$id", default, ("$id", demo.Account.Id))) == 0, "Ending deletes challenges");
        Check(await service.GetAsync(other.Token, false, default) is not null, "Ending one visitor preserves another visitor");
        clock.Advance(TimeSpan.FromMinutes(30));
        await service.CleanupAsync(default);
        Check(await service.GetAsync(other.Token, false, default) is null && (await store.CredentialsAsync(other.Account.Id, default)).Count == 0, "Idle cleanup revokes accounts, credentials and sessions");
        Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges", default)) == 0, "Idle cleanup deletes outstanding challenges");

        await PrizeMigrationChecks.RunAsync(root);
        await IdentityChecks.RunAsync(root);
        await HttpRateLimitFixture.RunChecksAsync();
        await AuditExportChecks.RunAsync(root);
        await PrizeStoreChecks.RunAsync(root);
        await PrizeEventChecks.RunAsync(root);
        await PresenterChecks.RunAsync(root);
        await EventAccessChecks.RunAsync(root);
        await Phase65eChecks.RunAsync(root);
        await LocalizationChecks.RunAsync(root);
        await ExperienceChecks.RunAsync(root);
        await MultiCredentialChecks.RunAsync(root);
        await MonitorKeyChecks.RunAsync(root);
        await SessionStatusChecks.RunAsync(root);
        await PrizeRoundsChecks.RunAsync(root);
        await SessionDemoChecks.RunAsync(root);
        await RoundAccountEndChecks.RunAsync(root);
        await ClickFixChecks.RunAsync(root);
        BrandingChecks.Run(root);
        await EventEditionChecks.RunAsync(root);
        await HttpChecks.RunAsync(root);
        Console.WriteLine($"PASS: {_checks} checks covering services, configuration and HTTPS integration. No physical authenticator test claimed.");
    }
}
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan amount) => _now += amount;
}
