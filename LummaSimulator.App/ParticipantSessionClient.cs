using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LummaSimulator.App.Services;

public sealed record AppConnection(string RequestId,string Key,DateTimeOffset ExpiresAt);
public sealed record AppConnectionStatus(string Status,DateTimeOffset ExpiresAt);
public sealed record RoundEvent(string Id,string Name);
public sealed record RoundParticipant(string Id,int Round,string Alias,string ConsentStatus,bool CanOpen);
public sealed record RoundRoster(string EventId,long SelectionVersion,IReadOnlyList<RoundParticipant> Participants);
public sealed record ConsentReply(string? RequestId,string Status,string? EventName,int? Round,string? Alias,DateTimeOffset? ExpiresAt);
public sealed record OpenReply(string ViewId,string Token,DateTimeOffset ExpiresAt);
public sealed record HelperWebCompatibility(int[] Protocols,string WebVersion);
public interface IParticipantSessionClient:IDisposable
{
    string? PortalOrigin => null;
    Task<AppConnection> BeginAsync(CancellationToken ct);
    Uri ApprovalUri(string id);
    Task<AppConnectionStatus> ConnectionAsync(AppConnection connection,CancellationToken ct);
    Task<IReadOnlyList<RoundEvent>> EventsAsync(AppConnection connection,CancellationToken ct);
    Task SelectAsync(AppConnection connection,string eventId,long version,CancellationToken ct);
    Task<RoundRoster> RosterAsync(AppConnection connection,string eventId,CancellationToken ct);
    Task<ConsentReply> RequestConsentAsync(AppConnection connection,string eventId,string participantId,string runId,CancellationToken ct);
    Task<Uri> OpenAsync(AppConnection connection,string eventId,string participantId,CancellationToken ct);
    Task DisconnectAsync(AppConnection connection,CancellationToken ct);
    Task CancelAsync(AppConnection connection,string runId,CancellationToken ct);
}
public sealed class ParticipantSessionClient:IParticipantSessionClient
{
    private readonly HttpClient _http;
    private readonly string _prefix;
    public string PortalOrigin => _http.BaseAddress!.GetLeftPart(UriPartial.Authority);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private static bool Id(string value)=>Guid.TryParseExact(value,"D",out _);
    private static bool Token(string value)=>value is {Length:43}&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
    private static bool Label(string value,int limit)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=limit&&!value.Any(char.IsControl);
    public ParticipantSessionClient(string origin,HttpMessageHandler? testHandler=null,bool remote=false)
    {
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0
            ||origin!=uri.GetLeftPart(UriPartial.Authority)||(!remote&&uri.Host!="localhost"&&(uri.Host!="portal.example.test"||!uri.IsDefaultPort)))throw new ArgumentException("Invalid portal origin.");
        _prefix=remote?"/api/helper/participant-demo":"/api/local/participant-demo";
        _http=new(testHandler??(remote?new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}:new SocketsHttpHandler{
            UseProxy=false,UseCookies=false,AllowAutoRedirect=false,
            ConnectCallback=async(context,ct)=>{
                foreach(var ip in (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,ct)).Where(IPAddress.IsLoopback)) {
                    var socket=new Socket(ip.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(ip,context.DnsEndPoint.Port),ct);return new NetworkStream(socket,true); }
                    catch { socket.Dispose();if(ct.IsCancellationRequested)throw; }
                }
                throw new HttpRequestException("Portal must resolve to loopback.");
            }
        })){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(5),MaxResponseContentBufferSize=65536};
    }
    private string Path(AppConnection c)
    { if(!Id(c.RequestId)||!Token(c.Key))throw new InvalidDataException("Invalid app connection.");return _prefix+"/connections/"+c.RequestId; }
    private string EventPath(AppConnection c,string eventId)
    { if(!Id(eventId))throw new InvalidDataException("Invalid event.");return Path(c)+"/events/"+eventId; }
    private async Task<T> Send<T>(HttpMethod method,string path,AppConnection? connection,object? body,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,path);request.Headers.Add("X-Lumma-App-Protocol","1");
        if(connection is not null)request.Headers.Add("X-Lumma-App-Key",connection.Key);
        if(body is not null)request.Content=JsonContent.Create(body,options:Json);
        using var response=await _http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(ct),Json)??throw new InvalidDataException("Invalid portal reply.");
    }
    public async Task<AppConnection> BeginAsync(CancellationToken ct)
    {
        var c=await Send<AppConnection>(HttpMethod.Post,_prefix+"/connections",null,new{},ct);
        if(!Id(c.RequestId)||!Token(c.Key)||c.ExpiresAt.Offset!=TimeSpan.Zero)throw new InvalidDataException("Invalid connection reply.");return c;
    }
    public Task<HelperWebCompatibility> CompatibilityAsync(CancellationToken ct) => Send<HelperWebCompatibility>(HttpMethod.Get, _prefix + "/compatibility", null, null, ct);
    public async Task HelperHeartbeatAsync(AppConnection connection,string id,string identity,string device,string version,string pairing,string health,CancellationToken ct)
    {
        _=await Send<JsonElement>(HttpMethod.Post,Path(connection)+"/heartbeat",connection,new{id,identity,device,version,pairing,health},ct);
    }
    public Uri ApprovalUri(string id)
    { if(!Id(id))throw new InvalidDataException();return new(_http.BaseAddress!,(_prefix.StartsWith("/api/helper/",StringComparison.Ordinal)?"/Presenter/HelperConnection":"/Presenter/AppConnection")+"?requestId="+id); }
    public async Task<AppConnectionStatus> ConnectionAsync(AppConnection connection,CancellationToken ct)
    {
        var status=await Send<AppConnectionStatus>(HttpMethod.Get,Path(connection),connection,null,ct);
        if(status.Status is not ("Pending" or "Ready")||status.ExpiresAt.Offset!=TimeSpan.Zero)throw new InvalidDataException();return status;
    }
    public async Task<IReadOnlyList<RoundEvent>> EventsAsync(AppConnection connection,CancellationToken ct)
    {
        var rows=await Send<List<RoundEvent>>(HttpMethod.Get,Path(connection)+"/events",connection,null,ct);
        if(rows.Count>100||rows.Any(r=>!Id(r.Id)||!Label(r.Name,100))||rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)throw new InvalidDataException();return rows;
    }
    private sealed record SelectionReply(string EventId,long SelectionVersion);
    public async Task SelectAsync(AppConnection connection,string eventId,long version,CancellationToken ct)
    {
        _=EventPath(connection,eventId);
        var selected=await Send<SelectionReply>(HttpMethod.Post,Path(connection)+"/selection",connection,new{eventId,version},ct);
        if(selected.EventId!=eventId||selected.SelectionVersion!=version)throw new InvalidDataException();
    }
    public async Task<RoundRoster> RosterAsync(AppConnection connection,string eventId,CancellationToken ct)
    {
        var rows=await Send<RoundRoster>(HttpMethod.Get,EventPath(connection,eventId)+"/participants",connection,null,ct);
        if(rows.EventId!=eventId||rows.SelectionVersion<1||rows.Participants is null||rows.Participants.Any(r=>r is null)||rows.Participants.Count>200||rows.Participants.Select(r=>r.Id).Distinct().Count()!=rows.Participants.Count
            ||rows.Participants.Any(r=>!Id(r.Id)||r.Round<1||!Label(r.Alias,40)||r.ConsentStatus is not ("None" or "Pending" or "Granted" or "Declined" or "Revoked" or "Expired")
                ||r.CanOpen&&r.ConsentStatus!="Granted"))throw new InvalidDataException();return rows;
    }
    public async Task<ConsentReply> RequestConsentAsync(AppConnection connection,string eventId,string participantId,string runId,CancellationToken ct)
    {
        if(!Id(participantId)||!Id(runId))throw new InvalidDataException();
        var reply=await Send<ConsentReply>(HttpMethod.Post,EventPath(connection,eventId)+"/consent",connection,new{participantId,wpfRunId=runId},ct);
        if(reply.RequestId is null||!Id(reply.RequestId)||reply.Status is not ("Pending" or "Granted")||reply.ExpiresAt is null||reply.ExpiresAt.Value.Offset!=TimeSpan.Zero
            ||reply.Round is null or <1||reply.Alias is null||!Label(reply.Alias,40)||reply.EventName is null||!Label(reply.EventName,100))throw new InvalidDataException();return reply;
    }
    public async Task<Uri> OpenAsync(AppConnection connection,string eventId,string participantId,CancellationToken ct)
    {
        if(!Id(participantId))throw new InvalidDataException();
        var reply=await Send<OpenReply>(HttpMethod.Post,EventPath(connection,eventId)+"/open",connection,new{participantId},ct);
        if(!Id(reply.ViewId)||!Token(reply.Token)||reply.ExpiresAt.Offset!=TimeSpan.Zero)throw new InvalidDataException();
        return new(_http.BaseAddress!,"/SessionDemo/"+reply.ViewId+"#open="+reply.Token);
    }
    public async Task DisconnectAsync(AppConnection connection,CancellationToken ct)
    { _=await Send<JsonElement>(HttpMethod.Post,Path(connection)+"/disconnect",connection,new{},ct); }
    public async Task CancelAsync(AppConnection connection,string runId,CancellationToken ct)
    { if(!Id(runId))throw new InvalidDataException();_=await Send<JsonElement>(HttpMethod.Post,Path(connection)+"/cancel",connection,new{wpfRunId=runId},ct); }
    public void Dispose()=>_http.Dispose();
}
