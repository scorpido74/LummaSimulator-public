using System.Net;
using System.Text.Json;
using System.Security.Cryptography;

namespace LummaSimulator.Helper;

// This listener does not provision HTTP.sys, certificates, firewall rules or accounts.
public sealed class ReceiverHost : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string> _pairingHash;
    private readonly Func<Task<ReceiverInfo>> _info;
    private readonly Func<HelperSelection, Task<SelectionReceipt>> _selection;
    private readonly string _releases;
    private readonly Func<bool, Task<bool>>? _updateLease;
    private readonly Func<CancellationToken,Task<ReceiverQrState>>? _currentSelection;
    private readonly Func<string,CancellationToken,Task<bool>>? _openParticipants;
    private Task? _loop;
    public ReceiverHost(string origin, Func<string> pairingHash, Func<Task<ReceiverInfo>> info, Func<HelperSelection, Task<SelectionReceipt>> selection, string releases, Func<bool, Task<bool>>? updateLease = null, Func<CancellationToken,Task<ReceiverQrState>>? currentSelection = null,Func<string,CancellationToken,Task<bool>>? openParticipants=null)
    {
        var uri = HelperProtocol.Origin(origin);
        if (!uri.IsDefaultPort) throw new InvalidDataException("Receiver requires TCP 443.");
        _listener.Prefixes.Add(origin + HelperProtocol.Prefix); _pairingHash = pairingHash; _info = info; _selection = selection; _releases = Path.GetFullPath(releases); _updateLease = updateLease; _currentSelection = currentSelection;
        _openParticipants=openParticipants;
    }
    public void Start() { _listener.Start(); _loop = Loop(); }
    private async Task Loop()
    {
        while (!_stop.IsCancellationRequested) {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or OperationCanceledException) { break; }
            // Serialized, bounded requests; no unlimited task creation from untrusted clients.
            await Handle(context);
        }
    }
    private async Task Handle(HttpListenerContext c)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            var r = c.Request; var supplied = r.Headers["X-Lumma-Pairing"]; var hash = _pairingHash();
            var path = r.Url!.AbsolutePath;
            if (r.IsSecureConnection && r.HttpMethod == "GET" && path is "/lumma-helper/download/helper.zip" or "/lumma-helper/download/helper-release.json") {
                // Public, fixed release files contain no installation settings, pairing or visitor data.
                var manifest = Path.Combine(_releases, "helper-release.json");
                var file = path.EndsWith(".zip", StringComparison.Ordinal) ? Path.Combine(_releases, "helper-package.zip") : manifest;
                if (!File.Exists(manifest) || !File.Exists(file)) { c.Response.StatusCode = 404; return; }
                c.Response.ContentType = path.EndsWith(".zip", StringComparison.Ordinal) ? "application/zip" : "application/json";
                await using var release = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                c.Response.ContentLength64 = release.Length; await release.CopyToAsync(c.Response.OutputStream, deadline.Token); return;
            }
            if (!r.IsSecureConnection || r.Headers["Origin"] is not null || r.Headers["Sec-Fetch-Site"] is not null || r.Headers["Forwarded"] is not null
                || !HelperProtocol.IsToken(supplied) || !HelperProtocol.IsHash(hash)
                || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(HelperProtocol.Hash(supplied!)), Convert.FromHexString(hash))) { c.Response.StatusCode = 403; return; }
            c.Response.Headers["Cache-Control"] = "no-store";
            // Update v1 stays independent of the demo protocol so older supported helpers can recover compatibility.
            if (r.HttpMethod == "GET" && path is "/lumma-helper/updates/v1/manifest" or "/lumma-helper/updates/v1/package.zip") {
                var file = Path.Combine(_releases, path.EndsWith("manifest", StringComparison.Ordinal) ? "helper-release.json" : "helper-package.zip");
                if (!File.Exists(file)) { c.Response.StatusCode = 404; return; }
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                c.Response.ContentLength64 = input.Length; c.Response.ContentType = path.EndsWith("manifest", StringComparison.Ordinal) ? "application/json" : "application/zip";
                await input.CopyToAsync(c.Response.OutputStream, deadline.Token); return;
            }
            if (r.Headers["X-Lumma-Helper-Protocol"] != "1") { c.Response.StatusCode = 409; return; }
            object reply;
            if (r.HttpMethod == "GET" && path == "/lumma-helper/v1/info") reply = await _info();
            else if(r.HttpMethod=="GET"&&path=="/lumma-helper/v1/selection"&&_currentSelection is not null)reply=await _currentSelection(deadline.Token).WaitAsync(deadline.Token);
            else if(r.HttpMethod=="POST"&&path=="/lumma-helper/v1/open-participants"&&_openParticipants is not null){
                if(r.ContentLength64 is <1 or >256){c.Response.StatusCode=400;return;}
                var bytes=new byte[(int)r.ContentLength64];await r.InputStream.ReadExactlyAsync(bytes,deadline.Token);
                var request=JsonSerializer.Deserialize<OpenParticipantsRequest>(bytes,HelperProtocol.Json)??throw new InvalidDataException();
                if(!Guid.TryParseExact(request.EventId,"D",out _))throw new InvalidDataException();
                reply=new OpenParticipantsReceipt(await _openParticipants(request.EventId,deadline.Token).WaitAsync(deadline.Token));
            }
            else if (r.HttpMethod == "POST" && path is "/lumma-helper/v1/update-lease" or "/lumma-helper/v1/update-release") {
                if (_updateLease is null || !await _updateLease(path.EndsWith("update-lease", StringComparison.Ordinal))) { c.Response.StatusCode = 409; return; }
                reply = new { granted = true };
            }
            else if (r.HttpMethod == "POST" && path == "/lumma-helper/v1/selection") {
                if (r.ContentLength64 is < 1 or > 16384) { c.Response.StatusCode = 400; return; }
                var buffer = new byte[(int)r.ContentLength64]; await r.InputStream.ReadExactlyAsync(buffer, deadline.Token);
                var request = JsonSerializer.Deserialize<HelperSelection>(buffer, HelperProtocol.Json) ?? throw new InvalidDataException();
                if (request.Protocol != 1 || !Guid.TryParseExact(request.ConnectionId, "D", out _) || !Guid.TryParseExact(request.EventId, "D", out _) || !HelperProtocol.IsToken(request.ConnectionKey)) throw new InvalidDataException();
                if ((await _info()).DemoActive) { c.Response.StatusCode = 409; return; }
                reply = await _selection(request);
            } else { c.Response.StatusCode = 404; return; }
            c.Response.ContentType = "application/json"; await JsonSerializer.SerializeAsync(c.Response.OutputStream, reply, HelperProtocol.Json, deadline.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { try { c.Response.StatusCode = e is InvalidDataException or JsonException ? 400 : 503; } catch { } }
        finally { c.Response.Close(); }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Close(); if (_loop is not null) await _loop; _stop.Dispose(); }
}
