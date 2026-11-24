using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LummaSimulator.LocalTesting;

var port=8080;
var managed=args.Length==2 && args[1]=="--managed";
if(args.Length>2 || (args.Length==2 && !managed) || (args.Length>0 && (!int.TryParse(args[0],out port) || port is <1 or >65535))) {
    Console.Error.WriteLine("Usage: LummaSimulator.LocalTestServer [port 1-65535] [--managed]");return 1;
}
using var stop=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
// A redirected private stdin pipe is the owner's lifetime lease. EOF also stops the server if its parent dies.
if(managed)_=Task.Run(async()=>{try {await Console.In.ReadLineAsync(stop.Token);stop.Cancel();}catch(OperationCanceledException) { }catch(IOException){stop.Cancel();}});
var instance=Guid.NewGuid().ToString("N");
using var listener=new TcpListener(IPAddress.Loopback,port);
try {
    listener.Start();Console.WriteLine($"Lumma synthetic receiver ready on IPv4 loopback port {port}.");
    while(!stop.IsCancellationRequested) {
        using var client=await listener.AcceptTcpClientAsync(stop.Token);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try {await HandleAsync(client,port,instance,timeout.Token);}
        catch(Exception e) when(e is IOException or OperationCanceledException or FormatException or InvalidDataException or JsonException) { }
    }
} catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
catch(SocketException) {Console.Error.WriteLine("Loopback port is unavailable.");return 1;}
return 0;

static async Task HandleAsync(TcpClient client,int port,string instance,CancellationToken token)
{
    await using var stream=client.GetStream();var bytes=new List<byte>();var buffer=new byte[4096];
    while(true) {
        var count=await stream.ReadAsync(buffer,token);if(count==0)throw new InvalidDataException();
        bytes.AddRange(buffer.Take(count));if(bytes.Count>65536)throw new InvalidDataException();
        var text=Encoding.UTF8.GetString(bytes.ToArray());var boundary=text.IndexOf("\r\n\r\n",StringComparison.Ordinal);if(boundary<0)continue;
        var lines=text[..boundary].Split("\r\n");
        var lengths=lines.Where(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).ToArray();
        var get=lines[0].StartsWith("GET /lumma-health?nonce=",StringComparison.Ordinal);
        int length=0;
        if(lengths.Length>1 || (!get && lengths.Length!=1) || (lengths.Length==1 && (!int.TryParse(lengths[0].Split(':',2)[1].Trim(),out length) || length is <0 or >49152)))throw new InvalidDataException();
        if(lines.Any(l=>l.StartsWith("Transfer-Encoding:",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException();
        var headerLength=Encoding.UTF8.GetByteCount(text[..(boundary+4)]);if(bytes.Count<headerLength+length)continue;
        var request=lines[0].Split(' ');var valid=false;var body="";var nonce="";
        if(get && length==0 && request.Length==3 && request[2]=="HTTP/1.1") {
            nonce=request[1]["/lumma-health?nonce=".Length..];valid=Guid.TryParseExact(nonce,"N",out _);
            if(valid)body=JsonSerializer.Serialize(new LocalServerIdentity(LocalTestServerProtocol.Product,LocalTestServerProtocol.Version,nonce,instance,Environment.ProcessId,port));
        } else if(lines[0]=="POST /lumma-demo HTTP/1.1") {
            var ids=lines.Where(l=>l.StartsWith("X-Lumma-DemoRunId:",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(ids.Length==1 && Guid.TryParseExact(ids[0].Split(':',2)[1].Trim(),"D",out var id)) {
                using var data=JsonDocument.Parse(bytes.Skip(headerLength).Take(length).ToArray());var root=data.RootElement;
                var expected=new Dictionary<string,string> { ["DemoRunId"]=id.ToString("D"),["Browser"]="Fictional Browser",["Profile"]="Demo profile",["Url"]="https://example.invalid/demo",["Username"]="demo@example.invalid",["Password"]="FAKE-PASSWORD-ONLY",["Cookie"]="FAKE-COOKIE-ONLY",["Token"]="FAKE-TOKEN-ONLY" };
                valid=root.ValueKind==JsonValueKind.Object && root.EnumerateObject().Count()==expected.Count && expected.All(e=>root.TryGetProperty(e.Key,out var v) && v.ValueKind==JsonValueKind.String && v.GetString()==e.Value);
            }
        }
        var identity=new LocalServerIdentity(LocalTestServerProtocol.Product,LocalTestServerProtocol.Version,nonce,instance,Environment.ProcessId,port);
        var payload=Encoding.UTF8.GetBytes(body);var status=valid?"200 OK":"400 Bad Request";
        var headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\n{LocalTestServerProtocol.IdentityHeader}: {LocalTestServerProtocol.Stamp(identity)}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers,token);await stream.WriteAsync(payload,token);return;
    }
}
