using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using LummaSimulator.App;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

internal static class WindowShutdownChecks
{
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
    public static async Task RunAsync()
    {
        var vm = Create(); var calls = 0; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new MainWindow(vm, () => { calls++; return Task.CompletedTask; });
        var closingEvents = 0; window.Closing += (_, _) => closingEvents++; window.Closed += (_, _) => closed.TrySetResult();
        window.Show(); vm.EventEdition.SetRun(Guid.NewGuid().ToString("D"));
        var password = (PasswordBox)window.FindName("EventPairingCode"); password.Password = "synthetic-marker";
        window.Close(); window.Close(); window.Close();
        Check(calls == 0 && window.IsVisible, "Synchronous shutdown is unconditionally deferred outside the original Closing event");
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(calls == 1 && closingEvents == 4 && !window.IsVisible, "Three close requests perform one shutdown and one final non-reentrant close");
        vm.EventEdition.SetRun(Guid.NewGuid().ToString("D"));
        Check(password.Password == "synthetic-marker", "Closed window no longer receives model run-change notifications");
        await vm.ShutdownAsync();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var asyncCalls = 0;
        var asyncVm = Create(); var asyncClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asyncWindow = new MainWindow(asyncVm, () => { asyncCalls++; return pending.Task; });
        asyncWindow.Closed += (_, _) => asyncClosed.TrySetResult(); asyncWindow.Show(); asyncWindow.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        asyncWindow.Close(); asyncWindow.Close();
        Check(asyncCalls == 1 && asyncWindow.IsVisible, "Repeated close requests wait for the same incomplete shutdown");
        pending.TrySetResult(); await asyncClosed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await asyncVm.ShutdownAsync();
        var nestedVm = Create(); var nestedCalls = 0; var nestedClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nested = new MainWindow(nestedVm, () => { nestedCalls++; return Task.CompletedTask; }); var firstClosing = true;
        nested.Closing += (_, _) => {
            if (!firstClosing) return; firstClosing = false;
            var frame = new DispatcherFrame();
            nested.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Check(nested.IsVisible, "Final Close remains pending throughout another subscriber's nested Closing frame");
        };
        nested.Closed += (_, _) => nestedClosed.TrySetResult(); nested.Show(); nested.Close();
        await nestedClosed.Task.WaitAsync(TimeSpan.FromSeconds(5)); Check(nestedCalls == 1, "Nested dispatcher pumping does not re-enter shutdown"); await nestedVm.ShutdownAsync();
        foreach (var synchronous in new[] { true, false })
        {
            var failedVm = Create(); var failures = 0; var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new MainWindow(failedVm, () => { failures++; if (synchronous) throw new InvalidOperationException("Synthetic shutdown failure."); return completion.Task; });
            failed.Show(); failed.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!synchronous) completion.TrySetException(new InvalidOperationException("Synthetic asynchronous shutdown failure."));
            await failed.ShutdownCompletion!.WaitAsync(TimeSpan.FromSeconds(5)); failed.Close();
            Check(failures == 1 && failed.IsVisible && !string.IsNullOrEmpty(failed.ShutdownError), "Shutdown exception is observed; window stays open and does not retry disposal");
            // Retain until the fixture's Application.Shutdown; no force-close path in production.
            failed.Hide(); await failedVm.ShutdownAsync();
        }
        var idleVm = Create(); var idleClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idle = new MainWindow(idleVm); idle.Closed += (_, _) => idleClosed.TrySetResult(); idle.Show(); idle.Close();
        await idleClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(idleVm.ShutdownAsync().IsCompletedSuccessfully, "Actual idle MainViewModel shutdown closes without the original synchronous-completion crash");
    }
    private static MainViewModel Create() => new(new SimulationService(), new MemoryLog(), sessionMonitor: new(new MonitorClient()), eventEdition: new(new EventClient()));
    private sealed class MemoryLog : IJsonLogService
    {
        public string DirectoryPath => Path.GetTempPath();
        public Task<string> SaveAsync(string run, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases) => Task.FromResult("memory-only");
    }
    private sealed class MonitorClient : ISessionMetadataClient
    {
        public bool IsConfigured => false; public string Origin => "https://localhost:7243";
        public Task<SessionMetadata> FetchAsync(string run, CancellationToken ct) => throw new InvalidOperationException("No HTTP in this fixture.");
        public void Dispose() { }
    }
    private sealed class EventClient : IEventEditionClient
    {
        public Task<EventSnapshot> PollAsync(string run, string code, CancellationToken ct) => throw new InvalidOperationException("No HTTP in this fixture.");
        public void Dispose() { }
    }
}
