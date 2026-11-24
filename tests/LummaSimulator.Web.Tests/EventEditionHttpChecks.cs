using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Tests;
internal static class EventEditionHttpChecks
{
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    public static async Task<string> RunAsync(HttpClient visitor, HttpClient presenter, string origin, Func<Task> browserChoice)
    {
        const string page = "/Presenter/EventEdition";
        TestProgram.Check((await visitor.GetAsync(page)).StatusCode == HttpStatusCode.Redirect, "Event Edition management rejects visitor authorization");
        async Task<HttpResponseMessage> Post(params (string Name, string Value)[] values)
        {
            var html = await presenter.GetStringAsync(page);
            var message = new HttpRequestMessage(HttpMethod.Post, page) { Content = new FormUrlEncodedContent(values.Select(v => new KeyValuePair<string, string>(v.Name, v.Value))
                .Append(new("__RequestVerificationToken", Csrf(html)))) };
            message.Headers.Add("Origin", origin); return await presenter.SendAsync(message);
        }
        var create = await Post(("operation", "create"), ("name", "Synthetic exhibition"), ("start", "2026-11-06T09:00"), ("end", "2026-11-06T18:00"), ("confirmed", "true"));
        TestProgram.Check(create.StatusCode == HttpStatusCode.Redirect, "Operator schedules event using existing WebAuthn session and antiforgery");
        var planned = await presenter.GetStringAsync(page);
        var eventId = Regex.Match(planned, "name=\"id\" value=\"([^\"]+)\"").Groups[1].Value;
        TestProgram.Check(Guid.TryParse(eventId, out _), "Scheduled event stored outside SQLite and rendered with stable reference");
        using(var scheduled=await visitor.GetAsync("/"))
            TestProgram.Check(scheduled.StatusCode==HttpStatusCode.OK&&scheduled.Headers.CacheControl?.NoStore==true,"No discoverable live event keeps the ordinary start page without caching");
        TestProgram.Check((await Post(("operation", "start"), ("id", eventId), ("confirmed", "true"))).StatusCode == HttpStatusCode.Redirect, "Operator explicitly starts exhibition");
        using(var single=await visitor.GetAsync("/"))
            TestProgram.Check(single.StatusCode==HttpStatusCode.Redirect&&single.Headers.CacheControl?.NoStore==true
                &&single.Headers.Location?.AbsoluteUri==origin+"/Index?eventKind=edition&eventId="+eventId,"Exactly one accessible event uses its temporary visitor URL");
        async Task<HttpResponseMessage> Manage(params (string Key,string Value)[] fields)
        {
            var management=await HttpRateLimitFixture.GetStringAsync(presenter,"/Presenter/Events");
            return await HttpRateLimitFixture.SendAsync(async()=>{
                using var request=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Events"){
                    Content=new FormUrlEncodedContent(fields.Select(field=>new KeyValuePair<string,string>(field.Key,field.Value))
                        .Append(new("__RequestVerificationToken",Csrf(management))).Append(new("confirmed","true")))};
                request.Headers.Add("Origin",origin);
                return await presenter.SendAsync(request);
            });
        }
        using(var secondEvent=await Manage(("operation","create"),("kind","experience"),("name","Synthetic redirect choice")))
        {
            var id=Regex.Match(secondEvent.Headers.Location?.OriginalString??"",@"[?&]id=([0-9a-f-]{36})").Groups[1].Value;
            TestProgram.Check(secondEvent.StatusCode==HttpStatusCode.Redirect&&Guid.TryParse(id,out _),"Presenter creates a second discoverable event");
            using(var multiple=await visitor.GetAsync("/"))
                TestProgram.Check(multiple.StatusCode==HttpStatusCode.Redirect&&multiple.Headers.Location?.Query==$"?eventKind=experience&eventId={id}",
                    "The active Experience default takes precedence over the separate discoverable exhibition");
            using var closed=await Manage(("operation","close"),("kind","experience"),("id",id));
            TestProgram.Check(closed.StatusCode==HttpStatusCode.Redirect,"Presenter closes the second discoverable event");
        }
        // A separate new visitor explicitly enters this event before WebAuthn registration.
        var landing=await visitor.GetStringAsync("/?eventKind=edition&eventId="+eventId);
        async Task Routes(params string[] enabled)
        {
            var management="/Presenter/Events?kind=edition&id="+eventId;
            var html=await presenter.GetStringAsync(management);
            using var request=new HttpRequestMessage(HttpMethod.Post,"/Presenter/Events") { Content=new FormUrlEncodedContent(new Dictionary<string,string> {
                ["kind"]="edition",["id"]=eventId,["operation"]="login-routes",["confirmed"]="true",["__RequestVerificationToken"]=Csrf(html)
            }.Concat(enabled.Select(route=>new KeyValuePair<string,string>(route,"true")))) };
            request.Headers.Add("Origin",origin);
            using var response=await presenter.SendAsync(request);
            TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect,"Presenter saves event sign-in routes using existing CSRF and event authorization");
        }
        TestProgram.Check(Regex.Matches(landing,"class=\"choice-card").Count==4,"Existing event initially offers all four routes");
        await Routes("laptop");
        landing=await visitor.GetStringAsync("/?eventKind=edition&eventId="+eventId);
        TestProgram.Check(Regex.Matches(landing,"class=\"choice-card").Count==1&&landing.Contains("value=\"laptop\""),"Single enabled route renders one card");
        using(var direct=new HttpRequestMessage(HttpMethod.Post,"/"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"route","phone"},{"eventKind","edition"},{"eventId",eventId},{"__RequestVerificationToken",Csrf(landing)}})}){
            direct.Headers.Add("Origin",origin);using var response=await visitor.SendAsync(direct);
            TestProgram.Check(response.StatusCode==HttpStatusCode.OK&&!(await response.Content.ReadAsStringAsync()).Contains("data-method=\"phone\""),"Direct POST cannot admit a disabled route");
        }
        await Routes();
        using(var unavailable=await visitor.GetAsync("/"))
            TestProgram.Check(unavailable.StatusCode==HttpStatusCode.OK,"An event without enabled routes is excluded from automatic selection");
        landing=await visitor.GetStringAsync("/?eventKind=edition&eventId="+eventId);
        TestProgram.Check(Regex.Matches(landing,"class=\"choice-card").Count==0&&landing.Contains("LoginRoutesUnavailable"),"No enabled routes shows the localized assistance message");
        await Routes("laptop","phone");
        landing=await visitor.GetStringAsync("/?eventKind=edition&eventId="+eventId);
        TestProgram.Check(Regex.Matches(landing,"class=\"choice-card").Count==2,"Multiple enabled routes render matching card count");
        using(var admission=new HttpRequestMessage(HttpMethod.Post,"/"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"route","laptop"},{"eventKind","edition"},{"eventId",eventId},{"__RequestVerificationToken",Csrf(landing)}})}){
            admission.Headers.Add("Origin",origin);TestProgram.Check((await visitor.SendAsync(admission)).StatusCode==HttpStatusCode.Redirect,"Exhibition visitor admission uses explicit event context");
        }
        var demoPage=await visitor.GetStringAsync("/Demo");
        await Routes();
        TestProgram.Check((await visitor.GetAsync("/Demo")).StatusCode==HttpStatusCode.OK,"Existing demo resumes when all offered routes are disabled");
        using(var resume=new HttpRequestMessage(HttpMethod.Post,"/"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"route","laptop"},{"eventKind","edition"},{"eventId",eventId},{"__RequestVerificationToken",Csrf(demoPage)}})}){
            resume.Headers.Add("Origin",origin);using var response=await visitor.SendAsync(resume);
            TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect,"Existing demo resumes without creating a new demo when all routes are disabled");
        }
        await Routes("yubikey","laptop","phone","ipad");
        using(var returning=await visitor.GetAsync("/"))
            TestProgram.Check(returning.StatusCode==HttpStatusCode.OK,"A visitor with an active demo keeps the ordinary entry page");
        var webAuthnOperation="";
        async Task<HttpResponseMessage> Api(string operation,object body){if(operation.EndsWith("/options",StringComparison.Ordinal))webAuthnOperation=Guid.NewGuid().ToString("D");using var request=new HttpRequestMessage(HttpMethod.Post,"/api/webauthn/"+operation){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};request.Headers.Add("Origin",origin);request.Headers.Add("X-CSRF-TOKEN",Csrf(demoPage));request.Headers.Add("X-Lumma-Operation",webAuthnOperation);return await visitor.SendAsync(request);}
        using var key=new SoftwareAuthenticator();
        using(var options=await Api("register/options",new{})){using var registered=await Api("register/verify",key.Register(await options.Content.ReadAsStringAsync(),origin));TestProgram.Check(registered.IsSuccessStatusCode,"Scoped exhibition visitor registers passkey");}
        using(var options=await Api("authenticate/options",new{})){using var authenticated=await Api("authenticate/verify",key.Authenticate(await options.Content.ReadAsStringAsync(),origin));TestProgram.Check(authenticated.IsSuccessStatusCode,"Scoped exhibition visitor authenticates passkey");}
        // Only this synthetic cookie is passed to the isolated browser fixture.
        var accountPage=await visitor.GetStringAsync("/Demo");
        var portalRun=Regex.Match(accountPage,"data-demo-run-id=\"([^\"]+)\"").Groups[1].Value;
        TestProgram.Check(Guid.TryParse(portalRun,out _),"Scoped demo page exposes its public run reference");
        var wpf = Guid.NewGuid().ToString("D");
        var link = await Post(("operation", "link"), ("id", eventId), ("portalRun", portalRun), ("wpfRun", wpf), ("confirmed", "true"));
        var linked = await link.Content.ReadAsStringAsync();
        var code = Regex.Match(linked, @"<code>([A-Za-z0-9_-]{43})</code>").Groups[1].Value;
        TestProgram.Check(code.Length == 43 && !linked.Contains("SessionHash"), "Operator receives one-time display of separate run-bound read code");
        var exercise = await visitor.GetStringAsync("/SignedIn");
        using var beginRequest=new HttpRequestMessage(HttpMethod.Post,"/SignedIn?handler=BeginExercise"){Content=new StringContent("{}",Encoding.UTF8,"application/json")};
        beginRequest.Headers.Add("Origin",origin);beginRequest.Headers.Add("X-CSRF-TOKEN",Csrf(exercise));
        using var begun=await visitor.SendAsync(beginRequest);begun.EnsureSuccessStatusCode();
        using var beginData=JsonDocument.Parse(await begun.Content.ReadAsStringAsync());
        var ticket=beginData.RootElement.GetProperty("attempt").GetString()!;
        TestProgram.Check(ticket.Length > 0, "Authenticated paired visitor receives server-issued action ticket");
        async Task<HttpResponseMessage> Choice(string body, bool csrf = true, string? sourceOrigin = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/ClickFix?handler=Choice") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("Origin", sourceOrigin ?? origin);
            if (csrf) request.Headers.Add("X-CSRF-TOKEN", Csrf(exercise));
            return await visitor.SendAsync(request);
        }
        var body = JsonSerializer.Serialize(new { attempt = ticket, actionType = "ClickFixFollowSimulated" });
        TestProgram.Check((await Choice(body, false)).StatusCode == HttpStatusCode.BadRequest, "Choice rejects missing antiforgery");
        TestProgram.Check((await Choice(body, true, "https://other.invalid")).StatusCode == HttpStatusCode.BadRequest, "Choice rejects wrong Origin");
        TestProgram.Check((await Choice(JsonSerializer.Serialize(new { attempt = ticket, actionType = "ClickFixStopSelected", demoRunId = Guid.NewGuid() }))).StatusCode == HttpStatusCode.BadRequest, "Choice rejects client-supplied run reference/unknown fields");
        await browserChoice();
        var received = await Choice(body); var duplicate = await Choice(body);
        TestProgram.Check(received.IsSuccessStatusCode && await received.Content.ReadAsStringAsync() == await duplicate.Content.ReadAsStringAsync(), "HTTPS choice submission and retry return same server event");
        var endpoint = "/api/event-edition/poll/" + wpf;
        TestProgram.Check((await visitor.GetAsync(endpoint)).StatusCode == HttpStatusCode.Unauthorized, "Visitor cookie alone cannot poll WPF actions");
        async Task<HttpResponseMessage> Poll(string? forwarded = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint); request.Headers.Add("X-Lumma-Event-Code", code);
            if (forwarded is not null) request.Headers.Add("X-Forwarded-For", forwarded);
            return await visitor.SendAsync(request);
        }
        var snapshot = await Poll();
        using var document = JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());
        TestProgram.Check(snapshot.IsSuccessStatusCode && document.RootElement.GetProperty("portalRunId").GetString() == portalRun
            && document.RootElement.GetProperty("action").GetProperty("sequence").GetInt32() == 1, "WPF HTTPS polling transfers exactly the linked visitor action");
        TestProgram.Check((await Poll("127.0.0.1")).StatusCode == HttpStatusCode.Forbidden, "Forwarded/proxied polling is rejected before forwarded-header processing");
        TestProgram.Check((await Post(("operation", "close"), ("id", eventId), ("confirmed", "true"))).StatusCode == HttpStatusCode.Redirect, "Operator explicitly closes exhibition");
        TestProgram.Check((await presenter.GetStringAsync(page)).Contains("ClickFixFollowSimulated"), "Closing exhibition retains action history");
        return code;
    }
}
