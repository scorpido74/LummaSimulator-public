using System.Security.Cryptography;
using System.Formats.Cbor;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Configuration;

namespace LummaSimulator.Web.Services;

public sealed class DemoService(DemoStore store, IFido2 fido, TimeProvider clock, ILogger<DemoService> logger, DemoMetrics? metrics = null, ExperienceSettings? settings = null, SecurityAuditService? audit = null, EventEditionStore? edition = null, SessionDemoState? sessionDemos = null)
{
    private readonly SecurityAuditService _audit = audit ?? new(store, clock);
    private readonly DemoMetrics _metrics = metrics ?? new();
    private readonly ExperienceSettings _settings = settings ?? new();
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(29);
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private long Cutoff => Now - (long)TimeSpan.FromMinutes(_settings.AccountIdleMinutes).TotalMilliseconds;

    public async Task<NewDemo> CreateAsync(string route, CancellationToken token, string? previousToken = null)
    {
        // Lock order is manifest -> demo gate -> SQLite, matching EventEditionService.
        // Never acquire the manifest while holding the demo gate or a database transaction.
        var editionId=previousToken is not null&&DemoIdentity.IsToken(previousToken)
            ?await store.ScalarAsync("SELECT x.EventId FROM PresenterDemoContexts x JOIN Demos d ON d.Id=x.DemoId WHERE x.Kind='edition' AND d.SessionHash=$hash AND d.ClosedAt IS NULL",token,("$hash",DemoIdentity.Hash(previousToken))) as string:null;
        if(editionId is null)return await CreateCoreAsync(route,token,previousToken,null);
        if(edition is null)throw new DemoRequestException("PresenterDenied");
        return await edition.AccessAsync(false,async manifest=>{
            if(!manifest.Events.Any(e=>e.Id==editionId&&e.Status=="Active"))throw new DemoRequestException("PresenterDenied");
            return await CreateCoreAsync(route,token,previousToken,editionId);
        },token);
    }
    private async Task<NewDemo> CreateCoreAsync(string route,CancellationToken token,string? previousToken,string? verifiedEditionId)
    {
        if (!DemoIdentity.IsRoute(route)) throw new DemoRequestException("Kies een geldige demonstratie.");
        await store.Gate.WaitAsync(token);
        try
        {
            await CleanupCoreAsync(token);
            var previous = previousToken is null ? null : await OwnedCoreAsync(previousToken, false, token)
                ?? throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
            var secret = DemoIdentity.RandomToken();
            var account = new DemoAccount(DemoIdentity.RandomToken(), Guid.NewGuid().ToString("D"), "",
                RandomNumberGenerator.GetBytes(32), DemoIdentity.Hash(secret), route, Now, Now, 0, 0, 0);
            await using var connection = await store.OpenAsync(token);
            using var transaction = connection.BeginTransaction(deferred: false);
            account=account with{Name=await Prizes.PrizeRoundsService.AllocateVisitorAliasAsync(connection,transaction,token)};
            string contextKind="experience",contextId=ExperienceStatistics.UnassignedEvent;
            if(previous is not null){
                using var context=EventAccess.Command(connection,transaction,"""
                    SELECT x.Kind,x.EventId FROM PresenterDemoContexts x
                    JOIN PresenterEventScopes s ON s.Kind=x.Kind AND s.SourceId=x.EventId
                    WHERE x.DemoId=$previous AND (s.OwnerId IS NULL OR EXISTS(
                      SELECT 1 FROM PresenterAccounts a WHERE a.Id=s.OwnerId AND a.Active=1 AND EXISTS(SELECT 1 FROM PresenterKeys k WHERE k.AccountId=a.Id)))
                    AND ((x.Kind='experience' AND (x.EventId=$open OR EXISTS(SELECT 1 FROM ExperienceEvents e WHERE e.Id=x.EventId AND e.Status='Active')))
                      OR (x.Kind='prize' AND EXISTS(SELECT 1 FROM PrizeEvents p WHERE p.Id=x.EventId AND p.Status='Open'))
                      OR (x.Kind='edition' AND x.EventId=$edition))
                    """,("$previous",previous.Id),("$open",ExperienceStatistics.UnassignedEvent),("$edition",verifiedEditionId));
                using var reader=await context.ExecuteReaderAsync(token);
                if(!await reader.ReadAsync(token))throw new DemoRequestException("PresenterDenied");
                contextKind=reader.GetString(0);contextId=reader.GetString(1);
            }
            if(previous is not null) {
                var mask = Convert.ToInt32(await EventAccess.Scalar(connection,transaction,"SELECT COALESCE((SELECT Mask FROM EventLoginRoutes WHERE Kind=$kind AND EventId=$id),15)",token,("$kind",contextKind),("$id",contextId)));
                if (!new LoginRoutes(mask).Allows(route)) throw new DemoRequestException("LoginRouteDisabled");
            }
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM Demos";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) >= 200)
                throw new DemoRequestException("Alle demoplekken zijn bezet. Probeer het later opnieuw.");
            if (previous is not null)
            {
                command.CommandText = "UPDATE Demos SET SessionHash=$revoked,ClosedAt=$closed WHERE Id=$previous AND SessionHash=$old AND ClosedAt IS NULL";
                command.Parameters.AddWithValue("$revoked", DemoIdentity.Hash(DemoIdentity.RandomToken()));
                command.Parameters.AddWithValue("$previous", previous.Id); command.Parameters.AddWithValue("$old", previous.SessionHash);
                command.Parameters.AddWithValue("$closed", Now);
                if (await command.ExecuteNonQueryAsync(token) != 1) throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
                command.CommandText = "DELETE FROM Challenges WHERE DemoId=$previous; DELETE FROM MonitorGrants WHERE DemoId=$previous; DELETE FROM PresenterMonitorCapabilities WHERE DemoId=$previous";
                await command.ExecuteNonQueryAsync(token); command.Parameters.Clear();
                await _audit.RevokeWithinAsync(connection,transaction,previous,null,true,token);
            }
            command.CommandText = """
                INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen)
                VALUES($id,$run,$name,$handle,$hash,$route,$now,$now)
                """;
            foreach (var (key, value) in new (string, object)[] { ("$id", account.Id), ("$run", account.DemoRunId), ("$name", account.Name),
                ("$handle", account.UserHandle), ("$hash", account.SessionHash), ("$route", route), ("$now", account.Created) }) command.Parameters.AddWithValue(key, value);
            await command.ExecuteNonQueryAsync(token);
            await EventAccess.Execute(connection,transaction,"""
                INSERT INTO PresenterDemoContexts(DemoId,Kind,EventId) VALUES($demo,$kind,$event)
                """,token,("$demo",account.Id),("$kind",contextKind),("$event",contextId));
            await _audit.WriteAsync(connection,transaction,IdentityEvent.IDENTITY_CREATED,account,"Success",token);
            transaction.Commit();
            logger.LogInformation(new EventId(100, "DemoCreated"), "Temporary demonstration created");
            return new(account, secret);
        }
        finally { store.Gate.Release(); }
    }

    private async Task<DemoAccount?> OwnedCoreAsync(string? secret, bool touch, CancellationToken token)
    {
        if (!DemoIdentity.IsToken(secret)) return null;
        var account = (await store.AccountsAsync(DemoIdentity.Hash(secret!), token)).SingleOrDefault();
        if (account is null) return null;
        if (account.LastSeen <= Cutoff)
        {
            await DeleteEndedAccountAsync(account,false,token);
            _metrics.RecordExpired(1);
            return null;
        }
        if (touch) await _audit.TouchAsync(account, token);
        if (touch) await store.ExecuteAsync("UPDATE Demos SET LastSeen=$now WHERE Id=$id", token, ("$now", Now), ("$id", account.Id));
        return account;
    }

    public async Task<DemoAccount?> GetAsync(string? secret, bool touch, CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try {
            var account=await OwnedCoreAsync(secret,touch,token);
            return account is not null && !await _audit.IsAuthenticatedAsync(account,token) ? account with {LastAuthenticatedAt=null} : account;
        }
        finally { store.Gate.Release(); }
    }

    public async Task<string> OptionsAsync(string? secret, bool registration, CancellationToken token, string? operationId = null)
    {
        await store.Gate.WaitAsync(token);
        try
        {
            var account = await OwnedCoreAsync(secret, true, token) ?? throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
            operationId ??= Guid.NewGuid().ToString("D");
            if (!Guid.TryParseExact(operationId, "D", out _)) throw new DemoRequestException("Ongeldige operatie-identificatie.");
            await store.ExecuteAsync("DELETE FROM Challenges WHERE DemoId=$id AND Expires <= $now", token, ("$id", account.Id), ("$now", Now));
            if (Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$id", token, ("$id", account.Id))) != 0)
                throw new DemoOperationPendingException();
            var credentials = await store.CredentialsAsync(account.Id, token);
            if (registration && credentials.Count >= 5) throw new DemoRequestException("Maximaal vijf passkeys per demonstratie.");
            if (!registration && credentials.Count == 0) throw new DemoRequestException("Registreer eerst een passkey.");
            var visibleName=await store.ScalarAsync("SELECT r.Alias FROM PrizeParticipants p JOIN PrizeRoundParticipants r ON r.EventId=p.EventId AND r.ParticipantId=p.Id WHERE p.AccountId=$account",token,("$account",account.Id)) as string??account.Name;
            var descriptors = credentials.Select(c => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(c.Id))).ToArray();
            string options;
            if (registration)
            {
                var selection = new AuthenticatorSelection
                {
                    UserVerification = UserVerificationRequirement.Required,
                    ResidentKey = ResidentKeyRequirement.Required
                };
                // Attachment is deliberately unconstrained: the browser can offer platform, USB/NFC or hybrid.
                options = fido.RequestNewCredential(new()
                {
                    User = new() { Id = account.UserHandle, Name = visibleName, DisplayName = visibleName },
                    ExcludeCredentials = descriptors,
                    AuthenticatorSelection = selection,
                    AttestationPreference = AttestationConveyancePreference.None
                }).ToJson();
            }
            else options = fido.GetAssertionOptions(new()
            {
                AllowedCredentials = descriptors,
                UserVerification = UserVerificationRequirement.Required
            }).ToJson();
            await store.ExecuteAsync("""
                INSERT INTO Challenges(DemoId,SessionHash,Kind,Options,Expires,OperationId) VALUES($id,$hash,$kind,$options,$expires,$operation)
                """, token, ("$id", account.Id), ("$hash", account.SessionHash), ("$kind", registration ? "register" : "authenticate"),
                ("$options", options), ("$expires", Now + (long)ChallengeLifetime.TotalMilliseconds), ("$operation", operationId));
            await _audit.RecordAsync(registration ? IdentityEvent.PASSKEY_REGISTRATION_STARTED : IdentityEvent.AUTHENTICATION_STARTED,account,"Started",token,operation:operationId);
            return options;
        }
        finally { store.Gate.Release(); }
    }

    public async Task<VerificationReceipt> VerifyAsync(string? secret, bool registration, JsonElement response, CancellationToken token, string? operationId = null)
    {
        await store.Gate.WaitAsync(token);
        try
        {
            var account = await OwnedCoreAsync(secret, true, token) ?? throw new DemoRequestException("Je demonstratie is verlopen. Start opnieuw.");
            // Atomic DELETE RETURNING consumes the challenge before any untrusted response is parsed or verified.
            var original = await store.ScalarAsync("""
                DELETE FROM Challenges WHERE DemoId=$id AND SessionHash=$hash AND Kind=$kind
                  AND ($operation IS NULL OR OperationId=$operation)
                RETURNING CASE WHEN Expires > $now THEN Options ELSE NULL END
                """, token, ("$id", account.Id), ("$hash", account.SessionHash), ("$kind", registration ? "register" : "authenticate"), ("$now", Now), ("$operation", operationId));
            try
            {
                var credentialAlias = "";
                VisitorReceipt? visitor = null;
                if (original is not string json) throw new DemoRequestException("Challenge ontbreekt of is verlopen.");
                if (registration)
                {
                    var raw = response.Deserialize<AuthenticatorAttestationRawResponse>() ?? throw new JsonException();
                    RequireAnonymousAttestation(raw.Response.AttestationObject);
                    var result = await fido.MakeNewCredentialAsync(new()
                    {
                        AttestationResponse = raw,
                        OriginalOptions = CredentialCreateOptions.FromJson(json),
                        IsCredentialIdUniqueToUserCallback = async (args, ct) =>
                            Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Credentials WHERE Id=$id", ct,
                                ("$id", WebEncoders.Base64UrlEncode(args.CredentialId)))) == 0
                    }, token);
                    if (!result.User.Id.AsSpan().SequenceEqual(account.UserHandle)) throw new DemoRequestException("Gebruiker komt niet overeen.");
                    credentialAlias = "Passkey " + ((await store.CredentialsAsync(account.Id, token)).Count + 1);
                    await using var registrationConnection = await store.OpenAsync(token);
                    using var registrationTransaction = registrationConnection.BeginTransaction(deferred:false);
                    using var registrationCommand = SecurityAuditService.Command(registrationConnection, registrationTransaction, """
                        INSERT INTO Credentials(Id,DemoId,PublicKey,Counter,BackupEligible,BackedUp,Alias) VALUES($id,$demo,$key,$counter,$be,$bs,$alias)
                        """, ("$id", WebEncoders.Base64UrlEncode(result.Id)), ("$demo", account.Id), ("$key", result.PublicKey),
                        ("$counter", (long)result.SignCount), ("$be", result.IsBackupEligible), ("$bs", result.IsBackedUp), ("$alias", credentialAlias));
                    await registrationCommand.ExecuteNonQueryAsync(token);
                    await _audit.WriteAsync(registrationConnection,registrationTransaction,IdentityEvent.PASSKEY_REGISTERED,account,"Success",token,WebEncoders.Base64UrlEncode(result.Id),operation:operationId);
                    registrationTransaction.Commit();
                    logger.LogInformation(new EventId(101, "RegistrationVerified"), "WebAuthn registration verified");
                }
                else
                {
                    var raw = response.Deserialize<AuthenticatorAssertionRawResponse>() ?? throw new JsonException();
                    var id = WebEncoders.Base64UrlEncode(raw.RawId);
                    var credential = (await store.CredentialsAsync(account.Id, token)).SingleOrDefault(c => c.Id == id)
                        ?? throw new DemoRequestException("Credential hoort niet bij deze demonstratie.");
                    credentialAlias = credential.Alias;
                    var verified = await fido.MakeAssertionAsync(new()
                    {
                        AssertionResponse = raw,
                        OriginalOptions = AssertionOptions.FromJson(json),
                        StoredPublicKey = credential.PublicKey,
                        StoredSignatureCounter = credential.Counter,
                        IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                            args.UserHandle.AsSpan().SequenceEqual(account.UserHandle)
                            && WebEncoders.Base64UrlEncode(args.CredentialId) == credential.Id)
                    }, token);
                    // Version 4.3.1 verifies the signature/flags but has no StoredBackupEligible input.
                    // Compare the permanent BE bit in the now-verified authenticator data with registration.
                    if (((raw.Response.AuthenticatorData[32] & 0x08) != 0) != credential.BackupEligible)
                        throw new DemoRequestException("Credentialeigenschappen komen niet overeen.");
                    visitor = await new ExperienceStatistics(store).CommitAuthenticationAsync(account.Id, account.SessionHash,
                        credential.Id, credential.Counter, verified.SignCount, verified.IsBackedUp, Now, Cutoff, token, _audit, account);
                    logger.LogInformation(new EventId(102, "AuthenticationVerified"), "WebAuthn authentication verified");
                }
                var current = (await store.AccountsAsync(account.SessionHash, token)).Single();
                return new(registration ? "register" : "authenticate", current.Name, clock.GetUtcNow(), current.CredentialCount, current.Successes,
                    CredentialAlias: credentialAlias, VisitorNumber: visitor?.Number, EventId: visitor?.EventId, EventName: visitor?.EventName);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not Microsoft.Data.Sqlite.SqliteException)
            {
                if (!registration)
                    await store.ExecuteAsync("UPDATE Demos SET Failures=Failures+1 WHERE Id=$id", CancellationToken.None, ("$id", account.Id));
                await _audit.RecordAsync(registration ? IdentityEvent.PASSKEY_REGISTRATION_FAILED : IdentityEvent.AUTHENTICATION_FAILED,account,"Failure",CancellationToken.None,operation:operationId);
                logger.LogWarning(new EventId(103, "CeremonyRejected"), "WebAuthn ceremony rejected; operation {Operation}", registration ? "registration" : "authentication");
                throw new DemoRequestException("Verificatie mislukt of verlopen. Probeer opnieuw met de passkey van deze demonstratie.");
            }
        }
        finally { store.Gate.Release(); }
    }

    public async Task CancelAsync(string? secret, bool registration, string operationId, CancellationToken token)
    {
        if (!Guid.TryParseExact(operationId, "D", out _)) throw new DemoRequestException("Ongeldige operatie-identificatie.");
        await store.Gate.WaitAsync(token);
        try
        {
            var account = await OwnedCoreAsync(secret, false, token);
            if (account is null) return;
            await store.ExecuteAsync("DELETE FROM Challenges WHERE DemoId=$id AND SessionHash=$hash AND Kind=$kind AND OperationId=$operation", token,
                ("$id", account.Id), ("$hash", account.SessionHash), ("$kind", registration ? "register" : "authenticate"), ("$operation", operationId));
        }
        finally { store.Gate.Release(); }
    }

    public async Task<bool> EndOwnAsync(string? secret, CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try
        {
            var account = await OwnedCoreAsync(secret, false, token);
            if (account is null) return false;
            await DeleteEndedAccountAsync(account,true,token);
            logger.LogInformation(new EventId(104, "DemoEnded"), "Temporary demonstration and dependent data deleted");
            return true;
        }
        finally { store.Gate.Release(); }
    }

    // Called only by authorized presenter pages. Never exposed as an anonymous ID-based endpoint.
    public async Task EndAsPresenterAsync(string id, CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try {
            var account=(await store.AccountsAsync(null,token)).SingleOrDefault(a=>a.Id==id);
            if(account is not null) await DeleteEndedAccountAsync(account,false,token);
        }
        finally { store.Gate.Release(); }
    }

    public async Task<List<DemoAccount>> DashboardAsync(CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try { await CleanupCoreAsync(token); return await store.AccountsAsync(null, token); }
        finally { store.Gate.Release(); }
    }

    private async Task DeleteEndedAccountAsync(DemoAccount account,bool logout,CancellationToken token)
    {
        await DemoAccountRemoval.DeleteAsync(store,_audit,account,logout,sessionDemos,token);
    }

    private async Task CleanupCoreAsync(CancellationToken token)
    {
        var expired=(await store.AccountsAsync(null,token,true)).Where(a=>a.LastSeen<=Cutoff).ToArray();
        foreach(var account in expired)await DeleteEndedAccountAsync(account,false,token);
        var count=expired.Length;
        _metrics.RecordExpired(count);
        await store.ExecuteAsync("DELETE FROM Challenges WHERE Expires <= $now; DELETE FROM PresenterSessions WHERE Expires <= $now; DELETE FROM MonitorGrants WHERE Expires <= $now;", token, ("$now", Now));
        if (count > 0) logger.LogInformation(new EventId(105, "ExpiredDemosDeleted"), "Deleted {Count} expired demonstrations", count);
    }
    public async Task CleanupAsync(CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try { await CleanupCoreAsync(token); }
        finally { store.Gate.Release(); }
    }

    private static void RequireAnonymousAttestation(byte[] attestation)
    {
        // Enforce the requested privacy mode before library verification. No certificate attestation,
        // metadata lookup or revocation download is needed. This only checks CBOR shape, not cryptography.
        var reader = new CborReader(attestation, CborConformanceMode.Strict);
        reader.ReadStartMap();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 3; i++)
        {
            var field = reader.ReadTextString();
            if (!fields.Add(field)) throw new DemoRequestException("Ongeldige attestation.");
            switch (field)
            {
                case "fmt":
                    if (reader.ReadTextString() != "none") throw new DemoRequestException("Alleen anonieme attestation is toegestaan.");
                    break;
                case "authData": reader.ReadByteString(); break;
                case "attStmt": reader.ReadStartMap(); reader.ReadEndMap(); break;
                default: throw new DemoRequestException("Ongeldige attestation.");
            }
        }
        reader.ReadEndMap();
        if (reader.BytesRemaining != 0) throw new DemoRequestException("Ongeldige attestation.");
    }
}
