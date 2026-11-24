using System.Security.Claims;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

internal static class ExperienceChecks
{
    private static DemoService Service(DemoStore store, TestClock clock) => new(store,
        new Fido2(new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] }.ToFidoConfiguration()),
        clock, NullLogger<DemoService>.Instance);
    public static async Task RunAsync(string root)
    {
        var db = Path.Combine(root, "experience.db");
        using var store = new DemoStore(db); await store.InitializeAsync();
        using var peer = new DemoStore(db); await peer.InitializeAsync();
        var clock = new TestClock(); var service = Service(store, clock); var secondService = Service(peer, clock);
        var stats = new ExperienceStatistics(store);
        var presenters=PresenterChecks.Service(store,clock);var peerPresenters=PresenterChecks.Service(peer,clock);
        using var presenterKey=new SoftwareAuthenticator();
        var invitation=await presenters.IssueBootstrapAsync("experience-admin","Synthetic experience admin",default);
        await PresenterChecks.Enroll(presenters,invitation.Code,presenterKey);
        var presenter=await PresenterChecks.Login(presenters,"experience-admin",presenterKey);
        var access=new EventAccess(store,presenters,clock);
        var initial=(await access.ListAsync(presenter,default)).Single(e=>e.State=="Active");
        await PresenterChecks.Reauth(presenters,presenter,presenterKey);
        await access.TransferAsync(presenter,"experience",initial.Id,presenter.FindFirstValue(ClaimTypes.NameIdentifier)!,"recover",default);
        var participants = new List<(string Token, string Id, SoftwareAuthenticator Auth)>();
        try
        {
            foreach (var route in new[] { "yubikey", "laptop", "phone", "ipad", "yubikey", "laptop" })
            {
                var demo = await service.CreateAsync(route, default); var auth = new SoftwareAuthenticator();
                participants.Add((demo.Token, demo.Account.Id, auth));
                await access.BindVisitorAsync(demo.Token,"experience",initial.Id,default);
                await service.VerifyAsync(demo.Token, true, auth.Register(await service.OptionsAsync(demo.Token, true, default)), default);
            }
            var before = await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
            TestProgram.Check(before.TotalVisitors == 0 && before.Authentications == 0, "Registration alone does not count a visitor or authentication");
            var requests = new List<Func<Task<LummaSimulator.Web.Models.VerificationReceipt>>>();
            for (var i = 0; i < participants.Count; i++)
            {
                var p = participants[i]; var owner = i % 2 == 0 ? service : secondService;
                var response = p.Auth.Authenticate(await owner.OptionsAsync(p.Token, false, default));
                requests.Add(() => owner.VerifyAsync(p.Token, false, response, default));
            }
            var receipts = await Task.WhenAll(requests.Select(r => Task.Run(r)));
            TestProgram.Check(receipts.Select(r => r.VisitorNumber).Order().SequenceEqual(Enumerable.Range(1, 6).Select(n => (long?)n)),
                "Concurrent real WebAuthn authentications across separate stores allocate unique consecutive visitor numbers");
            var first = participants[0];
            var repeated = await service.VerifyAsync(first.Token, false, first.Auth.Authenticate(await service.OptionsAsync(first.Token, false, default)), default);
            TestProgram.Check(repeated.VisitorNumber == receipts[0].VisitorNumber, "Repeated authentication retains the same visitor number");
            var summary = await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
            TestProgram.Check(summary.TotalVisitors == 6 && summary.VisitorsToday == 6 && summary.EventVisitors == 6 && summary.Authentications == 7,
                "Persistent totals, UTC daily visitors, active event and authentication totals are distinct");
            await service.OptionsAsync(first.Token, false, default);
            var previous = await service.GetAsync(first.Token, false, default);
            var next = await service.CreateAsync("phone", default, first.Token);
            TestProgram.Check(await service.GetAsync(first.Token, false, default) is null
                && next.Account.Id != previous!.Id && !next.Account.UserHandle.SequenceEqual(previous.UserHandle)
                && next.Account.Successes == 0 && next.Account.CredentialCount == 0, "New visitor revokes old token and creates a fresh empty WebAuthn identity");
            TestProgram.Check((await store.CredentialsAsync(first.Id, default)).Count == 1
                && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Demos WHERE Id=$id", default, ("$id", first.Id))) == 1,
                "New visitor preserves previous account and credential until automatic cleanup");
            TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$id", default, ("$id", first.Id))) == 0,
                "New visitor removes the previous outstanding challenge");
            TestProgram.Check((await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default)).ActiveAccounts == 6,
                "Retained closed visitor accounts are not reported as active visitors");
            await TestProgram.RejectAsync(() => service.OptionsAsync(first.Token, false, default), "Old session cannot obtain options for previous or new visitor");
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            var passwordPresenter = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Presenter")], "password"));
            foreach (var denied in new[] { anonymous, passwordPresenter })
            {
                var rejected = false;
                try { await stats.StartEventAsync(denied, summary.ActiveEvent.Id, "Denied", true, clock.GetUtcNow().ToUnixTimeMilliseconds(), default); }
                catch (UnauthorizedAccessException) { rejected = true; }
                TestProgram.Check(rejected, "Event management is closed without a validated presenter-WebAuthn identity");
            }
            // Use the current management service so new events also have an authorization scope.
            var unconfirmed = false;
            try { await stats.StartEventAsync(presenter, summary.ActiveEvent.Id, "Unconfirmed", false, clock.GetUtcNow().ToUnixTimeMilliseconds(), default); }
            catch (UnauthorizedAccessException) { unconfirmed = true; }
            TestProgram.Check(unconfirmed, "New event requires explicit confirmation");
            async Task<bool> Switch(PresenterService backend, string name)
            {
                try { await backend.EventAsync(presenter,summary.ActiveEvent.Id,name,false,true,default); return true; }
                catch (Exception e) when (e is InvalidOperationException or LummaSimulator.Web.Models.DemoRequestException) { return false; }
            }
            var switches = await Task.WhenAll(Task.Run(() => Switch(presenters, "Event A")), Task.Run(() => Switch(peerPresenters, "Event B")));
            TestProgram.Check(switches.Count(x => x) == 1, "Concurrent event changes use expected-event compare-and-swap; exactly one wins");
            var changed = await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
            TestProgram.Check(changed.EventVisitors == 0 && changed.TotalVisitors == 6 && changed.Events.Count == 2
                && changed.Events.Single(e => e.Status == "Closed").EndedAt.HasValue, "New event starts at zero and retains closed event history");
            var priorContext=(string?)await store.ScalarAsync("SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$demo",default,("$demo",next.Account.Id));
            TestProgram.Check(priorContext==summary.ActiveEvent.Id,"An unregistered demo created before the switch keeps its original explicit event context");
            // Selecting the new event is explicit and must precede credential registration.
            await new EventAccess(store,presenters,clock).BindVisitorAsync(next.Token,"experience",changed.ActiveEvent.Id,default);
            using var nextAuth = new SoftwareAuthenticator();
            await service.VerifyAsync(next.Token, true, nextAuth.Register(await service.OptionsAsync(next.Token, true, default)), default);
            var newReceipt = await service.VerifyAsync(next.Token, false, nextAuth.Authenticate(await service.OptionsAsync(next.Token, false, default)), default);
            await NumberCheck(store,next.Account.Id,newReceipt,changed.ActiveEvent.Id,1,"New event has its own consecutive visitor numbers");
            var oldParticipant = participants[1];
            var oldReceipt = await service.VerifyAsync(oldParticipant.Token, false, oldParticipant.Auth.Authenticate(await service.OptionsAsync(oldParticipant.Token, false, default)), default);
            TestProgram.Check(oldReceipt.EventId == summary.ActiveEvent.Id && oldReceipt.VisitorNumber == receipts[1].VisitorNumber,
                "An existing counted account is not counted again when the active event changes");
            TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM EventPrizePolicies WHERE Enabled<>0 OR Awarded<>0", default)) == 0,
                "Future prize pools are separate per event and remain disabled with no draws or awards");
            var preserved = await stats.SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
            clock.Advance(TimeSpan.FromMinutes(31)); await service.CleanupAsync(default);
            using var restarted = new DemoStore(db); await restarted.InitializeAsync();
            var after = await new ExperienceStatistics(restarted).SummaryAsync(clock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
            TestProgram.Check(after.ActiveAccounts == 0 && after.TotalVisitors == preserved.TotalVisitors && after.Authentications == preserved.Authentications,
                "Cleanup and server-store restart preserve visitor and authentication history");
            TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM VisitorCounts WHERE DemoId IS NOT NULL", default)) == 0,
                "Cleanup removes temporary identity linkage from permanent visitor counts");
        }
        finally { foreach (var p in participants) p.Auth.Dispose(); }

        using var legacyStore = new DemoStore(Path.Combine(root, "legacy-experience.db")); await legacyStore.InitializeAsync();
        var legacyClock = new TestClock(); var legacyService = Service(legacyStore, legacyClock);
        var legacyDemo = await legacyService.CreateAsync("yubikey", default); using var legacyAuth = new SoftwareAuthenticator();
        await legacyService.VerifyAsync(legacyDemo.Token, true, legacyAuth.Register(await legacyService.OptionsAsync(legacyDemo.Token, true, default)), default);
        await legacyStore.ExecuteAsync("UPDATE Demos SET Successes=3; DELETE FROM ExperienceMigrations WHERE Version=3", default);
        // This fixture deliberately removes marker 3; test that historic migration directly.
        // Full startup now correctly rejects incomplete migration histories before any repair.
        await new ExperienceStatistics(legacyStore).InitializeAsync(default); await legacyStore.InitializeAsync();
        var legacyStats = new ExperienceStatistics(legacyStore); var imported = await legacyStats.SummaryAsync(legacyClock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
        TestProgram.Check(imported.TotalVisitors == 1 && imported.Authentications == 3 && imported.VisitorsToday == 0
            && (await legacyStore.CredentialsAsync(legacyDemo.Account.Id, default)).Count == 1,
            "Idempotent legacy migration preserves credentials, imports retained successes once and invents no daily history");
        await legacyService.VerifyAsync(legacyDemo.Token, false, legacyAuth.Authenticate(await legacyService.OptionsAsync(legacyDemo.Token, false, default)), default);
        var legacyAfter = await legacyStats.SummaryAsync(legacyClock.GetUtcNow().ToUnixTimeMilliseconds(), 29, default);
        TestProgram.Check(legacyAfter.TotalVisitors == 1 && legacyAfter.Authentications == 4, "Previously successful legacy visitor is never counted twice");
        await IndependentEvents(root);
    }
    private static async Task NumberCheck(DemoStore store,string demo,VerificationReceipt receipt,string expectedEvent,long expectedNumber,string label)
    {
        var context=(string?)await store.ScalarAsync("SELECT EventId FROM PresenterDemoContexts WHERE DemoId=$demo",default,("$demo",demo));
        var countedEvent=(string?)await store.ScalarAsync("SELECT EventId FROM VisitorCounts WHERE DemoId=$demo",default,("$demo",demo));
        var number=Convert.ToInt64(await store.ScalarAsync("SELECT VisitorNumber FROM VisitorCounts WHERE DemoId=$demo",default,("$demo",demo)));
        var counter=Convert.ToInt64(await store.ScalarAsync("SELECT Visitors FROM ExperienceEvents WHERE Id=$event",default,("$event",expectedEvent)));
        TestProgram.Check(receipt.VisitorNumber==expectedNumber&&receipt.EventId==expectedEvent&&context==expectedEvent&&countedEvent==expectedEvent&&number==expectedNumber,
            $"{label}; expected number={expectedNumber}, receipt number={receipt.VisitorNumber}, stored number={number}, expected event={expectedEvent}, receipt event={receipt.EventId}, context={context}, counted event={countedEvent}, event counter={counter}");
    }
    private static async Task IndependentEvents(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"experience-independent-events.db"));await store.InitializeAsync();
        using var peer=new DemoStore(store.DatabasePath);await peer.InitializeAsync();
        var clock=new TestClock();var presenters=PresenterChecks.Service(store,clock);using var adminKey=new SoftwareAuthenticator();
        var bootstrap=await presenters.IssueBootstrapAsync("numbering-admin","Synthetic numbering admin",default);
        await PresenterChecks.Enroll(presenters,bootstrap.Code,adminKey);var admin=await PresenterChecks.Login(presenters,"numbering-admin",adminKey);
        var access=new EventAccess(store,presenters,clock);
        var eventA=await access.CreateExperienceAsync(admin,"Numbering A",default);var eventB=await access.CreateExperienceAsync(admin,"Numbering B",default);
        var service=Service(store,clock);var second=Service(peer,clock);
        var participants=new List<(NewDemo Demo,string Event,SoftwareAuthenticator Key,DemoService Service)>();
        async Task Add(string eventId,DemoService owner,bool register=true){
            var demo=await owner.CreateAsync("laptop",default);var key=new SoftwareAuthenticator();participants.Add((demo,eventId,key,owner));
            await access.BindVisitorAsync(demo.Token,"experience",eventId,default);
            if(register)await owner.VerifyAsync(demo.Token,true,key.Register(await owner.OptionsAsync(demo.Token,true,default)),default);
        }
        try {
            for(var i=0;i<8;i++)await Add(i%2==0?eventA:eventB,i%4<2?service:second,false);
            await Task.WhenAll(participants.Select(person=>Task.Run(async()=>{
                await person.Service.VerifyAsync(person.Demo.Token,true,person.Key.Register(await person.Service.OptionsAsync(person.Demo.Token,true,default)),default);
            })));
            TestProgram.Check(Convert.ToInt64(await store.ScalarAsync("SELECT SUM(Visitors) FROM ExperienceEvents WHERE Id IN ($a,$b)",default,("$a",eventA),("$b",eventB)))==0,"Two event registrations alone allocate no visitor numbers");
            var requests=new List<Func<Task<VerificationReceipt>>>();
            foreach(var person in participants){
                var assertion=person.Key.Authenticate(await person.Service.OptionsAsync(person.Demo.Token,false,default));
                requests.Add(()=>person.Service.VerifyAsync(person.Demo.Token,false,assertion,default));
            }
            var receipts=await Task.WhenAll(requests.Select(request=>Task.Run(request)));
            foreach(var eventId in new[]{eventA,eventB}){
                var numbers=receipts.Where(r=>r.EventId==eventId).Select(r=>r.VisitorNumber).Order().ToArray();
                TestProgram.Check(numbers.SequenceEqual(Enumerable.Range(1,4).Select(n=>(long?)n)),
                    $"Independent concurrent event numbering; event={eventId}, expected=1,2,3,4, actual={string.Join(",",numbers)}");
            }
            for(var i=0;i<participants.Count;i++)await NumberCheck(store,participants[i].Demo.Account.Id,receipts[i],participants[i].Event,receipts[i].VisitorNumber??-1,"Concurrent receipt matches its selected event and stored number");
            await store.InitializeAsync();await peer.InitializeAsync();
            foreach(var eventId in new[]{eventA,eventB}){
                await Add(eventId,service);var person=participants[^1];
                var receipt=await service.VerifyAsync(person.Demo.Token,false,person.Key.Authenticate(await service.OptionsAsync(person.Demo.Token,false,default)),default);
                await NumberCheck(store,person.Demo.Account.Id,receipt,eventId,5,"Repeated initialization preserves each event's next number");
            }
            var original=participants[0];var again=await original.Service.VerifyAsync(original.Demo.Token,false,original.Key.Authenticate(await original.Service.OptionsAsync(original.Demo.Token,false,default)),default);
            await NumberCheck(store,original.Demo.Account.Id,again,eventA,receipts[0].VisitorNumber??-1,"Repeat authentication retains the first event number");
            TestProgram.Check(Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM VisitorCounts WHERE EventId=$event",default,("$event",eventA)))==5
                &&Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM VisitorCounts WHERE EventId=$event",default,("$event",eventB)))==5,"Repeat authentication never allocates an extra event visitor");
            TestProgram.Check(await store.ScalarAsync("PRAGMA foreign_key_check",default) is null&&(string?)await store.ScalarAsync("PRAGMA integrity_check",default)=="ok","Independent event numbering preserves SQLite integrity and foreign keys");
        }finally{foreach(var person in participants)person.Key.Dispose();}
    }
}
