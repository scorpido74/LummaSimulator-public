using System.Text.Json;
using System.Text.RegularExpressions;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;

internal static class NavigatorChecks
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        _checks++;
    }
    public static async Task RunAsync()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "LummaSimulator.slnx"))) repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("Repository root not found.");
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repo.FullName, "docs/references/enterprise-attack-20.0-subset.json")));
        var official = data.RootElement.GetProperty("techniques").EnumerateArray().ToDictionary(t => t.GetProperty("id").GetString()!);
        var format = await File.ReadAllTextAsync(Path.Combine(repo.FullName, "docs/references/navigator-layer-format-v4.5.md"));
        Check(format.Contains("**Version 4.5**"), "Pinned official format version");
        Check(data.RootElement.GetProperty("attackVersion").GetString() == AttackCatalog.AttackVersion, "Dataset version matches reference");
        var phases = Enumerable.Range(1, 6).Select(i => new SimulationPhase(i, "PRIVATE-USER-DO-NOT-EXPORT", "C:\\sensitive\\profile")).ToArray();
        foreach (var live in new[] { false, true })
        {
            foreach (var phase in phases)
            {
                phase.Prepare(live);
                var reference = official[phase.TechniqueId];
                Check(reference.GetProperty("name").GetString() == phase.TechniqueName, "Official technique name");
                Check(reference.GetProperty("tactics").EnumerateArray().Any(t => t.GetString() == phase.Technique.Tactic), "Official tactic membership");
                Check(!reference.GetProperty("revoked").GetBoolean() && !reference.GetProperty("deprecated").GetBoolean(), "Mapping is not deprecated or revoked");
                Check(reference.GetProperty("platforms").EnumerateArray().Any(p => p.GetString() == "Windows"), "Windows technique applicability");
                phase.RecordActivity(live && phase.Number != 2 ? ActivityState.Executed : ActivityState.Simulated);
                phase.Status = PhaseStatus.Completed;
                phase.Duration = TimeSpan.FromMilliseconds(1200);
                Check(phase.Assessments.All(a => a.Detection == DetectionState.NotEvaluated && a.Observation == ObservationState.NotEvaluated), "Completion never implies observation/detection");
            }
            Check(phases[0].TechniqueId == (live ? "T1033" : "T1204"), "whoami uses T1033, not T1204");
            Check(phases[4].TechniqueId == "T1074.001", "Local staging uses the precise sub-technique");
            Check(phases[5].TechniqueActivity == ActivityState.Simulated, "Loopback transport never makes T1041 executed");
            if (live)
            {
                Check(phases[2].Activity == ActivityState.Executed && phases[2].TechniqueActivity == ActivityState.Simulated, "Real file creation is not real browser discovery");
                Check(phases[3].Activity == ActivityState.Executed && phases[3].TechniqueActivity == ActivityState.Simulated, "Reading dummy credentials is not real credential theft");
                Check(phases[0].TechniqueActivity == ActivityState.Executed && phases[4].TechniqueActivity == ActivityState.Executed, "Controlled discovery and dummy local staging execution");
            }
            var json = NavigatorExportService.CreateLayer(Guid.NewGuid().ToString("D"), phases);
            Validate(json, format, official);
            Check(!json.Contains("PRIVATE-USER") && !json.Contains("sensitive") && !json.Contains("FAKE-PASSWORD-ONLY"), "Export does not copy names, paths or raw credential records");
            using var layer = JsonDocument.Parse(json);
            Check(layer.RootElement.GetProperty("techniques").EnumerateArray().All(t => t.GetProperty("score").GetInt32() < 100), "No automatic detection score");
        }
        var guarded = false;
        try { phases[0].ConfirmAssessment(SecurityProducts.All[0], AssessmentKind.Detected, false); }
        catch (InvalidOperationException) { guarded = true; }
        Check(guarded, "Presenter confirmation is mandatory");
        phases[0].ConfirmAssessment(SecurityProducts.All[0], AssessmentKind.Observed, true);
        Check(phases[0].Assessments[0].Observation == ObservationState.Observed && phases[0].Assessments[0].Detection == DetectionState.NotEvaluated, "Observed does not mean Detected");
        phases[1].ConfirmAssessment(SecurityProducts.All[0], AssessmentKind.NotDetected, true);
        Check(phases[1].Assessments[0].Detection == DetectionState.NotDetected && phases[1].Assessments[0].Observation == ObservationState.NotEvaluated, "Not detected requires review, does not imply observation");
        phases[2].ConfirmAssessment(SecurityProducts.All[1], AssessmentKind.Detected, true);
        var run = Guid.NewGuid().ToString("D");
        var confirmedJson = NavigatorExportService.CreateLayer(run, phases);
        Validate(confirmedJson, format, official);
        using (var confirmed = JsonDocument.Parse(confirmedJson))
        {
            var technique = confirmed.RootElement.GetProperty("techniques")[2];
            Check(technique.GetProperty("score").GetInt32() == 100 && technique.GetProperty("color").GetString() == "#3a9d5d", "Confirmed detection has explicit score/color");
            var metadata = technique.GetProperty("metadata").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("value").GetString());
            Check(metadata["WithSecure Elements EDR detection"] == "Detected" && metadata["Rapid7 InsightIDR detection"] == "Not evaluated", "Export preserves per-product status without cross-product inference");
            Check(metadata["DemoRunId"] == run && metadata["WithSecure Elements EDR confirmation"]!.Contains("Presenter"), "Export records run and source of confirmation");
        }
        var path = await new NavigatorExportService().ExportAsync(Path.Combine(Path.GetTempPath(), "LummaNavigatorTests"), run, phases);
        Validate(await File.ReadAllTextAsync(path), format, official);
        File.Delete(path);
        phases[2].Prepare(false);
        Check(phases[2].Assessments.All(a => a.Detection == DetectionState.NotEvaluated), "Reset clears product confirmations");
        var sessionTechniques = new[] { new SimulationPhase(7, "Exposure", ""), new SimulationPhase(8, "Replay", "") };
        foreach (var sessionTechnique in sessionTechniques)
        {
            sessionTechnique.RecordActivity(ActivityState.Simulated); sessionTechnique.Status = PhaseStatus.Completed;
            Check(official[sessionTechnique.TechniqueId].GetProperty("name").GetString() == sessionTechnique.TechniqueName, "Official session technique name");
            Check(official[sessionTechnique.TechniqueId].GetProperty("tactics").EnumerateArray().Any(t => t.GetString() == sessionTechnique.Technique.Tactic), "Official session technique tactic");
            Check(sessionTechnique.TechniqueActivity == ActivityState.Simulated, "Cookie techniques are simulation-only");
        }
        var sessionLayer = NavigatorExportService.CreateLayer(run, phases.Concat(sessionTechniques), run, Guid.NewGuid().ToString("D"));
        Validate(sessionLayer, format, official, 8);
        Console.WriteLine($"PASS: {_checks} ATT&CK mapping/status/export assertions against Enterprise 20.0 and the pinned official Navigator 4.5 format.");
    }

    private static HashSet<string> Fields(string markdown, string heading)
    {
        var start = markdown.IndexOf("## " + heading, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Official specification section missing: " + heading);
        var end = markdown.IndexOf("\n## ", start + 4, StringComparison.Ordinal);
        var section = end < 0 ? markdown[start..] : markdown[start..end];
        return section.Split('\n').Where(l => l.StartsWith('|')).Select(l => l.Split('|')[1].Trim())
            .Where(name => name != "Name" && !name.StartsWith(':')).ToHashSet(StringComparer.Ordinal);
    }
    private static void Shape(JsonElement obj, HashSet<string> allowed)
    {
        Check(obj.ValueKind == JsonValueKind.Object, "Format object type");
        foreach (var property in obj.EnumerateObject()) Check(allowed.Contains(property.Name), "Only officially defined properties: " + property.Name);
    }
    private static void Validate(string json, string spec, Dictionary<string, JsonElement> official, int expectedTechniques = 6)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Shape(root, Fields(spec, "Property Table"));
        Check(root.GetProperty("name").ValueKind == JsonValueKind.String && root.GetProperty("domain").GetString() == "enterprise-attack", "Required name/domain");
        var versions = root.GetProperty("versions");
        Shape(versions, Fields(spec, "Version Object Properties"));
        Check(versions.GetProperty("layer").GetString() == "4.5" && versions.GetProperty("attack").GetString() == "20.0" && Version.Parse(versions.GetProperty("navigator").GetString()!) >= new Version(4, 9, 0), "Official version constraints");
        Check(root.GetProperty("sorting").GetInt32() is >= 0 and <= 3 && root.GetProperty("hideDisabled").ValueKind == JsonValueKind.False, "Sorting and boolean types");
        Shape(root.GetProperty("layout"), Fields(spec, "Layout Object properties"));
        Check(root.GetProperty("layout").GetProperty("expandedSubtechniques").GetString() == "annotated", "Sub-technique visibility is a legal format enum");
        Shape(root.GetProperty("filters"), Fields(spec, "Filter Object Properties"));
        Check(root.GetProperty("filters").GetProperty("platforms").EnumerateArray().All(p => p.GetString() is "Windows" or "SaaS"), "Official platform values include session replay's SaaS platform when needed");
        var techniques = root.GetProperty("techniques");
        Check(techniques.GetArrayLength() == expectedTechniques, "Correct number of applicable technique entries");
        foreach (var technique in techniques.EnumerateArray())
        {
            Shape(technique, Fields(spec, "Technique Object properties"));
            var id = technique.GetProperty("techniqueID").GetString()!;
            Check(Regex.IsMatch(id, @"^T\d{4}(\.\d{3})?$") && official.ContainsKey(id), "Valid technique ID in the specified dataset");
            Check(official[id].GetProperty("tactics").EnumerateArray().Any(t => t.GetString() == technique.GetProperty("tactic").GetString()), "Technique/tactic pair exists");
            Check(technique.GetProperty("score").ValueKind == JsonValueKind.Number && technique.GetProperty("score").GetDouble() is >= 0 and <= 100, "Numeric score");
            Check(Regex.IsMatch(technique.GetProperty("color").GetString()!, "^#[0-9a-fA-F]{6}$"), "RGB color");
            Check(technique.GetProperty("comment").ValueKind == JsonValueKind.String && technique.GetProperty("enabled").ValueKind == JsonValueKind.True, "Technique comment/enabled types");
            Metadata(technique.GetProperty("metadata"), spec);
        }
        Metadata(root.GetProperty("metadata"), spec);
        var gradient = root.GetProperty("gradient"); Shape(gradient, Fields(spec, "Gradient Object properties"));
        Check(gradient.GetProperty("colors").GetArrayLength() >= 2 && gradient.GetProperty("minValue").GetDouble() < gradient.GetProperty("maxValue").GetDouble(), "Gradient required values");
        foreach (var legend in root.GetProperty("legendItems").EnumerateArray())
        {
            Shape(legend, Fields(spec, "LegendItem Object properties"));
            Check(legend.GetProperty("label").ValueKind == JsonValueKind.String && Regex.IsMatch(legend.GetProperty("color").GetString()!, "^#[0-9a-fA-F]{6}$"), "Legend required strings/colors");
        }
    }
    private static void Metadata(JsonElement metadata, string spec)
    {
        Check(metadata.ValueKind == JsonValueKind.Array, "Metadata must be an array");
        foreach (var item in metadata.EnumerateArray())
        {
            Shape(item, Fields(spec, "Metadata Object properties"));
            Check(item.GetProperty("name").ValueKind == JsonValueKind.String && item.GetProperty("value").ValueKind == JsonValueKind.String, "Metadata values must be strings, not nested objects");
        }
    }
}
