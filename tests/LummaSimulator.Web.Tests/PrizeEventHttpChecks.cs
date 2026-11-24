using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace LummaSimulator.Web.Tests;

internal static class PrizeEventHttpChecks
{
    static string Hidden(string html,string name)
    {
        var tag=Regex.Matches(html,"<input[^>]*>").FirstOrDefault(m=>m.Value.Contains("name=\""+name+"\""))?.Value??"";
        var value=Regex.Match(tag,"value=\"([^\"]*)\"").Groups[1].Value;
        if(value.Length==0)throw new InvalidOperationException("Missing hidden field: "+name);
        return WebUtility.HtmlDecode(value);
    }
    static string Csrf(string html)=>Hidden(html,"__RequestVerificationToken");
    static Task<HttpResponseMessage> Post(HttpClient client,string route,string html,string? origin,params (string,string)[] fields) =>
        Send(client,route,origin,Csrf(html),fields);
    static Task<HttpResponseMessage> Send(HttpClient client,string route,string? origin,string? csrf,IEnumerable<(string,string)> fields) =>
        HttpRateLimitFixture.SendAsync(async () =>
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,route);
            if(origin is not null) request.Headers.Add("Origin",origin); // Missing is distinct from literal "null".
            var values=fields.Select(f=>new KeyValuePair<string,string>(f.Item1,f.Item2));
            if(csrf is not null) values=values.Append(new("__RequestVerificationToken",csrf));
            request.Content=new FormUrlEncodedContent(values);
            var response=await client.SendAsync(request);
            // Never print Location: it may contain an identifier, query token or supplied text.
            var location=response.Headers.Location;
            var redirect=location is null?"none":location.IsAbsoluteUri?"absolute":location.OriginalString.Split('?')[0] switch {
                "/Presenter/Login"=>"login", "/Presenter/Denied"=>"access-denied", _=>"other-relative" };
            var originKind=origin is null?"missing":origin=="null"?"opaque":"present";
            Console.WriteLine($"Prize HTTPS POST: origin={originKind}; status={(int)response.StatusCode}; redirect={redirect}");
            return response;
        });
    static async Task<long> AccountCount(string temporaryRoot,string login)
    {
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM PresenterAccounts WHERE Login=$login";command.Parameters.AddWithValue("$login",login);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    static async Task<string> ScopedEventId(string temporaryRoot,string kind,string ownerLogin,string name)
    {
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="""
            SELECT s.SourceId FROM PresenterEventScopes s JOIN PresenterAccounts a ON a.Id=s.OwnerId
            WHERE s.Kind=$kind AND a.Login=$login AND s.Name=$name
            """;command.Parameters.AddWithValue("$kind",kind);command.Parameters.AddWithValue("$login",ownerLogin);command.Parameters.AddWithValue("$name",name);
        return (string?)await command.ExecuteScalarAsync()??throw new InvalidOperationException("Missing isolated owner-scoped event.");
    }
    static async Task<long> EnrollmentInviteCount(string temporaryRoot,string eventId,string targetLogin)
    {
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="""
            SELECT COUNT(*) FROM PresenterEventInvites i JOIN PresenterAccounts a ON a.Id=i.TargetId
            WHERE i.Kind='prize' AND i.EventId=$event AND a.Login=$login AND i.EnrollmentHash IS NOT NULL
            """;command.Parameters.AddWithValue("$event",eventId);command.Parameters.AddWithValue("$login",targetLogin);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    internal static string PersonId(string html,string name)=>Regex.Match(html,"<option value=\"([^\"]+)\">"+Regex.Escape(name)+"</option>").Groups[1].Value;
    static async Task<string> Snapshot(string temporaryRoot)
    {
        // This is ONLY the new database explicitly configured by HttpChecks' own child.
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());
        await connection.OpenAsync(); using var transaction=connection.BeginTransaction(deferred:true);
        var rows=new List<string>();
        foreach(var table in new[]{"PrizeEvents","PrizeTypes","PrizeCreationOperations","PrizeOperations","PrizeAudit","PrizeOutbox","PrizeInstallationSettings","PrizeProfiles","PrizeGroups","PrizeRoundSettings","PrizeRoundParticipants","PrizeRoundResults"}) {
            var order=table switch {
                "PrizeCreationOperations"=>"ActorId,OperationKind,IdempotencyKey",
                "PrizeRoundSettings"=>"EventId","PrizeRoundParticipants"=>"EventId,ParticipantId","PrizeRoundResults"=>"EventId,GroupId",_=>"Id"};
            using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=$"SELECT * FROM {table} ORDER BY {order}";
            using var reader=await command.ExecuteReaderAsync();rows.Add(table);
            while(await reader.ReadAsync()) {
                var values=new object[reader.FieldCount];reader.GetValues(values);rows.Add(JsonSerializer.Serialize(values));
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",rows))));
    }
    static async Task<(string Status,string Mode,int Capacity,int Rounds)> PolicyState(string temporaryRoot,string eventId)
    {
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();using var query=connection.CreateCommand();
        query.CommandText="SELECT Status,Mode,GroupSize,(SELECT COUNT(*) FROM PrizeGroups WHERE EventId=PrizeEvents.Id) FROM PrizeEvents WHERE Id=$event";
        query.Parameters.AddWithValue("$event",eventId);using var reader=await query.ExecuteReaderAsync();
        if(!await reader.ReadAsync())throw new InvalidOperationException("Missing isolated Prize event.");
        return(reader.GetString(0),reader.GetString(1),reader.GetInt32(2),reader.GetInt32(3));
    }
    internal static void WriteConfigureServerDiagnostic(string logs)
    {
        var found=false;
        foreach(var line in logs.Split('\n'))
        {
            // Parse the captured JSON console records; never dump raw server output.
            try
            {
                using var record=JsonDocument.Parse(line);var root=record.RootElement;
                if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("Category",out var category)
                    ||category.ValueKind!=JsonValueKind.String
                    ||category.GetString()!="Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.PageActionInvoker"
                    ||!root.TryGetProperty("Message",out var message)||message.ValueKind!=JsonValueKind.String)continue;
                var text=message.GetString()??"";
                if(!text.Contains("EventModel.OnPostConfigureAsync",StringComparison.Ordinal))continue;
                var state=Regex.Match(text,@"ModelState is (Valid|Invalid|Unvalidated|Skipped)\b");
                if(!state.Success)continue;
                Console.WriteLine("Prize Configure server diagnostic: ModelState="+state.Groups[1].Value);found=true;
            }
            catch(JsonException) { } // Other child output is deliberately omitted.
        }
        if(!found)Console.WriteLine("Prize Configure server diagnostic: no matching model-state record captured.");
    }
    static async Task ConfigureDiagnostic(HttpResponseMessage response,(string,string)[] fields,string temporaryRoot,string eventId)
    {
        var body=await response.Content.ReadAsStringAsync();
        // Only the fixed localization span for a known error is eligible for output.
        var error=Regex.Match(body,"<span data-i18n=\"(PrizeInvalidSettings|PrizeSettingsLocked|PrizeEventChanged)\">([^<]*)</span>");
        Console.WriteLine($"Prize Configure response diagnostic: status={(int)response.StatusCode}; error={(error.Success?error.Groups[1].Value:"no known validation message")}");
        if(error.Success)Console.WriteLine("Prize Configure validation message: "+WebUtility.HtmlDecode(error.Groups[2].Value));
        Console.WriteLine($"Prize Configure request shape: RoundId submitted={fields.Any(f=>f.Item1=="RoundId")}; Input.Mode submitted={fields.Any(f=>f.Item1=="Input.Mode")}");

        // Inspect the same default MVC validation metadata as the portal; no submitted values.
        var services=new ServiceCollection();services.AddLogging();services.AddMvcCore().AddDataAnnotations();
        using var provider=services.BuildServiceProvider();
        var metadata=provider.GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForProperty(typeof(LummaSimulator.Web.Pages.Presenter.Prizes.EventModel),"RoundId");
        if(metadata.IsRequired&&!fields.Any(f=>f.Item1=="RoundId"))
        {
            var required=metadata.ValidatorMetadata.OfType<RequiredAttribute>().FirstOrDefault();
            var validation=required?.GetValidationResult(null,new ValidationContext(new object()){MemberName="RoundId",DisplayName="RoundId"});
            Console.WriteLine("Prize Configure metadata diagnostic: omitted required bound field RoundId; "+validation?.ErrorMessage);
        }
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=Path.Combine(temporaryRoot,"http.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();using var query=connection.CreateCommand();
        query.CommandText="SELECT Status='Open',EXISTS(SELECT 1 FROM PrizeGroups WHERE EventId=PrizeEvents.Id) FROM PrizeEvents WHERE Id=$event";
        query.Parameters.AddWithValue("$event",eventId);using var reader=await query.ExecuteReaderAsync();
        if(await reader.ReadAsync())Console.WriteLine($"Prize Configure event diagnostic: started={reader.GetBoolean(0)}; hasRounds={reader.GetBoolean(1)}");
    }
    internal static async Task RunAsync(HttpClient visitor,HttpClient admin,HttpClient owner,HttpClient member,HttpClient outsider,string origin,string temporaryRoot,bool diagnoseOwnerConfigure=false)
    {
        const string basePath="/Presenter/Prizes";
        foreach(var path in new[]{basePath,basePath+"/New",basePath+"/Event/"+Guid.NewGuid()})
            TestProgram.Check((await HttpRateLimitFixture.GetAsync(visitor,path)).StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS visitor cannot access management: "+path);
        async Task CreateExperience(HttpClient client,string name)
        {
            var dashboard=await HttpRateLimitFixture.GetStringAsync(client,"/Presenter/Events");
            using var response=await Post(client,"/Presenter/Events",dashboard,origin,("operation","create"),("kind","experience"),("name",name),("confirmed","true"));
            TestProgram.Check(response.StatusCode==HttpStatusCode.Redirect,"Any authenticated presenter can create an event by direct request: "+name);
        }
        await CreateExperience(owner,"Owner-created experience");
        await CreateExperience(member,"Member-created experience");
        await CreateExperience(outsider,"Unshared-presenter-created experience");
        var ownerExperienceId=await ScopedEventId(temporaryRoot,"experience","http-event-owner","Owner-created experience");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(owner,"/Presenter/Events?kind=experience&id="+ownerExperienceId)).StatusCode==HttpStatusCode.OK,
            "Presenter can read the event they created by direct request");

        var ownerNewPage=await HttpRateLimitFixture.GetStringAsync(owner,basePath+"/New");
        var ownerNewFields=new (string,string)[]{("ProfileId","builtin-instant"),("OperationKey",Hidden(ownerNewPage,"OperationKey")),("Name","Owner scoped policy"),("start","true"),("Input.Mode","Instant")};
        using var ownerCreated=await Post(owner,basePath+"/New",ownerNewPage,origin,ownerNewFields);
        TestProgram.Check(ownerCreated.StatusCode==HttpStatusCode.Redirect,"Presenter can create a Prize event directly");
        var ownerTarget=ownerCreated.Headers.Location!.ToString();var ownerEventId=await ScopedEventId(temporaryRoot,"prize","http-event-owner","Owner scoped policy");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(owner,ownerTarget)).StatusCode==HttpStatusCode.OK,"Owner can directly read own Prize event");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(member,ownerTarget)).StatusCode==HttpStatusCode.NotFound
            && (await HttpRateLimitFixture.GetAsync(outsider,ownerTarget)).StatusCode==HttpStatusCode.NotFound,
            "Unshared presenters cannot read another presenter's Prize event");
        (string,string)[] ActionFields(string page)=>[("ExpectedEventId",Hidden(page,"ExpectedEventId")),("Version",Hidden(page,"Version")),("OperationKey",Hidden(page,"OperationKey")),("confirmed","true")];
        var ownerDetails=await HttpRateLimitFixture.GetStringAsync(owner,ownerTarget);
        var initialPolicy=await PolicyState(temporaryRoot,ownerEventId);
        TestProgram.Check(initialPolicy is {Status:"Open",Mode:"Instant",Rounds:0},"Owner policy fixture is started without any rounds");
        var ownerSettings=ActionFields(ownerDetails).Concat(new (string,string)[]{("Input.GroupSize","17"),("Input.PrizeName","Owner policy"),("Input.Mode","Instant")}).ToArray();
        TestProgram.Check(!ownerSettings.Any(f=>f.Item1=="RoundId"),"Owner Configure submits no RoundId");
        using(var allowed=await Post(owner,ownerTarget+"?handler=Configure",ownerDetails,origin,ownerSettings))
        {
            if(diagnoseOwnerConfigure)await ConfigureDiagnostic(allowed,ownerSettings,temporaryRoot,ownerEventId);
            TestProgram.Check(allowed.StatusCode==HttpStatusCode.Redirect,"Event owner can change Prize policy by direct request");
        }
        TestProgram.Check(await PolicyState(temporaryRoot,ownerEventId)==("Open","Instant",17,0),
            "Owner Configure without RoundId saves policy and preserves the no-round scenario");
        if(diagnoseOwnerConfigure)return;
        var adminDetails=await HttpRateLimitFixture.GetStringAsync(admin,ownerTarget);var adminSettings=ActionFields(adminDetails).Concat(new (string,string)[]{("Input.GroupSize","18"),("Input.PrizeName","Admin must not change policy"),("Input.Mode","Instant")}).ToArray();
        var beforeAdminPolicy=await Snapshot(temporaryRoot);
        using(var denied=await Post(admin,ownerTarget+"?handler=Configure",adminDetails,origin,adminSettings))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.Forbidden && await Snapshot(temporaryRoot)==beforeAdminPolicy,
                "Administrator without event ownership cannot change Prize policy");
        var outsiderPage=await HttpRateLimitFixture.GetStringAsync(outsider,"/Presenter/Events");var outsiderSettings=ActionFields(ownerDetails).Concat(new (string,string)[]{("Input.GroupSize","19"),("Input.PrizeName","Outsider must not change policy")}).ToArray();
        var beforeOutsiderPolicy=await Snapshot(temporaryRoot);
        using(var denied=await Post(outsider,ownerTarget+"?handler=Configure",outsiderPage,origin,outsiderSettings))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.NotFound && await Snapshot(temporaryRoot)==beforeOutsiderPolicy,
                "Presenter without event access cannot change Prize policy by direct request");

        var ownerEvents=await HttpRateLimitFixture.GetStringAsync(owner,"/Presenter/Events?kind=prize&id="+ownerEventId);
        var memberId=PersonId(ownerEvents,"HTTP Presenter");
        TestProgram.Check(Guid.TryParse(memberId,out _) && ownerEvents.Contains("name=\"operation\" value=\"invite\""),
            "Owner can select and invite an existing presenter");
        using(var ownerInvite=await Post(owner,"/Presenter/Events",ownerEvents,origin,("operation","invite"),("kind","prize"),("id",ownerEventId),("target",memberId),("confirmed","true")))
            TestProgram.Check(ownerInvite.StatusCode==HttpStatusCode.Redirect,"Owner can invite an existing presenter by direct request");
        var memberInvitations=await HttpRateLimitFixture.GetStringAsync(member,"/Presenter/Events");var ownerInviteId=Hidden(memberInvitations,"invitation");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(member,ownerTarget)).StatusCode==HttpStatusCode.NotFound,
            "Pending owner invitation does not grant event access");
        using(var accepted=await Post(member,"/Presenter/Events",memberInvitations,origin,("operation","accept"),("invitation",ownerInviteId),("confirmed","true")))
            TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect,"Invited presenter accepts owner event invitation by direct request");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(member,ownerTarget)).StatusCode==HttpStatusCode.OK,
            "Presenter can read an event shared by its owner");
        var memberEvents=await HttpRateLimitFixture.GetStringAsync(member,"/Presenter/Events?kind=prize&id="+ownerEventId);
        TestProgram.Check(memberEvents.Contains("name=\"operation\" value=\"invite\"")&&!memberEvents.Contains("name=\"operation\" value=\"new\""),
            "Event member can invite existing presenters but is not offered new-account enrollment");
        var outsiderId=PersonId(memberEvents,"HTTP Event Outsider");
        TestProgram.Check(Guid.TryParse(outsiderId,out _),"Event member can select an existing presenter outside the event");
        using(var memberInvite=await Post(member,"/Presenter/Events",memberEvents,origin,("operation","invite"),("kind","prize"),("id",ownerEventId),("target",outsiderId),("confirmed","true")))
            TestProgram.Check(memberInvite.StatusCode==HttpStatusCode.Redirect,"Event member can invite an existing presenter by direct request");
        var outsiderInvitations=await HttpRateLimitFixture.GetStringAsync(outsider,"/Presenter/Events");
        using(var accepted=await Post(outsider,"/Presenter/Events",outsiderInvitations,origin,("operation","accept"),("invitation",Hidden(outsiderInvitations,"invitation")),("confirmed","true")))
            TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect,"Second invited presenter accepts shared-event access");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(outsider,ownerTarget)).StatusCode==HttpStatusCode.OK,
            "Second presenter reads the event after accepting its invitation");

        var ownerNewCount=await AccountCount(temporaryRoot,"http-event-owner-new");
        using(var denied=await Post(owner,"/Presenter/Events",ownerEvents,origin,("operation","new"),("kind","prize"),("id",ownerEventId),("login","http-event-owner-new"),("name","Owner-created account"),("confirmed","true")))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.OK&&await AccountCount(temporaryRoot,"http-event-owner-new")==ownerNewCount,
                "Non-administrator owner cannot invite a new presenter account");
        var memberNewCount=await AccountCount(temporaryRoot,"http-event-member-new");
        using(var denied=await Post(member,"/Presenter/Events",memberEvents,origin,("operation","new"),("kind","prize"),("id",ownerEventId),("login","http-event-member-new"),("name","Member-created account"),("confirmed","true")))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.OK&&await AccountCount(temporaryRoot,"http-event-member-new")==memberNewCount,
                "Event member cannot invite a new presenter account");
        var ownerWorkspace="/Presenter/Events?kind=prize&id="+ownerEventId;
        var overviewWorkspace=ownerWorkspace+"&overview=true";
        var policyBeforeInvite=await PolicyState(temporaryRoot,ownerEventId);
        using var adminWorkspace=await HttpRateLimitFixture.GetAsync(admin,ownerWorkspace);
        using var adminOverview=await HttpRateLimitFixture.GetAsync(admin,overviewWorkspace);
        using var memberOverview=await HttpRateLimitFixture.GetAsync(member,overviewWorkspace);
        Console.WriteLine($"Prize invitation fixture event={ownerEventId}; status={policyBeforeInvite.Status}; mode={policyBeforeInvite.Mode}; rounds={policyBeforeInvite.Rounds}; administrator GET {ownerWorkspace} -> {(int)adminWorkspace.StatusCode}; GET {overviewWorkspace} -> {(int)adminOverview.StatusCode}");
        TestProgram.Check(policyBeforeInvite is {Status:"Open",Mode:"Instant",Rounds:0}
            &&adminWorkspace.StatusCode==HttpStatusCode.NotFound&&adminOverview.StatusCode==HttpStatusCode.OK,
            "Administrator outside the event uses the administrative overview to open the existing Prize event");
        var adminEvents=await adminOverview.Content.ReadAsStringAsync();
        TestProgram.Check(memberOverview.StatusCode==HttpStatusCode.OK
            &&!(await memberOverview.Content.ReadAsStringAsync()).Contains("name=\"operation\" value=\"new\"")
            &&adminEvents.Contains("name=\"operation\" value=\"new\""),
            "Overview exposes new-account enrollment only to the administrator");
        using(var adminInvite=await Post(admin,"/Presenter/Events",adminEvents,origin,("operation","new"),("kind","prize"),("id",ownerEventId),("login","http-event-admin-new"),("name","Administrator-invited account"),("confirmed","true")))
            TestProgram.Check(adminInvite.StatusCode==HttpStatusCode.OK&&await AccountCount(temporaryRoot,"http-event-admin-new")==1
                &&await EnrollmentInviteCount(temporaryRoot,ownerEventId,"http-event-admin-new")==1,
                "Administrator can invite a new presenter account for an event");

        var memberDetails=await HttpRateLimitFixture.GetStringAsync(member,ownerTarget);
        var memberSettings=ActionFields(memberDetails).Concat(new (string,string)[]{("Input.GroupSize","20"),("Input.PrizeName","Member must not change policy"),("Input.Mode","Instant")}).ToArray();
        var beforeMemberPolicy=await Snapshot(temporaryRoot);
        using(var denied=await Post(member,ownerTarget+"?handler=Configure",memberDetails,origin,memberSettings))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.NotFound&&await Snapshot(temporaryRoot)==beforeMemberPolicy,
                "Shared event member cannot change owner Prize policy by direct request");
        var outsiderDetails=await HttpRateLimitFixture.GetStringAsync(outsider,ownerTarget);
        var outsiderMemberSettings=ActionFields(outsiderDetails).Concat(new (string,string)[]{("Input.GroupSize","21"),("Input.PrizeName","Second presenter must not change policy")}).ToArray();
        var beforeOutsiderMemberPolicy=await Snapshot(temporaryRoot);
        using(var denied=await Post(outsider,ownerTarget+"?handler=Configure",outsiderDetails,origin,outsiderMemberSettings))
            TestProgram.Check(denied.StatusCode==HttpStatusCode.NotFound&&await Snapshot(temporaryRoot)==beforeOutsiderMemberPolicy,
                "Accepted non-owner event member cannot change Prize policy");

        var groupNew=await HttpRateLimitFixture.GetStringAsync(owner,basePath+"/New");
        using var groupCreated=await Post(owner,basePath+"/New",groupNew,origin,("ProfileId","builtin-cybertruck"),
            ("OperationKey",Hidden(groupNew,"OperationKey")),("Name","Round ID guards"),("start","true"),("Input.GroupSize","2"));
        TestProgram.Check(groupCreated.StatusCode==HttpStatusCode.Redirect,"Owner starts a group event for round-specific HTTP guards");
        var groupTarget=groupCreated.Headers.Location!.ToString();
        var groupDetails=await HttpRateLimitFixture.GetStringAsync(owner,groupTarget);
        foreach(var handler in new[]{"CloseRound","NextRoundCapacity"})
        {
            var beforeRoundAction=await Snapshot(temporaryRoot);
            using var missingRound=await Post(owner,groupTarget+"?handler="+handler,groupDetails,origin,("confirmed","true"),("NextRoundCapacity","3"));
            TestProgram.Check(missingRound.StatusCode==HttpStatusCode.BadRequest&&await Snapshot(temporaryRoot)==beforeRoundAction,
                "Round-specific HTTPS handler rejects a missing RoundId without changes: "+handler);
        }
        var beforeLockedPolicy=await Snapshot(temporaryRoot);
        var lockedFields=ActionFields(groupDetails).Concat(new (string,string)[]{("Input.GroupSize","3"),("Input.PrizeName","Locked round policy")}).ToArray();
        using(var locked=await Post(owner,groupTarget+"?handler=Configure",groupDetails,origin,lockedFields))
        {
            var lockedBody=await locked.Content.ReadAsStringAsync();
            TestProgram.Check(locked.StatusCode==HttpStatusCode.Conflict&&lockedBody.Contains("data-i18n=\"PrizeSettingsLocked\"",StringComparison.Ordinal)
                &&await Snapshot(temporaryRoot)==beforeLockedPolicy,"Owner Configure without RoundId still enforces the started round's prize policy lock");
        }

        var html=await HttpRateLimitFixture.GetStringAsync(admin,basePath+"/New");
        TestProgram.Check(Regex.IsMatch(html,"<option[^>]*value=\"builtin-cybertruck\"[^>]*selected"),"5.2a HTTPS first default is Cybertruck");
        TestProgram.Check(html.Contains("name=\"Input.GroupSize\"") && html.Contains("<details") && html.Contains("Input.RetentionDays"),"5.2a HTTPS basic and collapsed advanced controls render with model prefix");
        var key=Hidden(html,"OperationKey");
        (string,string)[] fields=[("ProfileId","builtin-instant"),("OperationKey",key),("Name","<script>Synthetic</script>"),("start","true"),("Input.Mode","Instant")];
        foreach(var scenario in new[]{"no-csrf","invalid-csrf","external-origin","opaque-origin","missing-origin"}) {
            var before=await Snapshot(temporaryRoot);
            var requestOrigin=scenario switch {"external-origin"=>"https://other.invalid","opaque-origin"=>"null","missing-origin"=>null,_=>origin};
            var csrf=scenario switch {"no-csrf"=>null,"invalid-csrf"=>"synthetic-invalid-csrf",_=>Csrf(html)};
            using var rejected=await Send(admin,basePath+"/New",requestOrigin,csrf,fields);
            TestProgram.Check(rejected.StatusCode==HttpStatusCode.BadRequest && rejected.Headers.Location is null,
                $"Prize creation rejects {scenario}; actual status={(int)rejected.StatusCode}; redirect={(rejected.Headers.Location is null?"none":"present")}");
            TestProgram.Check(await Snapshot(temporaryRoot)==before,"Rejected Prize request leaves event/operation/audit/outbox/profile state unchanged: "+scenario);
        }
        var invalid=fields.Append(("Input.GroupSize","0")).ToArray();
        var invalidResponse=await Post(admin,basePath+"/New",html,origin,invalid);
        var invalidHtml=await invalidResponse.Content.ReadAsStringAsync();
        TestProgram.Check(invalidResponse.StatusCode==HttpStatusCode.BadRequest
            && invalidHtml.Contains("role=\"alert\"") && invalidHtml.Contains("Controleer de instellingen")
            && invalidHtml.Contains("class=\"panel prize-event-form\"") && Hidden(invalidHtml,"OperationKey")==key,
            "Invalid creation returns the form with understandable error and preserves retry intent");
        var beforeCreate=await Snapshot(temporaryRoot);
        var created=await Post(admin,basePath+"/New",invalidHtml,origin,fields);
        TestProgram.Check(created.StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS admin can create and start event");
        TestProgram.Check(await Snapshot(temporaryRoot)!=beforeCreate,"Valid same-origin Prize request commits event state");
        var target=created.Headers.Location!.ToString();
        var retry=await Post(admin,basePath+"/New",html,origin,fields);
        TestProgram.Check(retry.StatusCode==HttpStatusCode.Redirect && retry.Headers.Location!.ToString()==target,"5.2a HTTPS duplicate submission returns same event");
        var changed=fields.Select(f=>f.Item1=="Name"?("Name","Different"):f).ToArray();
        TestProgram.Check((await Post(admin,basePath+"/New",html,origin,changed)).StatusCode==HttpStatusCode.Conflict,"5.2a HTTPS changed payload conflicts");
        var peerNew=await HttpRateLimitFixture.GetStringAsync(member,basePath+"/New");
        TestProgram.Check(Regex.IsMatch(peerNew,"<option[^>]*value=\"builtin-instant\"[^>]*selected"),"5.2a HTTPS remembered selection shared by presenters");
        TestProgram.Check(peerNew.Contains("Geavanceerd") && peerNew.Contains("Evenementnaam"),"5.2a HTTPS presenter sees Dutch management labels");
        var applied=await HttpRateLimitFixture.GetStringAsync(member,basePath+"/New?profileId=builtin-draw");
        TestProgram.Check(Regex.IsMatch(applied,"<option[^>]*value=\"builtin-draw\"[^>]*selected"),"5.2a HTTPS explicit profile selection applied");
        TestProgram.Check(Regex.IsMatch(await HttpRateLimitFixture.GetStringAsync(member,basePath+"/New"),"<option[^>]*value=\"builtin-instant\"[^>]*selected"),"5.2a HTTPS selecting a profile alone never changes preference");
        TestProgram.Check((await HttpRateLimitFixture.GetAsync(member,target)).StatusCode==HttpStatusCode.NotFound,"6.5 unrelated presenter cannot read Prize detail by direct URL");
        var scopedId=target.Split('/').Last();var workspace="/Presenter/Events?kind=prize&id="+scopedId;
        var management=await HttpRateLimitFixture.GetStringAsync(admin,workspace);
        var targetId=Regex.Match(management,"<option value=\"([^\"]+)\">HTTP Presenter</option>").Groups[1].Value;
        TestProgram.Check(Guid.TryParse(targetId,out _),"6.5 owner can select an existing presenter");
        using(var invitation=await Post(admin,"/Presenter/Events",management,origin,("operation","invite"),("kind","prize"),("id",scopedId),("target",targetId),("confirmed","true")))TestProgram.Check(invitation.StatusCode==HttpStatusCode.Redirect,"6.5 owner issues event invitation");
        var invitations=await HttpRateLimitFixture.GetStringAsync(member,"/Presenter/Events");
        var invitationId=Hidden(invitations,"invitation");
        using(var accepted=await Post(member,"/Presenter/Events",invitations,origin,("operation","accept"),("invitation",invitationId),("confirmed","true")))TestProgram.Check(accepted.StatusCode==HttpStatusCode.Redirect,"6.5 intended presenter accepts invitation through authenticated HTTPS");
        var detail=await HttpRateLimitFixture.GetStringAsync(member,target);
        TestProgram.Check(detail.Contains("&lt;script&gt;Synthetic&lt;/script&gt;") && !detail.Contains("<script>Synthetic</script>"),"5.2a HTTPS event name is HTML encoded");
        TestProgram.Check(detail.Contains("name=\"ExpectedEventId\"") && !detail.Contains("claimcode",StringComparison.OrdinalIgnoreCase),"5.2a HTTPS details show scoped management without claim code");
        var af=ActionFields(detail);
        var mismatch=af.Select(f=>f.Item1=="ExpectedEventId"?("ExpectedEventId",Guid.NewGuid().ToString("D")):f).ToArray();
        TestProgram.Check((await Post(member,target+"?handler=Close",detail,origin,mismatch)).StatusCode==HttpStatusCode.BadRequest,"5.2a HTTPS cross-event body mismatch rejected");
        var close=await Post(member,target+"?handler=Close",detail,origin,af);
        TestProgram.Check(close.StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS presenter closes event");
        TestProgram.Check((await Post(member,target+"?handler=Close",detail,origin,af)).StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS close retry does not duplicate mutation");
        var stale=af.Select(f=>f.Item1=="OperationKey"?("OperationKey",Guid.NewGuid().ToString("D")):f).Append(("Name","Stale name")).ToArray();
        TestProgram.Check((await Post(member,target+"?handler=Rename",detail,origin,stale)).StatusCode==HttpStatusCode.NotFound,"6.5 linked presenter cannot change owner settings through direct POST");
        var closed=await HttpRateLimitFixture.GetStringAsync(admin,target);
        TestProgram.Check((await Post(admin,target+"?handler=Reopen",closed,origin,ActionFields(closed))).StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS admin reopens within retention");
        var settingsPage=await HttpRateLimitFixture.GetStringAsync(admin,target);
        var settingsFields=ActionFields(settingsPage).Concat(new (string,string)[]{("Input.GroupSize","17"),("Input.PrizeName","Updated synthetic prize"),("Input.Mode","Instant")}).ToArray();
        TestProgram.Check((await Post(admin,target+"?handler=Configure",settingsPage,origin,settingsFields)).StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS settings can be saved server-side");
        var settingsAfter=await HttpRateLimitFixture.GetStringAsync(admin,target);
        TestProgram.Check(settingsAfter.Contains("value=\"Updated synthetic prize\""),"5.2a HTTPS updated configuration reloads");
        var invalidFields=ActionFields(settingsAfter).Append(("Input.GroupSize","0")).ToArray();
        TestProgram.Check((await Post(admin,target+"?handler=Configure",settingsAfter,origin,invalidFields)).StatusCode==HttpStatusCode.BadRequest,"5.2a HTTPS invalid settings rejected");
        var profilePage=await HttpRateLimitFixture.GetStringAsync(admin,target);
        var profileFields=ActionFields(profilePage).Append(("ProfileName","HTTP custom")).ToArray();
        TestProgram.Check((await Post(admin,target+"?handler=Profile",profilePage,origin,profileFields)).StatusCode==HttpStatusCode.Redirect,"5.2a HTTPS explicit save as new profile");
        TestProgram.Check((await HttpRateLimitFixture.GetStringAsync(member,basePath+"/New")).Contains("HTTP custom"),"5.2a HTTPS custom profile visible to another presenter");
        TestProgram.Check((await HttpRateLimitFixture.GetStringAsync(admin,basePath)).Contains("&lt;script&gt;Synthetic&lt;/script&gt;"),"5.2a HTTPS overview shows existing events");
        foreach(var path in new[]{"/api/prizes","/Prizes","/Prizes/Register","/PrizeExperience","/api/prizes/events"})
            TestProgram.Check((await HttpRateLimitFixture.GetAsync(visitor,path)).StatusCode==HttpStatusCode.NotFound,"5.2a HTTPS no public Prize route: "+path);
    }
}
