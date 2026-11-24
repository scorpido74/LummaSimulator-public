using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using LummaSimulator.App.Infrastructure;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.Configuration;

namespace LummaSimulator.App.ViewModels;

public enum MonitorSessionState { NotEvaluated, NoAuthenticatedSession, ActiveWebAuthnSession, FictitiousExposure, InvalidExpired, Unavailable }
public sealed class SessionMonitorViewModel : ObservableObject, IDisposable
{
    private readonly ISessionMetadataClient _client;
    public MonitorIntegrationSettings? Integration { get; }
    private readonly string _logs;
    private CancellationTokenSource? _cancellation;
    private Task? _work;
    private bool _busy;
    private string _runId = "";
    private MonitorSessionState _state;
    private SessionMetadata? _metadata;
    private DateTimeOffset? _checkedAt;
    private FictitiousSession? _sandbox;
    private string _notice = "Alleen servermetadata. Er worden geen browsercookies of sessietokens gelezen.";
    public ObservableCollection<SimulationPhase> Techniques { get; } = [new(7, "Simulated session exposure", ""), new(8, "Simulated session replay", "")];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SimulateExposureCommand { get; }
    public RelayCommand SimulateReplayCommand { get; }
    public RelayCommand CancelCommand { get; }
    public string Origin => _client.Origin;
    public bool IsConfigured => _client.IsConfigured;
    public string ConfigurationStatus => Integration?.Status ?? (IsConfigured ? "Apart integratiegeheim geconfigureerd" : "Gesloten: configureer LUMMA_MONITOR_KEY voor dit proces");
    public string PortalDemoRunId
    {
        get => _runId;
        set
        {
            if (IsBusy) return;
            var input = value.Trim();
            Integration?.ClearGrant();
            Set(ref _runId, Guid.TryParseExact(input, "D", out var parsedRun) ? parsedRun.ToString("D") : input);
            _sandbox = null; _metadata = null; _checkedAt = null; Notify(nameof(CheckedAtText)); State = MonitorSessionState.NotEvaluated;
            foreach (var technique in Techniques) { technique.Status = PhaseStatus.Pending; technique.Duration = TimeSpan.Zero; technique.Prepare(false); }
            RefreshCommands();
        }
    }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Notify(nameof(IsIdle)); RefreshCommands(); } }
    public bool IsIdle => !IsBusy;
    public MonitorSessionState State { get => _state; private set { Set(ref _state, value); Notify(nameof(StateText)); Notify(nameof(ServerSessionText)); Notify(nameof(ReplayText)); } }
    public string StateText => State switch
    {
        MonitorSessionState.NoAuthenticatedSession => "Geen geauthenticeerde sessie",
        MonitorSessionState.ActiveWebAuthnSession => "Actieve, server-side bevestigde WebAuthn-sessie",
        MonitorSessionState.FictitiousExposure => "Fictief blootgestelde demosessie · uitsluitend sandbox",
        MonitorSessionState.InvalidExpired => "Ongeldige, ingetrokken of verlopen sessie",
        MonitorSessionState.Unavailable => "Sessiestatus niet beschikbaar · geen conclusie mogelijk",
        _ => "Not evaluated · nog niet gecontroleerd"
    };
    public string ServerSessionText => _metadata?.SessionActive == true ? "Active session: bevestigd bij de laatste controle; geen gestolen sessie." : "Geen actieve WebAuthn-sessie bevestigd.";
    public string CheckedAtText => _checkedAt is { } at ? $"Laatst gecontroleerd: {at.ToLocalTime():HH:mm:ss} · momentopname" : "Nog geen serverbevestiging ontvangen";
    public string ReplayText => _sandbox?.Replayed == true ? "Simulated session replay: fictief object in geheugen; geen echte sessie gebruikt." : "Simulated session replay: nog niet uitgevoerd.";
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public SessionMonitorViewModel(ISessionMetadataClient? client = null, string? logDirectory = null)
    {
        var settings = DesktopSettings.Load();
        if (client is null) { Integration = new(settings); _client = Integration; Integration.PropertyChanged += OnIntegrationChanged; }
        else _client = client;
        _logs = logDirectory ?? RuntimePaths.Logs(settings.LogDirectory);
        RefreshCommand = new(() => Start(false, false), () => !IsBusy && IsConfigured && Guid.TryParseExact(PortalDemoRunId, "D", out _));
        SimulateExposureCommand = new(() => Start(true, false), () => !IsBusy && _metadata?.SessionActive == true && _sandbox is null);
        SimulateReplayCommand = new(() => Start(false, true), () => !IsBusy && _metadata?.SessionActive == true && _sandbox is { Replayed: false });
        CancelCommand = new(() => _cancellation?.Cancel(), () => IsBusy);
    }
    private void RefreshCommands()
    {
        RefreshCommand?.Refresh(); SimulateExposureCommand?.Refresh(); SimulateReplayCommand?.Refresh(); CancelCommand?.Refresh();
    }
    private void OnIntegrationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorIntegrationSettings.Status) && Integration is { } settings
            && (!settings.IsConfigured || settings.Status is "Monitor key present; not tested" or "Monitor key rejected or revoked")) {
            try { _cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            _metadata = null; _sandbox = null; _checkedAt = null;
            State = MonitorSessionState.NotEvaluated; Notify(nameof(CheckedAtText));
        }
        Notify(nameof(Origin)); Notify(nameof(IsConfigured)); Notify(nameof(ConfigurationStatus)); RefreshCommands();
    }
    private void Start(bool exposure, bool replay)
    {
        if (IsBusy) return;
        _cancellation = new(); IsBusy = true;
        _work = ExecuteAsync(exposure, replay, _cancellation.Token);
    }
    private async Task ExecuteAsync(bool exposure, bool replay, CancellationToken token)
    {
        try
        {
            // Re-check real server state before either sandbox step. Never reuse a stale snapshot as proof.
            _metadata = await _client.FetchAsync(PortalDemoRunId, token);
            _checkedAt = DateTimeOffset.UtcNow;
            Notify(nameof(CheckedAtText));
            State = _metadata.State switch { "ActiveWebAuthnSession" => _sandbox is null ? MonitorSessionState.ActiveWebAuthnSession : MonitorSessionState.FictitiousExposure,
                "NoAuthenticatedSession" => MonitorSessionState.NoAuthenticatedSession, _ => MonitorSessionState.InvalidExpired };
            if (!_metadata.SessionActive) _sandbox = null;
            if ((exposure || replay) && !_metadata.SessionActive) Notice = "Geen actieve serverbevestiging. De fictieve stap is niet uitgevoerd.";
            else if (exposure || replay)
            {
                var technique = Techniques[exposure ? 0 : 1];
                technique.Status = PhaseStatus.Running;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await Task.Delay(300, token);
                    if (exposure) _sandbox = new(Guid.NewGuid(), false);
                    else if (_sandbox is { } sandbox) _sandbox = sandbox with { Replayed = true };
                    else throw new InvalidOperationException("Start de fictieve exposure eerst.");
                    technique.RecordActivity(ActivityState.Simulated);
                    technique.Status = PhaseStatus.Completed;
                    State = MonitorSessionState.FictitiousExposure;
                    Notice = "Alleen een fictief sessieobject is gebruikt. De echte applicatiesessie en beveiligingsproducten zijn niet gewijzigd.";
                }
                catch { technique.Status = PhaseStatus.Failed; throw; }
                finally { technique.Duration = timer.Elapsed; }
            }
            else Notice = "Servermetadata gecontroleerd (momentopname). Controleer opnieuw voor de actuele status. Dit is geen cookie-uitlezing, EDR-waarneming of detectie.";
        }
        catch (MonitorKeyRejectedException) { _metadata = null; _sandbox = null; State = MonitorSessionState.Unavailable; Notice = "Monitor key rejected or revoked"; }
        catch (SessionAccessException) { _metadata = null; _sandbox = null; State = MonitorSessionState.InvalidExpired; Notice = "Demo onbekend, verlopen of niet door de presentator vrijgegeven."; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Notice = "Monitoractie geannuleerd. Controleer opnieuw voor een actuele serverbevestiging."; _metadata = null; _sandbox = null; State = MonitorSessionState.NotEvaluated; }
        catch (Exception) { _metadata = null; _sandbox = null; State = MonitorSessionState.Unavailable; Notice = "Controleer lokale HTTPS/DNS, het integratiegeheim en de toestemming in het presentatordashboard. Er is geen sessieovername vastgesteld."; }
        finally
        {
            Notify(nameof(ServerSessionText)); Notify(nameof(ReplayText));
            if (Guid.TryParseExact(PortalDemoRunId, "D", out _))
            {
                try
                {
                    Directory.CreateDirectory(_logs);
                    var audit = new { version = "0.4", demoRunId = PortalDemoRunId, checkedAt = DateTimeOffset.UtcNow,
                        state = State.ToString(), sessionActive = _metadata?.SessionActive == true, sessionTokenExposed = false,
                        simulatedExposure = _sandbox is not null, simulatedReplay = _sandbox?.Replayed == true };
                    await File.WriteAllTextAsync(Path.Combine(_logs, $"session-monitor-{Guid.Parse(PortalDemoRunId):D}.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { Notice += " De lokale auditlog kon niet worden opgeslagen."; }
            }
            _cancellation?.Dispose(); _cancellation = null; IsBusy = false;
        }
    }
    public async Task StopAndWaitAsync()
    {
        var failures = new List<Exception>();
        try { _cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* Worker finally may already have disposed this source; still await its task. */ }
        catch (Exception error) { failures.Add(error); }
        if (_work is not null) { try { await _work; } catch (Exception error) { failures.Add(error); } }
        if (Integration is not null) { try { await Integration.StopAsync(); } catch (Exception error) { failures.Add(error); } }
        if (failures.Count != 0) throw new AggregateException("Monitor shutdown was not fully confirmed.", failures);
    }
    public void Dispose() { if (Integration is not null) Integration.PropertyChanged -= OnIntegrationChanged; try { _cancellation?.Cancel(); } finally { _client.Dispose(); } }
    private sealed record FictitiousSession(Guid SandboxId, bool Replayed);
}
