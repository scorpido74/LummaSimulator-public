using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;

namespace LummaSimulator.Web.Tests;
internal static class Phase65eHttpChecks
{
    static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    static string Run(string html)=>Regex.Match(html,"data-demo-run-id=\"([^\"]+)\"").Groups[1].Value;
    static string Capability(string html)=>Regex.Match(html,"id=\"monitor-issued-key\">([A-Za-z0-9_-]{43})</code>").Groups[1].Value;
    internal static async Task RunAsync(HttpClient admin,HttpClient colleague,string origin,string monitorKey,
        Func<(HttpClient Client,CookieContainer Jar)> create,Func<string,object,HttpClient?,Task<HttpResponseMessage>> presenterApi,Func<Task> reauthAdmin,Func<Task> reauthColleague,Action<string> secret)
    {
        async Task<string> Get(HttpClient client,string path)=>await HttpRateLimitFixture.GetStringAsync(client,path);
        Task<HttpResponseMessage> Post(HttpClient client,string path,string csrf,IEnumerable<(string,string)> fields,string? source=null)=>HttpRateLimitFixture.SendAsync(async()=>{
            using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new FormUrlEncodedContent(fields.Select(p=>new KeyValuePair<string,string>(p.Item1,p.Item2)).Append(new("__RequestVerificationToken",csrf)))};
            request.Headers.Add("Origin",source??origin);return await client.SendAsync(request);
        });
        string Workspace(string id)=>"/Presenter/Events?kind=experience&id="+id;
        async Task<HttpResponseMessage> Manage(string id,params(string,string)[] fields)=>await Post(admin,"/Presenter/Events",Field(await Get(admin,Workspace(id)),"__RequestVerificationToken"),fields.Concat(new[]{("kind","experience"),("id",id),("confirmed","true")}));
        async Task<string> Event(string name){
            using var response=await Post(admin,"/Presenter/Events",Field(await Get(admin,"/Presenter/Events"),"__RequestVerificationToken"),new[]{("operation","create"),("kind","experience"),("name",name),("confirmed","true")});
            var id=WebUtility.UrlDecode(Regex.Match(response.Headers.Location?.OriginalString??"","[?&]id=([^&]+)").Groups[1].Value);
            TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect&&Guid.TryParse(id,out _),"6.5e HTTPS creates an explicitly scoped synthetic event");return id;
        }
        async Task Start(HttpClient client,string? id){
            // Request the intended scoped landing page. For the standalone POST contract,
            // obtain antiforgery from a neutral page because home now selects a default.
            var page=await Get(client,id is null?"/Privacy":"/Index?eventKind=experience&eventId="+id);
            var fields=new List<(string,string)>{("route","laptop")};if(id is not null){fields.Add(("eventKind","experience"));fields.Add(("eventId",id));}
            using var response=await Post(client,"/",Field(page,"__RequestVerificationToken"),fields);
            TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect,"6.5e HTTPS visitor admission succeeds");
        }
        async Task<HttpResponseMessage> VisitorApi(HttpClient client,string path,object body){
            if(path.EndsWith("/options",StringComparison.Ordinal)){client.DefaultRequestHeaders.Remove("X-Lumma-Operation");client.DefaultRequestHeaders.Add("X-Lumma-Operation",Guid.NewGuid().ToString("D"));}
            var html=await Get(client,"/Demo");
            return await HttpRateLimitFixture.SendAsync(async()=>{using var request=new HttpRequestMessage(HttpMethod.Post,"/api/webauthn/"+path){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};request.Headers.Add("Origin",origin);request.Headers.Add("X-CSRF-TOKEN",Field(html,"__RequestVerificationToken"));return await client.SendAsync(request);});
        }
        async Task<(long Number,string Event)> Authenticate(HttpClient client){
            using var key=new SoftwareAuthenticator();using(var options=await VisitorApi(client,"register/options",new{})){using var registered=await VisitorApi(client,"register/verify",key.Register(await options.Content.ReadAsStringAsync(),origin));TestProgram.Check(registered.IsSuccessStatusCode,"6.5e HTTPS synthetic visitor registers");}
            using var login=await VisitorApi(client,"authenticate/options",new{});using var verified=await VisitorApi(client,"authenticate/verify",key.Authenticate(await login.Content.ReadAsStringAsync(),origin));
            TestProgram.Check(verified.IsSuccessStatusCode,"6.5e HTTPS synthetic visitor authenticates");using var json=JsonDocument.Parse(await verified.Content.ReadAsStringAsync());return(json.RootElement.GetProperty("visitorNumber").GetInt64(),json.RootElement.GetProperty("eventId").GetString()!);
        }
        async Task<HttpStatusCode> Metadata(string run,string? grantCode){
            using var request=new HttpRequestMessage(HttpMethod.Get,"/api/local/session-status/"+run);request.Headers.Add("X-Lumma-Monitor-Key",monitorKey);if(grantCode is not null)request.Headers.Add("X-Lumma-Monitor-Grant",grantCode);
            using var response=await admin.SendAsync(request);return response.StatusCode;
        }
        var a=await Event("P1 synthetic A");var b=await Event("P1 synthetic B");
        var pair=create();using var visitor=pair.Client;await Start(visitor,a);var oldHtml=await Get(visitor,"/Demo");var oldRun=Run(oldHtml);var oldToken=pair.Jar.GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value;secret(oldToken);
        var first=await Authenticate(visitor);TestProgram.Check(first==(1L,a),"6.5e HTTPS first event A visitor starts at 1");
        var workspace=await Get(admin,Workspace(a));var demoForm=Regex.Matches(workspace,"<form\\b.*?</form>",RegexOptions.Singleline).Cast<Match>().Select(m=>m.Value).Single(form=>form.Contains(oldRun,StringComparison.Ordinal));
        using var granted=await Manage(a,("operation","grant"),("demo",Field(demoForm,"demo")));var cap=Capability(await granted.Content.ReadAsStringAsync());secret(cap);
        TestProgram.Check(cap.Length==43&&await Metadata(oldRun,cap)==HttpStatusCode.OK,"6.5e HTTPS old run has a real authorized monitor capability");
        foreach(var invalid in new[]{"null","https://other.invalid"}){
            using var denied=await Post(visitor,"/Demo?handler=NewVisitor",Field(oldHtml,"__RequestVerificationToken"),[],invalid);
            TestProgram.Check(denied.StatusCode==HttpStatusCode.BadRequest,"6.5e rotation rejects opaque/external Origin");
        }
        using(var missingOrigin=await visitor.PostAsync("/Demo?handler=NewVisitor",new FormUrlEncodedContent(new[]{new KeyValuePair<string,string>("__RequestVerificationToken",Field(oldHtml,"__RequestVerificationToken"))})))TestProgram.Check(missingOrigin.StatusCode==HttpStatusCode.BadRequest,"6.5e rotation rejects missing Origin");
        using(var csrf=await Post(visitor,"/Demo?handler=NewVisitor","invalid",[]))TestProgram.Check(csrf.StatusCode==HttpStatusCode.BadRequest,"6.5e rotation rejects invalid antiforgery");
        using(var tamper=await Post(visitor,"/Demo?handler=NewVisitor",Field(oldHtml,"__RequestVerificationToken"),new[]{("eventId",b)}))TestProgram.Check(tamper.StatusCode==HttpStatusCode.BadRequest,"6.5e rotation rejects client event binding in form");
        using(var tamper=await Post(visitor,"/Demo?handler=NewVisitor&eventId="+b,Field(oldHtml,"__RequestVerificationToken"),[]))TestProgram.Check(tamper.StatusCode==HttpStatusCode.BadRequest,"6.5e rotation rejects client event binding in query");
        TestProgram.Check(Run(await Get(visitor,"/Demo"))==oldRun,"6.5e rejected rotation leaves the visitor and run unchanged");
        using(var rotated=await Post(visitor,"/Demo?handler=NewVisitor",Field(oldHtml,"__RequestVerificationToken"),[]))TestProgram.Check(rotated.StatusCode==HttpStatusCode.Redirect,"6.5e authorized HTTPS visitor rotation succeeds");
        var nextHtml=await Get(visitor,"/Demo");var nextRun=Run(nextHtml);secret(pair.Jar.GetCookies(new Uri(origin))[VisitorCookie.Name]!.Value);
        TestProgram.Check(nextRun!=oldRun&&await Metadata(oldRun,cap)==HttpStatusCode.NotFound&&await Metadata(nextRun,cap)==HttpStatusCode.NotFound&&await Metadata(nextRun,null)==HttpStatusCode.NotFound,"6.5e old grants cannot read either the closed run or replacement run");
        var stalePair=create();using(var stale=stalePair.Client){stalePair.Jar.Add(new Uri(origin),new Cookie(VisitorCookie.Name,oldToken,"/"){Secure=true,HttpOnly=true});using var denied=await stale.GetAsync("/Demo");TestProgram.Check(denied.StatusCode==HttpStatusCode.Redirect,"6.5e old visitor cookie cannot reopen its closed demo");}
        var second=await Authenticate(visitor);TestProgram.Check(second==(2L,a),"6.5e HTTPS rotation retains A and allocates number 2");
        var bPair=create();using(var bVisitor=bPair.Client){await Start(bVisitor,b);TestProgram.Check(await Authenticate(bVisitor)==(1L,b),"6.5e B independently starts at 1");var html=await Get(bVisitor,"/Demo");using var rotate=await Post(bVisitor,"/Demo?handler=NewVisitor",Field(html,"__RequestVerificationToken"),[]);TestProgram.Check(rotate.StatusCode==HttpStatusCode.Redirect&&await Authenticate(bVisitor)==(2L,b),"6.5e B rotation retains B and independently allocates 2");}
        var publicPair=create();using(var publicVisitor=publicPair.Client){await Start(publicVisitor,null);var html=await Get(publicVisitor,"/Demo");using var rotate=await Post(publicVisitor,"/Demo?handler=NewVisitor",Field(html,"__RequestVerificationToken"),[]);TestProgram.Check(rotate.StatusCode==HttpStatusCode.Redirect&&(await Authenticate(publicVisitor)).Event==ExperienceStatistics.UnassignedEvent,"6.5e public HTTPS demo stays Open demo with active managed events");}
        using(var closed=await Manage(a,("operation","close")))TestProgram.Check(closed.StatusCode==HttpStatusCode.Redirect,"6.5e event can be closed before rotation");
        using(var denied=await Post(visitor,"/Demo?handler=NewVisitor",Field(nextHtml,"__RequestVerificationToken"),[]))TestProgram.Check(denied.StatusCode==HttpStatusCode.Redirect&&Run(await Get(visitor,"/Demo"))==nextRun,"6.5e closed-event rotation is refused without changing the existing run");

        // Use a separate event for enrollment lifecycle and transfer, with real independent sessions.
        var enrollmentEvent=await Event("P1 enrollment transfer");var management=await Get(admin,Workspace(enrollmentEvent));
        var colleagueId=Regex.Match(management,"<option value=\"([^\"]+)\">HTTP Presenter</option>").Groups[1].Value;
        TestProgram.Check(Guid.TryParse(colleagueId,out _),"6.5e existing presenter can be selected as ownership recipient");
        using(var invited=await Manage(enrollmentEvent,("operation","invite"),("target",colleagueId)))TestProgram.Check(invited.StatusCode==HttpStatusCode.Redirect,"6.5e existing presenter invitation succeeds");
        var invites=await Get(colleague,"/Presenter/Events");using(var accepted=await Post(colleague,"/Presenter/Events",Field(invites,"__RequestVerificationToken"),new[]{("operation","accept"),("invitation",Field(invites,"invitation")),("confirmed","true")}))TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect,"6.5e existing presenter accepts the correct event invitation");
        async Task<string> Invite(string login){using var response=await Manage(enrollmentEvent,("operation","new"),("login",login),("name","Synthetic pending"));var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());var code=Regex.Match(html,"#invite=([A-Za-z0-9_-]{43})").Groups[1].Value;TestProgram.Check(code.Length==43,"6.5e HTTPS one-time pending enrollment issued");secret(code);return code;}
        var raw=await Invite("p1-http-raw");var redeemed=await Invite("p1-http-grant");var active=await Invite("p1-http-active");
        var grantPair=create();using var grantClient=grantPair.Client;
        using(var opened=await presenterApi("enrollment/open",new{code=redeemed},grantClient))TestProgram.Check(opened.IsSuccessStatusCode,"6.5e HTTPS grant is redeemed before transfer");
        using var pendingOptionsResponse=await presenterApi("enroll/options",new{useInvitation=true},grantClient);TestProgram.Check(pendingOptionsResponse.IsSuccessStatusCode,"6.5e HTTPS pending enrollment receives options before transfer");var optionsBody=await pendingOptionsResponse.Content.ReadAsStringAsync();
        var activePair=create();using var activeClient=activePair.Client;using var activeKey=new SoftwareAuthenticator();
        using(var opened=await presenterApi("enrollment/open",new{code=active},activeClient))TestProgram.Check(opened.IsSuccessStatusCode,"6.5e positive enrollment redemption succeeds");
        using(var activeOptions=await presenterApi("enroll/options",new{useInvitation=true},activeClient)){using var registered=await presenterApi("enroll/verify",new{useInvitation=true,keyName="Synthetic active",response=activeKey.Register(await activeOptions.Content.ReadAsStringAsync(),origin)},activeClient);TestProgram.Check(registered.IsSuccessStatusCode,"6.5e positive enrollment completes before transfer");}
        await reauthAdmin();using(var offered=await Manage(enrollmentEvent,("operation","offer"),("target",colleagueId)))TestProgram.Check(offered.StatusCode==HttpStatusCode.Redirect,"6.5e HTTPS ownership offer succeeds after fresh proof");
        await reauthColleague();var transferPage=await Get(colleague,Workspace(enrollmentEvent));
        var transferFields=new[]{("operation","transfer"),("kind","experience"),("id",enrollmentEvent),("confirmed","true")};
        using(var denied=await Post(colleague,"/Presenter/Events",Field(transferPage,"__RequestVerificationToken"),transferFields,"null"))TestProgram.Check(denied.StatusCode==HttpStatusCode.BadRequest,"6.5e ownership acceptance retains exact Origin protection");
        using(var transferred=await Post(colleague,"/Presenter/Events",Field(transferPage,"__RequestVerificationToken"),transferFields))TestProgram.Check(transferred.StatusCode==HttpStatusCode.Redirect,"6.5e HTTPS ownership recipient accepts with own fresh proof and revokes pending enrollment atomically");
        var rawPair=create();using(var rawClient=rawPair.Client){using var denied=await presenterApi("enrollment/open",new{code=raw},rawClient);TestProgram.Check(denied.StatusCode==HttpStatusCode.BadRequest,"6.5e raw HTTPS enrollment is revoked by ownership change");}
        using var pendingKey=new SoftwareAuthenticator();using(var denied=await presenterApi("enroll/verify",new{useInvitation=true,keyName="Denied",response=pendingKey.Register(optionsBody,origin)},grantClient))TestProgram.Check(denied.StatusCode==HttpStatusCode.BadRequest,"6.5e already-issued HTTPS enrollment ceremony cannot insert a key after transfer");
        using(var denied=await presenterApi("enroll/options",new{useInvitation=true},grantClient))TestProgram.Check(denied.StatusCode==HttpStatusCode.BadRequest,"6.5e redeemed HTTPS grant cannot request new options after transfer");
        using(var loginOptions=await presenterApi("login/options",new{},activeClient)){using var login=await presenterApi("login/verify",new{response=activeKey.Authenticate(await loginOptions.Content.ReadAsStringAsync(),origin)},activeClient);TestProgram.Check(login.IsSuccessStatusCode,"6.5e already activated account and credential survive ownership change");}
        using(var denied=await activeClient.GetAsync(Workspace(enrollmentEvent)))TestProgram.Check(denied.StatusCode==HttpStatusCode.NotFound,"6.5e retained account receives no implicit event rights after transfer");
        using(var denied=await colleague.GetAsync(Workspace(b)))TestProgram.Check(denied.StatusCode==HttpStatusCode.NotFound,"6.5e ownership of shared event never grants another event");
    }
}
