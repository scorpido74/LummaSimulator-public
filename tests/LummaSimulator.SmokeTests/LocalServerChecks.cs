using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;
using LummaSimulator.LocalTesting;

internal static class LocalServerChecks
{
    private static int _checks;
    private static void Check(bool value,string name) {if(!value)throw new InvalidOperationException(name);_checks++;Console.WriteLine("PASS: Local receiver: "+name);}
    internal static int FreePort() {using var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();return ((IPEndPoint)probe.LocalEndpoint).Port;}
    private static async Task Until(Func<bool> predicate) {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));while(!predicate())await Task.Delay(30,timeout.Token);}
    private static bool Exited(int pid) {try {using var p=Process.GetProcessById(pid);return p.HasExited;}catch(ArgumentException){return true;}}
    internal static async Task RunAsync()
    {
        var port=FreePort();await using var manager=new LocalTestServerManager();
        await manager.ConfigureAsync(true,port);
        Check(manager.State==LocalServerState.Ready && manager.OwnedProcessId is not null,"automatic start waits for actual health readiness");
        var pid=manager.OwnedProcessId!.Value;
        using var http=LocalTestServerProtocol.CreateClient();
        var identity=await LocalTestServerProtocol.ProbeAsync(http,port,default);
        Check(identity?.Pid==pid,"identity endpoint proves protocol, fresh nonce, process and port");
        await using(var borrower=new LocalTestServerManager()) {
            await borrower.ConfigureAsync(true,port);
            Check(borrower.State==LocalServerState.Ready && borrower.OwnedProcessId is null,"existing identified receiver is reused without adopting process ownership");
            await borrower.ConfigureAsync(false,port);
            Check(!Exited(pid) && await LocalTestServerProtocol.ProbeAsync(http,port,default) is not null,"unchecking a borrower leaves another instance's receiver running");
        }
        Check(!Exited(pid),"disposing a borrower leaves the external receiver running");
        await manager.ConfigureAsync(false,port);
        Check(manager.State==LocalServerState.Disabled && Exited(pid),"unchecking stops and reaps only the owned process");
        for(var i=0;i<3;i++) {
            var start=manager.ConfigureAsync(true,port);var off=manager.ConfigureAsync(false,port);var restart=manager.ConfigureAsync(true,port);
            await Task.WhenAll(start,off,restart);
            Check(manager.State==LocalServerState.Ready,"rapid on/off/on converges to one ready server, iteration "+i);
            pid=manager.OwnedProcessId!.Value;await manager.ConfigureAsync(false,port);Check(Exited(pid),"repeated stop leaves no child process, iteration "+i);
        }
        using(var unknown=new TcpListener(IPAddress.Loopback,0)) {
            unknown.Start();var unknownPort=((IPEndPoint)unknown.LocalEndpoint).Port;
            using var done=new CancellationTokenSource();var unknownRequests=RejectUnknownAsync(unknown,done.Token);
            await manager.ConfigureAsync(true,unknownPort);
            Check(manager.State==LocalServerState.Error && manager.Error=="PortConflict" && manager.OwnedProcessId is null,"unknown occupied port is rejected without starting or killing a process");
            await manager.ConfigureAsync(false,unknownPort);
            using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,unknownPort);
            Check(client.Connected,"unknown service is still listening after disable");done.Cancel();await unknownRequests;
        }
        await manager.ConfigureAsync(true,port);pid=manager.OwnedProcessId!.Value;
        await using(var observer=new LocalTestServerManager()) {
            await observer.ConfigureAsync(true,port);
            using(var process=Process.GetProcessById(pid)) {process.Kill();await process.WaitForExitAsync();}
            await Until(()=>manager.State==LocalServerState.Error && observer.State==LocalServerState.Error);
            Check(observer.OwnedProcessId is null && observer.State==LocalServerState.Error,"health monitoring also detects failure of a borrowed receiver");
        }
        Check(manager.State==LocalServerState.Error,"unexpected server exit removes operational status");
        await manager.ConfigureAsync(false,port);

        var vm=new MainViewModel(new SimulationService(),new JsonLogService(),localServer:manager) {IsLiveTelemetry=true,LocalPort=port.ToString()};
        vm.EnableLocalHttp=true;
        Check(!vm.LocalHttpReady && !vm.StartCommand.CanExecute(null),"checked request is not operational while starting");
        await vm.LocalHttpUpdate;Check(vm.LocalHttpReady && vm.StartCommand.CanExecute(null),"readiness enables the live run");pid=manager.OwnedProcessId!.Value;
        var culture=CultureInfo.CurrentUICulture;try {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("nl-NL");Check(vm.LocalHttpStatus==$"Actief – 127.0.0.1:{port}","Dutch status resource");
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en-GB");Check(vm.LocalHttpStatus==$"Active – 127.0.0.1:{port}","English status resource");
        }finally {CultureInfo.CurrentUICulture=culture;}
        vm.StartCommand.Execute(null);
        using(var child=Process.GetProcessById(pid)) {child.Kill();await child.WaitForExitAsync();}
        await Until(()=>!vm.IsRunning);
        Check(vm.Status=="Failed" && vm.Phases[5].Status==LummaSimulator.App.Models.PhaseStatus.Failed
            && vm.Entries.Any(e=>e.Activity=="HttpPost" && e.Outcome=="Failed") && !vm.LocalHttpReady,
            "server failure during a run fails the actual network phase and cannot report active HTTP telemetry");
        vm.EnableLocalHttp=false;await vm.LocalHttpUpdate;vm.EnableLocalHttp=true;await vm.LocalHttpUpdate;
        pid=manager.OwnedProcessId!.Value;
        await vm.ShutdownAsync();Check(Exited(pid) && !vm.LocalHttpReady,"application shutdown stops and reaps its receiver even while idle");
        // Closing during startup also waits for any partial child creation to be cleaned up.
        await using var startup=new LocalTestServerManager();var pending=startup.ConfigureAsync(true,FreePort());
        await Task.WhenAll(startup.DisposeAsync().AsTask(),startup.DisposeAsync().AsTask());await pending;Check(startup.OwnedProcessId is null && startup.State==LocalServerState.Disabled,"shutdown racing startup cannot leave a process behind");
        using(var leased=Process.Start(LocalTestServerManager.StartInfo(FreePort()))!) {
            leased.BeginOutputReadLine();leased.BeginErrorReadLine();
            leased.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try {await leased.WaitForExitAsync(timeout.Token);}finally {if(!leased.HasExited){leased.Kill();await leased.WaitForExitAsync();}}
            Check(leased.ExitCode==0,"closed owner pipe stops the managed receiver without a shutdown HTTP endpoint");
        }
        Console.WriteLine($"PASS: {_checks} local receiver lifecycle checks.");
    }
    private static async Task RejectUnknownAsync(TcpListener listener,CancellationToken token)
    {
        try {while(true) {using var client=await listener.AcceptTcpClientAsync(token);await using var stream=client.GetStream();
            var buffer=new byte[4096];if(await stream.ReadAsync(buffer,token)>0)await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"),token);}}
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        // Windows can surface cancellation of NetworkStream.ReadAsync as IOException/SocketException.
        // Ignore only fixture teardown cancellation; errors during the assertions still fail the suite.
        catch(IOException) when(token.IsCancellationRequested) { }
    }
}
