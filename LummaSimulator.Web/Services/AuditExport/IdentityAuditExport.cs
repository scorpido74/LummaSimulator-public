using System.Collections.Concurrent;
using System.Globalization;

namespace LummaSimulator.Web.Services.AuditExport;

public sealed record IdentityAuditExportOptions(bool Enabled,int BatchSize,int PollSeconds,bool Valid)
{
    public static IdentityAuditExportOptions Read(IConfiguration configuration)
    {
        var section=configuration.GetSection("SecurityAudit:WindowsEventLog");
        var enabled=section["Enabled"];
        var valid=enabled is null || bool.TryParse(enabled,out _);
        var batch=50;var poll=10;
        if(section["BatchSize"] is { } b) valid &= int.TryParse(b,NumberStyles.None,CultureInfo.InvariantCulture,out batch) && batch is >=1 and <=200;
        if(section["PollSeconds"] is { } p) valid &= int.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out poll) && poll is >=1 and <=300;
        return new(bool.TryParse(enabled,out var value) && value,Math.Clamp(batch,1,200),Math.Clamp(poll,1,300),valid);
    }
}
public sealed record IdentityAuditExportStatus(string Exporter,string State,long LastSequence,
    DateTimeOffset? LastAttempt,DateTimeOffset? LastSuccess,string? ErrorCode);
public sealed record IdentityAuditExporterRegistration(IIdentityAuditExporter Exporter,IdentityAuditExportOptions Options);

public sealed class IdentityAuditExportStatusStore(IdentityAuditExportOptions options)
{
    private readonly ConcurrentDictionary<string,IdentityAuditExportStatus> _items=new(StringComparer.Ordinal);
    public IReadOnlyList<IdentityAuditExportStatus> Snapshot() {
        Get(WindowsEventLogAuditExporter.ExporterName);
        return _items.Values.OrderBy(s=>s.Exporter,StringComparer.Ordinal).ToArray();
    }
    public IdentityAuditExportStatus Get(string name) => _items.GetOrAdd(name,n=>new(n,!options.Valid ? "Error" : options.Enabled ? "Waiting" : "Disabled",0,null,null,!options.Valid ? "InvalidConfiguration" : null));
    internal void Configure(string name,IdentityAuditExportOptions configuration) => _items.TryAdd(name,new(name,
        !configuration.Valid?"Error":configuration.Enabled?"Waiting":"Disabled",0,null,null,!configuration.Valid?"InvalidConfiguration":null));
    internal void Success(string name,long sequence,DateTimeOffset now,bool written) {
        var previous=Get(name);
        _items[name]=previous with {State="Ready",LastSequence=sequence,LastAttempt=now,LastSuccess=written?now:previous.LastSuccess,ErrorCode=null};
    }
    internal void Failure(string name,DateTimeOffset now,Exception error) {
        var code=error is UnauthorizedAccessException or System.Security.SecurityException ? "AccessDenied" :
            error is AuditExportException {Code: "SourceMissing" or "AccessDenied" or "UnsupportedPlatform" or
            "InvalidConfiguration" or "InvalidCheckpoint" or "InvalidExporterName" or "CheckpointRegression" or
            "InvalidAuditEvent" or "WindowsWriteFailed"} known ? known.Code : "ExportFailed";
        _items[name]=Get(name) with {State="Error",LastAttempt=now,ErrorCode=code};
    }
    internal void Attempt(string name,long sequence,DateTimeOffset now) => _items[name]=Get(name) with {LastSequence=sequence,LastAttempt=now};
}
public sealed class AuditExportException(string code) : Exception(code)
{
    public string Code { get; }=code;
}
public interface IAuditExportCheckpointLease : IDisposable
{
    Guid StreamId { get; }
    long LastSequence { get; }
    void Advance(long sequence);
}
public interface IAuditExportCheckpoints
{
    IAuditExportCheckpointLease Open(string exporter);
}

public sealed class IdentityAuditExportDispatcher(IServiceScopeFactory scopes,IEnumerable<IdentityAuditExporterRegistration> registrations,
    IAuditExportCheckpoints checkpoints,IdentityAuditExportStatusStore status,TimeProvider clock)
{
    private readonly IdentityAuditExporterRegistration[] _registrations=registrations.ToArray();
    internal bool HasEnabled => _registrations.Any(r=>r.Options.Enabled && r.Options.Valid);
    internal int PollSeconds => _registrations.Where(r=>r.Options.Enabled && r.Options.Valid).Select(r=>r.Options.PollSeconds).DefaultIfEmpty(10).Min();
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        // Disabled/invalid configuration performs no source, checkpoint or sink I/O.
        foreach(var registration in _registrations) {
            var exporter=registration.Exporter;var options=registration.Options;
            status.Configure(exporter.Name,options);
            if(!options.Enabled || !options.Valid) continue;
            try {
                ct.ThrowIfCancellationRequested();
                await exporter.CheckAvailabilityAsync(ct);
                using var lease=checkpoints.Open(exporter.Name);
                status.Attempt(exporter.Name,lease.LastSequence,clock.GetUtcNow());
                await using var scope=scopes.CreateAsyncScope();
                var audit=scope.ServiceProvider.GetRequiredService<SecurityAuditService>();
                var batch=await audit.ReadExportBatchAsync(lease.LastSequence,options.BatchSize,ct);
                if(batch.Count>0) {
                    await exporter.ExportAsync(new(lease.StreamId,batch),ct);
                    // Never advance after a partial/failing export. Retry can duplicate records.
                    lease.Advance(batch[^1].Sequence);
                }
                status.Success(exporter.Name,lease.LastSequence,clock.GetUtcNow(),batch.Count>0);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { return; }
            catch(Exception error) { status.Failure(exporter.Name,clock.GetUtcNow(),error); }
        }
    }
}
public sealed class IdentityAuditExportWorker(IdentityAuditExportDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try {
            while(!stoppingToken.IsCancellationRequested) {
                await dispatcher.RunOnceAsync(stoppingToken);
                if(!dispatcher.HasEnabled) return;
                await Task.Delay(TimeSpan.FromSeconds(dispatcher.PollSeconds),stoppingToken);
            }
        } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
}
public static class IdentityAuditExportRegistration
{
    public static IServiceCollection AddIdentityAuditExport(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddSingleton(IdentityAuditExportOptions.Read(configuration));
        services.AddSingleton<IdentityAuditExportStatusStore>();
        services.AddSingleton<IAuditExportCheckpoints,FileAuditExportCheckpoints>();
        services.AddSingleton<IWindowsIdentityEventWriter,WindowsIdentityEventWriter>();
        services.AddSingleton<WindowsEventLogAuditExporter>();
        services.AddSingleton<IIdentityAuditExporter>(sp=>sp.GetRequiredService<WindowsEventLogAuditExporter>());
        services.AddSingleton(sp=>new IdentityAuditExporterRegistration(sp.GetRequiredService<WindowsEventLogAuditExporter>(),sp.GetRequiredService<IdentityAuditExportOptions>()));
        services.AddSingleton<IdentityAuditExportDispatcher>();
        services.AddHostedService<IdentityAuditExportWorker>();
        return services;
    }
}
