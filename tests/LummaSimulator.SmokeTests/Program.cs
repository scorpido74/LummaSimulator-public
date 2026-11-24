using System.Text.Json;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

static void Check(bool condition, string message)
{
 if (!condition) throw new InvalidOperationException(message);
}
static async Task WaitAsync(MainViewModel vm)
{
 using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
 while (vm.IsRunning) await Task.Delay(20, timeout.Token);
}
if(args.Contains("--local-server-only")) { await LocalServerChecks.RunAsync(); return; }
if(args.Contains("--shutdown-only")) { await ShutdownChecks.RunAsync(); return; }
if(args.Contains("--event-participants-checks")) { await EventParticipantsChecks.RunAsync(); return; }
var logger = new JsonLogService();
var vm = new MainViewModel(new SimulationService(), logger);
Check(vm.Phases.All(p => p.Status == PhaseStatus.Pending), "Initial states");
vm.StartCommand.Execute(null);
Check(!vm.StartCommand.CanExecute(null) && !vm.ResetCommand.CanExecute(null), "Commands disabled during run");
await WaitAsync(vm);
Check(vm.Status == "Completed" && vm.Progress == 100, "Successful completion");
Check(vm.Phases.All(p => p.Status == PhaseStatus.Completed && p.Duration.TotalSeconds >= 2), "Phase results and durations");
var startedPhases = vm.Entries.Where(e => e.Message.StartsWith("Phase ")).Select(e => e.Message[6]).ToArray();
Check(startedPhases.SequenceEqual("123456"), "Sequential phase order");
var path = vm.LogStatus["Saved: ".Length..];
using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
{
 Check(json.RootElement.GetProperty("safeSimulation").GetBoolean(), "Safe marker in JSON");
 Check(json.RootElement.GetProperty("status").GetString() == "Completed", "Final log checkpoint");
 Check(json.RootElement.GetProperty("phases").GetArrayLength() == 6, "Six JSON phases");
}
vm.ResetCommand.Execute(null);
Check(vm.Progress == 0 && vm.Phases.All(p => p.Status == PhaseStatus.Pending && p.Duration == TimeSpan.Zero), "Reset");
Check(File.Exists(path), "Reset preserves logs");
vm.StartCommand.Execute(null);
await Task.Delay(200);
await vm.StopAndWaitAsync();
Check(vm.Status == "Stopped" && vm.Phases[0].Status == PhaseStatus.Failed && vm.Phases.Skip(1).All(p => p.Status == PhaseStatus.Pending), "Cancellation");
Check(vm.StartCommand.CanExecute(null) && !vm.StopCommand.CanExecute(null), "Command recovery");
var failed = new MainViewModel(new BrokenSimulation(), logger);
failed.StartCommand.Execute(null);
await WaitAsync(failed);
Check(failed.Status == "Failed" && failed.Phases[0].Status == PhaseStatus.Failed, "Service failure");
var unavailable = new MainViewModel(new InstantSimulation(), new BrokenLogger());
unavailable.StartCommand.Execute(null);
await WaitAsync(unavailable);
Check(unavailable.Status == "Completed" && unavailable.LogStatus.StartsWith("JSON save failed:"), "Visible logging failure without crash");
Console.WriteLine("PASS: sequential execution, timing, JSON, reset, cancellation, command state, service failure, logging failure.");
Console.WriteLine($"Smoke-test session logs: {logger.DirectoryPath}");
await LocalServerChecks.RunAsync();
await TelemetryChecks.RunAsync();
await NavigatorChecks.RunAsync();
await SessionMonitorChecks.RunAsync();
await EventEditionChecks.RunAsync();
await EventParticipantsChecks.RunAsync();
await MonitorIntegrationChecks.RunAsync();
await ShutdownChecks.RunAsync();
sealed class BrokenSimulation : ISimulationService
{
 public Task ExecuteAsync(int phase, Action<string> report, CancellationToken token) => Task.FromException(new InvalidOperationException("Synthetic test failure"));
}
sealed class InstantSimulation : ISimulationService
{
 public Task ExecuteAsync(int phase, Action<string> report, CancellationToken token) => Task.CompletedTask;
}
sealed class BrokenLogger : IJsonLogService
{
 public string DirectoryPath => "Synthetic unavailable destination";
 public Task<string> SaveAsync(string session, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases) => Task.FromException<string>(new IOException("Synthetic write failure"));
}
