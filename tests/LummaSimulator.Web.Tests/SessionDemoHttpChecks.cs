using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Tests;

internal static class SessionDemoHttpChecks
{
    private static string Field(string html, string name)
    {
        var input=Regex.Matches(html,"<input\\b[^>]*>").Select(m=>m.Value)
            .FirstOrDefault(tag=>Regex.IsMatch(tag,"\\bname=\""+Regex.Escape(name)+"\""))??"";
        return WebUtility.HtmlDecode(Regex.Match(input,"\\bvalue=\"([^\"]*)\"").Groups[1].Value);
    }
    private static Task<HttpResponseMessage> Form(HttpClient client, string path, string origin, Dictionary<string, string> fields) =>
        HttpRateLimitFixture.SendAsync(async () => {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Add("Origin", origin); return await client.SendAsync(request);
        });
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    internal static async Task RunAsync(HttpClient owner, HttpClient member, HttpClient outsider, Func<HttpClient> createClient, string origin, Action<string> secret)
    {
        async Task<string> Event(HttpClient actor, string name)
        {
            var html = await HttpRateLimitFixture.GetStringAsync(actor, "/Presenter/Prizes/New");
            using var created = await Form(actor, "/Presenter/Prizes/New", origin, new() {
                ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken"), ["OperationKey"] = Field(html, "OperationKey"),
                ["ProfileId"] = "builtin-cybertruck", ["Name"] = name, ["start"] = "true", ["Input.GroupSize"] = "2",
                ["Input.PrizeEnabled"] = "true", ["Input.Stock"] = "1", ["Input.WinnersPerGroup"] = "1"
            });
            TestProgram.Check(created.StatusCode == HttpStatusCode.Redirect, "Session HTTPS fixture creates its isolated round event");
            return created.Headers.Location!.ToString().Split('/').Last();
        }
        var qrEvent=await Event(owner,"Synthetic QR lifecycle");
        using(var clean=createClient()){
            var admissionPath=PrizeRoundsService.FixedJoinPath(qrEvent);
            using var admission=await clean.GetAsync(admissionPath);
            var admissionHtml=await admission.Content.ReadAsStringAsync();
            var main=Regex.Match(admissionHtml,"<main\\b[^>]*>.*?</main>",RegexOptions.Singleline).Value;
            var correctEvent=main.Contains("id=\"prize-round-entry\"")&&main.Contains("Synthetic QR lifecycle")
                &&Field(main,"eventKind")=="prize"&&Field(main,"eventId")==qrEvent;
            var offersYubiKey=main.Contains("data-method=\"yubikey\"");
            var admissionUnavailable=main.Contains("id=\"visitor-admission-unavailable\"");
            var renderedDenial=main.Contains("De aanvraag is geweigerd of verlopen");
            var denialInCatalog=Regex.Match(admissionHtml,"<template id=\"localization-data\">.*?</template>",RegexOptions.Singleline)
                .Value.Contains("De aanvraag is geweigerd of verlopen");
            var redirect=admission.Headers.Location is null?"none":"<redacted Location; redirect not followed>";
            Console.WriteLine($"Session anonymous GET {new Uri(clean.BaseAddress!,admissionPath)} -> {(int)admission.StatusCode}; redirect={redirect}; event={correctEvent}; yubikey={offersYubiKey}; unavailable={admissionUnavailable}; denialInMain={renderedDenial}; denialInCatalog={denialInCatalog}");
            TestProgram.Check(admission.StatusCode==HttpStatusCode.OK&&admission.Headers.Location is null&&correctEvent&&offersYubiKey
                &&!admissionUnavailable&&!renderedDenial,"Clean anonymous first GET for an open owned Prize event offers passkey routes without denial");
            var detail="/Presenter/Prizes/Event/"+qrEvent;
            async Task<string> SetRoutes(string? method){
                var form=await HttpRateLimitFixture.GetStringAsync(owner,detail);
                TestProgram.Check(form.Contains("class=\"login-options\""),"Prize detail uses the shared sign-in settings form");
                var fields=new Dictionary<string,string>{["__RequestVerificationToken"]=Field(form,"__RequestVerificationToken"),["kind"]="prize",
                    ["id"]=qrEvent,["confirmed"]="true",["prizeDetails"]="true"};
                if(method is not null)fields[method]="true";
                using var response=await Form(owner,"/Presenter/Events?handler=LoginRoutes",origin,fields);
                TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location?.OriginalString==detail,
                    "Saving Prize sign-in settings returns to the Prize detail page");
                return await HttpRateLimitFixture.GetStringAsync(owner,detail);
            }
            var noMethods=await SetRoutes(null);
            var unchanged=await clean.GetStringAsync(admissionPath);
            TestProgram.Check(noMethods.Contains("LoginOptionsRequired")&&Regex.Matches(unchanged,"class=\"choice-card").Count==4,
                "Rejecting an empty Prize selection preserves all default visitor choices");
            await SetRoutes("laptop");
            var disabled=await clean.GetStringAsync(admissionPath);
            TestProgram.Check(disabled.Contains("data-method=\"windows-hello\"")&&!disabled.Contains("data-method=\"yubikey\""),"The explicit Prize link renders only the selected sign-in method");
            using(var rejected=await Form(clean,admissionPath,origin,new(){["__RequestVerificationToken"]=Field(admissionHtml,"__RequestVerificationToken"),["route"]="yubikey",["eventKind"]="prize",["eventId"]=qrEvent}))
                TestProgram.Check(rejected.StatusCode==HttpStatusCode.OK&&(!rejected.Headers.TryGetValues("Set-Cookie",out var cookies)||!cookies.Any(c=>c.StartsWith(VisitorCookie.Name+"=",StringComparison.Ordinal))),"A direct POST for a disabled Prize method creates no visitor session");
            await SetRoutes("yubikey");
            admissionHtml=await clean.GetStringAsync(admissionPath);
            using var accepted=await Form(clean,admissionPath,origin,new(){["__RequestVerificationToken"]=Field(admissionHtml,"__RequestVerificationToken"),["route"]="yubikey",["eventKind"]="prize",["eventId"]=qrEvent});
            TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect&&accepted.Headers.Location?.ToString()=="/Demo","Anonymous YubiKey choice enters passkey flow without presenter login when participation is on");
            var passkeyPage=await clean.GetStringAsync("/Demo");
            TestProgram.Check(passkeyPage.Contains("id=\"register-passkey\"")&&passkeyPage.Contains("id=\"authenticate-passkey\""),
                "New event visitor receives registration and sign-in controls");
        }
        var qrStatePath="/Presenter/ParticipantQr?handler=State&id="+qrEvent;
        var qrImagePath="/Presenter/ParticipantQr?handler=Image&id="+qrEvent;
        async Task CheckQr(HttpClient client,string actor,bool allowed)
        {
            foreach(var (handlerPath,mediaType) in new[]{(qrStatePath,"application/json"),(qrImagePath,"image/svg+xml")})
            {
                using var response=await client.GetAsync(handlerPath);
                var location=response.Headers.Location;
                var safeLocation=location is null?"none":new Uri(client.BaseAddress!,location).AbsolutePath switch {
                    "/Presenter/Login"=>"/Presenter/Login (query omitted)",
                    "/Presenter/Denied"=>"/Presenter/Denied (query omitted)",
                    _=>"<redacted>"
                };
                var type=response.Content.Headers.ContentType?.MediaType;
                Console.WriteLine($"Session QR GET {actor} {handlerPath} -> {(int)response.StatusCode}; Location={safeLocation}; Content-Type={type??"none"}");
                if(!allowed)
                {
                    TestProgram.Check(response.StatusCode==HttpStatusCode.NotFound&&location is null&&type!=mediaType,
                        $"{actor} cannot obtain another event QR through {handlerPath.Split('?')[1].Split('&')[0]}");
                    continue;
                }
                TestProgram.Check(response.StatusCode==HttpStatusCode.OK&&location is null&&type==mediaType&&response.Headers.CacheControl?.NoStore==true,
                    $"{actor} receives the uncached QR response as {mediaType}");
                if(mediaType=="application/json")
                {
                    var data=await Read<ParticipantQrEntry>(response);
                    TestProgram.Check(data.Url==origin+PrizeRoundsService.FixedJoinPath(qrEvent)&&data.LaptopOnly,"Dashboard QR uses the exact existing fixed HTTPS participants URL and identifies the loopback fixture");
                }
                else TestProgram.Check((await response.Content.ReadAsStringAsync()).Contains("crispEdges"),"QR image contains the locally generated SVG");
            }
        }
        await CheckQr(owner,"Event owner",true);
        await CheckQr(outsider,"Unrelated presenter",false);
        const string eventManagement="/Presenter/Events";
        var inviteForm=await HttpRateLimitFixture.GetStringAsync(owner,eventManagement+"?kind=prize&id="+qrEvent);
        var memberId=PrizeEventHttpChecks.PersonId(inviteForm,"HTTP Presenter");
        TestProgram.Check(Guid.TryParse(memberId,out _),"QR fixture resolves the intended presenter for the event invitation");
        using(var invited=await Form(owner,eventManagement,origin,new(){["__RequestVerificationToken"]=Field(inviteForm,"__RequestVerificationToken"),["operation"]="invite",["kind"]="prize",["id"]=qrEvent,["target"]=memberId,["confirmed"]="true"}))
            TestProgram.Check(invited.StatusCode==HttpStatusCode.Redirect,"QR event owner invites the intended presenter");
        await CheckQr(member,"Invited presenter before acceptance",false);
        var invitations=await HttpRateLimitFixture.GetStringAsync(member,eventManagement);
        var invitationId=Field(invitations,"invitation");
        TestProgram.Check(Guid.TryParse(invitationId,out _),"Invited presenter receives the event invitation");
        using(var accepted=await Form(member,eventManagement,origin,new(){["__RequestVerificationToken"]=Field(invitations,"__RequestVerificationToken"),["operation"]="accept",["invitation"]=invitationId,["confirmed"]="true"}))
            TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect,"Invited presenter accepts QR event membership");
        await CheckQr(member,"Accepted event member",true);
        var qrManagePath="/Presenter/Prizes/Event/"+qrEvent;var qrManage=await owner.GetStringAsync(qrManagePath);
        TestProgram.Check(qrManage.Contains("id=\"participant-qr-copy\"")&&qrManage.Contains("Synthetic QR lifecycle"),"Event management displays the event name and copy participant link button beside its QR");
        using(var closed=await Form(owner,qrManagePath+"?handler=Close",origin,new(){["__RequestVerificationToken"]=Field(qrManage,"__RequestVerificationToken"),["ExpectedEventId"]=qrEvent,["Version"]=Field(qrManage,"Version"),["OperationKey"]=Field(qrManage,"OperationKey"),["confirmed"]="true"}))
            TestProgram.Check(closed.StatusCode==HttpStatusCode.Redirect,"QR fixture closes only its own new synthetic event");
        TestProgram.Check((await owner.GetAsync(qrStatePath)).StatusCode==HttpStatusCode.NotFound&&(await owner.GetAsync(qrImagePath)).StatusCode==HttpStatusCode.NotFound,"Closed event immediately refuses both invitation state and QR image");
        var a = await Event(owner, "Synthetic session A"); var b = await Event(owner, "Synthetic session B"); var c = await Event(outsider, "Synthetic unrelated session event");
        async Task<(string Csrf, string Alias)> Visitor(HttpClient visitor, string eventId)
        {
            var landing = await visitor.GetStringAsync(PrizeRoundsService.FixedJoinPath(eventId));
            using var start = await Form(visitor, "/", origin, new() {
                ["__RequestVerificationToken"] = Field(landing, "__RequestVerificationToken"), ["route"] = "laptop", ["eventKind"] = "prize", ["eventId"] = eventId
            });
            TestProgram.Check(start.StatusCode == HttpStatusCode.Redirect, "Session HTTPS visitor enters through the fixed round QR");
            var csrf = Field(await visitor.GetStringAsync("/Demo"), "__RequestVerificationToken");
            using var key = new SoftwareAuthenticator(); string? alias = null;
            foreach (var operation in new[] { "register", "authenticate" })
            {
                visitor.DefaultRequestHeaders.Remove("X-Lumma-Operation"); visitor.DefaultRequestHeaders.Add("X-Lumma-Operation", Guid.NewGuid().ToString("D"));
                using var options = await Browser(visitor, "/api/webauthn/" + operation + "/options", new { }, csrf, origin);
                options.EnsureSuccessStatusCode(); var json = await options.Content.ReadAsStringAsync();
                using var verified = await Browser(visitor, "/api/webauthn/" + operation + "/verify", operation == "register" ? key.Register(json, origin) : key.Authenticate(json, origin), csrf, origin);
                verified.EnsureSuccessStatusCode();
                if (operation == "authenticate") { using var receipt = JsonDocument.Parse(await verified.Content.ReadAsStringAsync()); alias = receipt.RootElement.GetProperty("round").GetProperty("alias").GetString(); }
            }
            var signedIn=await visitor.GetStringAsync("/SignedIn");
            TestProgram.Check(signedIn.Contains(WebUtility.HtmlEncode(alias!))&&signedIn.Contains("id=\"start-clickfix\"")
                &&signedIn.Contains("id=\"session-demo-consent\""),"Fixed QR authentication reaches SignedIn with the same round alias and explicit consent controls");
            TestProgram.Check((await visitor.GetStringAsync("/SignedIn")).Contains(WebUtility.HtmlEncode(alias!)),"SignedIn reload preserves the round alias without an event query parameter");
            return (csrf, alias!);
        }
        using var first = createClient(); using var second = createClient(); using var foreign = createClient(); using var desktop = createClient(); using var viewer = createClient();
        var one = await Visitor(first, a); var two = await Visitor(second, a); var other = await Visitor(foreign, b);
        var wrongEntry=await first.GetStringAsync(PrizeRoundsService.FixedJoinPath(b));
        TestProgram.Check(wrongEntry.Contains("VisitorEventMismatch")&&!wrongEntry.Contains("class=\"choice-card"),"A QR for another event explains the existing visitor conflict instead of offering misleading start buttons");
        using(var wrongStart=await Form(first,"/",origin,new(){["__RequestVerificationToken"]=Field(wrongEntry,"__RequestVerificationToken"),["route"]="laptop",["eventKind"]="prize",["eventId"]=b})) {
            TestProgram.Check(wrongStart.StatusCode==HttpStatusCode.OK&&(await wrongStart.Content.ReadAsStringAsync()).Contains("VisitorEventMismatch"),"Direct conflicting QR POST cannot silently resume or move the old participant");
        }
        TestProgram.Check((await first.GetStringAsync("/SignedIn")).Contains(WebUtility.HtmlEncode(one.Alias)),"Conflicting QR leaves the existing signed-in participant alias intact");
        var sameEntry=await first.GetStringAsync(PrizeRoundsService.FixedJoinPath(a));
        using(var resume=await Form(first,"/",origin,new(){["__RequestVerificationToken"]=Field(sameEntry,"__RequestVerificationToken"),["route"]="laptop",["eventKind"]="prize",["eventId"]=a}))
            TestProgram.Check(resume.StatusCode==HttpStatusCode.Redirect,"QR for the same event safely resumes the existing demo");
        async Task<HttpResponseMessage> Native(string path, object? body = null, string? key = null, string? browserOrigin = null, bool protocol = true, bool forwarded = false)
        {
            return await HttpRateLimitFixture.SendAsync(async () => {
                using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
                if (body is not null) request.Content = JsonContent.Create(body);
                if (protocol) request.Headers.Add("X-Lumma-App-Protocol", "1");
                if (key is not null) request.Headers.Add("X-Lumma-App-Key", key);
                if (browserOrigin is not null) request.Headers.Add("Origin", browserOrigin);
                if (forwarded) request.Headers.Add("X-Forwarded-For", "127.0.0.1");
                return await desktop.SendAsync(request);
            });
        }
        using (var compatibility = await Native("/api/helper/participant-demo/compatibility")) {
            using var json = JsonDocument.Parse(await compatibility.Content.ReadAsStringAsync());
            TestProgram.Check(compatibility.IsSuccessStatusCode && json.RootElement.GetProperty("protocols").EnumerateArray().Any(p => p.GetInt32() == 1), "Helper HTTPS contract advertises its explicit supported protocol");
        }
        using var helperBegin = await Native("/api/helper/participant-demo/connections", new { });
        var helper = await Read<DesktopConnection>(helperBegin); secret(helper.Key);
        var helperPath = "/api/helper/participant-demo/connections/" + helper.RequestId;
        var installationId=Guid.NewGuid().ToString("D");
        var installationIdentity=DemoIdentity.RandomToken(); secret(installationIdentity);
        var heartbeat=new{id=installationId,identity=installationIdentity,device="Synthetic helper",version="0.1.0",pairing="Unknown",health="Unknown"};
        using (var pending = await Native(helperPath + "/heartbeat", heartbeat, helper.Key))
            TestProgram.Check(pending.StatusCode == HttpStatusCode.NotFound, "Pending helper cannot register an installation heartbeat");
        var helperApproval = "/Presenter/HelperConnection?requestId=" + helper.RequestId;
        var helperApprovalHtml = await HttpRateLimitFixture.GetStringAsync(owner, helperApproval);
        using (var approved = await Form(owner, helperApproval, origin, new() {
            ["__RequestVerificationToken"] = Field(helperApprovalHtml, "__RequestVerificationToken"), ["RequestId"] = helper.RequestId, ["confirmed"] = "true"
        })) TestProgram.Check(approved.StatusCode == HttpStatusCode.OK, "Helper reuses the presenter's existing WebAuthn session and explicit approval");
        using (var recorded = await Native(helperPath + "/heartbeat", heartbeat, helper.Key))
            TestProgram.Check(recorded.StatusCode == HttpStatusCode.OK,"Approved helper records an authenticated installation heartbeat");
        using (var spoofed = await Native(helperPath + "/heartbeat", heartbeat with {identity=DemoIdentity.RandomToken()}, helper.Key))
            TestProgram.Check(spoofed.StatusCode == HttpStatusCode.NotFound,"Another installation identity cannot overwrite a registered helper");
        using (var events = await Native(helperPath + "/events", key: helper.Key)) {
            var available = await Read<List<ParticipantEvent>>(events);
            TestProgram.Check(available.Any(e => e.Id == a) && available.Any(e => e.Id == b) && !available.Any(e => e.Id == c), "Helper and app use central event IDs and exclude an unrelated presenter's event");
        }
        using (var denied = await Native(helperPath + "/selection", new { eventId = c, version = 1 }, helper.Key))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Remote helper scope is checked server-side on selection");
        using (var disconnected = await Native(helperPath + "/disconnect", new { }, helper.Key)) disconnected.EnsureSuccessStatusCode();
        using (var revoked = await Native(helperPath + "/heartbeat", heartbeat, helper.Key))
            TestProgram.Check(revoked.StatusCode == HttpStatusCode.NotFound,"Disconnected helper cannot renew its installation heartbeat");
        using (var denied = await Native(helperPath + "/events", key: helper.Key))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Disconnected helper cannot retain event access");
        using (var denied = await Native("/api/local/participant-demo/connections", new { }, protocol: false))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.Forbidden, "Native session API rejects requests without the app protocol marker");
        using (var denied = await Native("/api/local/participant-demo/connections", new { }, browserOrigin: origin))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.Forbidden, "Browser-origin requests cannot create a native app connection");
        using (var denied = await Native("/api/local/participant-demo/connections", new { }, forwarded: true))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.Forbidden, "Forwarded requests cannot use the loopback app contract");
        using var begun = await Native("/api/local/participant-demo/connections", new { });
        var app = await Read<DesktopConnection>(begun); secret(app.Key);
        var path = "/api/local/participant-demo/connections/" + app.RequestId;
        using (var denied = await Native(path + "/events", key: app.Key))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Unapproved native connection has no event access over HTTPS");
        var approval = "/Presenter/AppConnection?requestId=" + app.RequestId;
        using (var anonymous = await desktop.GetAsync(approval))
            TestProgram.Check(anonymous.StatusCode == HttpStatusCode.Redirect && anonymous.Headers.Location!.ToString().Contains("/Presenter/Login"), "App approval requires an existing presenter sign-in");
        var approvalHtml = await HttpRateLimitFixture.GetStringAsync(owner, approval);
        using (var rejected = await Form(owner, approval, origin, new() { ["RequestId"] = app.RequestId, ["confirmed"] = "true" }))
            TestProgram.Check(rejected.StatusCode == HttpStatusCode.BadRequest, "App approval retains Razor antiforgery protection");
        using (var approved = await Form(owner, approval, origin, new() {
            ["__RequestVerificationToken"] = Field(approvalHtml, "__RequestVerificationToken"), ["RequestId"] = app.RequestId, ["confirmed"] = "true"
        })) TestProgram.Check(approved.StatusCode == HttpStatusCode.OK, "A presenter can approve the app connection without administrator privileges");
        using (var ready = await Native(path, key: app.Key)) TestProgram.Check((await Read<DesktopConnectionStatus>(ready)).Status == "Ready", "Approved app receives its temporary connection without copied codes");
        using (var denied = await Native(path + "/events")) TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Presenter browser cookies alone do not replace the app key");
        using (var catalog = await Native(path + "/events", key: app.Key))
        {
            var choices = await Read<List<ParticipantEvent>>(catalog);
            TestProgram.Check(choices.Any(e => e.Id == a) && choices.Any(e => e.Id == b) && choices.All(e => e.Id != c), "HTTPS app catalog excludes an unrelated presenter's event");
        }
        using (var selected = await Native(path + "/selection", new { eventId = b, version = 1 }, app.Key)) selected.EnsureSuccessStatusCode();
        using var foreignRosterResponse = await Native(path + "/events/" + b + "/participants", key: app.Key);
        var foreignParticipant = (await Read<ParticipantRoster>(foreignRosterResponse)).Participants.Single(p => p.Alias == other.Alias);
        using (var selected = await Native(path + "/selection", new { eventId = a, version = 2 }, app.Key)) selected.EnsureSuccessStatusCode();
        using var rosterResponse = await Native(path + "/events/" + a + "/participants", key: app.Key);
        var roster = await Read<ParticipantRoster>(rosterResponse);
        TestProgram.Check(roster.Participants.Count == 2 && roster.Participants.All(p => p.Round == 1 && !p.CanOpen) && one.Alias != two.Alias, "HTTPS roster lists the two QR aliases in the same round without implicit consent");
        var p1 = roster.Participants.Single(p => p.Alias == one.Alias); var p2 = roster.Participants.Single(p => p.Alias == two.Alias);
        var refreshed=await Read<ParticipantRoster>(await Native(path+"/events/"+a+"/participants",key:app.Key));
        TestProgram.Check(refreshed.Participants.Single(p=>p.Id==p1.Id).Alias==one.Alias
            &&refreshed.Participants.Single(p=>p.Id==p2.Id).Alias==two.Alias
            &&refreshed.Participants.All(p=>p.ConsentStatus=="None"&&!p.CanOpen),"List refresh returns the exact SignedIn aliases before any session-demo consent");
        var eventPath = path + "/events/" + a;
        using (var denied = await Native(eventPath + "/open", new { participantId = p1.Id }, app.Key)) TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Event owner cannot open an alias before visitor consent over HTTPS");
        using var request1 = await Native(eventPath + "/consent", new { participantId = p1.Id, wpfRunId = Guid.NewGuid().ToString("D") }, app.Key);
        using var request2 = await Native(eventPath + "/consent", new { participantId = p2.Id, wpfRunId = Guid.NewGuid().ToString("D") }, app.Key);
        var consent1 = await Read<VisitorConsent>(request1); var consent2 = await Read<VisitorConsent>(request2);
        var allow = new { requestId = consent1.RequestId, allow = true, confirmed = true };
        using (var denied = await Browser(first, "/api/session-demo/consent", allow, one.Csrf, null)) TestProgram.Check(denied.StatusCode == HttpStatusCode.BadRequest, "Consent POST requires the exact browser Origin");
        using (var denied = await Browser(first, "/api/session-demo/consent", allow, null, origin)) TestProgram.Check(denied.StatusCode == HttpStatusCode.BadRequest, "Consent POST requires antiforgery even for the authenticated visitor");
        using (var denied = await Browser(second, "/api/session-demo/consent", allow, two.Csrf, origin)) TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "One QR visitor cannot approve the other alias's consent request");
        using (var granted = await Browser(first, "/api/session-demo/consent", allow, one.Csrf, origin)) granted.EnsureSuccessStatusCode();
        using (var declined = await Browser(second, "/api/session-demo/consent", new { requestId = consent2.RequestId, allow = false, confirmed = true }, two.Csrf, origin)) declined.EnsureSuccessStatusCode();
        using (var updated = await Native(eventPath + "/participants", key: app.Key))
        {
            var rows = (await Read<ParticipantRoster>(updated)).Participants;
            TestProgram.Check(rows.Single(p => p.Id == p1.Id) is { ConsentStatus: "Granted", CanOpen: true }
                && rows.Single(p => p.Id == p2.Id) is { ConsentStatus: "Declined", CanOpen: false }, "HTTPS app enables Open only for the consenting alias");
        }
        foreach (var (scope, participant) in new[] { (a, p2.Id), (a, foreignParticipant.Id), (b, foreignParticipant.Id) })
        {
            using var denied = await Native(path + "/events/" + scope + "/open", new { participantId = participant }, app.Key);
            TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Declined consent and foreign event/participant combinations remain closed over HTTPS");
        }
        using (var denied = await Native(path + "/selection", new { eventId = c, version = 3 }, app.Key)) TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Presenter cannot select another presenter's event through the native API");
        using var issued = await Native(eventPath + "/open", new { participantId = p1.Id }, app.Key);
        var link = await Read<SessionDemoOpenLink>(issued); secret(link.Token);
        var viewPage = await viewer.GetStringAsync("/SessionDemo/" + link.ViewId); var viewCsrf = Field(viewPage, "__RequestVerificationToken");
        var viewPath = "/api/session-demo/views/" + link.ViewId;
        using (var opened = await Browser(viewer, viewPath + "/redeem", new { token = link.Token }, viewCsrf, origin))
        {
            var view = await Read<SyntheticSessionView>(opened);
            var cookie = opened.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(SessionDemoEndpoints.ViewerCookie + "=", StringComparison.Ordinal));
            secret(cookie.Split(';')[0].Split('=', 2)[1]);
            TestProgram.Check(view.EventId == a && view.Alias == one.Alias && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
                && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "Synthetic view receives a distinct Secure HttpOnly Strict cookie for the approved alias");
        }
        using (var ordinaryDemo = await viewer.GetAsync("/Demo")) TestProgram.Check(ordinaryDemo.StatusCode == HttpStatusCode.Redirect, "Synthetic viewer has no authority to open the real visitor portal");
        using (var replay = await Browser(viewer, viewPath + "/redeem", new { token = link.Token }, viewCsrf, origin)) TestProgram.Check(replay.StatusCode == HttpStatusCode.NotFound, "HTTPS synthetic open link is single-use");
        using (var active = await viewer.GetAsync(viewPath)) TestProgram.Check(active.IsSuccessStatusCode && active.Headers.CacheControl?.NoStore == true, "Active synthetic session is read-only and never cached");
        using (var withdrawn = await Browser(first, "/api/session-demo/consent", new { requestId = consent1.RequestId, allow = false, confirmed = true }, one.Csrf, origin)) withdrawn.EnsureSuccessStatusCode();
        using (var closed = await viewer.GetAsync(viewPath)) TestProgram.Check(closed.StatusCode == HttpStatusCode.NotFound, "Visitor withdrawal ends access from an already open synthetic session over HTTPS");
        using (var denied = await Native(eventPath + "/open", new { participantId = p1.Id }, app.Key)) TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Withdrawn HTTPS session cannot issue a new open link");
        async Task<string> OpenView(string scope, string participant, HttpClient visitor, string csrf)
        {
            using var request = await Native(path + "/events/" + scope + "/consent", new { participantId = participant, wpfRunId = Guid.NewGuid().ToString("D") }, app.Key);
            var consent = await Read<VisitorConsent>(request);
            using (var granted = await Browser(visitor, "/api/session-demo/consent", new { requestId = consent.RequestId, allow = true, confirmed = true }, csrf, origin)) granted.EnsureSuccessStatusCode();
            using var issued = await Native(path + "/events/" + scope + "/open", new { participantId = participant }, app.Key);
            var next = await Read<SessionDemoOpenLink>(issued); secret(next.Token);
            var html = await viewer.GetStringAsync("/SessionDemo/" + next.ViewId);
            using var redeemed = await Browser(viewer, "/api/session-demo/views/" + next.ViewId + "/redeem", new { token = next.Token }, Field(html, "__RequestVerificationToken"), origin);
            redeemed.EnsureSuccessStatusCode();
            secret(redeemed.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(SessionDemoEndpoints.ViewerCookie + "=", StringComparison.Ordinal)).Split(';')[0].Split('=', 2)[1]);
            return "/api/session-demo/views/" + next.ViewId;
        }
        var openEndView = await OpenView(a, p1.Id, first, one.Csrf);
        using (var ended = await Form(first, "/Demo?handler=End", origin, new() { ["__RequestVerificationToken"] = one.Csrf }))
            TestProgram.Check(ended.StatusCode == HttpStatusCode.Redirect && ended.Headers.Location!.ToString() == "/Complete", "Explicit HTTPS end in an open round succeeds without a foreign-key error");
        using (var closed = await viewer.GetAsync(openEndView)) TestProgram.Check(closed.StatusCode == HttpStatusCode.NotFound, "Open-round HTTPS end immediately removes synthetic viewer access");
        using (var ordinary = await first.GetAsync("/Demo")) TestProgram.Check(ordinary.StatusCode == HttpStatusCode.Redirect, "Ended open-round visitor cannot reopen the portal");
        using (var survivors = await Native(eventPath + "/participants", key: app.Key)) TestProgram.Check((await Read<ParticipantRoster>(survivors)).Participants.Single().Alias == two.Alias, "Ending one HTTPS visitor leaves the other event participant active");
        using (var selected = await Native(path + "/selection", new { eventId = b, version = 3 }, app.Key)) selected.EnsureSuccessStatusCode();
        var closedEndView = await OpenView(b, foreignParticipant.Id, foreign, other.Csrf);
        using (var active = await viewer.GetAsync(closedEndView))
            TestProgram.Check(active.StatusCode == HttpStatusCode.OK, "HTTPS synthetic view is active before its round closes");
        var exerciseForm = Regex.Match(await foreign.GetStringAsync("/SignedIn"),
            "<form\\b[^>]*\\bid=\"clickfix-antiforgery\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
        var exerciseCsrf = Field(exerciseForm, "__RequestVerificationToken");
        TestProgram.Check(exerciseCsrf.Length > 0, "HTTPS winner fixture finds the exercise antiforgery field");
        using (var begunExercise = await Browser(foreign, "/SignedIn?handler=BeginExercise", new { }, exerciseCsrf, origin))
            TestProgram.Check(!(await Read<JsonElement>(begunExercise)).GetProperty("completed").GetBoolean(), "HTTPS winner fixture starts a fresh exercise");
        using (var completedExercise = await Browser(foreign, "/ClickFix?handler=Complete", new { }, exerciseCsrf, origin))
            TestProgram.Check((await Read<JsonElement>(completedExercise)).GetProperty("completed").GetBoolean(), "HTTPS winner fixture completes the exercise before the draw");

        var bDetails = "/Presenter/Prizes/Event/" + b;
        var bHtml = await HttpRateLimitFixture.GetStringAsync(owner, bDetails);
        var roundId = Field(bHtml, "RoundId");
        TestProgram.Check(Guid.TryParse(roundId, out _), "HTTPS winner fixture identifies the open round on the event page");
        var progressPath = $"/Presenter/Events?handler=Progress&id={b}&roundId={roundId}";
        async Task<RoundProgress> Result(string phase)
        {
            using var response = await HttpRateLimitFixture.GetAsync(owner, progressPath);
            Console.WriteLine($"Session round {phase} GET {new Uri(owner.BaseAddress!, progressPath)} -> {(int)response.StatusCode}; expectedAlias={other.Alias}");
            TestProgram.Check(response.StatusCode == HttpStatusCode.OK, "HTTPS round progress returns OK for the requested round");
            var progress = await Read<RoundProgress>(response);
            var round = progress.Round;
            Console.WriteLine($"Session round {phase} event={round.EventId}; roundId={round.GroupId}; round={round.Number}; status={round.Status}; completed={progress.Completed}; eligible={progress.Eligible}; prizeAvailable={progress.PrizeAvailable}; winnerCount={round.WinnerCount}; winners={string.Join(", ", round.Winners.Select(w => $"{w.Alias} / {w.PrizeName}"))}");
            TestProgram.Check(round.EventId == b && round.GroupId == roundId && round.Number == foreignParticipant.Round,
                "HTTPS round progress identifies the participant's exact event and round");
            return progress;
        }

        var beforeClose = await Result("before close");
        TestProgram.Check(beforeClose is { Completed: 1, Eligible: 1, CanClose: true, PrizeAvailable: true }
            && beforeClose.Round.Status == "Open" && beforeClose.Participants is [{ Completed: true } candidate] && candidate.Alias == other.Alias,
            "HTTPS round has exactly one eligible participant and a prize before closing");
        using (var closedRound = await Form(owner, bDetails + "?handler=CloseRound", origin, new() {
            ["__RequestVerificationToken"] = Field(bHtml, "__RequestVerificationToken"), ["RoundId"] = roundId, ["confirmed"] = "true"
        })) TestProgram.Check(closedRound.StatusCode == HttpStatusCode.Redirect, "HTTPS fixture closes the round while keeping its participant session active");
        using (var closed = await viewer.GetAsync(closedEndView))
            TestProgram.Check(closed.StatusCode == HttpStatusCode.NotFound, "Closing the round immediately removes synthetic viewer access");
        using (var denied = await Native(path + "/events/" + b + "/consent", new { participantId = foreignParticipant.Id, wpfRunId = Guid.NewGuid().ToString("D") }, app.Key))
            TestProgram.Check(denied.StatusCode == HttpStatusCode.NotFound, "Closed-round HTTPS participant cannot receive a new consent request");
        async Task<string> CheckWinner(string phase, string assertion)
        {
            var result = (await Result(phase)).Round;
            TestProgram.Check(result is { Status: "Closed", WinnerCount: 1 } && result.Winners.Count == 1
                && result.Winners[0].Alias == other.Alias, "HTTPS closed-round result names the expected winner " + phase);
            var prize = result.Winners.Single().PrizeName;
            using var response = await HttpRateLimitFixture.GetAsync(owner, bDetails);
            var html = await response.Content.ReadAsStringAsync();
            var rounds = Regex.Match(html, "<section\\b[^>]*\\bid=\"prize-round-management\"[^>]*>.*?(?=<form\\b[^>]*\\bid=\"prize-settings\"|</main>)", RegexOptions.Singleline).Value;
            var resultRows = Regex.Matches(rounds, "<li\\b[^>]*>(.*?)</li>", RegexOptions.Singleline);
            var winnerRendered = resultRows.Any(row => WebUtility.HtmlDecode(Regex.Replace(row.Groups[1].Value, "<[^>]+>", "")).Trim()
                .EndsWith($" {result.Number} · {other.Alias} · {prize}", StringComparison.Ordinal));
            Console.WriteLine($"Session round {phase} HTML GET {new Uri(owner.BaseAddress!, bDetails)} -> {(int)response.StatusCode}; resultRows={resultRows.Count}; winnerRendered={winnerRendered}");
            TestProgram.Check(response.StatusCode == HttpStatusCode.OK && winnerRendered, assertion);
            return prize;
        }

        var prizeBeforeEnd = await CheckWinner("before account end", "Closed-round HTTPS result contains the active winner alias before explicit end");
        using (var ended = await Form(foreign, "/Demo?handler=End", origin, new() { ["__RequestVerificationToken"] = other.Csrf }))
            TestProgram.Check(ended.StatusCode == HttpStatusCode.Redirect && ended.Headers.Location!.ToString() == "/Complete", "Explicit HTTPS end of a closed-round winner succeeds without a foreign-key error");
        using (var ordinary = await foreign.GetAsync("/Demo"))
            TestProgram.Check(ordinary.StatusCode == HttpStatusCode.Redirect, "Ended closed-round visitor cannot reopen the portal");
        var prizeAfterEnd = await CheckWinner("after account end", "Closed-round HTTPS result retains the same winner alias after account removal");
        TestProgram.Check(prizeBeforeEnd == prizeAfterEnd, "Closed-round HTTPS result retains the same prize after account removal");
        using (var disconnected = await Native(path + "/disconnect", new { }, app.Key)) disconnected.EnsureSuccessStatusCode();
    }
    private static Task<HttpResponseMessage> Browser(HttpClient client, string path, object body, string? csrf, string? origin) =>
        HttpRateLimitFixture.SendAsync(async () => {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
            if (origin is not null) request.Headers.Add("Origin", origin);
            return await client.SendAsync(request);
        });
}
