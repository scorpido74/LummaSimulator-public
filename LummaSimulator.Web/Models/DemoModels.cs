using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace LummaSimulator.Web.Models;

public sealed record DemoAccount(string Id, string DemoRunId, string Name, byte[] UserHandle,
    string SessionHash, string Route, long Created, long LastSeen, int Successes, int Failures, int CredentialCount, long? LastAuthenticatedAt = null);
public sealed record Credential(string Id, string DemoId, byte[] PublicKey, uint Counter, bool BackupEligible, bool BackedUp, string Alias = "");
public sealed record NewDemo(DemoAccount Account, string Token);
public class DemoRequestException(string message) : Exception(message);
public sealed class DemoOperationPendingException() : DemoRequestException("Er is al een passkey-aanvraag actief voor jouw demonstratie. Rond die af of annuleer hem in de andere tab.");
public sealed record VerificationReceipt(string Operation, string Username, DateTimeOffset VerifiedAt,
    int CredentialCount, int SuccessfulAuthentications, string VerificationStatus = "Verified", string CredentialAlias = "",
    long? VisitorNumber = null, string? EventId = null, string? EventName = null);

public static class DemoIdentity
{
    public static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public static bool IsToken(string? token) => token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string VisitorName()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return "Visitor-" + string.Concat(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
    }
    public static bool IsRoute(string route) => route is "phone" or "laptop" or "yubikey" or "ipad";
}
