using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using LummaSimulator.App.Infrastructure;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;

namespace LummaSimulator.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private bool _showScenarioIntroduction = true;
    public ScenarioOverview Scenario => ScenarioOverview.EventEdition;
    public bool ShowScenarioIntroduction { get => _showScenarioIntroduction; private set => Set(ref _showScenarioIntroduction, value); }
    public RelayCommand ShowScenarioCommand { get; }
    public RelayCommand ContinueToControlsCommand { get; }
    public EventEditionViewModel EventEdition { get; }
    public EventParticipantsViewModel EventParticipants { get; }
    private DateTimeOffset _helperUpdateUntil;
    public bool TryHoldForHelperUpdate(bool hold)
    {
        if (hold && (IsRunning || EventParticipants.HasActiveConsent)) return false;
        _helperUpdateUntil = hold ? DateTimeOffset.UtcNow.AddMinutes(2) : DateTimeOffset.MinValue;
        StartCommand.Refresh(); return true;
    }
    public RelayCommand PrepareEventRunCommand { get; }
    private string? _preparedEventRun;
    private bool _eventEditionEnabled;
    private bool _legacyEventLinkEnabled, _participantRun, _participantConsentEstablished;
    public bool ParticipantFlowEnabled => EventEditionEnabled && !LegacyEventLinkEnabled;
    public bool LegacyEventLinkEnabled
    {
        get => _legacyEventLinkEnabled;
        set { if (!IsRunning) { Set(ref _legacyEventLinkEnabled, value); EventModeChanged(); } }
    }
    public bool EventEditionEnabled
    {
        get => _eventEditionEnabled;
        set { if (!IsRunning) { Set(ref _eventEditionEnabled, value); if (value) IsLiveTelemetry = false; EventModeChanged(); } }
    }
    private void EventModeChanged()
    {
        Notify(nameof(ParticipantFlowEnabled));
        Notify(nameof(ModeDescription));
        if (!ParticipantFlowEnabled) _run = EventParticipants.CancelRunAsync();
        PrepareEventRunCommand.Refresh(); StartCommand.Refresh();
    }
    private readonly ISimulationService _simulation;
    private readonly IJsonLogService _logs;
    private readonly ITelemetryService _telemetry;
    private readonly LocalTestServerManager _localServer;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private bool _shuttingDown;
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;
    public Task LocalHttpUpdate { get; private set; } = Task.CompletedTask;
    public bool LocalHttpReady => EnableLocalHttp && _localServer.State == LocalServerState.Ready;
    private static readonly System.Resources.ResourceManager HttpResources = new(
        typeof(MainViewModel).Assembly.GetManifestResourceNames().Single(n => n.EndsWith(".LocalHttpText.resources", StringComparison.Ordinal))[..^10], typeof(MainViewModel).Assembly);
    private static string HttpText(string key) => HttpResources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo(
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en-GB" : "nl-NL"))!;
    public string LocalHttpStatus => _localServer.State switch {
        LocalServerState.Starting => HttpText("Starting"), LocalServerState.Ready => string.Format(HttpText("Ready"), _localServer.Port),
        LocalServerState.Error => HttpText("Error"), _ => HttpText("Disabled") };
    public string LocalHttpDetail => HttpText(_localServer.Error switch { "PortConflict" => "PortConflict", "ServerFilesMissing" => "ServerFilesMissing", "InvalidPort" => "InvalidPort", _ => _localServer.State == LocalServerState.Error ? "UnavailableDetail" : "Info" });
    public string LocalHttpError => _localServer.State == LocalServerState.Error ? LocalHttpDetail : "";
    private void OnLocalServerChanged()
    {
        void Update() { Notify(nameof(LocalHttpReady)); Notify(nameof(LocalHttpStatus)); Notify(nameof(LocalHttpDetail)); Notify(nameof(LocalHttpError)); StartCommand.Refresh(); }
        if (_ui is not null && SynchronizationContext.Current != _ui) _ui.Post(_ => Update(), null); else Update();
    }
    private void ConfigureLocalHttp() => LocalHttpUpdate = _localServer.ConfigureAsync(EnableLocalHttp && !_shuttingDown, int.TryParse(LocalPort, out var port) ? port : 0);
    private CancellationTokenSource? _cancellation;
    private Task? _run;
    private bool _isRunning;
    private bool _isLiveTelemetry;
    private bool _enableLocalHttp;
    private string _localPort = "8080";
    private string _demoRunId = "";
    private string _runMode = "SAFE SIMULATION";
    private string _status = "Ready";
    private string _logStatus = "JSON logs are saved locally during each run.";
    private double _progress;
    private SimulationPhase? _activePhase;
    private SimulationPhase? _selectedPhase;
    private string _selectedProduct = SecurityProducts.All[0];
    private bool _assessmentConfirmed;
    private string _exportStatus = "";
    private AssessmentKind _selectedAssessment = AssessmentKind.Observed;
    public IReadOnlyList<string> Products => SecurityProducts.All;
    public IReadOnlyList<AssessmentKind> AssessmentKinds { get; } = Enum.GetValues<AssessmentKind>();
    public IReadOnlyList<string> AssessmentOptions { get; } = ["Observed", "Detected", "Not detected", "Not evaluated"];
    public string SelectedAssessmentText
    {
        get => AttackCatalog.Label(SelectedAssessment);
        set => SelectedAssessment = value switch { "Detected" => AssessmentKind.Detected, "Not detected" => AssessmentKind.NotDetected, "Not evaluated" => AssessmentKind.NotEvaluated, _ => AssessmentKind.Observed };
    }
    public SimulationPhase? SelectedPhase { get => _selectedPhase; set { Set(ref _selectedPhase, value); AssessmentConfirmed = false; } }
    public string SelectedProduct { get => _selectedProduct; set { Set(ref _selectedProduct, value); AssessmentConfirmed = false; } }
    public AssessmentKind SelectedAssessment { get => _selectedAssessment; set { Set(ref _selectedAssessment, value); Notify(nameof(SelectedAssessmentText)); AssessmentConfirmed = false; } }
    public bool AssessmentConfirmed { get => _assessmentConfirmed; set { Set(ref _assessmentConfirmed, value); ConfirmAssessmentCommand?.Refresh(); } }
    public string ExportStatus { get => _exportStatus; private set => Set(ref _exportStatus, value); }
    public SessionMonitorViewModel SessionMonitor { get; }

    public ObservableCollection<SimulationPhase> Phases { get; } =
    [
        new(1, "Initial execution", "Visual payload / live: visible whoami.exe"),
        new(2, "System discovery", "Fixed fictional inventory in both modes"),
        new(3, "Browser discovery", "Imaginary profiles / live: create dummy JSON"),
        new(4, "Credential collection", "Dummy records / live: read own dummy JSON"),
        new(5, "Data staging", "Imaginary package / live: write collected JSON"),
        new(6, "Simulated exfiltration", "Simulated delivery / optional loopback HTTP")
    ];
    public ObservableCollection<LogEntry> Entries { get; } = [];
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand CleanupCommand { get; }
    public RelayCommand ConfirmAssessmentCommand { get; }
    public RelayCommand ExportAttackCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            Set(ref _isRunning, value);
            EventParticipants.SetRunActive(value);
            Notify(nameof(IsIdle));
            Notify(nameof(ModeLabel));
            Notify(nameof(ModeDescription));
            StartCommand.Refresh();
            StopCommand.Refresh();
            ShowScenarioCommand.Refresh();
            PrepareEventRunCommand.Refresh();
            ResetCommand.Refresh();
            CleanupCommand.Refresh();
            ConfirmAssessmentCommand.Refresh();
            ExportAttackCommand.Refresh();
        }
    }
    public bool IsIdle => !IsRunning;
    public bool IsLiveTelemetry
    {
        get => _isLiveTelemetry;
        set
        {
            if (IsRunning || (value && EventEditionEnabled)) return;
            Set(ref _isLiveTelemetry, value);
            if(!value && EnableLocalHttp) EnableLocalHttp=false;
            Notify(nameof(ModeLabel));
            Notify(nameof(ModeDescription));
            StartCommand.Refresh();
            if (Phases.All(p => p.Status == PhaseStatus.Pending)) foreach (var phase in Phases) phase.Prepare(value);
        }
    }
    public bool EnableLocalHttp
    {
        get => _enableLocalHttp;
        set { if (!IsRunning && !_shuttingDown && value != _enableLocalHttp) { Set(ref _enableLocalHttp, value); ConfigureLocalHttp(); } }
    }
    public string LocalPort
    {
        get => _localPort;
        set { if (!IsRunning && !_shuttingDown && value != _localPort) { Set(ref _localPort, value); if(EnableLocalHttp) ConfigureLocalHttp(); } }
    }
    public string ModeLabel => IsRunning ? _runMode : IsLiveTelemetry ? "LIVE TELEMETRY" : "SAFE SIMULATION";
    public string ModeDescription => ParticipantFlowEnabled
        ? "Visual simulation with an optional HTTPS participant session; fictional data only."
        : ModeLabel == "LIVE TELEMETRY"
        ? "Real local process and file activity; synthetic browser data only. HTTP is optional and loopback-only."
        : "Visual training only: no process execution, browser file access or network traffic.";
    public string DemoRunId { get => _demoRunId; private set => Set(ref _demoRunId, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string LogStatus { get => _logStatus; private set => Set(ref _logStatus, value); }
    public string LogDirectory => _logs.DirectoryPath;
    public double Progress
    {
        get => _progress;
        private set { Set(ref _progress, value); Notify(nameof(ProgressText)); }
    }
    public string ProgressText => $"{Progress:0}%";

    public MainViewModel(ISimulationService simulation, IJsonLogService logs, ITelemetryService? telemetry = null, SessionMonitorViewModel? sessionMonitor = null, LocalTestServerManager? localServer = null, EventEditionViewModel? eventEdition = null, EventParticipantsViewModel? eventParticipants = null)
    {
        _simulation = simulation;
        _logs = logs;
        _telemetry = telemetry ?? new TelemetryService();
        SessionMonitor = sessionMonitor ?? new SessionMonitorViewModel(logDirectory: logs.DirectoryPath);
        EventEdition = eventEdition ?? new(new EventEditionClient(DesktopSettings.Load().PortalOrigin));
        var desktopSettings = DesktopSettings.Load();
        EventParticipants = eventParticipants ?? new(new ParticipantSessionClient(desktopSettings.PortalOrigin, remote: desktopSettings.UseRemotePortal));
        PrepareEventRunCommand = new(() => { _preparedEventRun = Guid.NewGuid().ToString("D"); EventEdition.SetRun(_preparedEventRun); }, () => IsIdle && EventEditionEnabled && LegacyEventLinkEnabled && !_shuttingDown);
        _localServer = localServer ?? new LocalTestServerManager();
        StartCommand = new(Start, () => IsIdle && !_shuttingDown && _helperUpdateUntil <= DateTimeOffset.UtcNow && (!ParticipantFlowEnabled || EventParticipants.CanStart) && (!IsLiveTelemetry || !EnableLocalHttp || LocalHttpReady));
        ShowScenarioCommand = new(() => ShowScenarioIntroduction = true, () => IsIdle && !_shuttingDown);
        ContinueToControlsCommand = new(() => ShowScenarioIntroduction = false, () => true);
        _localServer.Changed += OnLocalServerChanged;
        StopCommand = new(Stop, () => IsRunning && _cancellation is { IsCancellationRequested: false });
        ResetCommand = new(() => { if (_shuttingDown) return; Reset(); _preparedEventRun = null; EventEdition.SetRun(""); _run = EventParticipants.CancelRunAsync(); }, () => IsIdle && !_shuttingDown);
        CleanupCommand = new(StartCleanup, () => IsIdle && !_shuttingDown);
        ExportAttackCommand = new(() => _run = ExportAttackAsync(), () => IsIdle && !_shuttingDown && !SessionMonitor.IsBusy
            && ((Guid.TryParse(DemoRunId, out _) && Phases.Any(p => p.Status != PhaseStatus.Pending))
                || (Guid.TryParse(SessionMonitor.PortalDemoRunId, out _) && SessionMonitor.Techniques.Any(p => p.Status != PhaseStatus.Pending))));
        SessionMonitor.PropertyChanged += OnSessionMonitorChanged;
        EventParticipants.PropertyChanged += OnEventParticipantsChanged;
        ConfirmAssessmentCommand = new(() => _run = ConfirmAssessmentAsync(), () => IsIdle && !_shuttingDown && AssessmentConfirmed && Guid.TryParse(DemoRunId, out _)
            && SelectedPhase?.Status is PhaseStatus.Completed or PhaseStatus.Failed);
        _selectedPhase = Phases[0];
        AddLog("INFO", "Ready. Browser data is always fictional. No EDR detection has been confirmed.");
    }

    private void Start()
    {
        if (!StartCommand.CanExecute(null) || (EventEditionEnabled && IsLiveTelemetry)) return;
        var port = 8080;
        if (IsLiveTelemetry && EnableLocalHttp && (!int.TryParse(LocalPort, out port) || port is < 1 or > 65535))
        {
            Status = "Invalid local port";
            AddLog("ERROR", "Choose a local HTTP port from 1 through 65535.");
            return;
        }
        BeginSession(ModeLabel);
        ShowScenarioIntroduction = false;
        var liveRun = IsLiveTelemetry ? new TelemetryRun(DemoRunId, EnableLocalHttp, port) : null;
        _run = RunAsync(liveRun, _cancellation!.Token);
    }

    private void BeginSession(string mode, bool eventRun = true)
    {
        Reset();
        Entries.Clear();
        _participantRun = eventRun && ParticipantFlowEnabled; _participantConsentEstablished = false;
        DemoRunId = eventRun && EventEditionEnabled && LegacyEventLinkEnabled && _preparedEventRun is not null ? _preparedEventRun : Guid.NewGuid().ToString("D");
        if (eventRun) { _preparedEventRun = null; if (EventEditionEnabled && LegacyEventLinkEnabled) EventEdition.SetRun(DemoRunId); }
        _runMode = mode;
        _cancellation = new CancellationTokenSource();
        IsRunning = true;
        AddLog("INFO", $"{mode} · DemoRunId={DemoRunId}. No EDR detection has been confirmed.");
    }

    private async Task RunAsync(TelemetryRun? liveRun, CancellationToken token)
    {
        SimulationPhase? active = null;
        var timer = new Stopwatch();
        var completed = false;
        try
        {
            Status = "Running";
            AddLog("INFO", _participantRun ? "Participant session demo requested. No phase starts before explicit visitor consent." : liveRun is null
                ? "Visual simulation started. No host discovery or external connections."
                : "Controlled Windows telemetry started. Only owned synthetic files will be accessed.");
            await SaveAsync();
            if (_participantRun)
            {
                Status = "Waiting for visitor consent · no phase started";
                if (!await EventParticipants.WaitForConsentAsync(DemoRunId, token))
                {
                    Status = "Stopped · no visitor consent";
                    AddLog("INFO", "Session demo not approved. No simulation phase started.");
                    return;
                }
                _participantConsentEstablished = true;
                AddLog("INFO", "Temporary session demo approved by visitor. Only synthetic data is available.");
            }
            else if (EventEditionEnabled)
            {
                Status = "Waiting for presenter confirmation · no phase started";
                var confirmation = await EventEdition.WaitForApprovalAsync(token);
                AddLog("INFO", "Event Edition: " + confirmation + ". No MDR detection is inferred.");
                if (confirmation == "VictimStopConfirmed")
                {
                    Status = "Stopped · visitor declined ClickFix";
                    ReportSimulation("ClickFix gestopt door bezoeker. Geen fictieve installatie of aanvalsfase gestart.");
                    return;
                }
                Status = "SIMULATION · fictieve Lumma Stealer-installatie";
                ReportSimulation("Fictieve Lumma Stealer-installatie gestart na expliciete presentatorbevestiging. Geen download, installer of proces.");
                await SaveAsync();
                await Task.Delay(1500, token);
                ReportSimulation("Fictieve installatie voltooid in de presentatie. Er is niets op het endpoint geïnstalleerd.");
            }
            foreach (var phase in Phases)
            {
                token.ThrowIfCancellationRequested();
                active = phase;
                _activePhase = phase;
                phase.Status = PhaseStatus.Running;
                Status = $"Running · {phase.Name}";
                AddLog("INFO", $"Phase {phase.Number}: {phase.Name} started.");
                timer.Restart();
                var work = liveRun is null
                    ? _simulation.ExecuteAsync(phase.Number, ReportSimulation, token)
                    : _telemetry.ExecuteAsync(phase.Number, liveRun, ReportTelemetry, token);
                while (!work.IsCompleted)
                {
                    await Task.WhenAny(work, Task.Delay(100));
                    phase.Duration = timer.Elapsed;
                }
                await work;
                token.ThrowIfCancellationRequested();
                timer.Stop();
                phase.Duration = timer.Elapsed;
                phase.Status = PhaseStatus.Completed;
                if (phase.Activity == ActivityState.NotEvaluated) phase.RecordActivity(ActivityState.Simulated);
                Progress = phase.Number / 6.0 * 100;
                AddLog("OK", $"{phase.Name} completed in {phase.DurationText}.");
                active = null;
                _activePhase = null;
                await SaveAsync();
            }
            Status = "Completed";
            AddLog("OK", liveRun is null
                ? "All six visual phases completed. No real data collected or transmitted."
                : "Local telemetry run completed. This does not confirm an EDR alert or detection.");
            if (EventEditionEnabled && LegacyEventLinkEnabled) ReportSimulation("Onthulling: ClickFix verleidt tot uitvoeren van instructies. Passkeys beschermen de aanmelding; ze voorkomen geen endpointcompromittering. Deze installatie en alle aanvallersactiviteiten waren fictief; geen MDR-detectie bevestigd.");
            completed = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Fail(active, timer.Elapsed);
            Status = "Stopped";
            AddLog("WARN", "Stopped by user. Interrupted phase failed; remaining phases pending. Demo files remain available for cleanup.");
        }
        catch (Exception exception)
        {
            Fail(active, timer.Elapsed);
            Status = "Failed";
            AddLog("ERROR", exception.Message, "PhaseFailure", "Failed", new { phase = active?.Number });
        }
        finally
        {
            _participantConsentEstablished = false;
            if (_participantRun && !completed) await EventParticipants.CancelRunAsync();
            _activePhase = null;
            await SaveAsync();
            EndSession();
        }
    }

    private void StartCleanup()
    {
        if (IsRunning || _shuttingDown) return;
        BeginSession("LIVE TELEMETRY", false);
        _run = CleanupAsync(_cancellation!.Token);
    }

    private async Task CleanupAsync(CancellationToken token)
    {
        Status = "Cleaning demo files";
        AddLog("INFO", "Cleanup started. Only recognized demo run directories are eligible; JSON logs are preserved.");
        await SaveAsync();
        try
        {
            await _telemetry.CleanupAsync(ReportTelemetry, token);
            Status = "Cleanup completed";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Status = "Cleanup stopped";
            AddLog("WARN", "Cleanup cancelled. Remaining demo files retained.");
        }
        catch (Exception exception)
        {
            Status = "Cleanup failed";
            AddLog("ERROR", exception.Message, "Cleanup", "Failed");
        }
        finally
        {
            await SaveAsync();
            EndSession();
        }
    }

    private void EndSession()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        IsRunning = false;
    }

    private static void Fail(SimulationPhase? phase, TimeSpan duration)
    {
        if (phase is null) return;
        phase.Duration = duration;
        phase.Status = PhaseStatus.Failed;
    }

    private async Task SaveAsync()
    {
        try { LogStatus = $"Saved: {await _logs.SaveAsync(DemoRunId, Status, Entries, Phases)}"; }
        catch (Exception exception)
        {
            LogStatus = $"JSON save failed: {exception.Message}";
            AddLog("ERROR", LogStatus, "LogSave", "Failed");
        }
    }

    private void Stop()
    {
        if (!IsRunning || _cancellation is null) return;
        _cancellation?.Cancel();
        Status = "Stopping…";
        StopCommand.Refresh();
    }

    public async Task StopAndWaitAsync()
    {
        if (!IsRunning) return;
        Stop();
        if (_run is not null) await _run;
    }

    private void OnSessionMonitorChanged(object? sender, PropertyChangedEventArgs e) => ExportAttackCommand.Refresh();

    private void OnEventParticipantsChanged(object? sender, PropertyChangedEventArgs e)
    {
        StartCommand.Refresh();
        if (e.PropertyName == nameof(EventParticipantsViewModel.HasActiveConsent) && _participantConsentEstablished && !EventParticipants.HasActiveConsent)
            _cancellation?.Cancel();
    }

    public Task ShutdownAsync()
    {
        lock (_shutdownSync)
        {
            if (_shutdownTask is not null) return _shutdownTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownTask = completion.Task; // Before Refresh/Cancel/Dispose callbacks can re-enter.
            _shuttingDown = true;
            _ = ShutdownCoreAsync(completion);
            return _shutdownTask;
        }
    }

    private async Task ShutdownCoreAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        async Task Attempt(Func<Task> action)
        {
            try { await action(); } catch (Exception error) { failures.Add(error); }
        }
        void AttemptSync(Action action)
        {
            try { action(); } catch (Exception error) { failures.Add(error); }
        }
        foreach (var command in new[] { StartCommand, ResetCommand, CleanupCommand, ConfirmAssessmentCommand, ExportAttackCommand, PrepareEventRunCommand, ShowScenarioCommand })
            AttemptSync(command.Refresh);
        AttemptSync(Stop);
        // Await the actual task even if cancellation/notification threw, or IsRunning was reset.
        var run = _run;
        if (run is not null) await Attempt(() => run);
        await Attempt(SessionMonitor.StopAndWaitAsync);
        await Attempt(EventParticipants.StopAndDisconnectAsync);
        AttemptSync(() => _localServer.Changed -= OnLocalServerChanged);
        await Attempt(() => _localServer.DisposeAsync().AsTask());
        AttemptSync(() => SessionMonitor.PropertyChanged -= OnSessionMonitorChanged);
        AttemptSync(() => EventParticipants.PropertyChanged -= OnEventParticipantsChanged);
        AttemptSync(SessionMonitor.Dispose);
        AttemptSync(EventEdition.Dispose);
        if (failures.Count == 0) completion.TrySetResult();
        else completion.TrySetException(new AggregateException("Shutdown cleanup was not fully confirmed.", failures));
    }

    private void Reset()
    {
        if (IsRunning) return;
        foreach (var phase in Phases) { phase.Status = PhaseStatus.Pending; phase.Duration = TimeSpan.Zero; phase.Prepare(IsLiveTelemetry); }
        Entries.Clear();
        Progress = 0;
        Status = "Ready";
        DemoRunId = "";
        AssessmentConfirmed = false;
        ExportStatus = "";
        ExportAttackCommand.Refresh();
        _runMode = ModeLabel;
        LogStatus = "JSON logs are saved locally during each run.";
        AddLog("INFO", "Ready. All browser records are fictional. Existing demo files and logs retained.");
    }

    private void ReportSimulation(string message)
    {
        _activePhase?.RecordActivity(ActivityState.Simulated);
        AddLog("SIM", message, "Simulation", "Simulated");
    }

    private void ReportTelemetry(TelemetryActivity activity)
    {
        if(activity.Activity=="HttpPost" && activity.Outcome=="Failed") _localServer.ReportPostFailure();
        if (activity.Outcome is "Succeeded" or "Received") _activePhase?.RecordActivity(ActivityState.Executed);
        else if (activity.Outcome is "Simulated" or "Skipped") _activePhase?.RecordActivity(ActivityState.Simulated);
        AddLog(
        activity.Outcome is "Failed" or "Cancelled" ? "WARN" : activity.Outcome == "Simulated" ? "SIM" : "LIVE",
        activity.Message, activity.Activity, activity.Outcome, activity.Details);
    }

    private async Task ConfirmAssessmentAsync()
    {
        if (!ConfirmAssessmentCommand.CanExecute(null)) return;
        IsRunning = true;
        try
        {
            SelectedPhase!.ConfirmAssessment(SelectedProduct, SelectedAssessment, AssessmentConfirmed);
            AddLog("INFO", $"Presenter confirmed {SelectedAssessment} for {SelectedPhase.TechniqueId} in {SelectedProduct}.", "PresenterAssessment", "Confirmed");
            AssessmentConfirmed = false;
            await SaveAsync();
        }
        catch (Exception exception) { ExportStatus = "Assessment could not be saved: " + exception.Message; }
        finally { IsRunning = false; }
    }

    private async Task ExportAttackAsync()
    {
        if (!ExportAttackCommand.CanExecute(null)) return;
        IsRunning = true;
        try
        {
            var portalRun = Guid.TryParseExact(SessionMonitor.PortalDemoRunId, "D", out _) ? SessionMonitor.PortalDemoRunId : null;
            var sessionTechniques = SessionMonitor.Techniques.Where(p => p.Status != PhaseStatus.Pending).ToArray();
            var exportRun = sessionTechniques.Length > 0 && portalRun is not null ? portalRun : DemoRunId;
            var exportDirectory = LummaSimulator.Configuration.RuntimePaths.Exports(DesktopSettings.Load().ExportDirectory ?? LogDirectory);
            var path = await new NavigatorExportService().ExportAsync(exportDirectory, exportRun, Phases.Concat(sessionTechniques), portalRun,
                Guid.TryParse(DemoRunId, out _) ? DemoRunId : null);
            ExportStatus = "ATT&CK layer saved: " + path;
            AddLog("INFO", "MITRE ATT&CK layer exported locally. Scores are not detection coverage.", "NavigatorExport", "Succeeded");
            if (Guid.TryParse(DemoRunId, out _)) await SaveAsync();
        }
        catch (Exception exception) { ExportStatus = "Export failed: " + exception.Message; }
        finally { IsRunning = false; }
    }

    private void AddLog(string level, string message, string activity = "Session", string outcome = "Info", object? details = null)
    {
        Entries.Add(new(DateTimeOffset.Now, level, message)
        {
            DemoRunId = DemoRunId,
            Mode = _runMode,
            Activity = activity,
            Outcome = outcome,
            Details = details
        });
    }
}
