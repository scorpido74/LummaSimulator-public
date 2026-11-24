using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LummaSimulator.App.Models;
namespace LummaSimulator.App.Services;
public interface ISimulationService
{
 Task ExecuteAsync(int phase, Action<string> report, CancellationToken token);
}
public sealed class SimulationService : ISimulationService
{
 private static readonly string[][] Messages =
 [
  ["Fictional user approved training payload SIM-001; no real malicious content.", "Simulated execution complete; no process launched."],
  ["Mock host: TRAINING-PC; mock user: demo-user; mock OS: Windows Demo.", "Fictional inventory ready; no host information queried."],
  ["Mock browsers: Example Browser A and Example Browser B.", "Two imaginary profiles listed; no browser directories accessed."],
  ["Dummy record: demo@example.invalid / FAKE-PASSWORD-001.", "Three fictional credentials prepared; no secrets read."],
  ["Imaginary package SIM-PACKAGE-001 assembled in memory.", "Mock size: 12 KB; no archive or staging files created."],
  ["Simulating a fictional C2-channel delivery to an imaginary in-memory sink.", "Mock acknowledgement: no C2 connection or real exfiltration occurred."]
 ];
 public async Task ExecuteAsync(int phase, Action<string> report, CancellationToken token)
 {
  foreach (var message in Messages[phase - 1]) { await Task.Delay(1100, token); token.ThrowIfCancellationRequested(); report(message); }
 }
}
public interface IJsonLogService
{
 string DirectoryPath { get; }
 Task<string> SaveAsync(string session, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases);
}
public sealed class JsonLogService : IJsonLogService
{
 public string DirectoryPath { get; }
 public JsonLogService(string? directoryPath = null) => DirectoryPath = LummaSimulator.Configuration.RuntimePaths.Logs(directoryPath ?? DesktopSettings.Load().LogDirectory);
 private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
 public async Task<string> SaveAsync(string session, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases)
 {
  Directory.CreateDirectory(DirectoryPath);
  var path = Path.Combine(DirectoryPath, session + ".json");
  var records = entries.ToArray();
  var snapshot = new { version = "0.4", safeSimulation = records.FirstOrDefault()?.Mode != "LIVE TELEMETRY", demoRunId = session, mode = records.FirstOrDefault()?.Mode ?? "SAFE SIMULATION", session, status, edrDetectionConfirmed = phases.Any(p => p.Assessments.Any(a => a.Detection == DetectionState.Detected)), phases = phases.Select(p => new { p.Number, p.Name, p.Status, durationMs = p.Duration.TotalMilliseconds, technique = p.Technique, activity = p.ActivityText, techniqueActivity = p.TechniqueActivityText, assessments = p.Assessments }).ToArray(), entries = records };
  await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(snapshot, Options));
  File.Move(path + ".tmp", path, true);
  return path;
 }
}

