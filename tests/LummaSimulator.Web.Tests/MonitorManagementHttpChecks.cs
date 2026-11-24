using System.Net;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Tests;
internal static class MonitorManagementHttpChecks
{
    private const string Page = "/Presenter/Integrations/Monitor";
    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    internal static async Task RunAsync(HttpClient admin, HttpClient visitor, string origin,
        Func<Task> reauthenticate, Action<string> rememberSecret)
    {
        async Task<string> Get() {
            using var response = await HttpRateLimitFixture.GetAsync(admin, Page);
            TestProgram.Check(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true
                && response.Headers.GetValues("Referrer-Policy").Single() == "same-origin", "Monitor GET retains same-origin form Origin and no-store");
            return await response.Content.ReadAsStringAsync();
        }
        Task<HttpResponseMessage> Change(string html, string operation, string? requestOrigin, string? csrf, bool confirmed = true, HttpClient? client = null) =>
            HttpRateLimitFixture.SendAsync(() => {
                var fields = new List<KeyValuePair<string,string>> { new("operation", operation), new("revision", Field(html, "revision")) };
                if (confirmed) fields.Add(new("confirmed", "true"));
                if (csrf is not null) fields.Add(new("__RequestVerificationToken", csrf));
                var request = new HttpRequestMessage(HttpMethod.Post, Page + "?handler=Change") { Content = new FormUrlEncodedContent(fields) };
                if (requestOrigin is not null) request.Headers.Add("Origin", requestOrigin);
                request.Headers.Add("Sec-Fetch-Site", "same-origin"); // Never sufficient on its own.
                return (client ?? admin).SendAsync(request);
            });
        async Task<string> Mutate(string html, string operation) {
            using var response = await Change(html, operation, origin, Field(html, "__RequestVerificationToken"));
            TestProgram.Check(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true
                && response.Headers.GetValues("Referrer-Policy").Single() == "same-origin", "Authorized native monitor POST returns protected same-origin response: " + operation);
            return await response.Content.ReadAsStringAsync();
        }
        var html = await Get();
        // The surrounding fixture deliberately starts with a legacy hash. Revoke only that synthetic key.
        await reauthenticate(); html = await Mutate(html, "revoke");
        await reauthenticate();
        var csrf = Field(html, "__RequestVerificationToken");
        TestProgram.Check(csrf.Length > 0, "Monitor form renders antiforgery token");
        foreach (var invalidOrigin in new string?[] { null, "null", "https://other.invalid" }) {
            using var denied = await Change(html, "generate", invalidOrigin, csrf);
            TestProgram.Check(denied.StatusCode == HttpStatusCode.BadRequest, "Monitor rejects missing/null/foreign Origin even with same-origin fetch metadata");
            TestProgram.Check((await denied.Content.ReadAsStringAsync()).Length == 0, "Invalid Origin reaches empty BadRequest response before key generation");
        }
        foreach (var invalidCsrf in new string?[] { null, "invalid-antiforgery" }) {
            using var denied = await Change(html, "generate", origin, invalidCsrf);
            TestProgram.Check(denied.StatusCode == HttpStatusCode.BadRequest, "Monitor rejects missing or invalid antiforgery with correct Origin");
        }
        using (var denied = await Change(html, "generate", origin, csrf, confirmed: false))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.BadRequest, "Monitor requires explicit confirmation");
        using (var denied = await Change(html, "generate", origin, csrf, client: visitor))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.Redirect, "Visitor cannot submit monitor management action");
        // Rejected requests must not consume the fresh proof or mutate revision/state.
        var issuedHtml = await Mutate(html, "generate");
        var issued = WebUtility.HtmlDecode(Regex.Match(issuedHtml, "<code id=\"monitor-issued-key\">([^<]+)</code>").Groups[1].Value);
        if (issued.Length > 0) rememberSecret(issued);
        TestProgram.Check(LummaSimulator.Web.Models.DemoIdentity.IsToken(issued), "Same-origin management generates one key after verified identity and rejected invalid attempts");
        async Task<HttpStatusCode> VerifyKey() {
            using var request = new HttpRequestMessage(HttpMethod.Head, "/api/local/session-status/00000000-0000-0000-0000-000000000000");
            request.Headers.Add("X-Lumma-Monitor-Key", issued);
            using var response = await admin.SendAsync(request); return response.StatusCode;
        }
        TestProgram.Check(await VerifyKey() == HttpStatusCode.NoContent, "Generated key is genuinely accepted by monitor API");
        html = await Get();
        TestProgram.Check(!html.Contains(issued), "GET never repeats issued monitor key");
        var withoutProof = await Mutate(html, "rotate");
        TestProgram.Check(!withoutProof.Contains("id=\"monitor-issued-key\"") && Field(withoutProof, "revision") == Field(html, "revision"), "Successful generation consumes fresh proof; replay cannot rotate key");
        await reauthenticate(); await Mutate(html, "revoke");
        TestProgram.Check(await VerifyKey() == HttpStatusCode.Unauthorized, "Monitor key revocation remains effective after fixed form flow");
    }
}
