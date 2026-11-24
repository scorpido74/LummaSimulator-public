using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace LummaSimulator.Helper;

public static class HelperProtocol
{
    public const int Version = 1;
    public const string HelperVersion = "0.1.1";
    public const string Prefix = "/lumma-helper/";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static bool IsToken(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    public static Uri Origin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0
            || uri.Fragment.Length != 0 || value != uri.GetLeftPart(UriPartial.Authority)) throw new InvalidDataException("HTTPS-origin required.");
        return uri;
    }
}
public sealed record ReceiverInfo(string AppVersion, int[] Protocols, bool DemoActive);
public sealed record HelperSelection(int Protocol, string PortalOrigin, string ConnectionId, string ConnectionKey, string EventId);
public sealed record SelectionReceipt(string EventId, string EventName, string VisitorPath);
public sealed record ReceiverQrState(string Status,string? PortalOrigin=null,string? EventId=null,string? EventName=null,string? VisitorPath=null);
public sealed record OpenParticipantsRequest(string EventId);
public sealed record OpenParticipantsReceipt(bool Opened);

public sealed class AppReceiverClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _token;
    public AppReceiverClient(string origin, string certificateSha256, string token, HttpMessageHandler? testHandler = null)
    {
        var uri = HelperProtocol.Origin(origin);
        if (!uri.IsDefaultPort || !HelperProtocol.IsHash(certificateSha256) || !HelperProtocol.IsToken(token)) throw new InvalidDataException("Receiver provisioning invalid.");
        // Both normal PKI/name validation AND the installed pin are mandatory. No trust-on-first-use.
        _http = new(testHandler ?? new HttpClientHandler {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) => errors == SslPolicyErrors.None && cert is not null
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), Convert.FromHexString(certificateSha256))
        }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2097152 };
        _token = token;
    }
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? value, CancellationToken ct)
    {
        if (!path.StartsWith(HelperProtocol.Prefix, StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException();
        using var request = Request(method, path);
        if (value is not null) request.Content = new StringContent(JsonSerializer.Serialize(value, HelperProtocol.Json), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(HelperProtocol.Json, ct) ?? throw new InvalidDataException("Invalid receiver reply.");
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Add("X-Lumma-Pairing", _token);
        request.Headers.Add("X-Lumma-Helper-Protocol", HelperProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)); return request;
    }
    public Task<ReceiverInfo> InfoAsync(CancellationToken ct) => SendAsync<ReceiverInfo>(HttpMethod.Get, "/lumma-helper/v1/info", null, ct);
    public Task<ReceiverQrState> CurrentSelectionAsync(CancellationToken ct) => SendAsync<ReceiverQrState>(HttpMethod.Get, "/lumma-helper/v1/selection", null, ct);
    public Task<OpenParticipantsReceipt> OpenParticipantsAsync(string eventId,CancellationToken ct)=>SendAsync<OpenParticipantsReceipt>(HttpMethod.Post,"/lumma-helper/v1/open-participants",new OpenParticipantsRequest(eventId),ct);
    public Task<SelectionReceipt> SelectAsync(HelperSelection value, CancellationToken ct) => SendAsync<SelectionReceipt>(HttpMethod.Post, "/lumma-helper/v1/selection", value, ct);
    public async Task DownloadAsync(string path, string destination, long maximumBytes, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2)); ct = deadline.Token;
        if (path != "/lumma-helper/updates/v1/package.zip") throw new InvalidDataException("Invalid update path.");
        using var request = Request(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Package too large.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[65536]; long written = 0; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) {
            written += count; if (written > maximumBytes) throw new InvalidDataException("Package too large."); await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        await output.FlushAsync(ct); output.Flush(true);
    }
    public void Dispose() => _http.Dispose();
}
