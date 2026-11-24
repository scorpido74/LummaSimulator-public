using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;
using LummaSimulator.App.Models;

internal static class ShutdownChecks
{
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
    public static async Task RunAsync()
    {
        var monitor = new MonitorClient(); var events = new EventClient();
        var vm = Create(monitor, events);
        Task? reentrant = null;
        events.OnDispose = () => reentrant = vm.ShutdownAsync();
        var first = vm.ShutdownAsync(); var second = vm.ShutdownAsync();
        await first;
        Check(ReferenceEquals(first, second) && ReferenceEquals(first, reentrant), "Repeated and disposal-reentrant shutdown share one published task");
        Check(monitor.Disposals == 1 && events.Disposals == 1, "Idle shutdown disposes each client once without starting any activity");
        Check(!vm.StartCommand.CanExecute(null) && !vm.CleanupCommand.CanExecute(null) && !vm.ResetCommand.CanExecute(null), "Shutdown disables new execution, cleanup and reset against disposed clients");
        vm.StartCommand.Execute(null); vm.CleanupCommand.Execute(null); vm.ResetCommand.Execute(null);
        Check(monitor.Disposals == 1 && events.Disposals == 1 && !vm.IsRunning, "Late direct command callbacks cannot restart activity or reset disposed run state");
        var checkpoints = new MemoryLog(); var runClient = new EventClient();
        var running = new MainViewModel(new WaitingSimulation(), checkpoints, sessionMonitor: new(new MonitorClient()), eventEdition: new(runClient));
        var finalCheckpointBeforeDispose = false;
        runClient.OnDispose = () => finalCheckpointBeforeDispose = !running.IsRunning && checkpoints.Statuses.LastOrDefault() == "Stopped";
        running.StartCommand.Execute(null);
        Check(running.IsRunning, "Synthetic in-memory phase is active before shutdown");
        await running.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(finalCheckpointBeforeDispose, "Shutdown awaits the active run and its final log checkpoint before disposing clients");
        var failingMonitor = new MonitorClient { ThrowOnDispose = true }; var remaining = new EventClient();
        var failing = Create(failingMonitor, remaining);
        var failed = failing.ShutdownAsync(); var observed = false;
        try { await failed; } catch (AggregateException) { observed = true; }
        Check(observed && remaining.Disposals == 1, "Monitor disposal failure is observed and does not skip remaining cleanup");
        Check(ReferenceEquals(failed, failing.ShutdownAsync()) && failingMonitor.Disposals == 1, "Failed shutdown is memoized rather than disposing twice");
        var cancelling = new MonitorClient { ThrowOnCancel = true }; var afterCancellation = new EventClient();
        var active = Create(cancelling, afterCancellation);
        active.SessionMonitor.PortalDemoRunId = Guid.NewGuid().ToString("D");
        active.SessionMonitor.RefreshCommand.Execute(null);
        Check(active.SessionMonitor.IsBusy, "Synthetic monitor task is pending before cancellation");
        var cancellationObserved = false;
        try { await active.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { cancellationObserved = true; }
        Check(cancellationObserved && !active.SessionMonitor.IsBusy && cancelling.Disposals == 1 && afterCancellation.Disposals == 1,
            "Throwing cancellation callback still awaits monitor completion and attempts all remaining disposals");
    }
    private static MainViewModel Create(MonitorClient monitor, EventClient events) => new(new Simulation(), new MemoryLog(),
        sessionMonitor: new(monitor, Path.Combine(Path.GetTempPath(), "LummaShutdownChecks", Guid.NewGuid().ToString("N"))), eventEdition: new(events));
    private sealed class Simulation : ISimulationService
    { public Task ExecuteAsync(int phase, Action<string> report, CancellationToken ct) => throw new InvalidOperationException("No simulation should start during this fixture."); }
    private sealed class WaitingSimulation : ISimulationService
    { public Task ExecuteAsync(int phase, Action<string> report, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct); }
    private sealed class MemoryLog : IJsonLogService
    {
        public List<string> Statuses { get; } = [];
        public string DirectoryPath => Path.GetTempPath();
        public Task<string> SaveAsync(string run, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases) { Statuses.Add(status); return Task.FromResult("memory-only"); }
    }
    private sealed class EventClient : IEventEditionClient
    {
        public int Disposals; public Action? OnDispose;
        public Task<EventSnapshot> PollAsync(string run, string code, CancellationToken ct) => throw new InvalidOperationException("No polling in this fixture.");
        public void Dispose() { Disposals++; OnDispose?.Invoke(); }
    }
    private sealed class MonitorClient : ISessionMetadataClient
    {
        public bool ThrowOnDispose, ThrowOnCancel; public int Disposals;
        public bool IsConfigured => true; public string Origin => "https://localhost:7243";
        public async Task<SessionMetadata> FetchAsync(string run, CancellationToken ct)
        {
            var waiting = Task.Delay(Timeout.Infinite, ct);
            // Cancellation callbacks run LIFO: fail before the awaited task can dispose this registration.
            using var registration = ct.Register(() => { if (ThrowOnCancel) throw new InvalidOperationException("Synthetic cancellation failure."); });
            await waiting;
            throw new InvalidOperationException("Synthetic task must be cancelled.");
        }
        public void Dispose() { Disposals++; if (ThrowOnDispose) throw new InvalidOperationException("Synthetic disposal failure."); }
    }
}
