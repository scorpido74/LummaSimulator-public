using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LummaSimulator.Web.Tests;

internal static class RoundAccountEndChecks
{
    internal static async Task RunAsync(string root)
    {
        using var store = new DemoStore(Path.Combine(root, "round-account-end.db")); await store.InitializeAsync();
        var clock = new TestClock(); var presenters = PresenterChecks.Service(store, clock);
        using var presenterKey = new SoftwareAuthenticator();
        var bootstrap = await presenters.IssueBootstrapAsync("round-end-presenter", "Synthetic round end", default);
        await PresenterChecks.Enroll(presenters, bootstrap.Code, presenterKey);
        var actor = await PresenterChecks.Login(presenters, "round-end-presenter", presenterKey);
        var events = new PrizeEventService(store, presenters, clock); var rounds = new PrizeRoundsService(store, presenters, clock);
        var access = new EventAccess(store, presenters, clock); var state = new SessionDemoState(clock);
        var sessions = new SessionDemoService(state, store, presenters, clock, new ExperienceSettings());
        var demos = new DemoService(store, new Fido2(new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] }.ToFidoConfiguration()), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DemoService>.Instance, sessionDemos: state);
        async Task<(NewDemo Demo, PrizeRoundSnapshot Round)> Visitor(string eventId, SoftwareAuthenticator key)
        {
            var demo = await demos.CreateAsync("laptop", default); await access.BindVisitorAsync(demo.Token, "prize", eventId, default);
            await demos.VerifyAsync(demo.Token, true, key.Register(await demos.OptionsAsync(demo.Token, true, default)), default);
            await demos.VerifyAsync(demo.Token, false, key.Authenticate(await demos.OptionsAsync(demo.Token, false, default)), default);
            var round=(await rounds.JoinAuthenticatedAsync(demo.Token))!;
            await store.ExecuteAsync("INSERT INTO VisitorExercises(DemoId,StartedAt,CompletedAt) VALUES($id,100,200)",default,("$id",demo.Account.Id));
            return (demo,round);
        }
        Task<PrizeManagedEvent> Event(string name) => events.CreateAsync(actor, Guid.NewGuid().ToString("D"), name, "builtin-cybertruck",
            new(GroupSize: 2, WinnersPerGroup: 1, PrizeEnabled: true, Stock: 1, PrizeName: "Synthetic retained prize"), true);
        var openEvent = await Event("Synthetic open round end");
        using var oneKey = new SoftwareAuthenticator(); using var twoKey = new SoftwareAuthenticator();
        var one = await Visitor(openEvent.Id, oneKey); var two = await Visitor(openEvent.Id, twoKey);
        var prizeFingerprint = await store.ScalarAsync("SELECT Fingerprint FROM PrizeSchemaContract WHERE Version=6", default);
        var roundFingerprint = await store.ScalarAsync("SELECT Fingerprint FROM PrizeRoundsContract WHERE Version=1", default);
        var beforeMigration = JsonSerializer.Serialize(await rounds.BoardAsync(actor, openEvent.Id));
        // Recreate an actual pre-upgrade shape, only in this new synthetic fixture.
        await store.ExecuteAsync("DROP TABLE PrizeRoundHistory; DROP TABLE PrizeRoundHistoryContract", default);
        var migrationFailed = false;
        try { await PrizeRoundHistoryMigration.InitializeAsync(store, default, () => throw new InvalidOperationException("Synthetic migration interruption")); }
        catch (InvalidOperationException) { migrationFailed = true; }
        TestProgram.Check(migrationFailed && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM sqlite_schema WHERE name LIKE 'PrizeRoundHistory%'", default)) == 0,
            "Interrupted additive history migration rolls back all new objects");
        await PrizeRoundHistoryMigration.InitializeAsync(store, default); await store.InitializeAsync();
        TestProgram.Check(beforeMigration == JsonSerializer.Serialize(await rounds.BoardAsync(actor, openEvent.Id))
            && Equals(prizeFingerprint, await store.ScalarAsync("SELECT Fingerprint FROM PrizeSchemaContract WHERE Version=6", default))
            && Equals(roundFingerprint, await store.ScalarAsync("SELECT Fingerprint FROM PrizeRoundsContract WHERE Version=1", default)),
            "Round history upgrade preserves existing aliases, results and both historical schema contracts on reopen");
        var connection = await sessions.BeginAsync(default); await sessions.ApproveAsync(actor, connection.RequestId, true, default);
        async Task<(SessionDemoOpenLink Link, string Cookie)> Grant(string eventId, string alias, string token, long version)
        {
            await sessions.SelectAsync(connection.RequestId, connection.Key, eventId, version, default);
            var participant = (await sessions.RosterAsync(connection.RequestId, connection.Key, eventId, default)).Participants.Single(p => p.Alias == alias);
            var consent = await sessions.RequestConsentAsync(connection.RequestId, connection.Key, eventId, participant.Id, Guid.NewGuid().ToString("D"), default);
            await sessions.DecideAsync(token, consent.RequestId!, true, true, default);
            var link = await sessions.OpenAsync(connection.RequestId, connection.Key, eventId, participant.Id, default);
            return (link, (await sessions.RedeemAsync(link.ViewId, link.Token, default)).Cookie);
        }
        async Task Denied(Func<Task> action, string message)
        { var denied = false; try { await action(); } catch (DemoRequestException) { denied = true; } TestProgram.Check(denied, message); }
        async Task Deleted(NewDemo demo, string message)
        {
            var credentials = Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Credentials WHERE DemoId=$id", default, ("$id", demo.Account.Id)));
            var challenges = Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$id", default, ("$id", demo.Account.Id)));
            var accounts = Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Demos WHERE Id=$id", default, ("$id", demo.Account.Id)));
            var liveSessions = Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM IdentitySessions WHERE UserId=$id AND RevokedAt IS NULL", default, ("$id", demo.Account.Id)));
            TestProgram.Check(accounts == 0 && credentials == 0 && challenges == 0 && liveSessions == 0
                && state.Consents.Values.Where(c => c.AccountId == demo.Account.Id).All(c => c.Status == "Revoked")
                && await demos.GetAsync(demo.Token, false, default) is null && await rounds.ForDemoAsync(demo.Token) is null, message);
        }
        var openView = await Grant(openEvent.Id, one.Round.Alias!, one.Demo.Token, 1);
        var pendingAssertion = oneKey.Authenticate(await demos.OptionsAsync(one.Demo.Token, false, default));
        // A second registry session proves that account-wide revocation covers every session.
        await store.ExecuteAsync("""
            INSERT INTO IdentitySessions(Id,UserId,DemoRunId,BindingHash,CreatedAt,LastActivity,ExpiresAt,Method,SessionType)
            VALUES($id,$user,$run,$hash,$now,$now,$expires,'WebAuthn','Normal')
            """, default, ("$id", Guid.NewGuid().ToString("D")), ("$user", one.Demo.Account.Id), ("$run", one.Demo.Account.DemoRunId),
            ("$hash", DemoIdentity.Hash(DemoIdentity.RandomToken())), ("$now", clock.GetUtcNow().ToUnixTimeMilliseconds()), ("$expires", clock.GetUtcNow().AddMinutes(20).ToUnixTimeMilliseconds()));
        await store.ExecuteAsync("CREATE TRIGGER SyntheticEndFailure BEFORE DELETE ON Demos BEGIN SELECT RAISE(ABORT,'Synthetic end interruption'); END", default);
        var interrupted = false; try { await demos.EndOwnAsync(one.Demo.Token, default); } catch (SqliteException) { interrupted = true; }
        TestProgram.Check(interrupted && (await demos.GetAsync(one.Demo.Token, false, default))?.CredentialCount == 1
            && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$id", default, ("$id", one.Demo.Account.Id))) == 1
            && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundHistory", default)) == 0
            && (await sessions.ViewAsync(openView.Link.ViewId, openView.Cookie, default)).Alias == one.Round.Alias,
            "Interrupted demo end rolls back history transfer, session revocation and account deletion together");
        await store.ExecuteAsync("DROP TRIGGER SyntheticEndFailure", default);
        TestProgram.Check(await demos.EndOwnAsync(one.Demo.Token, default), "Explicit end in an open round succeeds without a foreign-key error");
        await Deleted(one.Demo, "Open-round end deletes the temporary account, server passkeys and challenges and revokes all sessions/consents");
        await Denied(() => sessions.ViewAsync(openView.Link.ViewId, openView.Cookie, default), "Open-round end immediately denies an already opened participant view");
        await Denied(() => demos.VerifyAsync(one.Demo.Token, false, pendingAssertion, default), "Ended account cannot finish an outstanding passkey assertion");
        TestProgram.Check(beforeMigration == JsonSerializer.Serialize(await rounds.BoardAsync(actor, openEvent.Id))
            && (await rounds.ForDemoAsync(two.Demo.Token))?.Alias == two.Round.Alias && (await demos.GetAsync(two.Demo.Token, false, default))?.CredentialCount == 1,
            "Open-round end preserves its history/capacity and leaves the other participant untouched");
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundHistory WHERE EventId=$event AND Alias=$alias AND IsWinner=0", default, ("$event", openEvent.Id), ("$alias", one.Round.Alias))) == 1,
            "Only anonymous round and alias history remains for the ended open-round participant");
        var openClosed = await rounds.CloseAsync(actor, openEvent.Id, one.Round.GroupId);
        TestProgram.Check(openClosed.Winners.Single().Alias is {} winningAlias && new[]{one.Round.Alias,two.Round.Alias}.Contains(winningAlias)
            &&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND Action='RoundEnteredDraw'",default,("$event",openEvent.Id)))==2,"Completed normal-ended and active participants both remain eligible for a later draw");

        var closedEvent = await Event("Synthetic closed round end");
        using var threeKey = new SoftwareAuthenticator(); using var fourKey = new SoftwareAuthenticator();
        var three = await Visitor(closedEvent.Id, threeKey); var four = await Visitor(closedEvent.Id, fourKey);
        var closedView = await Grant(closedEvent.Id,three.Round.Alias!,three.Demo.Token,2);
        var result = await rounds.CloseAsync(actor, closedEvent.Id, three.Round.GroupId);
        await Denied(()=>sessions.ViewAsync(closedView.Link.ViewId,closedView.Cookie,default),"Closing the group immediately revokes an existing fictional participant view");
        var winner = result.Winners.Single().Alias == three.Round.Alias ? three : four;
        var survivor = result.Winners.Single().Alias == three.Round.Alias ? four : three;
        TestProgram.Check((await rounds.ForDemoAsync(winner.Demo.Token))?.PrizeMessageKey=="VisitorPrizeWon"
            &&(await rounds.ForDemoAsync(survivor.Demo.Token))?.PrizeMessageKey=="VisitorPrizeLost","Active winner and loser receive distinct messages from the stored draw");
        var winnerKey = result.Winners.Single().Alias == three.Round.Alias ? threeKey : fourKey;
        await demos.VerifyAsync(winner.Demo.Token, false, winnerKey.Authenticate(await demos.OptionsAsync(winner.Demo.Token, false, default)), default);
        var reauthenticated = await rounds.JoinAuthenticatedAsync(winner.Demo.Token);
        TestProgram.Check(reauthenticated is { IsWinner: true, Status: "Closed" } && reauthenticated.Alias == winner.Round.Alias
            && JsonSerializer.Serialize(reauthenticated.Winners) == JsonSerializer.Serialize(result.Winners), "Closing a round still allows an active participant to sign in again and see the original result");
        await Denied(()=>Grant(closedEvent.Id,winner.Round.Alias!,winner.Demo.Token,3),"A closed group refuses fresh participant-view consent");
        var winningParticipant=(await rounds.DeliveriesAsync(actor,closedEvent.Id,default)).Single();
        await Task.WhenAll(rounds.RegisterDeliveryAsync(actor,closedEvent.Id,winningParticipant.ParticipantId,default),rounds.RegisterDeliveryAsync(actor,closedEvent.Id,winningParticipant.ParticipantId,default));
        TestProgram.Check((await rounds.DeliveriesAsync(actor,closedEvent.Id,default)).Single().Delivered && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$id AND Action='RoundPrizeDelivered'",default,("$id",closedEvent.Id)))==1,"Concurrent repeated handover confirmations record exactly one audited delivery");
        await demos.OptionsAsync(winner.Demo.Token, false, default);
        var beforeEnd = JsonSerializer.Serialize(await rounds.BoardAsync(actor, closedEvent.Id));
        var retention = Convert.ToString(await store.ScalarAsync("SELECT RetentionDays||':'||coalesce(PurgeAt,'') FROM PrizeEvents WHERE Id=$id", default, ("$id", closedEvent.Id)));
        TestProgram.Check(await demos.EndOwnAsync(winner.Demo.Token, default), "Explicit end of a closed-round winner succeeds without a foreign-key error");
        await Deleted(winner.Demo, "Closed-round end deletes authentication data and revokes every session and participant-demo consent");
        await Denied(() => sessions.ViewAsync(closedView.Link.ViewId, closedView.Cookie, default), "Closed-round end also immediately closes Open as participant");
        TestProgram.Check(beforeEnd == JsonSerializer.Serialize(await rounds.BoardAsync(actor, closedEvent.Id))
            && JsonSerializer.Serialize((await rounds.ForDemoAsync(survivor.Demo.Token))!.Winners) == JsonSerializer.Serialize(result.Winners)
            && retention == Convert.ToString(await store.ScalarAsync("SELECT RetentionDays||':'||coalesce(PurgeAt,'') FROM PrizeEvents WHERE Id=$id", default, ("$id", closedEvent.Id))),
            "Deleting a closed-round winner preserves the exact published result and existing retention boundary for the remaining participant");
        TestProgram.Check(!await demos.EndOwnAsync(winner.Demo.Token, default), "Repeating explicit end is harmless and cannot recreate access or duplicate history");
        using var fiveKey = new SoftwareAuthenticator(); var five = await Visitor(closedEvent.Id, fiveKey);
        var nextResult = await rounds.CloseAsync(actor, closedEvent.Id, five.Round.GroupId);
        TestProgram.Check(nextResult.WinnerCount == 0, "Deleting a winner never makes the already won finite prize available again");
        var cleanupEvent=await Event("Synthetic expiry cleanup");
        using var expiredKey=new SoftwareAuthenticator();var expired=await Visitor(cleanupEvent.Id,expiredKey);
        await store.ExecuteAsync("UPDATE Demos SET LastSeen=$at WHERE Id=$id",default,("$at",clock.GetUtcNow().AddMinutes(-30).ToUnixTimeMilliseconds()),("$id",expired.Demo.Account.Id));
        TestProgram.Check(await demos.GetAsync(expired.Demo.Token,false,default) is null
            &&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundHistory WHERE EventId=$event",default,("$event",cleanupEvent.Id)))==1,
            "Existing idle-owner cleanup uses the same account removal without a round FK error");
        using var metadataKey=new SoftwareAuthenticator();var metadataExpired=await Visitor(cleanupEvent.Id,metadataKey);
        await store.ExecuteAsync("UPDATE Demos SET LastSeen=$at WHERE Id=$id",default,("$at",clock.GetUtcNow().AddMinutes(-30).ToUnixTimeMilliseconds()),("$id",metadataExpired.Demo.Account.Id));
        var metadata=new SessionStatusService(store,clock,new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]},new ConfigurationBuilder().Build(),new DemoMetrics(),sessionDemos:state);
        TestProgram.Check(await metadata.GetAsync(metadataExpired.Demo.Account.DemoRunId,default) is null
            &&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Demos WHERE Id=$id",default,("$id",metadataExpired.Demo.Account.Id)))==0,
            "Existing metadata expiry cleanup cannot bypass anonymous round-history retention");
        var bulkEvent=await Event("Synthetic scheduled cleanup");
        using var bulkKey=new SoftwareAuthenticator();var bulkExpired=await Visitor(bulkEvent.Id,bulkKey);
        await store.ExecuteAsync("UPDATE Demos SET LastSeen=$at WHERE Id=$id",default,("$at",clock.GetUtcNow().AddMinutes(-30).ToUnixTimeMilliseconds()),("$id",bulkExpired.Demo.Account.Id));
        await demos.CleanupAsync(default);
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM Demos WHERE Id=$id",default,("$id",bulkExpired.Demo.Account.Id)))==0
            &&(await demos.GetAsync(survivor.Demo.Token,false,default))?.CredentialCount==1,
            "Scheduled/startup cleanup removes expired round accounts and leaves active visitors alone");
        var trio=await events.CreateAsync(actor,Guid.NewGuid().ToString("D"),"Synthetic three aliases","builtin-cybertruck",
            new(GroupSize:3,WinnersPerGroup:1,PrizeEnabled:true,Stock:1,PrizeName:"YubiKey synthetic"),true);
        using var cleanedKey=new SoftwareAuthenticator();using var activeKey=new SoftwareAuthenticator();using var incompleteKey=new SoftwareAuthenticator();
        var cleaned=await Visitor(trio.Id,cleanedKey);var active=await Visitor(trio.Id,activeKey);var incomplete=await Visitor(trio.Id,incompleteKey);
        await store.ExecuteAsync("DELETE FROM VisitorExercises WHERE DemoId=$id",default,("$id",incomplete.Demo.Account.Id));
        await store.ExecuteAsync("UPDATE Demos SET LastSeen=$at WHERE Id=$id",default,("$at",clock.GetUtcNow().AddMinutes(-30).ToUnixTimeMilliseconds()),("$id",cleaned.Demo.Account.Id));
        TestProgram.Check(await demos.GetAsync(cleaned.Demo.Token,false,default) is null,"Three-alias fixture performs real normal expiry cleanup");
        var beforeDraw=await rounds.ProgressAsync(actor,trio.Id,cleaned.Round.GroupId,default);
        TestProgram.Check(beforeDraw is {Eligible:2,Completed:2}&&beforeDraw.Participants.Count==3,"Cleaned and active completed aliases count, incomplete alias does not");
        var trioResult=await rounds.CloseAsync(actor,trio.Id,cleaned.Round.GroupId);
        var afterDraw=await rounds.ProgressAsync(actor,trio.Id,cleaned.Round.GroupId,default);
        TestProgram.Check(afterDraw.Participants.Count(p=>p.Status=="RoundRowWon")==1
            &&afterDraw.Participants.Count(p=>p.Status=="RoundRowNoPrize")==1
            &&afterDraw.Participants.Single(p=>p.Alias==incomplete.Round.Alias) is {Status:"RoundRowNotDrawn",Reason:"RoundIncompleteAtDraw"},
            "Three aliases show winner, entered without a prize, and not entered with known completion reason");
        TestProgram.Check((await rounds.ForDemoAsync(incomplete.Demo.Token))?.DrawResultKey=="RoundRowNotDrawn"
            &&(await rounds.ForDemoAsync(active.Demo.Token))?.DrawResultKey==afterDraw.Participants.Single(p=>p.Alias==active.Round.Alias).Status,
            "Visitor result wording uses the same stored draw facts as the participant list");
        var activeStatus=await rounds.ForDemoAsync(active.Demo.Token);
        TestProgram.Check(activeStatus is {DrawPerformed:true}&&activeStatus.PrizeMessageKey==(activeStatus.IsWinner?"VisitorPrizeWon":"VisitorPrizeLost")
            &&(await rounds.ForDemoAsync(incomplete.Demo.Token)) is {PrizeMessageKey:"VisitorPrizeNotEntered",DrawPerformed:true},
            "Visitor orange result cards use saved winner, loser and excluded states");
        var trioReplay=await rounds.CloseAsync(actor,trio.Id,cleaned.Round.GroupId);
        TestProgram.Check(JsonSerializer.Serialize(trioResult)==JsonSerializer.Serialize(trioReplay)
            &&JsonSerializer.Serialize(afterDraw)==JsonSerializer.Serialize(await rounds.ProgressAsync(actor,trio.Id,cleaned.Round.GroupId,default)),
            "Reload and repeated close leave the three-alias result unchanged");
        var afterEndSnapshot=await PrizeMigrationChecks.Snapshot(store);await store.InitializeAsync();
        TestProgram.Check(afterEndSnapshot==await PrizeMigrationChecks.Snapshot(store),"Reopening after account deletion preserves detached aliases and published results exactly");
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM pragma_foreign_key_check", default)) == 0,
            "Open and closed round endings leave all foreign keys valid without disabling enforcement");
    }
}
