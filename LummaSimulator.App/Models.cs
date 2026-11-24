using LummaSimulator.App.Infrastructure;
namespace LummaSimulator.App.Models;
public enum PhaseStatus { Pending, Running, Completed, Failed }
public sealed class SimulationPhase(int number, string name, string description) : ObservableObject
{
 public AttackTechnique Technique { get; private set; } = AttackCatalog.ForPhase(number, false);
 public string TechniqueLabel => $"{Technique.Id} · {Technique.Name}";
 public DateTimeOffset? StartedAt { get; private set; }
 public DateTimeOffset? FinishedAt { get; private set; }
 public DateTimeOffset? ActivityAt { get; private set; }
 public string TimestampText => (ActivityAt ?? StartedAt)?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
 public string TechniqueId => Technique.Id;
 public string TechniqueName => Technique.Name;
 public string TacticName => Technique.TacticName;
 public string ActivityDescription => Technique.ActivityDescription;
 public string MappingNote => Technique.MappingNote;
 public ActivityState Activity { get; private set; } = ActivityState.NotEvaluated;
 public ActivityState TechniqueActivity { get; private set; } = ActivityState.NotEvaluated;
 public string ActivityText => AttackCatalog.Label(Activity);
 public string TechniqueActivityText => AttackCatalog.Label(TechniqueActivity);
 public List<ProductAssessment> Assessments { get; private set; } = SecurityProducts.All.Select(p => new ProductAssessment(p, ObservationState.NotEvaluated, DetectionState.NotEvaluated)).ToList();
 public string ProductStatusText => string.Join("\n", Assessments.Select(p => $"{p.Product}: {AttackCatalog.Label(p.Observation)} / {AttackCatalog.Label(p.Detection)}"));
 public string EvidenceSummary => $"Observation: {(Assessments.Any(p => p.Observation == ObservationState.Observed) ? "Observed" : "Not evaluated")} · Detection: {(Assessments.Any(p => p.Detection == DetectionState.Detected) ? "Detected" : Assessments.Any(p => p.Detection == DetectionState.NotDetected) ? "Not detected" : "Not evaluated")}";
 public void Prepare(bool live)
 {
  Technique = AttackCatalog.ForPhase(Number, live);
  StartedAt = FinishedAt = ActivityAt = null;
  Notify(nameof(TimestampText));
  Activity = TechniqueActivity = ActivityState.NotEvaluated;
  Assessments = SecurityProducts.All.Select(p => new ProductAssessment(p, ObservationState.NotEvaluated, DetectionState.NotEvaluated)).ToList();
  foreach (var property in new[] { nameof(Technique), nameof(TechniqueLabel), nameof(TechniqueId), nameof(TechniqueName), nameof(TacticName), nameof(ActivityDescription), nameof(MappingNote), nameof(ProductStatusText) }) Notify(property);
  NotifyActivity();
  Notify(nameof(EvidenceSummary));
 }
 public void RecordActivity(ActivityState activity)
 {
  ActivityAt = DateTimeOffset.UtcNow;
  Notify(nameof(ActivityAt)); Notify(nameof(TimestampText));
  if (activity == ActivityState.Executed || Activity != ActivityState.Executed) Activity = activity;
  TechniqueActivity = Technique.CanExecuteTechnique && Activity == ActivityState.Executed ? ActivityState.Executed : ActivityState.Simulated;
  NotifyActivity();
 }
 private void NotifyActivity()
 {
  Notify(nameof(Activity)); Notify(nameof(TechniqueActivity)); Notify(nameof(ActivityText)); Notify(nameof(TechniqueActivityText));
 }
 public void ConfirmAssessment(string product, AssessmentKind kind, bool explicitPresenterConfirmation)
 {
  if (!explicitPresenterConfirmation || !SecurityProducts.All.Contains(product) || Status is PhaseStatus.Pending or PhaseStatus.Running)
   throw new InvalidOperationException("Only an explicit presenter confirmation for a finished or interrupted phase is allowed.");
  var index = Assessments.FindIndex(a => a.Product == product);
  var old = Assessments[index];
  var assessment = kind switch
  {
   AssessmentKind.Observed => old with { Observation = ObservationState.Observed },
   AssessmentKind.Detected => old with { Observation = ObservationState.Observed, Detection = DetectionState.Detected },
   AssessmentKind.NotDetected => old with { Detection = DetectionState.NotDetected },
   AssessmentKind.NotEvaluated => new ProductAssessment(product, ObservationState.NotEvaluated, DetectionState.NotEvaluated),
   _ => throw new ArgumentOutOfRangeException(nameof(kind))
  };
  Assessments[index] = assessment with { ConfirmedAt = DateTimeOffset.UtcNow, ConfirmationSource = "Presenter (explicit manual confirmation)" };
  Notify(nameof(Assessments)); Notify(nameof(ProductStatusText)); Notify(nameof(EvidenceSummary));
 }
 public int Number { get; } = number;
 public string Name { get; } = name;
 public string Description { get; } = description;
 private PhaseStatus _status;
 public PhaseStatus Status
 {
  get => _status;
  set
  {
   Set(ref _status, value);
   if (value == PhaseStatus.Running) StartedAt = DateTimeOffset.UtcNow;
   if (value is PhaseStatus.Completed or PhaseStatus.Failed) FinishedAt = DateTimeOffset.UtcNow;
   Notify(nameof(StartedAt)); Notify(nameof(FinishedAt)); Notify(nameof(TimestampText)); Notify(nameof(StatusText));
  }
 }
 public string StatusText => Status.ToString().ToLowerInvariant();
 private TimeSpan _duration;
 public TimeSpan Duration { get => _duration; set { Set(ref _duration, value); Notify(nameof(DurationText)); } }
 public string DurationText => $"{Duration.TotalSeconds:0.0} s";
}
public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message)
{
 public string DemoRunId { get; init; } = "";
 public string Mode { get; init; } = "SAFE SIMULATION";
 public string Activity { get; init; } = "Session";
 public string Outcome { get; init; } = "Info";
 public object? Details { get; init; }
 public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
}
