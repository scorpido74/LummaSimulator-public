using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

// Real SQLite + production ceremony service + Fido2 verifier; no real visitor data.
internal static class MultiCredentialChecks
{
    public static async Task RunAsync(string root)
    {
        using var store = new DemoStore(Path.Combine(root, "two-credentials.db"));
        await store.InitializeAsync();
        var settings = new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] };
        var service = new DemoService(store, new Fido2(settings.ToFidoConfiguration()), new TestClock(), NullLogger<DemoService>.Instance);
        var demo = await service.CreateAsync("laptop", default);
        using var a = new SoftwareAuthenticator();
        using var b = new SoftwareAuthenticator();
        var ids = new List<string>();
        var keys = new List<byte[]>();
        var counters = new uint[2];

        async Task AssertStateAsync(string stage, int count, int successes)
        {
            var account = (await service.GetAsync(demo.Token, false, default))!;
            var stored = await store.CredentialsAsync(demo.Account.Id, default);
            TestProgram.Check(account.Id == demo.Account.Id && account.DemoRunId == demo.Account.DemoRunId
                && account.UserHandle.SequenceEqual(demo.Account.UserHandle)
                && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Demos", default)) == 1,
                $"Two credentials: {stage}: original account and user handle retained; exactly one account");
            TestProgram.Check(account.CredentialCount == count && stored.Count == count && account.Successes == successes,
                $"Two credentials: {stage}: credentials={count}, successful authentications={successes}");
            TestProgram.Check(stored.Select(c => c.Id).ToHashSet().SetEquals(ids)
                && stored.All(c => c.DemoId == demo.Account.Id), $"Two credentials: {stage}: exact credential identities and ownership retained");
            for (var i = 0; i < ids.Count; i++)
            {
                var credential = stored.Single(c => c.Id == ids[i]);
                TestProgram.Check(credential.Counter == counters[i] && credential.PublicKey.SequenceEqual(keys[i]),
                    $"Two credentials: {stage}: credential {(i == 0 ? "A" : "B")} counter={counters[i]}, public key unchanged");
            }
        }

        await AssertStateAsync("created", 0, 0);
        foreach (var authenticator in new[] { a, b })
        {
            var operation = Guid.NewGuid().ToString("D");
            var options = await service.OptionsAsync(demo.Token, true, default, operation);
            await AssertStateAsync("registration challenge issued", ids.Count, 0);
            using (var parsed = JsonDocument.Parse(options))
                TestProgram.Check(parsed.RootElement.GetProperty("excludeCredentials").EnumerateArray()
                    .Select(c => c.GetProperty("id").GetString()!).ToHashSet().SetEquals(ids),
                    "Two credentials: registration excludes exactly the existing credentials");
            var response = authenticator.Register(options);
            var receipt = await service.VerifyAsync(demo.Token, true, response, default, operation);
            var id = response.GetProperty("id").GetString()!;
            TestProgram.Check(!ids.Contains(id), "Two credentials: independent authenticator generates a distinct credential");
            ids.Add(id);
            keys.Add((await store.CredentialsAsync(demo.Account.Id, default)).Single(c => c.Id == id).PublicKey.ToArray());
            TestProgram.Check(receipt.CredentialCount == ids.Count && receipt.SuccessfulAuthentications == 0,
                "Two credentials: registration receipt reports authoritative count without authentication");
            await AssertStateAsync("registered", ids.Count, 0);
        }
        TestProgram.Check(!keys[0].SequenceEqual(keys[1]), "Two credentials: independently stored public keys differ");

        var successes = 0;
        for (var index = 0; index < 2; index++)
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var operation = Guid.NewGuid().ToString("D");
            var options = await service.OptionsAsync(demo.Token, false, default, operation);
            await AssertStateAsync("authentication challenge issued", 2, successes);
            using (var parsed = JsonDocument.Parse(options))
            {
                var allowed = parsed.RootElement.GetProperty("allowCredentials").EnumerateArray()
                    .Select(c => c.GetProperty("id").GetString()!).ToArray();
                TestProgram.Check(allowed.Length == 2 && allowed.ToHashSet().SetEquals(ids),
                    "Two credentials: allowCredentials contains both distinct registered credentials");
            }
            var response = (index == 0 ? a : b).Authenticate(options);
            var receipt = await service.VerifyAsync(demo.Token, false, response, default, operation);
            counters[index]++;
            successes++;
            TestProgram.Check(receipt.VerificationStatus == "Verified" && receipt.SuccessfulAuthentications == successes
                && receipt.CredentialCount == 2 && receipt.CredentialAlias == $"Passkey {index + 1}",
                $"Two credentials: credential {(index == 0 ? "A" : "B")} authentication {attempt} verified");
            await AssertStateAsync("authenticated", 2, successes);
        }

        using var rejected = new SoftwareAuthenticator();
        var failedOperation = Guid.NewGuid().ToString("D");
        var failedOptions = await service.OptionsAsync(demo.Token, true, default, failedOperation);
        await TestProgram.RejectAsync(() => service.VerifyAsync(demo.Token, true,
            rejected.Register(failedOptions, origin: "https://other.invalid"), default, failedOperation),
            "Two credentials: invalid registration rejected by real verifier");
        await AssertStateAsync("failed registration", 2, 4);

        var cancelledOperation = Guid.NewGuid().ToString("D");
        var cancelledOptions = await service.OptionsAsync(demo.Token, true, default, cancelledOperation);
        var cancelledResponse = rejected.Register(cancelledOptions);
        await service.CancelAsync(demo.Token, true, cancelledOperation, default);
        await TestProgram.RejectAsync(() => service.VerifyAsync(demo.Token, true, cancelledResponse, default, cancelledOperation),
            "Two credentials: late registration response after cancellation is rejected");
        await AssertStateAsync("cancelled registration", 2, 4);

        // A freshly signed assertion with a repeated counter must not advance server state.
        foreach (var counter in new uint[] { 2, 1 })
        {
            var operation = Guid.NewGuid().ToString("D");
            var options = await service.OptionsAsync(demo.Token, false, default, operation);
            await TestProgram.RejectAsync(() => service.VerifyAsync(demo.Token, false,
                a.Authenticate(options, counter: counter), default, operation),
                $"Two credentials: freshly signed assertion with non-increasing counter {counter} rejected");
            await AssertStateAsync("counter rejection", 2, 4);
        }
        var staleOperation = Guid.NewGuid().ToString("D");
        await service.OptionsAsync(demo.Token, true, default, staleOperation);
        await service.CancelAsync(demo.Token, true, staleOperation, default);
        await AssertStateAsync("new challenge cancelled", 2, 4);
        Console.WriteLine("DIAGNOSTIC: two-credential scenario complete; credentials=2; successes=4; counters A=2 B=2. No credential IDs, challenges or session secrets logged.");
    }
}
