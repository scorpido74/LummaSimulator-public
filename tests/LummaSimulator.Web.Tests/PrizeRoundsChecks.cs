using System.Security.Claims;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.Extensions.DependencyInjection;

namespace LummaSimulator.Web.Tests;

internal static class PrizeRoundsChecks
{
    internal static async Task RunAsync(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"prize-rounds.db"));await store.InitializeAsync();
        var fingerprint=(string?)await store.ScalarAsync("SELECT Fingerprint FROM PrizeSchemaContract WHERE Version=6",default);
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundsContract WHERE Version=1",default))==1,
            "Round schema has its independent versioned contract");
        await PrizeRoundsMigration.InitializeAsync(store,default);await store.InitializeAsync();
        TestProgram.Check(fingerprint==(string?)await store.ScalarAsync("SELECT Fingerprint FROM PrizeSchemaContract WHERE Version=6",default)
            &&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundsContract",default))==1,
            "Round migration is repeatable and leaves the v6 Prize fingerprint unchanged");

        var clock=new TestClock();var presenters=PresenterChecks.Service(store,clock);var rounds=new PrizeRoundsService(store,presenters,clock);
        using var adminKey=new SoftwareAuthenticator();using var ownerKey=new SoftwareAuthenticator();using var memberKey=new SoftwareAuthenticator();
        var bootstrap=await presenters.IssueBootstrapAsync("round-admin","Round Admin",default);await PresenterChecks.Enroll(presenters,bootstrap.Code,adminKey);
        var admin=await PresenterChecks.Login(presenters,"round-admin",adminKey);
        await PresenterChecks.Reauth(presenters,admin,adminKey);var ownerInvite=await presenters.CreateAccountAsync(admin,"round-owner","Round Owner","Presenter",default);
        await PresenterChecks.Enroll(presenters,ownerInvite.Code,ownerKey);var owner=await PresenterChecks.Login(presenters,"round-owner",ownerKey);
        await PresenterChecks.Reauth(presenters,admin,adminKey);var memberInvite=await presenters.CreateAccountAsync(admin,"round-member","Round Member","Presenter",default);
        await PresenterChecks.Enroll(presenters,memberInvite.Code,memberKey);var member=await PresenterChecks.Login(presenters,"round-member",memberKey);
        var access=new EventAccess(store,presenters,clock);
        var service=new PrizeEventService(store,presenters,clock);
        var configuration=new PrizeConfiguration(PrizeEnabled:true,GroupSize:2,WinnersPerGroup:1,PrizeName:"Synthetic round prize",Stock:1);
        var prizeEvent=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic rounds","builtin-cybertruck",configuration,true,default);
        var emptyEvent=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic empty round","builtin-cybertruck",configuration,true,default);
        var originalExperience=await access.CreateExperienceAsync(admin,prizeEvent.Name,default);
        await store.ExecuteAsync("UPDATE PresenterInstallation SET FirstEvent=$id WHERE Id=1",default,("$id",originalExperience));
        var settings=new PresentationSettings(store,presenters,new ExperienceSettings(),clock);
        TestProgram.Check(await access.DefaultVisitorEventAsync(default) is {Kind:"experience",Available:true} initial&&initial.Id==originalExperience,
            "Initial visitor default reuses the existing onboarding Experience");
        var closedExperience=await access.CreateExperienceAsync(admin,"Closed Experience",default);
        await access.ChangeExperienceAsync(admin,closedExperience,true,default);
        await store.ExecuteAsync("INSERT INTO EventLoginRoutes VALUES('prize',$id,0)",default,("$id",emptyEvent.Id));
        var choices=await access.VisitorDefaultChoicesAsync(admin,default);
        TestProgram.Check(choices.First().Id==originalExperience&&choices.Count(row=>row.Name==prizeEvent.Name)==2
            &&choices.All(row=>row.Id!=closedExperience&&row.Id!=emptyEvent.Id)
            &&(await access.VisitorDefaultChoicesAsync(owner,default)).Count==0,
            "Default choices prioritize Experience, distinguish identical names by kind and exclude closed, disabled and unauthorized choices");
        await access.SetLoginRoutesAsync(admin,"prize",emptyEvent.Id,15,default);
        await PresenterChecks.Reauth(presenters,owner,ownerKey);
        await TestProgram.RejectAsync(()=>settings.SaveVisitorDefaultAsync(owner,"prize",prizeEvent.Id,default),"Prize owner cannot change portal settings");
        await TestProgram.RejectAsync(()=>settings.SaveVisitorDefaultAsync(admin,"prize",prizeEvent.Id,default),"Changing visitor default requires recent passkey verification");
        await PresenterChecks.Reauth(presenters,admin,adminKey);
        await settings.SaveVisitorDefaultAsync(admin,"prize",prizeEvent.Id,default);
        await store.InitializeAsync();
        TestProgram.Check(await new EventAccess(store,presenters,clock).DefaultVisitorEventAsync(default) is {Kind:"prize",Available:true} saved&&saved.Id==prizeEvent.Id,
            "Explicit Prize default survives reopening the database");
        await PresenterChecks.Reauth(presenters,admin,adminKey);
        await settings.SaveAsync(admin,settings.Current with{DisplayName="Updated display"},default);
        await store.ExecuteAsync("INSERT INTO EventLoginRoutes VALUES('prize',$id,0)",default,("$id",prizeEvent.Id));
        TestProgram.Check(await access.DefaultVisitorEventAsync(default) is {Kind:"prize",Available:false} disabledDefault&&disabledDefault.Id==prizeEvent.Id,
            "Ordinary presentation edits preserve the default and a disabled saved event does not fall back");
        await access.SetLoginRoutesAsync(admin,"prize",prizeEvent.Id,15,default);
        TestProgram.Check((await access.LoginRoutesAsync("experience",originalExperience,default)).Mask==15,
            "Experience without a stored choice retains all four methods");
        await access.SetLoginRoutesAsync(admin,"experience",originalExperience,4,default);
        await access.SetLoginRoutesAsync(owner,"prize",prizeEvent.Id,2,default);
        await TestProgram.RejectAsync(()=>access.SetLoginRoutesAsync(owner,"prize",prizeEvent.Id,0,default),"Prize cannot save all routes off");
        await TestProgram.RejectAsync(()=>access.SetLoginRoutesAsync(member,"prize",prizeEvent.Id,15,default),"A presenter without event management cannot change Prize sign-in methods");
        await store.InitializeAsync();
        TestProgram.Check((await access.LoginRoutesAsync("experience",originalExperience,default)).Mask==4
            &&(await access.LoginRoutesAsync("prize",prizeEvent.Id,default)).Mask==2
            &&(await access.ManagedLoginRoutesAsync(owner,"prize",prizeEvent.Id,default)).Mask==2,
            "Experience and Prize keep independent sign-in masks across startup");
        TestProgram.Check((await rounds.ProgressAsync(owner,prizeEvent.Id,null,default)).Eligible==0,
            "Enabling a sign-in method never grants Prize eligibility");
        await access.SetLoginRoutesAsync(owner,"prize",prizeEvent.Id,15,default);
        var emptyProgress=await rounds.ProgressAsync(owner,emptyEvent.Id,null,default);
        var emptyClosed=await rounds.CloseAsync(owner,emptyEvent.Id,emptyProgress.Round.GroupId,default);
        var emptyReplay=await rounds.CloseAsync(owner,emptyEvent.Id,emptyProgress.Round.GroupId,default);
        TestProgram.Check(emptyProgress.Eligible==0&&emptyClosed.WinnerCount==0&&emptyReplay.GroupId==emptyClosed.GroupId&&emptyReplay.Winners.Count==0,"Zero eligible entrants closes once with no winner, including repeated confirmation");
        var memberId=member.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await access.InviteAsync(owner,"prize",prizeEvent.Id,memberId,default);await access.AcceptAsync(member,(await access.InvitationsAsync(member,default)).Single().Id,true,default);
        TestProgram.Check((await rounds.PublicJoinInfoAsync(prizeEvent.Id)) is {Number:1,Capacity:2,Status:"Open"},
            "Owner default capacity starts the initial open round");
        TestProgram.Check(await access.CanAcceptVisitorAsync("prize",prizeEvent.Id,default),"Started Prize event with active owner admits anonymous visitors");
        await store.ExecuteAsync("UPDATE PresenterEventScopes SET OwnerId=NULL WHERE Kind='prize' AND SourceId=$id",default,("$id",prizeEvent.Id));
        TestProgram.Check(!await access.CanAcceptVisitorAsync("prize",prizeEvent.Id,default),"Ownerless event is unavailable before any visitor choice; admin management alone is insufficient");
        await store.ExecuteAsync("UPDATE PresenterEventScopes SET OwnerId=$owner WHERE Kind='prize' AND SourceId=$id",default,("$owner",owner.FindFirstValue(ClaimTypes.NameIdentifier)),("$id",prizeEvent.Id));
        var fixedQr=PrizeRoundsService.FixedJoinPath(prizeEvent.Id);
        var visitorQr=new ParticipantQrService(access,rounds,new WebAuthnSettings{RpId="cybertruck.example.test",Origins=["https://cybertruck.example.test"]});
        var configuredEntry=await visitorQr.ReadAsync(owner,prizeEvent.Id,"https://localhost:7243","/portal",default);
        TestProgram.Check(configuredEntry?.Url=="https://cybertruck.example.test/portal"+fixedQr,"Management localhost must not replace configured visitor origin, event query or path base");
        var secondQrEvent=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic QR second","builtin-cybertruck",configuration,true,default);
        var secondEntry=await visitorQr.ReadAsync(owner,secondQrEvent.Id,"https://localhost:7243","/portal",default);
        TestProgram.Check(secondEntry?.Url=="https://cybertruck.example.test/portal"+PrizeRoundsService.FixedJoinPath(secondQrEvent.Id)&&secondEntry.Url!=configuredEntry?.Url,"Two events have distinct exact configured visitor URLs");
        var experienceId=await access.CreateExperienceAsync(owner,"Synthetic QR experience",default);
        TestProgram.Check((await visitorQr.ReadAsync(owner,experienceId,"https://localhost:7243","/portal",default,"experience"))?.Url=="https://cybertruck.example.test/portal/Index?eventKind=experience&eventId="+experienceId,"QR supports the event card visitor-link label outside Prize");

        var demos=new DemoService(store,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DemoService>.Instance);
        async Task<NewDemo> AuthenticatedDemo(string route,SoftwareAuthenticator key,string? eventId=null)
        {
            var created=await demos.CreateAsync(route,default);await access.BindVisitorAsync(created.Token,"prize",eventId??prizeEvent.Id,default);
            var registration=await demos.OptionsAsync(created.Token,true,default);
            await demos.VerifyAsync(created.Token,true,key.Register(registration),default);
            var options=await demos.OptionsAsync(created.Token,false,default);await demos.VerifyAsync(created.Token,false,key.Authenticate(options),default);
            return created;
        }
        var singleEvent=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic single YubiKey","builtin-cybertruck",configuration with{PrizeName="YubiKey demo token"},true,default);
        using var singleKey=new SoftwareAuthenticator();var singleDemo=await AuthenticatedDemo("laptop",singleKey,singleEvent.Id);var singleRound=await rounds.JoinAuthenticatedAsync(singleDemo.Token,default);
        await store.ExecuteAsync("INSERT INTO VisitorExercises(DemoId,StartedAt,CompletedAt) VALUES($id,100,200)",default,("$id",singleDemo.Account.Id));
        var singleProgress=await rounds.ProgressAsync(owner,singleEvent.Id,singleRound!.GroupId,default);
        TestProgram.Check(singleProgress.PrizeNames.SequenceEqual(new[]{"YubiKey demo token"})&&singleProgress is {Eligible:1,PrizeAvailable:true,CanClose:true}&&singleProgress.Participants is [{Registered:true,SignedIn:true}],"One eligible participant has one row and a real available prize");
        TestProgram.Check((await rounds.VisitorStatusAsync(singleDemo.Token,default))?.LinkKey=="YubiKeyParticipationLink","YubiKey entry text derives from the configured prize, not authenticator route");
        var singleClosed=await rounds.CloseAsync(owner,singleEvent.Id,singleRound.GroupId,default);var singleReload=await rounds.ProgressAsync(owner,singleEvent.Id,singleRound.GroupId,default);
        TestProgram.Check(singleClosed.WinnerCount==1&&singleReload.Participants is [{Status:"RoundRowWon"}],"Reloading a closed round preserves its one winning participant row");
        TestProgram.Check((await rounds.ForDemoAsync(singleDemo.Token,default)) is {PrizeMessageKey:"VisitorPrizeWon",DrawPerformed:true},"Winning visitor sees the stored congratulation card");
        TestProgram.Check((await rounds.VisitorStatusAsync(singleDemo.Token,default))?.LinkKey=="YubiKeyResultLink","Visitor result link reads the stored winning round");
        using var firstKey=new SoftwareAuthenticator();using var secondKey=new SoftwareAuthenticator();using var thirdKey=new SoftwareAuthenticator();
        var first=await AuthenticatedDemo("laptop",firstKey);var firstRound=await rounds.JoinAuthenticatedAsync(first.Token,default);
        var second=await AuthenticatedDemo("phone",secondKey);var secondRound=await rounds.JoinAuthenticatedAsync(second.Token,default);
        TestProgram.Check(firstRound is {Number:1,Alias:not null}&&secondRound is {Number:1,Alias:not null}
            &&firstRound.Alias!=secondRound.Alias,"Two participants receive distinct anonymous aliases in round one");
        TestProgram.Check(firstRound!.Alias==first.Account.Name
            &&!firstRound.Alias.Contains("@",StringComparison.Ordinal),"Pre-registration synthetic alias is preserved in the round, without email data");
        var reauthOptions=await demos.OptionsAsync(first.Token,false,default);await demos.VerifyAsync(first.Token,false,firstKey.Authenticate(reauthOptions),default);
        var repeated=await rounds.JoinAuthenticatedAsync(first.Token,default);
        TestProgram.Check(repeated is {Number:1}&&repeated.Alias==firstRound.Alias,"Reload/re-authentication within a demo session preserves its original round and alias");
        var third=await AuthenticatedDemo("ipad",thirdKey);var full=await rounds.JoinAuthenticatedAsync(third.Token,default);
        TestProgram.Check(full is {Number:1,Status:"Full",Alias:null},"Full round rejects a new participant without moving the open round");
        TestProgram.Check(await rounds.HasPrizeDrawAsync(first.Token,default)&&await rounds.HasPrizeDrawAsync(third.Token,default),"Prize invitation follows the owned event even when the round is full");
        await rounds.SetNextCapacityAsync(member,prizeEvent.Id,firstRound.GroupId,3,default);
        var boardAfterCapacity=await rounds.BoardAsync(member,prizeEvent.Id,default);
        TestProgram.Check(boardAfterCapacity.NextCapacity==3
            &&boardAfterCapacity.Rounds.Single(r=>r.GroupId==firstRound.GroupId) is {Number:1,Capacity:2,Occupied:2,Status:"Open"},
            "Presenter sets round two capacity without changing the open first round or its participants");
        TestProgram.Check((await service.GetAsync(owner,prizeEvent.Id)) is {ConfigurationLocked:true} unchangedPolicy
            &&unchangedPolicy.Configuration==configuration,
            "Next round capacity remains editable while the complete event prize policy stays locked and unchanged");
        var progress=await rounds.ProgressAsync(owner,prizeEvent.Id,firstRound.GroupId,default);
        TestProgram.Check(progress is {Eligible:0,Completed:0}&&progress.Round.Occupied==2,"Full round reports unique active entrants separately from demo completion");
        await store.ExecuteAsync("INSERT INTO VisitorExercises(DemoId,StartedAt,CompletedAt) VALUES($id,100,200)",default,("$id",first.Account.Id));
        TestProgram.Check((await rounds.ProgressAsync(owner,prizeEvent.Id,firstRound.GroupId,default)).Completed==1,"One completed demo contributes once despite repeated authentication");
        var replay=new VisitorExerciseService(store,new SecurityAuditService(store,clock),clock);
        var participantsBeforeReplay = await store.ScalarAsync("SELECT COUNT(*) FROM PrizeParticipants WHERE EventId=$event",default,("$event",prizeEvent.Id));
        var entriesBeforeReplay = await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundParticipants WHERE EventId=$event",default,("$event",prizeEvent.Id));
        var completedAtBeforeReplay = await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",default,("$id",first.Account.Id));
        var completionAuditBeforeReplay = await store.ScalarAsync("SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND Action='RoundDemoCompleted'",default,("$event",prizeEvent.Id));
        var replayReceipt=await replay.BeginAsync(first.Token,default);
        TestProgram.Check(replayReceipt.Completed&&replayReceipt.Attempt is null&&(await rounds.ProgressAsync(owner,prizeEvent.Id,firstRound.GroupId,default)) is {Completed:1,Eligible:1},"Replay uses the same participant once, with no new round entry or completion");
        TestProgram.Check(Equals(participantsBeforeReplay,await store.ScalarAsync("SELECT COUNT(*) FROM PrizeParticipants WHERE EventId=$event",default,("$event",prizeEvent.Id))),"Replay adds no second Prize participant");
        TestProgram.Check(Equals(entriesBeforeReplay,await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundParticipants WHERE EventId=$event",default,("$event",prizeEvent.Id))),"Replay adds no second draw entry");
        TestProgram.Check(Equals(completedAtBeforeReplay,await store.ScalarAsync("SELECT CompletedAt FROM VisitorExercises WHERE DemoId=$id",default,("$id",first.Account.Id))),"Replay preserves the original completion timestamp");
        TestProgram.Check(Equals(completionAuditBeforeReplay,await store.ScalarAsync("SELECT COUNT(*) FROM PrizeAudit WHERE EventId=$event AND Action='RoundDemoCompleted'",default,("$event",prizeEvent.Id))),"Replay adds no second completion audit fact");
        var concurrent=new PrizeRoundsService(store,presenters,clock);
        var closed=await Task.WhenAll(Task.Run(()=>rounds.CloseAsync(owner,prizeEvent.Id,firstRound.GroupId,default)),
            Task.Run(()=>concurrent.CloseAsync(member,prizeEvent.Id,firstRound.GroupId,default)));
        TestProgram.Check(closed[0].GroupId==closed[1].GroupId&&closed[0].WinnerCount==1&&closed[1].Winners.Count==1,
            "Concurrent authorized presenters close one round and read the same single prize result");
        TestProgram.Check(Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeRoundResults WHERE EventId=$event AND GroupId=$group",default,("$event",prizeEvent.Id),("$group",firstRound.GroupId)))==1
            &&Convert.ToInt32(await store.ScalarAsync("SELECT COUNT(*) FROM PrizeGroups WHERE EventId=$event",default,("$event",prizeEvent.Id)))==2,
            "Concurrent close creates exactly one result and one next round");
        var next=await rounds.PublicJoinInfoAsync(prizeEvent.Id);
        TestProgram.Check(next is {Number:2,Capacity:3,Occupied:0}&&PrizeRoundsService.FixedJoinPath(prizeEvent.Id)==fixedQr,
            "The same fixed event link resolves to round two with its presenter-set capacity");
        TestProgram.Check((await rounds.BoardAsync(member,prizeEvent.Id,default)).Rounds.Single(r=>r.GroupId==firstRound.GroupId)
            is {Number:1,Capacity:2,Occupied:2,Status:"Closed"},"Opening round two preserves the capacity and occupancy of round one");
        var thirdRound=await rounds.JoinAuthenticatedAsync(third.Token,default);
        TestProgram.Check(thirdRound is {Number:2,Capacity:3,Alias:not null},"Previously full visitor can join the next round through the same event link");
        var closedWithoutPrize=await rounds.CloseAsync(owner,prizeEvent.Id,thirdRound!.GroupId,default);
        TestProgram.Check((await rounds.ForDemoAsync(third.Token))?.PrizeMessageKey=="VisitorNoPrize","No-stock result is stored for the whole round");
        TestProgram.Check(closedWithoutPrize.WinnerCount==0&&closedWithoutPrize.Winners.Count==0&&thirdRound.Alias is not null,
            "A round with no remaining prize closes and preserves its anonymous attendee result");

        foreach(var (demo,key,original) in new[]{(first,firstKey,firstRound),(second,secondKey,secondRound),(third,thirdKey,thirdRound)})
        {
            var before=await rounds.ForDemoAsync(demo.Token,default);
            var options=await demos.OptionsAsync(demo.Token,false,default);
            await demos.VerifyAsync(demo.Token,false,key.Authenticate(options),default);
            var after=await rounds.JoinAuthenticatedAsync(demo.Token,default);
            TestProgram.Check(before is {Status:"Closed"}&&after is {Status:"Closed"}
                &&after.GroupId==original!.GroupId&&after.Alias==original.Alias
                &&after.IsWinner==before.IsWinner&&after.PrizeName==before.PrizeName
                &&after.WinnerCount==before.WinnerCount&&after.Winners.SequenceEqual(before.Winners),
                "Re-authentication after round close preserves alias, original round and complete result");
        }

        var disabled=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic prizes disabled","builtin-cybertruck",
            configuration with{PrizeEnabled=false,PrizeName="YubiKey demo token"},true,default);
        using var disabledKey=new SoftwareAuthenticator();
        var disabledDemo=await AuthenticatedDemo("laptop",disabledKey,disabled.Id);
        var disabledJoined=await rounds.JoinAuthenticatedAsync(disabledDemo.Token,default);
        TestProgram.Check(!await rounds.HasPrizeDrawAsync(disabledDemo.Token,default),"Disabled prizes never select the prize-check exercise invitation");
        await store.ExecuteAsync("INSERT INTO VisitorExercises(DemoId,StartedAt,CompletedAt) VALUES($id,100,200)",default,("$id",disabledDemo.Account.Id));
        var disabledProgress=await rounds.ProgressAsync(owner,disabled.Id,disabledJoined!.GroupId,default);
        TestProgram.Check(disabledProgress.PrizeNames.SequenceEqual(new[]{"YubiKey demo token"})&&disabledProgress is {Eligible:1,PrizesEnabled:false,PrizeAvailable:false,CanClose:true,PrizeReason:"RoundPrizesDisabled"}&&disabledProgress.Participants is [{Status:"RoundNoDraw"}],"One entrant with prizes off closes without advertising a draw");
        TestProgram.Check((await rounds.VisitorStatusAsync(disabledDemo.Token,default))?.YubiKey==false,"Disabled prizes never show a YubiKey offer");
        TestProgram.Check((await rounds.ForDemoAsync(disabledDemo.Token))?.PrizeMessageKey=="VisitorPrizesOff","Disabled round shows a neutral no-draw message");
        var disabledClosed=await rounds.CloseAsync(owner,disabled.Id,disabledJoined!.GroupId,default);
        TestProgram.Check((await rounds.ProgressAsync(owner,disabled.Id,disabledJoined.GroupId,default)).Participants is [{Status:"RoundRowNotDrawn"}],"Closed prizes-off round remains readable with its unchanged no-prize result");
        var disabledOptions=await demos.OptionsAsync(disabledDemo.Token,false,default);
        await demos.VerifyAsync(disabledDemo.Token,false,disabledKey.Authenticate(disabledOptions),default);
        var disabledAgain=await rounds.JoinAuthenticatedAsync(disabledDemo.Token,default);
        TestProgram.Check(disabledClosed is {Status:"Closed",WinnerCount:0}&&disabledClosed.Winners.Count==0
            &&disabledAgain is {Status:"Closed",IsWinner:false,PrizeName:null,WinnerCount:0}
            &&disabledAgain.Alias==disabledJoined.Alias&&disabledAgain.GroupId==disabledJoined.GroupId,
            "Prizes-disabled round closes without a draw and preserves alias and result after re-authentication");

        var depleted=await service.CreateAsync(owner,Guid.NewGuid().ToString("D"),"Synthetic empty stock","builtin-cybertruck",
            configuration with{PrizeEnabled=true,PrizeName="Empty-stock prize",Stock=0},true,default);
        var depletedProgress=await rounds.ProgressAsync(owner,depleted.Id,null,default);
        TestProgram.Check(depletedProgress is {PrizeAvailable:false,PrizeReason:"RoundNoStock",CanClose:true}
            &&depletedProgress.PrizeNames.SequenceEqual(new[]{"Empty-stock prize"}),"Empty stock retains its saved prize name without offering a draw");
        await store.ExecuteAsync("UPDATE PrizeEvents SET FixedPrizeTypeId=NULL WHERE Id=$id",default,("$id",depleted.Id));
        var missingProgress=await rounds.ProgressAsync(owner,depleted.Id,null,default);
        TestProgram.Check(missingProgress is {PrizeAvailable:false,PrizeReason:"RoundNoPrizeConfigured",CanClose:true}
            &&missingProgress.PrizeNames.Count==0,"Missing event prize link differs from depleted stock");
        var switchedBack=await rounds.ProgressAsync(owner,singleEvent.Id,singleRound.GroupId,default);
        TestProgram.Check(switchedBack.PrizeNames.SequenceEqual(new[]{"YubiKey demo token"})&&switchedBack.PrizeReason=="RoundPrizeDrawn",
            "Returning to the closed event preserves its own prize configuration and saved draw");

        var openRoundId=(await rounds.BoardAsync(owner,prizeEvent.Id,default)).Rounds.Single(r=>r.Status=="Open").GroupId;
        foreach(var handler in new[]{"InvalidModel","Rename","Capacity","CloseRound"})
        {
            var http=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=owner};
            http.Request.Scheme="https";http.Request.Host=new Microsoft.AspNetCore.Http.HostString("localhost",7243);
            http.RequestServices=new ServiceCollection().AddSingleton(access).AddSingleton(new ParticipantQrService(access,rounds,new WebAuthnSettings{Origins=["https://localhost:7243"]})).BuildServiceProvider();
            http.Request.Headers.Origin="https://localhost:7243";
            var context=new Microsoft.AspNetCore.Mvc.RazorPages.PageContext{HttpContext=http,
                RouteData=new Microsoft.AspNetCore.Routing.RouteData()};
            context.RouteData.Values["id"]=prizeEvent.Id;
            var page=new LummaSimulator.Web.Pages.Presenter.Prizes.EventModel(service,rounds,
                new WebAuthnSettings{Origins=["https://localhost:7243"]},clock){PageContext=context,
                ExpectedEventId=prizeEvent.Id,NextRoundCapacity=0,Name="",OperationKey=Guid.NewGuid().ToString("D")};
            if(handler=="Capacity")page.RoundId=openRoundId;
            if(handler=="CloseRound")page.RoundId=disabledJoined.GroupId;
            if(handler=="InvalidModel")page.ModelState.AddModelError("Name","Synthetic validation error");
            var response=handler switch{
                "Capacity"=>await page.OnPostNextRoundCapacityAsync(),
                "CloseRound"=>await page.OnPostCloseRoundAsync(true),
                _=>await page.OnPostRenameAsync()};
            TestProgram.Check(response is Microsoft.AspNetCore.Mvc.RazorPages.PageResult&&http.Response.StatusCode>=400
                &&page.RoundBoard.Rounds.Count>0&&page.VisitorUrl=="https://localhost:7243"+fixedQr,
                "POST error reloads round board and visitor link before rendering: "+handler);
        }
    }
}
