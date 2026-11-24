using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Tests;

internal static class DiscoverablePresenterChecks
{
    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
    private static async Task<(string Browser,string Op,string Options)> Options(PresenterService s,string kind="login",ClaimsPrincipal? principal=null,string login="")
    {
        var browser=DemoIdentity.RandomToken();var op=Guid.NewGuid().ToString("D");
        return(browser,op,await s.OptionsAsync(browser,op,kind,login,null,principal??Anonymous,true,default));
    }
    private static Task<ClaimsPrincipal?> Verify(PresenterService s,(string Browser,string Op,string Options) ceremony,JsonElement response,string kind="login",ClaimsPrincipal? principal=null) =>
        s.VerifyAsync(ceremony.Browser,ceremony.Op,kind,response,"",null,principal??Anonymous,true,default);

    internal static async Task RunAsync(string root,DemoStore store,TestClock clock,PresenterService s,ClaimsPrincipal admin,
        SoftwareAuthenticator primary,SoftwareAuthenticator reserve,ClaimsPrincipal person,SoftwareAuthenticator personKey)
    {
        var a=await Options(s);using(var json=JsonDocument.Parse(a.Options))
            TestProgram.Check(json.RootElement.GetProperty("allowCredentials").GetArrayLength()==0 && json.RootElement.GetProperty("userVerification").GetString()=="required",
                "Username-less options have an empty allowlist and mandatory user verification");
        await TestProgram.RejectAsync(()=>s.OptionsAsync(a.Browser,Guid.NewGuid().ToString("D"),"login","",null,Anonymous,true,default),"Unbound login still permits only one active browser ceremony");
        var response=primary.Authenticate(a.Options);var session=(await Verify(s,a,response))!;
        TestProgram.Check(session!.FindFirstValue(ClaimTypes.NameIdentifier)==admin.FindFirstValue(ClaimTypes.NameIdentifier),"Discoverable primary credential resolves its persistent account without a username");
        await TestProgram.RejectAsync(()=>Verify(s,a,response),"Discoverable login challenge is one-time");
        var b=await Options(s);var backup=await Verify(s,b,reserve.Authenticate(b.Options));
        TestProgram.Check(backup!.FindFirstValue(ClaimTypes.NameIdentifier)==admin.FindFirstValue(ClaimTypes.NameIdentifier),"Second discoverable credential signs in to the same account");

        // A supplied account hint or existing browser session must never determine the selected identity.
        var other=await Options(s,principal:session,login:"admin-one");
        var selected=(await Verify(s,other,personKey.Authenticate(other.Options),principal:session))!;
        TestProgram.Check(selected!.FindFirstValue(ClaimTypes.NameIdentifier)==person.FindFirstValue(ClaimTypes.NameIdentifier)
            && selected.FindFirstValue("sid")!=session.FindFirstValue("sid") && !await s.ValidateAsync(session,default)
            && await s.ValidateAsync(person,default) && await s.ValidateAsync(admin,default),
            "Selected credential determines the account; session replacement affects only the carried browser session");
        foreach(var fault in new[]{"unknown","handle","empty-handle","missing-handle","signature","origin","rp","uv","challenge","backup","counter","id-mismatch"})
        {
            var c=await Options(s);using var unknown=new SoftwareAuthenticator();
            var assertion=(fault=="unknown"?unknown:primary).Authenticate(c.Options,
                userHandle:fault=="handle"?personKey.UserHandle:fault=="empty-handle"?[]:null,
                badSignature:fault=="signature",origin:fault=="origin"?"https://other.invalid":"https://localhost:7243",
                rpId:fault=="rp"?"other.invalid":"localhost",verified:fault!="uv",
                challenge:fault=="challenge"?"test-invalid":null,backupEligible:fault=="backup",counter:fault=="counter"?0u:null);
            if(fault is "missing-handle" or "id-mismatch") {
                var node=JsonNode.Parse(assertion.GetRawText())!;
                if(fault=="missing-handle")node["response"]!["userHandle"]=null;
                else node["id"]="AQID";
                assertion=JsonSerializer.SerializeToElement(node);
            }
            await TestProgram.RejectAsync(()=>Verify(s,c,assertion),"Discoverable assertion rejects "+fault);
            await TestProgram.RejectAsync(()=>Verify(s,c,primary.Authenticate(c.Options)),"Rejected "+fault+" assertion cannot reuse its challenge");
        }
        var bound=await Options(s,"reauth",person);
        using(var json=JsonDocument.Parse(bound.Options)) TestProgram.Check(json.RootElement.GetProperty("allowCredentials").GetArrayLength()==1,"Reauthentication retains only the current account's credentials");
        await TestProgram.RejectAsync(()=>Verify(s,bound,primary.Authenticate(bound.Options),"reauth",person),"Another account's valid presenter credential cannot reauthenticate the current account");
        var cross=await Options(s);await TestProgram.RejectAsync(()=>s.VerifyAsync(DemoIdentity.RandomToken(),cross.Op,"login",primary.Authenticate(cross.Options),"",null,Anonymous,true,default),"Another browser cannot consume a discoverable login challenge");
        var crossSession=await Verify(s,cross,primary.Authenticate(cross.Options));
        TestProgram.Check(await s.ValidateAsync(crossSession!,default),"Correct browser can still finish its own unconsumed challenge");
        var late=await Options(s);clock.Advance(TimeSpan.FromMinutes(3));
        await TestProgram.RejectAsync(()=>Verify(s,late,primary.Authenticate(late.Options)),"Expired discoverable challenge cannot create a session");

        using var peer=new DemoStore(store.DatabasePath);var serviceB=PresenterChecks.Service(peer,clock);
        var ca=await Options(s);var cb=await Options(serviceB);
        var concurrent=await Task.WhenAll(Task.Run(()=>Verify(s,ca,primary.Authenticate(ca.Options))),Task.Run(()=>Verify(serviceB,cb,personKey.Authenticate(cb.Options))));
        TestProgram.Check(concurrent[0]!.FindFirstValue(ClaimTypes.NameIdentifier)!=concurrent[1]!.FindFirstValue(ClaimTypes.NameIdentifier)
            && await s.ValidateAsync(concurrent[0]!,default) && await serviceB.ValidateAsync(concurrent[1]!,default),"Concurrent username-less presenters receive independent correct identities and sessions");
        var once=await Options(s);var onceResponse=primary.Authenticate(once.Options);
        async Task<bool> Attempt(PresenterService service) {try {await Verify(service,once,onceResponse);return true;}catch(DemoRequestException){return false;}}
        var replay=await Task.WhenAll(Task.Run(()=>Attempt(s)),Task.Run(()=>Attempt(serviceB)));
        TestProgram.Check(replay.Count(x=>x)==1,"Concurrent discoverable assertion replay succeeds exactly once across service instances");
        await PresenterMigrationChecks.RunAsync(root,store,clock,admin,primary,reserve,personKey);
    }
}
