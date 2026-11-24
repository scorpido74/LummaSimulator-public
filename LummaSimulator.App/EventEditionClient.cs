using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace LummaSimulator.App.Services;
public sealed record EventAction(string DemoRunId, string ActionType, long Sequence, DateTimeOffset OccurredAtUtc);
public sealed record EventSnapshot(string WpfRunId, string PortalRunId, string EventId, string EventName, string Status,
    DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, EventAction? Action);
public interface IEventEditionClient : IDisposable
{
    Task<EventSnapshot> PollAsync(string run, string code, CancellationToken ct);
}
public sealed class EventEditionClient : IEventEditionClient
{
    private readonly HttpClient _http;
    public EventEditionClient(string origin, HttpMessageHandler? testHandler = null)
    {
        // Same HTTPS/loopback transport boundary as the existing monitor; separate route and authorization.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || origin != uri.GetLeftPart(UriPartial.Authority)
            || (uri.Host != "localhost" && (uri.Host != "portal.example.test" || !uri.IsDefaultPort))) throw new ArgumentException("Invalid event origin.");
        _http = new(testHandler ?? new SocketsHttpHandler {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            ConnectCallback = async (context, ct) => {
                foreach (var ip in (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct)).Where(IPAddress.IsLoopback)) {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, true); }
                    catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
                }
                throw new HttpRequestException("Event origin must resolve to loopback.");
            }
        }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 16384 };
    }
    public async Task<EventSnapshot> PollAsync(string run, string code, CancellationToken ct)
    {
        if (!Guid.TryParseExact(run, "D", out _) || code.Length != 43 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new InvalidDataException("Invalid pairing.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event-edition/poll/" + run);
        request.Headers.Add("X-Lumma-Event-Code", code);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var value = JsonSerializer.Deserialize<EventSnapshot>(await response.Content.ReadAsStringAsync(ct), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException();
        if (value.WpfRunId != run || !Guid.TryParseExact(value.PortalRunId, "D", out _) || !Guid.TryParseExact(value.EventId, "D", out _)
            || string.IsNullOrWhiteSpace(value.EventName) || value.EventName.Length > 100 || value.EventName.Any(char.IsControl)
            || value.Status is not ("Active" or "Closed") || value.PlannedEnd <= value.PlannedStart
            || (value.Action is { } a && (a.DemoRunId != value.PortalRunId || a.Sequence != 1
                || a.ActionType is not ("ClickFixFollowSimulated" or "ClickFixStopSelected") || a.OccurredAtUtc.Offset != TimeSpan.Zero))) throw new InvalidDataException("Invalid event response.");
        return value;
    }
    public void Dispose() => _http.Dispose();
}
