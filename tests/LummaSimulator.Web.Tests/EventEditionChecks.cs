using System.Security.Claims;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;
internal static class EventEditionChecks
{
    public static async Task RunAsync(string root)
    {
        using var db = new DemoStore(Path.Combine(root, "edition-test.db")); await db.InitializeAsync();
        var clock = new TestClock();
        var fido = new Fido2(new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] }.ToFidoConfiguration());
        var presenters = new PresenterService(db, new ConfigurationBuilder().Build(), clock, fido);
        var demos = new DemoService(db, fido, clock, NullLogger<DemoService>.Instance);
        using var key = new SoftwareAuthenticator();
        var invitation = await presenters.IssueBootstrapAsync("edition-test", "Synthetic operator", default);
        var grant = await presenters.OpenEnrollmentAsync(invitation.Code, true, default);
        var browser = DemoIdentity.RandomToken(); var operation = Guid.NewGuid().ToString("D");
        var options = await presenters.OptionsAsync(browser, operation, "enroll", "", grant, new ClaimsPrincipal(), true, default);
        await presenters.VerifyAsync(browser, operation, "enroll", key.Register(options), "Synthetic key", grant, new ClaimsPrincipal(), true, default);
        operation = Guid.NewGuid().ToString("D");
        options = await presenters.OptionsAsync(browser, operation, "login", "", null, new ClaimsPrincipal(), true, default);
        var actor = (await presenters.VerifyAsync(browser, operation, "login", key.Authenticate(options), "", null, new ClaimsPrincipal(), true, default))!;
        var manifestPath = Path.Combine(root, "edition.manifest");
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "edition-test-keys")));
        EventEditionService Service() => new(new EventEditionStore(manifestPath, protection), db, presenters, clock, protection, new ExperienceSettings());
        var service = Service();
        await TestProgram.RejectAsync(() => service.CreateAsync(new ClaimsPrincipal(), "Denied", clock.GetUtcNow(), clock.GetUtcNow().AddHours(1), default), "Event metadata requires current operator authentication");
        await service.CreateAsync(actor, "Synthetic event", clock.GetUtcNow(), clock.GetUtcNow().AddHours(1), default);
        var item = (await service.ListAsync(actor, default)).Single();
        TestProgram.Check(item.Status == "Scheduled" && item.PresenterId == actor.FindFirstValue(ClaimTypes.NameIdentifier), "Event planned dates and assignment stored without granting new roles");
        await service.StatusAsync(actor, item.Id, false, default);
        var first = await demos.CreateAsync("laptop", default); var second = await demos.CreateAsync("phone", default);
        await new EventAccess(db,presenters,clock,new EventEditionStore(manifestPath,protection)).BindVisitorAsync(first.Token,"edition",item.Id,default);
        using var visitorKey = new SoftwareAuthenticator();
        options = await demos.OptionsAsync(first.Token, true, default);
        await demos.VerifyAsync(first.Token, true, visitorKey.Register(options), default);
        await TestProgram.RejectAsync(() => service.TicketAsync(first.Token, default), "ClickFix ticket requires login, not registration alone");
        options = await demos.OptionsAsync(first.Token, false, default);
        await demos.VerifyAsync(first.Token, false, visitorKey.Authenticate(options), default);
        var wpf = Guid.NewGuid().ToString("D");
        var code = await service.LinkAsync(actor, item.Id, first.Account.DemoRunId, wpf, default);
        var before = await service.PollAsync(code, wpf, default);
        TestProgram.Check(before.Action is null && before.PortalRunId == first.Account.DemoRunId, "Explicit server-side portal/WPF pairing contains no automatic choice");
        await TestProgram.RejectAsync(() => service.LinkAsync(actor, item.Id, second.Account.DemoRunId, wpf, default), "A WPF run cannot silently switch visitors");
        await TestProgram.RejectAsync(() => service.PollAsync(code, Guid.NewGuid().ToString("D"), default), "Read code cannot access another WPF run");
        await TestProgram.RejectAsync(() => service.PollAsync(DemoIdentity.RandomToken(), wpf, default), "Read code authorization fails closed");
        var ticket = (await service.TicketAsync(first.Token, default))!;
        var refreshedTicket = (await service.TicketAsync(first.Token, default))!;
        TestProgram.Check((await service.PollAsync(code, wpf, default)).Action is null, "Opening and refreshing ClickFix issue no action");
        await TestProgram.RejectAsync(() => service.ChoiceAsync(second.Token, ticket, "ClickFixFollowSimulated", default), "Visitor cannot use another visitor's action ticket");
        await TestProgram.RejectAsync(() => service.ChoiceAsync(first.Token, ticket, "Completed", default), "No arbitrary completed-step claims accepted");
        var action = await service.ChoiceAsync(first.Token, ticket, "ClickFixFollowSimulated", default);
        var duplicate = await service.ChoiceAsync(first.Token, refreshedTicket, "ClickFixFollowSimulated", default);
        TestProgram.Check(action == duplicate && action.Sequence == 1 && action.OccurredAtUtc == clock.GetUtcNow(), "Retry and refresh are idempotent with server-assigned sequence and time");
        await TestProgram.RejectAsync(() => service.ChoiceAsync(first.Token, ticket, "ClickFixStopSelected", default), "Conflicting choice cannot overwrite recorded history");
        var restarted = Service();
        TestProgram.Check((await restarted.PollAsync(code, wpf, default)).Action == action, "Manifest restart preserves pairing and deduplication");
        TestProgram.Check(!System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(manifestPath)).Contains("Synthetic event"), "Manifest contents encrypted outside public assets");
        await service.StatusAsync(actor, item.Id, true, default);
        var closed = await restarted.PollAsync(code, wpf, default);
        TestProgram.Check(closed.Status == "Closed" && closed.Action == action, "Closing event retains historical run result");
        await TestProgram.RejectAsync(() => service.StatusAsync(actor, item.Id, false, default), "Closed event cannot reopen and rewrite history");
        await TestProgram.RejectAsync(() => service.ChoiceAsync(first.Token, ticket, "ClickFixFollowSimulated", default), "Closed event rejects new action submission");
        var schema = await db.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%Edition%'", default);
        TestProgram.Check(Convert.ToInt32(schema) == 0, "Event Edition creates no SQLite schema objects");
        await presenters.SignOutAsync(actor, default);
        await TestProgram.RejectAsync(() => service.PollAsync(code, wpf, default), "Operator logout immediately revokes linked read code");
        await File.WriteAllTextAsync(manifestPath, "CORRUPTED-SYNTHETIC-MANIFEST");
        var failed = false; try { await new EventEditionStore(manifestPath, protection).AccessAsync(false, m => Task.FromResult(m.Events.Count), default); } catch { failed = true; }
        TestProgram.Check(failed, "Corruption fails closed without automatic restore");
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers.Cookie = $"{VisitorCookie.Name}={first.Token}";
        var exercise=await new VisitorExerciseService(db,new SecurityAuditService(db,clock),clock,Service()).BeginAsync(first.Token,default);
        TestProgram.Check(exercise.NotificationUnavailable && exercise.Attempt is null,"Unavailable event manifest disables notification but preserves the explicitly started authenticated exercise");
        await new EventEditionStore(manifestPath, protection).RecoverAsync(default);
        var recovered = await new EventEditionStore(manifestPath, protection).AccessAsync(false, m => Task.FromResult(m), default);
        TestProgram.Check(recovered.Events.All(e => e.Status == "Closed" && e.Runs.All(r => r.CodeHash == "")), "Explicit recovery closes events and revokes all previous codes");
        TestProgram.Check(Directory.GetFiles(root, "edition.manifest.damaged-*").Length == 1, "Recovery retains damaged evidence rather than deleting it");
    }
}
