using System.IO;
using System.Text.Json;
using LummaSimulator.App.Models;

namespace LummaSimulator.App.Services;

public sealed class NavigatorExportService
{
    public static (int Score, string Color, string Label) Appearance(SimulationPhase phase)
    {
        if (phase.Assessments.Any(a => a.Detection == DetectionState.Detected)) return (100, "#3a9d5d", "Detected (presenter confirmed)");
        if (phase.Assessments.Any(a => a.Observation == ObservationState.Observed)) return (50, "#5599dd", "Observed (presenter confirmed)");
        if (phase.Assessments.Any(a => a.Detection == DetectionState.NotDetected)) return (25, "#efaa55", "Not detected (presenter reviewed)");
        return phase.TechniqueActivity switch
        {
            ActivityState.Executed => (15, "#49c8bc", "Executed (controlled technique activity)"),
            ActivityState.Simulated => (5, "#b79ae0", "Simulated technique"),
            _ => (0, "#b5bcc5", "Not evaluated")
        };
    }

    public static string CreateLayer(string demoRunId, IEnumerable<SimulationPhase> phases, string? portalRunId = null, string? simulatorRunId = null)
    {
        if (!Guid.TryParseExact(demoRunId, "D", out var run)) throw new ArgumentException("A valid DemoRunId is required.", nameof(demoRunId));
        static object Meta(string name, string value) => new { name, value };
        var phaseArray = phases.ToArray();
        var layer = new
        {
            name = "LummaSimulator controlled training",
            versions = new { attack = AttackCatalog.AttackVersion, navigator = AttackCatalog.NavigatorVersion, layer = AttackCatalog.LayerVersion },
            domain = "enterprise-attack",
            description = "Controlled synthetic-data training. Scores represent reported activity/evidence, not detection coverage. No automatic detection claims.",
            filters = new { platforms = phaseArray.Any(p => p.TechniqueId == "T1550.004") ? new[] { "Windows", "SaaS" } : new[] { "Windows" } },
            sorting = 0,
            layout = new { layout = "side", showID = true, showName = true, expandedSubtechniques = "annotated" },
            hideDisabled = false,
            techniques = phaseArray.Select(phase =>
            {
                var appearance = Appearance(phase);
                return new
                {
                    techniqueID = phase.Technique.Id,
                    tactic = phase.Technique.Tactic,
                    enabled = true,
                    score = appearance.Score,
                    color = appearance.Color,
                    comment = phase.Technique.ActivityDescription + " " + phase.MappingNote,
                    metadata = new[]
                    {
                        Meta("DemoRunId", run.ToString("D")), Meta("Activity", phase.ActivityText), Meta("Technique activity", phase.TechniqueActivityText),
                        Meta("Phase status", phase.StatusText), Meta("Duration ms", phase.Duration.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                        Meta("Activity timestamp", phase.ActivityAt?.ToString("O") ?? "Not evaluated")
                    }.Concat(phase.Assessments.SelectMany(a => new[]
                    {
                        Meta(a.Product + " observation", AttackCatalog.Label(a.Observation)),
                        Meta(a.Product + " detection", AttackCatalog.Label(a.Detection)),
                        Meta(a.Product + " confirmation", a.ConfirmationSource),
                        Meta(a.Product + " confirmed at", a.ConfirmedAt?.ToString("O") ?? "Not evaluated")
                    })).ToArray()
                };
            }).ToArray(),
            gradient = new { colors = new[] { "#b5bcc5", "#3a9d5d" }, minValue = 0, maxValue = 100 },
            legendItems = new[]
            {
                new { label = "Detected: explicit presenter confirmation", color = "#3a9d5d" },
                new { label = "Observed: explicit presenter confirmation", color = "#5599dd" },
                new { label = "Not detected: explicit product review", color = "#efaa55" },
                new { label = "Executed technique: synthetic training activity", color = "#49c8bc" },
                new { label = "Simulated technique", color = "#b79ae0" },
                new { label = "Not evaluated", color = "#b5bcc5" }
            },
            selectTechniquesAcrossTactics = false,
            selectSubtechniquesWithParent = false,
            metadata = new[] { Meta("DemoRunId", run.ToString("D")), Meta("Product status", "Per-product explicit manual assessments; defaults are Not evaluated"), Meta("Source checked", "2026-10-29"), Meta("Application version", "0.4"),
                Meta("Portal DemoRunId", Guid.TryParse(portalRunId, out var portal) ? portal.ToString("D") : "Not linked"),
                Meta("Simulator DemoRunId", Guid.TryParse(simulatorRunId, out var simulator) ? simulator.ToString("D") : "Not linked"),
                Meta("Scope", "Private training layer, not publication in the MITRE ATT&CK knowledge base") }
        };
        // Strict allowlist DTO: no raw logs, paths, process output, account names or free-text evidence.
        return JsonSerializer.Serialize(layer, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> ExportAsync(string directory, string demoRunId, IEnumerable<SimulationPhase> phases, string? portalRunId = null, string? simulatorRunId = null)
    {
        var json = CreateLayer(demoRunId, phases, portalRunId, simulatorRunId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Lumma-Demo-{DateTime.UtcNow:yyyyMMdd}-{Guid.Parse(demoRunId):D}-{Guid.NewGuid():N}-ATTACK.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
