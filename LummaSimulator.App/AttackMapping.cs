using System.Text.Json;
using LummaSimulator.App.Infrastructure;

namespace LummaSimulator.App.Models;

public enum ActivityState { NotEvaluated, Simulated, Executed }
public enum ObservationState { NotEvaluated, Observed }
public enum DetectionState { NotEvaluated, Detected, NotDetected }
public enum AssessmentKind { Observed, Detected, NotDetected, NotEvaluated }
public static class SecurityProducts
{
    public static IReadOnlyList<string> All { get; } = ["Rapid7 InsightIDR", "WithSecure Elements EDR"];
}
public sealed record ProductAssessment(string Product, ObservationState Observation, DetectionState Detection,
    DateTimeOffset? ConfirmedAt = null, string ConfirmationSource = "Not evaluated");
public sealed record AttackTechnique(string Id, string Name, string Tactic, string TacticName, string ActivityDescription,
    string MappingNote, bool CanExecuteTechnique);

public static class AttackCatalog
{
    public const string AttackVersion = "20.0";
    public const string NavigatorVersion = "5.3.2";
    public const string LayerVersion = "4.5";
    public static AttackTechnique ForPhase(int phase, bool live) => phase switch
    {
        1 when live => new("T1033", "System Owner/User Discovery", "discovery", "Discovery",
            "Visible whoami.exe queries the current Windows identity; output is not captured.",
            "Controlled identity query, not User Execution or malicious code delivery.", true),
        1 => new("T1204", "User Execution", "execution", "Execution",
            "Fictional user-approved training payload; no process or malicious content is launched.",
            "Educational scenario only; pressing Start does not itself demonstrate adversarial User Execution.", false),
        2 => new("T1082", "System Information Discovery", "discovery", "Discovery",
            "Display a predefined fictional system inventory; no real inventory query.", "Technique remains simulated in both modes.", false),
        3 => new("T1217", "Browser Information Discovery", "discovery", "Discovery",
            live ? "Create an owned synthetic browser inventory JSON; no real browser profiles accessed." : "List fictional browsers and profiles.",
            "Browser discovery is simulated. Creating a dummy file is real telemetry, not discovery of a real browser.", false),
        4 => new("T1555.003", "Credentials from Web Browsers", "credential-access", "Credential Access",
            live ? "Read only the owned dummy browser JSON." : "Generate fictional credential records in memory.",
            "Credential access remains simulated; no browser secrets, cookies or tokens are collected.", false),
        5 => new("T1074.001", "Local Data Staging", "collection", "Collection",
            live ? "Write collected-data.json containing only fixed dummy records." : "Simulate an imaginary local package.",
            "Local staging refines parent T1074. Any executed staging uses synthetic records only.", live),
        6 => new("T1041", "Exfiltration Over C2 Channel", "exfiltration", "Exfiltration",
            "Simulate C2-channel delivery; optional loopback HTTP provides local transport telemetry only.",
            "There is no C2 channel or real exfiltration. A loopback POST never makes T1041 Executed.", false),
        7 => new("T1539", "Steal Web Session Cookie", "credential-access", "Credential Access",
            "Simulate exposure of a fictitious in-memory session object, separate from the real portal session.",
            "Educational cookie-exposure scenario only. No real browser cookies or session tokens are read.", false),
        8 => new("T1550.004", "Web Session Cookie", "lateral-movement", "Lateral Movement",
            "Simulate replay of the same fictitious object in an isolated application sandbox.",
            "Fictional SaaS/web-session scenario only. The object grants no real application access and is never sent as a cookie.", false),
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    public static string Label(object state) => state switch
    {
        ActivityState.NotEvaluated or ObservationState.NotEvaluated or DetectionState.NotEvaluated or AssessmentKind.NotEvaluated => "Not evaluated",
        DetectionState.NotDetected or AssessmentKind.NotDetected => "Not detected",
        _ => state.ToString()!
    };
}
