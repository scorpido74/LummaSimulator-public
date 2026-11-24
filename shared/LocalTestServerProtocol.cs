using System.Net.Http;
using System.Text.Json;

namespace LummaSimulator.LocalTesting;

public sealed record LocalServerIdentity(string Product, int Protocol, string Nonce, string Instance, int Pid, int Port);

public static class LocalTestServerProtocol
{
    public const string Product = "LummaSimulator.LocalTestServer";
    public const int Version = 1;
    public const string IdentityHeader = "X-Lumma-TestServer";
    public static string Stamp(LocalServerIdentity identity) => $"{Product}/{Version}/{identity.Instance}";
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { UseProxy=false, AllowAutoRedirect=false, UseCookies=false }) { Timeout=TimeSpan.FromSeconds(2) };

    public static async Task<LocalServerIdentity?> ProbeAsync(HttpClient http, int port, CancellationToken token)
    {
        var nonce=Guid.NewGuid().ToString("N");
        try {
            using var response=await http.GetAsync($"http://127.0.0.1:{port}/lumma-health?nonce={nonce}",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType!="application/json")return null;
            await response.Content.LoadIntoBufferAsync(2048,token).ConfigureAwait(false);
            var identity=JsonSerializer.Deserialize<LocalServerIdentity>(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            return identity is {Product:Product,Protocol:Version,Pid:>0} && identity.Port==port && identity.Nonce==nonce
                && Guid.TryParseExact(identity.Instance,"N",out _)
                && response.Headers.TryGetValues(IdentityHeader,out var headers) && headers.SingleOrDefault()==Stamp(identity) ? identity : null;
        } catch(Exception e) when(e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException) {
            token.ThrowIfCancellationRequested();return null;
        }
    }
}
