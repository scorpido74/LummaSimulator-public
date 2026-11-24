using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Tests;

// One-visitor baseline and QR/open-demo display regression using isolated synthetic accounts.
internal static class RoundVisitorBaseline
{
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,
        "name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    internal static async Task RunAsync(HttpClient presenter,HttpClient visitor,string origin,string root,string repository,bool verifyDisplay=false,bool openDemo=false)
    {
        string? eventId=null;var qr="/";
        if(!openDemo) {
        var newPage=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Prizes/New");
        var creationFields=new Dictionary<string,string>{
                ["__RequestVerificationToken"]=Field(newPage,"__RequestVerificationToken"),["OperationKey"]=Field(newPage,"OperationKey"),
                ["ProfileId"]="builtin-cybertruck",["Name"]="Synthetic visitor baseline",["start"]="true",["Input.GroupSize"]="2"};
        using var created=await HttpRateLimitFixture.SendAsync(async()=>{
            using var create=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Prizes/New"){Content=new FormUrlEncodedContent(creationFields)};
            create.Headers.Add("Origin",origin);return await presenter.SendAsync(create);
        });
        TestProgram.Check(created.StatusCode==HttpStatusCode.Redirect,"Baseline creates an isolated round event");
        eventId=created.Headers.Location!.ToString().Split('/').Last();
        qr=PrizeRoundsService.FixedJoinPath(eventId);
        }
        var landing=await visitor.GetStringAsync(qr);
        TestProgram.Check(landing.Contains("id=\"prize-round-entry\"")!=openDemo,openDemo?"Open-demo visitor uses the welcome page without an event":"One new visitor follows the fixed event QR to round one");
        var startFields=new Dictionary<string,string>{["__RequestVerificationToken"]=Field(landing,"__RequestVerificationToken"),["route"]="laptop"};
        if(eventId is not null){startFields["eventKind"]="prize";startFields["eventId"]=eventId;}
        using var start=new HttpRequestMessage(HttpMethod.Post,"/"){
            Content=new FormUrlEncodedContent(startFields)};
        start.Headers.Add("Origin",origin);
        using var started=await visitor.SendAsync(start);
        TestProgram.Check(started.StatusCode==HttpStatusCode.Redirect,"QR visitor starts an event-bound synthetic demo");
        var before=await visitor.GetStringAsync("/Demo");var csrf=Field(before,"__RequestVerificationToken");
        using var key=new SoftwareAuthenticator();
        async Task<JsonDocument> Ceremony(string operation)
        {
            visitor.DefaultRequestHeaders.Remove("X-Lumma-Operation");
            visitor.DefaultRequestHeaders.Add("X-Lumma-Operation",Guid.NewGuid().ToString("D"));
            async Task<HttpResponseMessage> Api(string step,object body)
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,"/api/webauthn/"+operation+"/"+step){
                    Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
                request.Headers.Add("Origin",origin);request.Headers.Add("X-CSRF-TOKEN",csrf);
                return await visitor.SendAsync(request);
            }
            using var options=await Api("options",new{});options.EnsureSuccessStatusCode();
            var json=await options.Content.ReadAsStringAsync();
            using var verified=await Api("verify",operation=="register"?key.Register(json,origin):key.Authenticate(json,origin));
            verified.EnsureSuccessStatusCode();return JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        }
        using var registered=await Ceremony("register");using var authenticated=await Ceremony("authenticate");
        var receipt=authenticated.RootElement;var round=receipt.GetProperty("round");
        var alias=round.ValueKind==JsonValueKind.Object?round.GetProperty("alias").GetString():null;
        var username=receipt.GetProperty("username").GetString()!;
        var after=await visitor.GetStringAsync("/Demo");
        var heading=Regex.Match(after,"<section class=\"demo-heading\">.*?</section>",RegexOptions.Singleline).Value;
        var sidebar=Regex.Match(after,"<aside class=\"panel session-panel\">.*?</aside>",RegexOptions.Singleline).Value;
        var script=await File.ReadAllTextAsync(Path.Combine(repository,"LummaSimulator.Web/wwwroot/js/passkeys.js"));
        TestProgram.Check(openDemo?alias is null:alias is {Length:>0}&&after.Contains(alias),openDemo?"Open demo receives no event alias":"Authenticated QR visitor receives an alias in the original round");
        var title=WebUtility.HtmlDecode(Regex.Match(after,"<title[^>]*>(.*?)</title>",RegexOptions.Singleline).Groups[1].Value);
        var confirmation=WebUtility.HtmlDecode(Regex.Match(after,"<dd id=\"verified-username\">([^<]*)</dd>").Groups[1].Value);
        if(verifyDisplay) {
            var displayName=alias??username;
            TestProgram.Check((heading.Contains("Welcome")||heading.Contains("Welkom"))&&sidebar.Contains(displayName)&&confirmation==displayName,
                openDemo?"Open-demo welcome and sidebar retain the display identity after reload":"QR event welcome and sidebar retain the same alias after reload");
            TestProgram.Check(title.Contains(displayName),
                openDemo?"Authenticated open-demo title welcomes the same alias":"Authenticated QR browser title welcomes the same alias");
            if(!openDemo)TestProgram.Check(alias==username,"QR display and credential registration preserve the same alias");
            using var repeated=await Ceremony("authenticate");
            var repeatedRound=repeated.RootElement.GetProperty("round");
            TestProgram.Check(openDemo?repeatedRound.ValueKind==JsonValueKind.Null:repeatedRound.GetProperty("alias").GetString()==alias,
                "Repeated authentication preserves the display identity for "+(openDemo?"open demo":"fixed QR"));
            var reloaded=await visitor.GetStringAsync("/Demo");
            TestProgram.Check(reloaded.Contains("data-visitor-display-name>"+WebUtility.HtmlEncode(displayName)),"Display identity remains stable after a second reload");
        }
        var observation=new {
            RoundPanelPresentBeforeAuthentication=before.Contains("id=\"prize-round-status\""),
            AliasInAuthenticationReceipt=alias is {Length:>0},
            AliasInRoundPanelAfterReload=alias is not null&&after.Contains("id=\"prize-round-status\"")&&after.Contains(alias),
            HeadingStillUsesVisitorName=heading.Contains(username),SidebarStillUsesVisitorName=sidebar.Contains(username),
            AuthenticationConfirmationStillUsesVisitorName=script.Contains("\"verified-username\").textContent = result.username",StringComparison.Ordinal),
            UsesSyntheticVisitorName=username.StartsWith("Visitor-",StringComparison.Ordinal)
        };
        var output=JsonSerializer.Serialize(observation);
        Console.WriteLine((verifyDisplay?"STEP5_VISITOR_DISPLAY=":"STEP5_VISITOR_BASELINE=")+output);
        await File.WriteAllTextAsync(Path.Combine(root,verifyDisplay?(openDemo?"visitor-display-open.json":"visitor-display-round.json"):"visitor-baseline.json"),output);
    }
}
