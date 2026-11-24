using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using LummaSimulator.LocalTesting;

namespace LummaSimulator.App.Services;

public enum LocalServerState { Disabled, Starting, Ready, Error }

/// <summary>Only this instance's retained Process handle grants permission to stop a server.</summary>
public sealed class LocalTestServerManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly object _sync=new();
    private readonly System.Net.Http.HttpClient _http=LocalTestServerProtocol.CreateClient();
    private CancellationTokenSource? _request;
    private Process? _owned;
    private Task _monitor=Task.CompletedTask;
    private int _revision;
    private bool _disposed;
    private Task? _shutdown;
    public LocalServerState State { get; private set; }
    public string? Error { get; private set; }
    public int Port { get; private set; }=8080;
    public int? OwnedProcessId => _owned?.Id;
    public event Action? Changed;
    public void ReportPostFailure()
    { lock(_sync) {if(State==LocalServerState.Ready)SetState(LocalServerState.Error,_revision,"ServerUnavailable");} }

    public Task ConfigureAsync(bool enabled,int port)
    {
        lock(_sync) {
            if(_disposed)return Task.CompletedTask;
            var revision=++_revision;
            _request?.Cancel();var request=new CancellationTokenSource();_request=request;
            SetState(enabled?LocalServerState.Starting:LocalServerState.Disabled,revision);
            return ApplyAsync(enabled,port,revision,request);
        }
    }
    private void SetState(LocalServerState state,int revision,string? error=null)
    { lock(_sync) {if(revision!=_revision)return;State=state;Error=error;} Changed?.Invoke(); }

    private async Task ApplyAsync(bool enabled,int port,int revision,CancellationTokenSource request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if(request.IsCancellationRequested)return;
            await StopOwnedAsync().ConfigureAwait(false);
            if(!enabled)return;
            if(port is <1 or >65535)throw new IOException("InvalidPort");
            Port=port;var token=request.Token;
            var identity=await LocalTestServerProtocol.ProbeAsync(_http,port,token).ConfigureAwait(false);
            if(identity is null) {
                using(var socket=new TcpClient()) {
                    try {await socket.ConnectAsync(IPAddress.Loopback,port,token).ConfigureAwait(false);throw new IOException("PortConflict");}
                    catch(SocketException) { }
                }
                token.ThrowIfCancellationRequested();
                var start=StartInfo(port);
                _owned=Process.Start(start) ?? throw new IOException("ServerStartFailed");
                _owned.Exited+=(_,_)=>{if(!token.IsCancellationRequested)SetState(LocalServerState.Error,revision,"ServerUnavailable");};
                _owned.EnableRaisingEvents=true;
                _owned.BeginOutputReadLine();_owned.BeginErrorReadLine();
                using var ready=CancellationTokenSource.CreateLinkedTokenSource(token);ready.CancelAfter(TimeSpan.FromSeconds(10));
                while(true) {
                    ready.Token.ThrowIfCancellationRequested();
                    if(_owned.HasExited)throw new IOException("ServerStartFailed");
                    identity=await LocalTestServerProtocol.ProbeAsync(_http,port,ready.Token).ConfigureAwait(false);
                    if(identity is not null && identity.Pid==_owned.Id)break;
                    await Task.Delay(100,ready.Token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            if(_owned?.HasExited==true)throw new IOException("ServerUnavailable");
            SetState(LocalServerState.Ready,revision);
            _monitor=MonitorAsync(identity,revision,token);
        } catch(OperationCanceledException) when(request.IsCancellationRequested) {await StopOwnedAsync().ConfigureAwait(false);}
        catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or SocketException) {
            await StopOwnedAsync().ConfigureAwait(false);SetState(LocalServerState.Error,revision,e.Message);
        } finally {if(request.IsCancellationRequested)request.Dispose();_gate.Release();}
    }
    private async Task MonitorAsync(LocalServerIdentity expected,int revision,CancellationToken token)
    {
        try {
            while(true) {
                await Task.Delay(750,token).ConfigureAwait(false);
                var current=await LocalTestServerProtocol.ProbeAsync(_http,expected.Port,token).ConfigureAwait(false);
                if(current is null || current.Instance!=expected.Instance || current.Pid!=expected.Pid) {
                    SetState(LocalServerState.Error,revision,"ServerUnavailable");return;
                }
            }
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }
    public static ProcessStartInfo StartInfo(int port)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"LocalTestServer");
        var exe=Path.Combine(directory,"LummaSimulator.LocalTestServer.exe");
        if(!File.Exists(exe))throw new IOException("ServerFilesMissing");
        var info=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            WorkingDirectory=directory,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        // The sidecar serves synthetic files only and never needs monitor credentials/control.
        info.Environment.Remove("LUMMA_MONITOR_KEY"); info.Environment.Remove("Monitor__KeyHash"); info.Environment.Remove("LUMMA_PORTAL_STOP_FILE");
        info.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));info.ArgumentList.Add("--managed");return info;
    }
    private async Task StopOwnedAsync()
    {
        // A previous monitor is cancelled by Configure/Dispose before entering this gate.
        await _monitor.ConfigureAwait(false);_monitor=Task.CompletedTask;
        var process=_owned;if(process is null)return;
        try {
            if(!process.HasExited) {
                try {await process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);process.StandardInput.Close();}
                catch(IOException) { }
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try {await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);}
                catch(OperationCanceledException) {if(!process.HasExited)process.Kill();await process.WaitForExitAsync().ConfigureAwait(false);}
            }
        } finally {process.Dispose();_owned=null;}
    }
    public ValueTask DisposeAsync()
    {
        lock(_sync) {
            if(_shutdown is not null)return new(_shutdown);
            _disposed=true;var revision=++_revision;_request?.Cancel();
            _shutdown=ShutdownAsync(revision);return new(_shutdown);
        }
    }
    private async Task ShutdownAsync(int revision)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {await StopOwnedAsync().ConfigureAwait(false);SetState(LocalServerState.Disabled,revision);_http.Dispose();_request?.Dispose();}
        finally {_gate.Release();}
    }
}
