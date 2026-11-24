using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LummaSimulator.Web.Services.AuditExport;

public interface IWindowsIdentityEventWriter
{
    void CheckAvailability();
    void Write(int eventId,bool warning,string json);
}

// Fixed projection intentionally omits user/session IDs, IP and credential hashes as well as secrets.
public sealed record WindowsIdentityAuditEvent(int SchemaVersion,Guid StreamId,long Sequence,DateTimeOffset At,
    int EventId,string EventName,string? DemoRunId,string? CorrelationId,string AuthenticationMethod,
    string Result,string SourceComponent,bool Simulation);

public sealed class WindowsEventLogAuditExporter(IWindowsIdentityEventWriter writer) : IIdentityAuditExporter
{
    public const string ExporterName="WindowsEventLog";
    public string Name => ExporterName;
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    public Task CheckAvailabilityAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();writer.CheckAvailability();return Task.CompletedTask;
    }
    public Task ExportAsync(IdentityAuditExportBatch batch,CancellationToken ct)
    {
        foreach(var entry in batch.Entries) {
            ct.ThrowIfCancellationRequested();
            var audit=entry.Event;
            if(audit.SchemaVersion!=1 || !Enum.IsDefined(typeof(IdentityEvent),audit.EventId) || entry.Sequence<=0 || batch.StreamId==Guid.Empty)
                throw new AuditExportException("InvalidAuditEvent");
            var kind=(IdentityEvent)audit.EventId;
            var payload=new WindowsIdentityAuditEvent(1,batch.StreamId,entry.Sequence,audit.At.ToUniversalTime(),audit.EventId,kind.ToString(),
                Guid.TryParseExact(audit.DemoRunId,"D",out var run)?run.ToString("D"):null,
                Guid.TryParseExact(audit.CorrelationId,"D",out var correlation)?correlation.ToString("D"):null,
                audit.AuthenticationMethod is "WebAuthn" or "None" ? audit.AuthenticationMethod : "Unknown",
                audit.Result is "Started" or "Success" or "Failure" ? audit.Result : "Unknown","LummaSimulator.Web",audit.Simulation);
            writer.Write(audit.EventId,kind is IdentityEvent.AUTHENTICATION_FAILED or IdentityEvent.PASSKEY_REGISTRATION_FAILED,
                JsonSerializer.Serialize(payload,JsonOptions));
        }
        return Task.CompletedTask;
    }
}

// Use native reporting to prevent .NET EventLog.WriteEntry's automatic source creation.
// No source registration, ACL modification, elevation, remote server or secret-bearing raw data.
public sealed class WindowsIdentityEventWriter : IWindowsIdentityEventWriter
{
    public const string Source="LummaSimulator.IdentityAudit";
    public const string Log="Application";
    public void CheckAvailability()
    {
        if(!OperatingSystem.IsWindows()) throw new AuditExportException("UnsupportedPlatform");
        CheckWindowsSource();
    }
    [SupportedOSPlatform("windows")]
    private static void CheckWindowsSource()
    {
        try {
            using var source=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog\Application\"+Source,writable:false);
            if(source is null) throw new AuditExportException("SourceMissing");
        } catch(UnauthorizedAccessException) {throw new AuditExportException("AccessDenied");}
        catch(System.Security.SecurityException) {throw new AuditExportException("AccessDenied");}
    }
    public void Write(int eventId,bool warning,string json)
    {
        if(!OperatingSystem.IsWindows()) throw new AuditExportException("UnsupportedPlatform");
        CheckWindowsSource();WriteWindows(eventId,warning,json);
    }
    [SupportedOSPlatform("windows")]
    private static void WriteWindows(int eventId,bool warning,string json)
    {
        try {
            using var handle=RegisterEventSource(null,Source);
            if(handle.IsInvalid) ThrowNative();
            if(!ReportEvent(handle,warning?(ushort)2:(ushort)4,0,(uint)eventId,IntPtr.Zero,1,0,[json],IntPtr.Zero)) ThrowNative();
        } catch(UnauthorizedAccessException) {throw new AuditExportException("AccessDenied");}
        catch(System.Security.SecurityException) {throw new AuditExportException("AccessDenied");}
    }
    private static void ThrowNative() => throw new AuditExportException(Marshal.GetLastWin32Error()==5 ? "AccessDenied" : "WindowsWriteFailed");
    private sealed class EventSourceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EventSourceHandle() : base(ownsHandle:true) { }
        protected override bool ReleaseHandle() => DeregisterEventSource(handle);
    }
    [DllImport("advapi32.dll",EntryPoint="RegisterEventSourceW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern EventSourceHandle RegisterEventSource(string? server,string source);
    [DllImport("advapi32.dll",EntryPoint="ReportEventW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReportEvent(EventSourceHandle handle,ushort type,ushort category,uint eventId,IntPtr sid,
        ushort count,uint size,[MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=5)] string[] strings,IntPtr rawData);
    [DllImport("advapi32.dll",ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterEventSource(IntPtr handle);
}
