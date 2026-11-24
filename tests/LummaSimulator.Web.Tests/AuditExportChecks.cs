using System.Text.Json;
using System.Security.Principal;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.AuditExport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;

internal static class AuditExportChecks
{
    private sealed class Writer : IWindowsIdentityEventWriter
    {
        internal readonly List<(int Id,bool Warning,string Payload)> Entries=[];
        internal string? Error;internal int FailOnCall=int.MaxValue;internal bool UnexpectedError;private int _calls;
        public void CheckAvailability() {
            if(Error is not null && FailOnCall==1) throw new AuditExportException(Error);
        }
        public void Write(int eventId,bool warning,string json) {
            if(UnexpectedError) throw new InvalidOperationException("secret-cookie-canary");
            if(Error is not null && ++_calls>=FailOnCall) throw new AuditExportException(Error);
            Entries.Add((eventId,warning,json));
        }
    }
    private sealed class ExplodingCheckpoints : IAuditExportCheckpoints
    {
        public IAuditExportCheckpointLease Open(string name) => throw new InvalidOperationException("Unexpected checkpoint access");
    }
    private static IdentityAuditExportDispatcher Dispatcher(IServiceScopeFactory scopes,IEnumerable<IIdentityAuditExporter> exporters,
        IAuditExportCheckpoints checkpoints,IdentityAuditExportOptions options,IdentityAuditExportStatusStore status,TimeProvider clock)
        => new(scopes,exporters.Select(e=>new IdentityAuditExporterRegistration(e,options)),checkpoints,status,clock);
    private static IdentityAuditEvent Fixture(int id=6101) => new(1,DateTimeOffset.UtcNow,id,"unsafe-cookie-canary",Guid.NewGuid().ToString("D"),
        "raw-user-token-canary","raw-credential-canary","raw-session-cookie-canary",id==6101?"None":"WebAuthn","Success","unsafe-source-canary",Guid.NewGuid().ToString("D"),"192.0.2.1",false);
    internal static async Task RunAsync(string root)
    {
        var config=new ConfigurationBuilder().Build();var defaults=IdentityAuditExportOptions.Read(config);
        TestProgram.Check(defaults is {Enabled:false,Valid:true},"Windows Event Log export is disabled by default");
        using var store=new DemoStore(Path.Combine(root,"audit-export.db"));await store.InitializeAsync();
        var clock=new TestClock();
        var services=new ServiceCollection().AddSingleton(store).AddSingleton<TimeProvider>(clock).AddScoped<SecurityAuditService>();
        await using var provider=services.BuildServiceProvider();
        var scopes=provider.GetRequiredService<IServiceScopeFactory>();var audit=new SecurityAuditService(store,clock);
        var checkpoints=new FileAuditExportCheckpoints(store);var writer=new Writer();
        var exporter=new WindowsEventLogAuditExporter(writer);
        var status=new IdentityAuditExportStatusStore(defaults);
        await Dispatcher(scopes,[exporter],new ExplodingCheckpoints(),defaults,status,clock).RunOnceAsync(default);
        TestProgram.Check(writer.Entries.Count==0 && status.Snapshot().Single().State=="Disabled","Disabled export performs no checkpoint or external writer access");
        var missingStatus=new IdentityAuditExportStatusStore(new(true,50,10,true));
        var missing=new WindowsEventLogAuditExporter(new Writer{Error="SourceMissing",FailOnCall=1});
        await Dispatcher(scopes,[missing],new ExplodingCheckpoints(),new(true,50,10,true),missingStatus,clock).RunOnceAsync(default);
        TestProgram.Check(missingStatus.Get(missing.Name).ErrorCode=="SourceMissing","Missing source is visible even with an empty audit, without checkpoint or write access");
        await new WindowsEventLogAuditExporter(writer).ExportAsync(new(Guid.NewGuid(),[new(1,Fixture())]),default);
        var json=writer.Entries.Single().Payload;
        using(var projection=JsonDocument.Parse(json)) {
            TestProgram.Check(projection.RootElement.GetProperty("eventName").GetString()=="IDENTITY_CREATED" && writer.Entries[0].Id==6101,"Event IDs and names derive from the stable identity contract");
            TestProgram.Check(projection.RootElement.GetProperty("demoRunId").GetString() is {Length:36}
                && projection.RootElement.GetProperty("correlationId").GetString() is {Length:36},"Structured export preserves safe run and correlation IDs");
        }
        TestProgram.Check(!json.Contains("canary") && !json.Contains("credential",StringComparison.OrdinalIgnoreCase) && !json.Contains("192.0.2.1"),"Allowlisted Windows payload omits credentials, tokens, cookies, IP and arbitrary source strings");
        foreach(var kind in Enum.GetValues<IdentityEvent>()) {
            writer.Entries.Clear();await exporter.ExportAsync(new(Guid.NewGuid(),[new(2,Fixture((int)kind))]),default);
            TestProgram.Check(writer.Entries.Single().Id==(int)kind && writer.Entries.Single().Warning==(kind is IdentityEvent.AUTHENTICATION_FAILED or IdentityEvent.PASSKEY_REGISTRATION_FAILED),"Stable native ID/severity: "+kind);
        }
        writer.Entries.Clear();
        var invalidEvent=false;
        try { await exporter.ExportAsync(new(Guid.NewGuid(),[new(3,Fixture(9999))]),default); }
        catch(AuditExportException e) {invalidEvent=e.Code=="InvalidAuditEvent";}
        TestProgram.Check(invalidEvent && writer.Entries.Count==0,"Unknown event IDs fail closed before native export");
        await audit.RecordAsync(IdentityEvent.IDENTITY_CREATED,null,"Success",default);
        await audit.RecordAsync(IdentityEvent.AUTHENTICATION_FAILED,null,"Failure",default);
        var enabled=new IdentityAuditExportOptions(true,50,10,true);status=new(enabled);
        var dispatcher=Dispatcher(scopes,[exporter],checkpoints,enabled,status,clock);
        var before=await PrizeMigrationChecks.Snapshot(store);
        foreach(var error in new[]{"SourceMissing","AccessDenied"}) {
            var failedWriter=new Writer{Error=error,FailOnCall=1};
            await Dispatcher(scopes,[new WindowsEventLogAuditExporter(failedWriter)],checkpoints,enabled,status,clock).RunOnceAsync(default);
            using var checkpoint=checkpoints.Open(exporter.Name);
            TestProgram.Check(checkpoint.LastSequence==0 && status.Get(exporter.Name).ErrorCode==error,"Missing source/non-administrator write denial is isolated, visible and retains backlog: "+error);
        }
        // Authentication still commits while an optional sink is known to be unavailable.
        var demos=new DemoService(store,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance,audit:audit);
        var demo=await demos.CreateAsync("laptop",default);using var key=new SoftwareAuthenticator();
        await demos.VerifyAsync(demo.Token,true,key.Register(await demos.OptionsAsync(demo.Token,true,default)),default);
        await demos.VerifyAsync(demo.Token,false,key.Authenticate(await demos.OptionsAsync(demo.Token,false,default)),default);
        TestProgram.Check(await audit.OwnerAsync(demo.Token,default) is not null && status.Get(exporter.Name).ErrorCode=="AccessDenied","WebAuthn authentication succeeds without requiring Event Log write privileges");
        before=await PrizeMigrationChecks.Snapshot(store);
        await dispatcher.RunOnceAsync(default);
        var count=writer.Entries.Count;
        TestProgram.Check(count>0 && status.Get(exporter.Name) is {State:"Ready",ErrorCode:null},"Recovered sink exports committed SQLite backlog and clears status error");
        TestProgram.Check(before==await PrizeMigrationChecks.Snapshot(store),"Export never mutates primary audit, sessions or credentials");
        await dispatcher.RunOnceAsync(default);
        TestProgram.Check(writer.Entries.Count==count,"Durable checkpoint prevents replay after an acknowledged batch");
        var restarted=Dispatcher(scopes,[exporter],new FileAuditExportCheckpoints(store),enabled,new(enabled),clock);
        await restarted.RunOnceAsync(default);
        TestProgram.Check(writer.Entries.Count==count,"Checkpoint survives dispatcher restart");
        Guid stream;
        using(var checkpoint=checkpoints.Open(exporter.Name)) { stream=checkpoint.StreamId; }
        var partialName="WindowsEventLogPartial";
        var partialWriter=new Writer{Error="AccessDenied",FailOnCall=2};
        var partial=new NamedExporter(partialName,new WindowsEventLogAuditExporter(partialWriter));
        await Dispatcher(scopes,[partial],checkpoints,enabled,status,clock).RunOnceAsync(default);
        using(var checkpoint=checkpoints.Open(partialName)) TestProgram.Check(checkpoint.LastSequence==0,"Partial delivery cannot advance a batch checkpoint");
        partialWriter.Error=null;
        await Dispatcher(scopes,[partial],checkpoints,enabled,status,clock).RunOnceAsync(default);
        var retried=partialWriter.Entries.Select(e=>JsonSerializer.Deserialize<WindowsIdentityAuditEvent>(e.Payload,new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        TestProgram.Check(retried.GroupBy(e=>(e.StreamId,e.Sequence)).Any(g=>g.Count()>1),"Partial failures retry with the same stream/sequence deduplication identity");
        using(var lease=checkpoints.Open(exporter.Name)) {
            var locked=false;try{using var other=checkpoints.Open(exporter.Name);}catch(IOException){locked=true;}
            TestProgram.Check(locked && lease.StreamId==stream,"Checkpoint identity is stable and an OS lock serializes exporters");
        }
        var unexpected=new NamedExporter("Broken",new WindowsEventLogAuditExporter(new Writer{UnexpectedError=true}));
        var independent=new NamedExporter("OtherSink",exporter);
        await Dispatcher(scopes,[unexpected,independent],checkpoints,enabled,status,clock).RunOnceAsync(default);
        TestProgram.Check(status.Get("Broken").ErrorCode=="ExportFailed" && status.Get("OtherSink").State=="Ready"
            && !JsonSerializer.Serialize(status.Snapshot()).Contains("secret-cookie-canary"),"Unexpected sink errors are sanitized and do not stop independent exporters");
        var unsafeError=new NamedExporter("UnsafeErrorCode",new WindowsEventLogAuditExporter(new Writer{Error="secret-cookie-canary",FailOnCall=1}));
        await Dispatcher(scopes,[unsafeError],checkpoints,enabled,status,clock).RunOnceAsync(default);
        TestProgram.Check(status.Get("UnsafeErrorCode").ErrorCode=="ExportFailed" && !JsonSerializer.Serialize(status.Snapshot()).Contains("secret-cookie-canary"),"Unknown exporter error codes cannot leak arbitrary exception data into presenter status");
        var independentlyConfiguredStatus=new IdentityAuditExportStatusStore(defaults);
        await new IdentityAuditExportDispatcher(scopes,[
            new(new NamedExporter("DisabledSink",new WindowsEventLogAuditExporter(new Writer{UnexpectedError=true})),defaults),
            new(new NamedExporter("EnabledSink",exporter),enabled)],checkpoints,independentlyConfiguredStatus,clock).RunOnceAsync(default);
        TestProgram.Check(independentlyConfiguredStatus.Get("DisabledSink").State=="Disabled" && independentlyConfiguredStatus.Get("EnabledSink").State=="Ready",
            "Exporter activation is independent; a disabled Windows sink cannot disable a future separate exporter");
        var checkpointPath=Path.Combine(Path.GetDirectoryName(store.DatabasePath)!,"identity-audit-export-WindowsEventLog.checkpoint.json");
        File.WriteAllText(checkpointPath,"{}");var countBeforeCorruption=writer.Entries.Count;
        await dispatcher.RunOnceAsync(default);
        TestProgram.Check(status.Get(exporter.Name).ErrorCode=="InvalidCheckpoint" && writer.Entries.Count==countBeforeCorruption
            && before==await PrizeMigrationChecks.Snapshot(store),"Corrupt checkpoint fails closed without automatic replay or primary-data changes");
        var badConfig=IdentityAuditExportOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["SecurityAudit:WindowsEventLog:Enabled"]="true",["SecurityAudit:WindowsEventLog:BatchSize"]="not-an-integer"}).Build());
        var badStatus=new IdentityAuditExportStatusStore(badConfig);
        await Dispatcher(scopes,[exporter],new ExplodingCheckpoints(),badConfig,badStatus,clock).RunOnceAsync(default);
        TestProgram.Check(!badConfig.Valid && badStatus.Snapshot().Single().ErrorCode=="InvalidConfiguration","Invalid optional-export configuration does not prevent host/authentication startup");
        var registrations=new ServiceCollection().AddSingleton(store).AddSingleton<TimeProvider>(clock).AddScoped<SecurityAuditService>();
        registrations.AddIdentityAuditExport(config);
        await using var registered=registrations.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
        TestProgram.Check(registered.GetServices<IIdentityAuditExporter>().Single() is WindowsEventLogAuditExporter
            && registered.GetRequiredService<IdentityAuditExportStatusStore>().Snapshot().Single().State=="Disabled","DI supplies separate exporter, dispatcher and disabled presenter status");
        if(!OperatingSystem.IsWindows()) {
            var unsupported=false;try{new WindowsIdentityEventWriter().Write(6101,false,"{}");}catch(AuditExportException e){unsupported=e.Code=="UnsupportedPlatform";}
            TestProgram.Check(unsupported,"Non-Windows export fails safely without native calls");
        }
    }
    private sealed class NamedExporter(string name,IIdentityAuditExporter inner) : IIdentityAuditExporter
    {
        public string Name=>name;
        public Task CheckAvailabilityAsync(CancellationToken ct)=>inner.CheckAvailabilityAsync(ct);
        public Task ExportAsync(IdentityAuditExportBatch batch,CancellationToken ct)=>inner.ExportAsync(batch,ct);
    }
    // Explicit, opt-in acceptance command. Never provision or elevate; never run in the normal suite.
    internal static async Task WindowsStandardUserSmokeAsync()
    {
        if(!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows required.");
        using var identity=WindowsIdentity.GetCurrent();
        if(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run this acceptance test as a regular, non-elevated Windows user.");
        var run=Guid.NewGuid().ToString("D");var correlation=Guid.NewGuid().ToString("D");var stream=Guid.NewGuid();
        var item=Fixture() with {DemoRunId=run,CorrelationId=correlation,Simulation=true};
        await new WindowsEventLogAuditExporter(new WindowsIdentityEventWriter()).ExportAsync(new(stream,[new(1,item)]),default);
        Console.WriteLine($"PASS: standard-user Event Log write; simulation=true; streamId={stream}; demoRunId={run}; correlationId={correlation}");
    }
}
