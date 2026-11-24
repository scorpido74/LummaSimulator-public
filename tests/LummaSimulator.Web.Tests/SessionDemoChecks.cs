using System.Security.Claims;
using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;

namespace LummaSimulator.Web.Tests;

internal static class SessionDemoChecks
{
    // ORDER BY on the outer aggregate does not order group_concat's input rows.
    private const string SchemaSql = "SELECT group_concat(name||':'||sql,';') FROM (SELECT name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY name)";
    private const string OriginalSchemaSql = "SELECT group_concat(name||':'||sql,';') FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY name";
    private sealed record SchemaSnapshot(string Schema, string MigrationData, long UserVersion);
    private static async Task<SchemaSnapshot> SnapshotAsync(DemoStore store)
    {
        var schema = Convert.ToString(await store.ScalarAsync(SchemaSql, default))!;
        var migrations = new SortedDictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await store.OpenAsync(default);
        foreach (var table in new[] { "ExperienceMigrations", "PrizeSchemaContract", "IdentitySchemaVersion", "PresenterEventMigrations", "PrizeRoundsContract", "PrizeRoundHistoryContract" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY Version";
            using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object?[]>();
            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            migrations.Add(table, JsonSerializer.Serialize(rows));
        }
        return new(schema, JsonSerializer.Serialize(migrations), Convert.ToInt64(await store.ScalarAsync("PRAGMA user_version", default)));
    }
    private static async Task Denied(Func<Task> action,string message)
    {
        var rejected=false;try { await action(); } catch(DemoRequestException e) when(e.Message=="SessionDemoUnavailable") { rejected=true; }
        TestProgram.Check(rejected,message);
    }
    private static async Task CheckHelperPresence(DemoStore store,PresenterService presenters,ClaimsPrincipal owner)
    {
        var clock=new TestClock();
        var service=new SessionDemoService(new SessionDemoState(clock),store,presenters,clock,new ExperienceSettings());
        var grant=await service.BeginAsync(default,remoteHelper:true);
        await service.ApproveAsync(owner,grant.RequestId,true,default);
        var first=new HelperHeartbeat(Guid.NewGuid().ToString("D"),DemoIdentity.RandomToken(),"Same device", "0.1.1","Confirmed","Healthy");
        var second=first with{Id=Guid.NewGuid().ToString("D"),Identity=DemoIdentity.RandomToken()};
        await service.HelperHeartbeatAsync(grant.RequestId,grant.Key,first,default);
        await service.HelperHeartbeatAsync(grant.RequestId,grant.Key,second,default);
        var rows=await service.HelperInstallationsAsync(default);
        TestProgram.Check(rows.Count==2&&rows.All(r=>r.Device=="Same device"&&r.Presence=="Online"&&r.Approved),"Duplicate helper names retain distinct online registrations");
        clock.Advance(TimeSpan.FromSeconds(46));
        TestProgram.Check((await service.HelperInstallationsAsync(default)).All(r=>r.Presence=="Verouderd"),"Missing heartbeat becomes stale after 45 seconds");
        clock.Advance(TimeSpan.FromSeconds(75));
        TestProgram.Check((await service.HelperInstallationsAsync(default)).All(r=>r.Presence=="Offline"),"Missing heartbeat becomes offline after 120 seconds");
        await service.HelperHeartbeatAsync(grant.RequestId,grant.Key,first with{Pairing="Rejected",Health="Rejected"},default);
        var restarted=new SessionDemoService(new SessionDemoState(clock),store,presenters,clock,new ExperienceSettings());
        rows=await restarted.HelperInstallationsAsync(default);
        TestProgram.Check(rows.Single(r=>r.Id==first.Id) is {Presence:"Online",Pairing:"Rejected",Health:"Rejected",Approved:false}
            &&rows.Single(r=>r.Id==second.Id).Presence=="Offline","Reconnect updates the same installation; restart retains observations and clears approval");
    }
    internal static async Task RunAsync(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"session-demo.db"));await store.InitializeAsync();
        var initializedSchema = await SnapshotAsync(store);
        var originalExpected = await store.ScalarAsync(OriginalSchemaSql, default);
        var clock=new TestClock();var presenters=PresenterChecks.Service(store,clock);
        using var adminKey=new SoftwareAuthenticator();using var ownerKey=new SoftwareAuthenticator();using var otherKey=new SoftwareAuthenticator();
        var bootstrap=await presenters.IssueBootstrapAsync("session-admin","Synthetic Admin",default);await PresenterChecks.Enroll(presenters,bootstrap.Code,adminKey);
        var admin=await PresenterChecks.Login(presenters,"session-admin",adminKey);
        async Task<ClaimsPrincipal> Presenter(string login,SoftwareAuthenticator key)
        {
            await PresenterChecks.Reauth(presenters,admin,adminKey);
            var invitation=await presenters.CreateAccountAsync(admin,login,"Synthetic presenter","Presenter",default);
            await PresenterChecks.Enroll(presenters,invitation.Code,key);return await PresenterChecks.Login(presenters,login,key);
        }
        var owner=await Presenter("session-owner",ownerKey);var other=await Presenter("session-other",otherKey);
        await CheckHelperPresence(store,presenters,owner);
        var access=new EventAccess(store,presenters,clock);var events=new PrizeEventService(store,presenters,clock);var rounds=new PrizeRoundsService(store,presenters,clock);
        async Task<PrizeManagedEvent> Event(ClaimsPrincipal actor,string name)=>await events.CreateAsync(actor,Guid.NewGuid().ToString("D"),name,"builtin-cybertruck",new(GroupSize:2),true);
        var a=await Event(owner,"Synthetic event A");var b=await Event(owner,"Synthetic event B");var c=await Event(other,"Synthetic unrelated event");
        var demos=new DemoService(store,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DemoService>.Instance);
        async Task<(NewDemo Demo,PrizeRoundSnapshot Round)> Visitor(string eventId,SoftwareAuthenticator key)
        {
            var demo=await demos.CreateAsync("laptop",default);await access.BindVisitorAsync(demo.Token,"prize",eventId,default);
            await demos.VerifyAsync(demo.Token,true,key.Register(await demos.OptionsAsync(demo.Token,true,default)),default);
            await demos.VerifyAsync(demo.Token,false,key.Authenticate(await demos.OptionsAsync(demo.Token,false,default)),default);
            return(demo,(await rounds.JoinAuthenticatedAsync(demo.Token,default))!);
        }
        using var firstKey=new SoftwareAuthenticator();using var secondKey=new SoftwareAuthenticator();using var foreignKey=new SoftwareAuthenticator();using var sharedKey=new SoftwareAuthenticator();
        var first=await Visitor(a.Id,firstKey);var second=await Visitor(a.Id,secondKey);var foreign=await Visitor(b.Id,foreignKey);var shared=await Visitor(c.Id,sharedKey);
        // Synthetic pre-step-5 schema: preserve the existing event, round and visitors across the additive upgrade.
        await store.ExecuteAsync("DROP TABLE PrizeRoundHistory; DROP TABLE PrizeRoundHistoryContract",default);
        await store.InitializeAsync();
        var beforeConsent = await SnapshotAsync(store);
        var originalBeforeConsent = await store.ScalarAsync(OriginalSchemaSql, default);
        var evidencePath = Path.Combine(root, "session-demo-schema-comparison.json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new { initializedSchema, beforeConsent, originalExpected, originalBeforeConsent }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Session-demo schema comparison: " + evidencePath);
        TestProgram.Check(initializedSchema == beforeConsent, "Synthetic history reinitialization preserves schema definitions and migration markers before consent");
        // Prove that sorting does not hide an unexpected schema change.
        await using (var probeConnection = await store.OpenAsync(default))
        {
            using var transaction = probeConnection.BeginTransaction();
            using var command = probeConnection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE SessionDemoSchemaProbe(Id INTEGER)";
            await command.ExecuteNonQueryAsync();
            command.CommandText = SchemaSql;
            TestProgram.Check(!Equals(beforeConsent.Schema, await command.ExecuteScalarAsync()), "Consent schema baseline detects an unexpected table");
            transaction.Rollback();
        }
        TestProgram.Check(await SnapshotAsync(store) == beforeConsent, "Schema detection probe rolls back without changing the consent baseline");
        var sessions=new SessionDemoService(new SessionDemoState(clock),store,presenters,clock,new ExperienceSettings());
        var connection=await sessions.BeginAsync(default);
        TestProgram.Check((await sessions.ConnectionAsync(connection.RequestId,connection.Key,default)).Status=="Pending","App connection initially confers no presenter authority");
        await Denied(()=>sessions.EventsAsync(connection.RequestId,connection.Key,default),"Unapproved app cannot list events");
        await sessions.ApproveAsync(owner,connection.RequestId,true,default);
        TestProgram.Check((await sessions.ConnectionAsync(connection.RequestId,connection.Key,default)).Status=="Ready","Existing presenter WebAuthn session authorizes the temporary app connection");
        var ownerId=owner.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await access.InviteAsync(other,"prize",c.Id,ownerId,default);
        await access.AcceptAsync(owner,(await access.InvitationsAsync(owner,default)).Single(i=>i.EventId==c.Id).Id,true,default);
        TestProgram.Check((await sessions.EventsAsync(connection.RequestId,connection.Key,default)).Any(e=>e.Id==c.Id),"Existing helper/app connection sees newly accepted event membership without a local event copy");
        var currentC=(await events.GetAsync(other,c.Id))!;
        await events.RenameAsync(other,c.Id,c.Id,currentC.Version,Guid.NewGuid().ToString("D"),"Synthetic shared event renamed");
        TestProgram.Check((await sessions.EventsAsync(connection.RequestId,connection.Key,default)).Single(e=>e.Id==c.Id).Name=="Synthetic shared event renamed","Event rename synchronizes while retaining its internal ID");
        TestProgram.Check(await access.CanAsync(owner,"prize",c.Id,false,default)
            && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterEventMembers WHERE Kind='prize' AND EventId=$event AND AccountId=$actor",default,("$event",c.Id),("$actor",ownerId)))==1,
            "Accepted fixture invitation creates the intended persisted membership before revocation");
        await sessions.SelectAsync(connection.RequestId,connection.Key,c.Id,1,default);
        var sharedParticipant=(await sessions.RosterAsync(connection.RequestId,connection.Key,c.Id,default)).Participants.Single();
        var sharedConsent=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,c.Id,sharedParticipant.Id,Guid.NewGuid().ToString("D"),default);
        await sessions.DecideAsync(shared.Demo.Token,sharedConsent.RequestId!,true,true,default);
        var sharedLink=await sessions.OpenAsync(connection.RequestId,connection.Key,c.Id,sharedParticipant.Id,default);
        var sharedView=await sessions.RedeemAsync(sharedLink.ViewId,sharedLink.Token,default);
        var unredeemed=await sessions.OpenAsync(connection.RequestId,connection.Key,c.Id,sharedParticipant.Id,default);
        // invite:true cancels a pending invitation; an accepted membership needs invite:false.
        await access.RemoveAsync(other,"prize",c.Id,ownerId,invite:false,ct:default);
        TestProgram.Check(!await access.CanAsync(owner,"prize",c.Id,false,default)
            && Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PresenterEventMembers WHERE Kind='prize' AND EventId=$event AND AccountId=$actor",default,("$event",c.Id),("$actor",ownerId)))==0,
            "Fixture actually revokes the accepted membership rather than an already consumed invitation");
        var afterRevocation=await sessions.EventsAsync(connection.RequestId,connection.Key,default);
        TestProgram.Check(afterRevocation.Select(e=>e.Id).ToHashSet().SetEquals([a.Id,b.Id]),"First refresh on the existing connection removes the revoked event and retains other authorized events");
        TestProgram.Check((await sessions.ConnectionAsync(connection.RequestId,connection.Key,default)).Status=="Ready","Event membership revocation does not unnecessarily replace or revoke the presenter's existing connection");
        await Denied(()=>sessions.RosterAsync(connection.RequestId,connection.Key,c.Id,default),"Revoked event immediately refuses roster reads on the existing selection");
        await Denied(()=>sessions.SelectAsync(connection.RequestId,connection.Key,c.Id,2,default),"Revoked event immediately refuses a new selection");
        await Denied(()=>sessions.RequestConsentAsync(connection.RequestId,connection.Key,c.Id,sharedParticipant.Id,Guid.NewGuid().ToString("D"),default),"Revoked event immediately refuses new consent requests");
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,c.Id,sharedParticipant.Id,default),"Revoked event refuses new viewer links despite prior visitor consent");
        await Denied(()=>sessions.ViewAsync(sharedLink.ViewId,sharedView.Cookie,default),"Membership revocation immediately blocks an already opened participant view");
        await Denied(()=>sessions.RedeemAsync(unredeemed.ViewId,unredeemed.Token,default),"Membership revocation blocks previously issued unredeemed viewer links");
        await Denied(()=>sessions.DecideAsync(shared.Demo.Token,sharedConsent.RequestId!,true,true,default),"A visitor cannot reactivate consent for a presenter whose event access was revoked");
        var choices=await sessions.EventsAsync(connection.RequestId,connection.Key,default);
        TestProgram.Check(choices.Select(e=>e.Id).ToHashSet().SetEquals([a.Id,b.Id])&&!choices.Any(e=>e.Id==c.Id),"App lists only accessible round events");
        await sessions.SelectAsync(connection.RequestId,connection.Key,a.Id,2,default);
        var roster=await sessions.RosterAsync(connection.RequestId,connection.Key,a.Id,default);
        TestProgram.Check(roster.Participants.Count==2&&roster.Participants.Select(p=>p.Alias).ToHashSet().SetEquals([first.Round.Alias!,second.Round.Alias!])
            &&roster.Participants.All(p=>p.Round==1&&p.ConsentStatus=="None"&&!p.CanOpen),"App roster shows two distinct aliases and rounds with no implicit consent");
        var p1=roster.Participants.Single(p=>p.Alias==first.Round.Alias);var p2=roster.Participants.Single(p=>p.Alias==second.Round.Alias);
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default),"Even an event owner cannot open a participant without visitor consent");
        var request1=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p1.Id,Guid.NewGuid().ToString("D"),default);
        var request2=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p2.Id,Guid.NewGuid().ToString("D"),default);
        await Denied(()=>sessions.DecideAsync(second.Demo.Token,request1.RequestId!,true,true,default),"Visitor cannot approve another alias's request");
        await Denied(()=>sessions.DecideAsync(first.Demo.Token,request1.RequestId!,true,false,default),"Grant requires an explicit checked confirmation");
        await sessions.DecideAsync(first.Demo.Token,request1.RequestId!,true,true,default);
        await sessions.DecideAsync(second.Demo.Token,request2.RequestId!,false,true,default);
        roster=await sessions.RosterAsync(connection.RequestId,connection.Key,a.Id,default);
        TestProgram.Check(roster.Participants.Single(p=>p.Id==p1.Id) is {ConsentStatus:"Granted",CanOpen:true}
            &&roster.Participants.Single(p=>p.Id==p2.Id) is {ConsentStatus:"Declined",CanOpen:false},"Only the consenting alias becomes available to open");
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p2.Id,default),"Declining alias stays closed server-side");
        var foreignId=Convert.ToString(await store.ScalarAsync("SELECT Id FROM PrizeParticipants WHERE EventId=$event",default,("$event",b.Id)))!;
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,foreignId,default),"Participant from another event cannot be opened through the selected event");
        await Denied(()=>sessions.RosterAsync(connection.RequestId,connection.Key,b.Id,default),"Even an accessible second event requires explicit selection");
        var link=await sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default);
        var opened=await sessions.RedeemAsync(link.ViewId,link.Token,default);
        TestProgram.Check(opened.View.EventId==a.Id&&opened.View.Alias==first.Round.Alias&&opened.View.Round==1,"Separate synthetic view retains the selected event, round and alias");
        TestProgram.Check(await demos.GetAsync(opened.Cookie,false,default) is null,"Synthetic-view cookie never authenticates as the original demo visitor");
        await Denied(()=>sessions.RedeemAsync(link.ViewId,link.Token,default),"Open link can be redeemed only once");
        await sessions.DecideAsync(first.Demo.Token,request1.RequestId!,false,true,default);
        await Denied(()=>sessions.ViewAsync(link.ViewId,opened.Cookie,default),"Visitor withdrawal immediately revokes further view access");
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default),"Withdrawal also prevents new open links");

        var renewed=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p1.Id,Guid.NewGuid().ToString("D"),default);
        await sessions.DecideAsync(first.Demo.Token,renewed.RequestId!,true,true,default);
        var renewedLink=await sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default);
        var renewedView=await sessions.RedeemAsync(renewedLink.ViewId,renewedLink.Token,default);
        await sessions.SelectAsync(connection.RequestId,connection.Key,b.Id,3,default);
        await Denied(()=>sessions.ViewAsync(renewedLink.ViewId,renewedView.Cookie,default),"Changing the selected event invalidates its previous viewer grant");
        await Denied(()=>sessions.SelectAsync(connection.RequestId,connection.Key,a.Id,2,default),"Late event-selection responses cannot restore an older scope");
        await Denied(()=>sessions.SelectAsync(connection.RequestId,connection.Key,c.Id,4,default),"App cannot select an unrelated presenter's event");
        await sessions.SelectAsync(connection.RequestId,connection.Key,a.Id,4,default);
        TestProgram.Check((await sessions.RosterAsync(connection.RequestId,connection.Key,a.Id,default)).Participants.All(p=>!p.CanOpen),"Returning to an event does not resurrect previous consent");
        var expiring=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p1.Id,Guid.NewGuid().ToString("D"),default);
        await sessions.DecideAsync(first.Demo.Token,expiring.RequestId!,true,true,default);
        var expiringLink=await sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default);
        clock.Advance(TimeSpan.FromSeconds(31));
        await Denied(()=>sessions.RedeemAsync(expiringLink.ViewId,expiringLink.Token,default),"Unredeemed open link expires after thirty seconds");
        clock.Advance(TimeSpan.FromMinutes(5));
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default),"Consent cannot outlive its five-minute limit");
        await sessions.SelectAsync(connection.RequestId,connection.Key,b.Id,5,default);
        var sourceRequest=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,b.Id,foreignId,Guid.NewGuid().ToString("D"),default);
        await sessions.DecideAsync(foreign.Demo.Token,sourceRequest.RequestId!,true,true,default);
        var sourceLink=await sessions.OpenAsync(connection.RequestId,connection.Key,b.Id,foreignId,default);
        var sourceView=await sessions.RedeemAsync(sourceLink.ViewId,sourceLink.Token,default);
        // Same revocation service as /Sessions?handler=Revoke; this preserves round history.
        await new SecurityAuditService(store,clock).RevokeAsync(foreign.Demo.Account,null,true,default);
        await Denied(()=>sessions.ViewAsync(sourceLink.ViewId,sourceView.Cookie,default),"Revoking the original visitor session immediately closes its synthetic viewer");
        TestProgram.Check((await sessions.RosterAsync(connection.RequestId,connection.Key,b.Id,default)).Participants.Count==0,"Revoked original visitor is no longer an active app participant");
        await sessions.SelectAsync(connection.RequestId,connection.Key,a.Id,6,default);
        var cancelledRun=Guid.NewGuid().ToString("D");
        var cancelledRequest=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p1.Id,cancelledRun,default);
        await sessions.DecideAsync(first.Demo.Token,cancelledRequest.RequestId!,true,true,default);
        var cancelledLink=await sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default);
        var cancelledView=await sessions.RedeemAsync(cancelledLink.ViewId,cancelledLink.Token,default);
        await sessions.CancelAsync(connection.RequestId,connection.Key,Guid.NewGuid().ToString("D"),default);
        TestProgram.Check((await sessions.ViewAsync(cancelledLink.ViewId,cancelledView.Cookie,default)).Alias==p1.Alias,"Cancelling another run cannot revoke the approved participant run");
        await sessions.CancelAsync(connection.RequestId,connection.Key,cancelledRun,default);
        await Denied(()=>sessions.ViewAsync(cancelledLink.ViewId,cancelledView.Cookie,default),"Stopping or resetting the matching app run invalidates an already opened view");
        await Denied(()=>sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default),"Cancelled app run cannot issue another open link");
        var signoutRequest=await sessions.RequestConsentAsync(connection.RequestId,connection.Key,a.Id,p1.Id,Guid.NewGuid().ToString("D"),default);
        await sessions.DecideAsync(first.Demo.Token,signoutRequest.RequestId!,true,true,default);
        var signoutLink=await sessions.OpenAsync(connection.RequestId,connection.Key,a.Id,p1.Id,default);
        var signoutView=await sessions.RedeemAsync(signoutLink.ViewId,signoutLink.Token,default);
        await presenters.SignOutAsync(owner,default);
        await Denied(()=>sessions.RosterAsync(connection.RequestId,connection.Key,a.Id,default),"Presenter sign-out invalidates app authority immediately");
        await Denied(()=>sessions.ViewAsync(signoutLink.ViewId,signoutView.Cookie,default),"Presenter sign-out also invalidates an existing synthetic view");
        var audit=Convert.ToString(await store.ScalarAsync("SELECT group_concat(coalesce(Actor,'')||Action||coalesce(Target,'')) FROM PresenterAudit",default))!;
        TestProgram.Check(new[]{connection.Key,first.Demo.Token,second.Demo.Token,link.Token,opened.Cookie}.All(secret=>!audit.Contains(secret,StringComparison.Ordinal)),"Session-demo audit records omit all tested visitor, app and viewer secrets");
        var afterConsent = await SnapshotAsync(store);
        var originalActual = await store.ScalarAsync(OriginalSchemaSql, default);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new { initializedSchema, beforeConsent, afterConsent, originalExpected, originalBeforeConsent, originalActual }, new JsonSerializerOptions { WriteIndented = true }));
        TestProgram.Check(beforeConsent == afterConsent,"Session-demo consent introduces no schema or migration changes");
    }
}
