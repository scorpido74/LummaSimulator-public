using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace LummaSimulator.App.Services;

public sealed record SessionMetadata(string DemoRunId, string Target, bool SessionFound, string AuthenticationMethod,
    bool SessionActive, bool SessionTokenExposed, string State, DateTimeOffset? VerifiedAt);
public sealed class SessionAccessException : Exception { }
public sealed class MonitorKeyRejectedException : Exception { }
public interface ISessionMetadataClient : IDisposable
{
    bool IsConfigured { get; }
    string Origin { get; }
    Task<SessionMetadata> FetchAsync(string demoRunId, CancellationToken token);
}

public sealed class SessionMetadataClient : ISessionMetadataClient
{
    private readonly HttpClient _http;
    private readonly string _key;
    private string _grant="";
    public void SetGrant(string grant) { if(grant.Length!=0 && !MonitorCredentialStore.ValidKey(grant))throw new ArgumentException("MonitorGrantFormat");_grant=grant; }
    public string Origin { get; }
    public bool IsConfigured => _key.Length == 43 && _key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public SessionMetadataClient(string origin, string key, HttpMessageHandler? testHandler = null)
    {
        var uri = ValidateOrigin(origin);
        Origin = origin;
        _key = key;
        var handler = testHandler ?? CreateHandler();
        _http = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 16384 };
    }
    public static Uri ValidateOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || origin != uri.GetLeftPart(UriPartial.Authority)
            || (uri.Host != "localhost" && (uri.Host != "portal.example.test" || !uri.IsDefaultPort)))
            throw new ArgumentException("Configure an exact HTTPS localhost or portal.example.test origin.");
        return uri;
    }
    private static SocketsHttpHandler CreateHandler() => new()
        {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                // This integration is deliberately local even when the RP uses the production DNS name.
                foreach (var address in addresses.Where(IPAddress.IsLoopback))
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); if (token.IsCancellationRequested) throw; }
                }
                throw new HttpRequestException("The monitor origin must resolve to this laptop's loopback interface.");
            }
        };
    public async Task TestAsync(CancellationToken token)
    {
        if (!IsConfigured) throw new MonitorKeyRejectedException();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/api/local/session-status/00000000-0000-0000-0000-000000000000");
        request.Headers.Add("X-Lumma-Monitor-Key", _key);
        using var response = await _http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MonitorKeyRejectedException();
        if (response.StatusCode != HttpStatusCode.NoContent || !response.Headers.TryGetValues("X-Lumma-Monitor-Protocol", out var versions) || versions.SingleOrDefault() != "1") throw new InvalidDataException("MonitorProtocol");
    }
    public async Task<SessionMetadata> FetchAsync(string demoRunId, CancellationToken token)
    {
        if (!IsConfigured || !Guid.TryParseExact(demoRunId, "D", out var parsedRun)) throw new SessionAccessException();
        demoRunId = parsedRun.ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/local/session-status/" + demoRunId);
        request.Headers.Add("X-Lumma-Monitor-Key", _key);
        if(_grant.Length!=0)request.Headers.Add("X-Lumma-Monitor-Grant",_grant);
        using var response = await _http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MonitorKeyRejectedException();
        if (response.StatusCode == HttpStatusCode.NotFound) throw new SessionAccessException();
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        var result = JsonSerializer.Deserialize<SessionMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Invalid session metadata.");
        var expectedTarget = new Uri(Origin).Host;
        if (result.DemoRunId != demoRunId || result.Target != expectedTarget || result.SessionTokenExposed || !result.SessionFound
            || result.State is not ("NoAuthenticatedSession" or "ActiveWebAuthnSession" or "InvalidExpired")
            || result.SessionActive != (result.State == "ActiveWebAuthnSession")
            || (result.State == "NoAuthenticatedSession" && (result.AuthenticationMethod != "None" || result.VerifiedAt is not null))
            || (result.SessionActive && (result.AuthenticationMethod != "WebAuthn" || result.VerifiedAt is null)))
            throw new InvalidDataException("Inconsistent session metadata.");
        return result;
    }
    public void Dispose() => _http.Dispose();
}
