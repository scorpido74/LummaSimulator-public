using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Pages;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LummaSimulator.Web.Tests;

internal static class ClickFixChecks
{
    public static async Task RunAsync(string root)
    {
        using var store = new DemoStore(Path.Combine(root, "clickfix-only.db"));
        await store.InitializeAsync();
        var clock = new TestClock();
        var settings = new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] };
        var demos = new DemoService(store, new Fido2(settings.ToFidoConfiguration()), clock, NullLogger<DemoService>.Instance);
        var visitor = await demos.CreateAsync("laptop", default);
        async Task<IActionResult> Open()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{VisitorCookie.Name}={visitor.Token}";
            return await new SignedInModel(new SecurityAuditService(store,clock),new LummaSimulator.Web.Services.Prizes.PrizeRoundsService(store,PresenterChecks.Service(store,clock),clock)) { PageContext = new PageContext { HttpContext = context },
                TempData = new TempDataDictionary(context, new TestTempDataProvider()) }.OnGetAsync();
        }
        TestProgram.Check(await Open() is RedirectToPageResult { PageName: "/Demo" }, "ClickFix rejects owned account without sign-in");
        using var authenticator = new SoftwareAuthenticator();
        var registration = await demos.OptionsAsync(visitor.Token, true, default);
        await demos.VerifyAsync(visitor.Token, true, authenticator.Register(registration), default);
        var options = await demos.OptionsAsync(visitor.Token, false, default);
        await demos.VerifyAsync(visitor.Token, false, authenticator.Authenticate(options), default);
        // Only this test's new synthetic database is changed to the preceding schema.
        var beforeCredential=(await store.CredentialsAsync(visitor.Account.Id,default)).Single();
        await store.ExecuteAsync("DROP TABLE VisitorExercises; DROP TABLE EventLoginRoutes",default);
        var injected=false;
        try { await VisitorFlowMigration.InitializeAsync(store,default,()=>throw new IOException("Synthetic rollback")); }
        catch(IOException) { injected=true; }
        TestProgram.Check(injected&&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('EventLoginRoutes','VisitorExercises')",default))==0,"Visitor schema creation rolls back completely on injected failure");
        await VisitorFlowMigration.InitializeAsync(store,default);
        await store.InitializeAsync();
        var afterCredential=(await store.CredentialsAsync(visitor.Account.Id,default)).Single();
        TestProgram.Check(beforeCredential.PublicKey.SequenceEqual(afterCredential.PublicKey)&&beforeCredential.Counter==afterCredential.Counter&&await demos.GetAsync(visitor.Token,false,default) is not null,"Visitor schema upgrade and repeat initialization preserve credentials and visitor session");
        TestProgram.Check(await Open() is PageResult, "ClickFix accepts real library-verified test assertion");
        var beginService=new VisitorExerciseService(store,new SecurityAuditService(store,clock),clock);
        var earlyContext=new DefaultHttpContext();earlyContext.Request.Headers.Cookie=$"{VisitorCookie.Name}={visitor.Token}";
        earlyContext.Request.Headers.Origin="https://localhost:7243";
        earlyContext.RequestServices=new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
        var early=new ClickFixModel(demos,clock,store,new SecurityAuditService(store,clock)){PageContext=new PageContext{HttpContext=earlyContext}};
        TestProgram.Check(await early.OnPostCompleteAsync() is BadRequestResult,"Exercise cannot complete without an explicit start");
        TestProgram.Check(!(await beginService.BeginAsync(visitor.Token,default)).Completed,"Explicit click starts the owned exercise without optimistic completion");
        TestProgram.Check(await early.OnPostCompleteAsync() is JsonResult,"Explicitly started exercise can complete without the removed automatic reading timer");
        clock.Advance(TimeSpan.FromSeconds(-1));
        TestProgram.Check(await Open() is RedirectToPageResult { PageName: "/Demo" }, "ClickFix rejects a future authentication timestamp");
        clock.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromMinutes(30));
        // Keep only this new synthetic account alive to isolate authentication expiry from idle expiry.
        await store.ExecuteAsync("UPDATE Demos SET LastSeen=$now WHERE Id=$id", default, ("$now", clock.GetUtcNow().ToUnixTimeMilliseconds()), ("$id", visitor.Account.Id));
        TestProgram.Check(await Open() is RedirectToPageResult { PageName: "/Demo" }, "ClickFix rejects authentication at the 30 minute boundary");
        var freshOptions = await demos.OptionsAsync(visitor.Token, false, default);
        await demos.VerifyAsync(visitor.Token, false, authenticator.Authenticate(freshOptions), default);
        TestProgram.Check(await Open() is PageResult, "ClickFix permits reopening after fresh authentication");
        var completionContext = new DefaultHttpContext();
        completionContext.Request.Headers.Cookie = $"{VisitorCookie.Name}={visitor.Token}";
        completionContext.Request.Headers.Origin = "https://localhost:7243";
        completionContext.RequestServices = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
        var completion = new ClickFixModel(demos,clock,store,new SecurityAuditService(store,clock)) { PageContext=new PageContext { HttpContext=completionContext } };
        // The original start and completion survive reauthentication.
        TestProgram.Check(await completion.OnPostCompleteAsync() is JsonResult,"Completion remains valid after reauthentication");
        var at=await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",default,("$id",visitor.Account.Id));
        TestProgram.Check(await completion.OnPostCompleteAsync() is JsonResult && Equals(at,await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",default,("$id",visitor.Account.Id))),"Completion retry preserves the original completion timestamp");
        TestProgram.Check((await beginService.BeginAsync(visitor.Token,default)).Completed,"Completed exercise authorizes visual replay without resetting durable completion");
        TestProgram.Check(Equals(at,await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",default,("$id",visitor.Account.Id)))&&await new SecurityAuditService(store,clock).OwnerAsync(visitor.Token,default) is not null,"Replay preserves completion timestamp and the same authenticated demo");
        clock.Advance(TimeSpan.FromMinutes(30));
        var deniedReplay=false;try{await beginService.BeginAsync(visitor.Token,default);}catch(LummaSimulator.Web.Models.DemoRequestException){deniedReplay=true;}
        TestProgram.Check(deniedReplay,"Expired authentication cannot authorize replay");
        completionContext.Request.Headers.Origin="https://other.invalid";
        TestProgram.Check(await completion.OnPostCompleteAsync() is BadRequestResult,"Completion rejects a different Origin");
        await demos.EndOwnAsync(visitor.Token, default);
        TestProgram.Check(await Open() is RedirectToPageResult { PageName: "/Demo" }, "SignedIn rejects ended account");
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
