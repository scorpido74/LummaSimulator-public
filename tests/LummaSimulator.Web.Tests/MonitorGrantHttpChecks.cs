using System.Net;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Tests;
internal static class MonitorGrantHttpChecks
{
    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static string GrantForm(string html, string id) => Regex.Matches(html, "<form\\b[^>]*action=\"[^\"]*handler=Monitor\"[^>]*>.*?</form>", RegexOptions.Singleline)
        .Cast<Match>().Select(m => m.Value).Single(form => Field(form, "id") == id);
    private static Task<HttpResponseMessage> Post(HttpClient client, string id, string? enabled, string csrf, string? origin, bool duplicate = false) =>
        HttpRateLimitFixture.SendAsync(() => {
            var fields = new List<KeyValuePair<string,string>> { new("id", id), new("__RequestVerificationToken", csrf) };
            if (enabled is not null) fields.Add(new("enabled", enabled));
            if (duplicate) fields.Add(new("enabled", "false"));
            var request = new HttpRequestMessage(HttpMethod.Post, "/Presenter?handler=Monitor") { Content = new FormUrlEncodedContent(fields) };
            if (origin is not null) request.Headers.Add("Origin", origin);
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            return client.SendAsync(request);
        });
    private static async Task<string> Dashboard(HttpClient client) {
        using var response = await HttpRateLimitFixture.GetAsync(client, "/Presenter");
        TestProgram.Check(response.StatusCode == HttpStatusCode.OK && response.Headers.GetValues("Referrer-Policy").Single() == "same-origin", "Dashboard native form preserves exact same-origin Origin");
        return await response.Content.ReadAsStringAsync();
    }
    internal static async Task AllowAsync(HttpClient presenter, string id, string origin, Func<Task<HttpStatusCode>> metadataStatus,Action<string> receiveCapability)
    {
        var html = await Dashboard(presenter); var form = GrantForm(html, id);
        TestProgram.Check(Field(form, "enabled") == "true" && form.Contains("data-i18n=\"Toestaan\""), "Actual Razor allow form renders string true, never boolean value marker");
        foreach (var invalidOrigin in new string?[] { null, "null", "https://other.invalid" }) {
            using var response = await Post(presenter, id, "true", Field(html, "__RequestVerificationToken"), invalidOrigin);
            TestProgram.Check(response.StatusCode == HttpStatusCode.Redirect, "Rejected grant Origin redirects for visible error, not success");
            html = await Dashboard(presenter);
            TestProgram.Check(html.Contains("role=\"alert\"") && html.Contains("data-i18n=\"MonitorGrantOriginInvalid\"") && await metadataStatus() == HttpStatusCode.NotFound,
                "Missing/null/foreign Origin cannot grant even with same-origin fetch metadata; error is visible");
        }
        using (var response = await Post(presenter, id, "true", "invalid-csrf", origin))
            TestProgram.Check(response.StatusCode == HttpStatusCode.BadRequest && await metadataStatus() == HttpStatusCode.NotFound, "Grant action retains Razor antiforgery protection");
        using (var response = await Post(presenter, id, Field(form, "enabled"), Field(html, "__RequestVerificationToken"), origin)) {
            var issued=await response.Content.ReadAsStringAsync();var match=Regex.Match(issued,"id=\"monitor-issued-key\">([A-Za-z0-9_-]{43})</code>");
            TestProgram.Check(response.StatusCode == HttpStatusCode.OK && match.Success,"Valid allow action displays the run-scoped capability once");
            receiveCapability(match.Groups[1].Value);
        }
        html = await Dashboard(presenter); form = GrantForm(html, id);
        TestProgram.Check(Field(form, "enabled") == "false" && form.Contains("data-i18n=\"Intrekken\"") && await metadataStatus() == HttpStatusCode.OK,
            "Persisted grant is read after redirect and permits only approved session metadata");
        foreach (var invalidEnabled in new string?[] { "value", "", null, "1" }) {
            using var response = await Post(presenter, id, invalidEnabled, Field(html, "__RequestVerificationToken"), origin);
            html = await Dashboard(presenter);
            TestProgram.Check(response.StatusCode == HttpStatusCode.Redirect && html.Contains("data-i18n=\"MonitorGrantInvalid\"")
                && Field(GrantForm(html, id), "enabled") == "false" && await metadataStatus() == HttpStatusCode.OK,
                "Malformed/missing enabled cannot silently revoke an existing grant and produces visible error");
        }
        using (var response = await Post(presenter, id, "true", Field(html, "__RequestVerificationToken"), origin, duplicate: true)) {
            html = await Dashboard(presenter);
            TestProgram.Check(response.StatusCode == HttpStatusCode.Redirect && html.Contains("data-i18n=\"MonitorGrantInvalid\"") && await metadataStatus() == HttpStatusCode.OK, "Duplicate enabled values cannot change monitor permission");
        }
        using (var response = await Post(presenter, id, "false", Field(html, "__RequestVerificationToken"), "null")) {
            html = await Dashboard(presenter);
            TestProgram.Check(response.StatusCode == HttpStatusCode.Redirect && html.Contains("data-i18n=\"MonitorGrantOriginInvalid\"") && await metadataStatus() == HttpStatusCode.OK, "Invalid Origin cannot revoke existing grant either");
        }
    }
    internal static async Task RevokeAsync(HttpClient presenter, string id, string origin)
    {
        var html = await Dashboard(presenter); var form = GrantForm(html, id);
        using var response = await Post(presenter, Field(form, "id"), Field(form, "enabled"), Field(html, "__RequestVerificationToken"), origin);
        TestProgram.Check(response.StatusCode == HttpStatusCode.Redirect, "Valid revoke follows Post Redirect Get");
        html = await Dashboard(presenter); form = GrantForm(html, id);
        TestProgram.Check(Field(form, "enabled") == "true" && form.Contains("data-i18n=\"Toestaan\""), "Revoked grant is absent when dashboard reads it back");
    }
}
