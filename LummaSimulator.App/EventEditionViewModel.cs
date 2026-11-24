using LummaSimulator.App.Infrastructure;
using LummaSimulator.App.Services;

namespace LummaSimulator.App.ViewModels;
public sealed class EventEditionViewModel(IEventEditionClient client) : ObservableObject, IDisposable
{
    private string _run = "", _code = "", _notice = "Optioneel en standaard uit. Bereid een run voor en koppel die via het presentatorportaal.";
    private bool _online, _busy, _waiting, _consent, _approved, _manual;
    private EventSnapshot? _snapshot;
    private CancellationTokenSource _scope = new();
    public string RunId => _run;
    public string Code { set { if (_code == value) return; _scope.Cancel(); _scope.Dispose(); _scope = new(); _code = value; _snapshot = null; _online = false; _consent = false; Changed(); } }
    public string EventText => _snapshot is { } s ? $"{s.EventName} · {s.Status} · {s.PlannedStart:u} – {s.PlannedEnd:u}" : "Evenement nog niet gecontroleerd";
    public string ActionText => _snapshot?.Action is { } a ? $"Slachtofferkeuze ontvangen: {a.ActionType} · {a.OccurredAtUtc:u} · volgnummer {a.Sequence}" : "Geen slachtofferactie ontvangen";
    public string PortalRunText => _snapshot?.PortalRunId ?? "Nog geen gekoppelde portalrun bevestigd";
    public string Notice => _notice;
    public string ConfirmationText => _approved ? (_manual
        ? "Handmatig bevestigde voortgang — geen ontvangen slachtofferactie of MDR-detectie bevestigd."
        : "Ontvangen slachtofferkeuze handmatig bevestigd — geen MDR-detectie bevestigd.")
        : "Voortgang nog niet door de presentator bevestigd.";
    public bool IsWaiting => _waiting;
    public bool CanApprove => _waiting && _online && _snapshot is { Status: "Active", Action: not null } && _consent;
    public bool CanFallback => _waiting && !_online && _consent;
    public bool CanRefresh => !_busy && Guid.TryParseExact(_run, "D", out _) && _code.Length == 43;
    public bool Consent { get => _consent; set { _consent = value; Changed(); } }
    public RelayCommand RefreshCommand => _refresh ??= new(() => _ = RefreshAsync(default), () => CanRefresh);
    public RelayCommand ApproveCommand => _approve ??= new(() => Approve(false), () => CanApprove);
    public RelayCommand FallbackCommand => _fallback ??= new(() => Approve(true), () => CanFallback);
    private RelayCommand? _refresh, _approve, _fallback;
    private void Changed()
    {
        foreach (var name in new[] { nameof(RunId), nameof(EventText), nameof(ActionText), nameof(PortalRunText), nameof(Notice), nameof(ConfirmationText), nameof(IsWaiting), nameof(Consent), nameof(CanRefresh), nameof(CanApprove), nameof(CanFallback) }) Notify(name);
        _refresh?.Refresh(); _approve?.Refresh(); _fallback?.Refresh();
    }
    public void SetRun(string run)
    {
        if (_run == run) return;
        Code = ""; _run = run; _approved = false; _manual = false; _snapshot = null; _online = false; _consent = false;
        _notice = "Kopieer deze WPF-run naar het presentatorportaal. Plaats de daar uitgegeven tijdelijke code hieronder."; Changed();
    }
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!CanRefresh) return;
        var run = _run; var scope = _scope.Token;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, scope);
        _busy = true; Changed();
        try
        {
            var response = await client.PollAsync(run, _code, linked.Token);
            if (scope.IsCancellationRequested || run != _run) return;
            if (response.WpfRunId != run || (_snapshot is { } previous && (previous.PortalRunId != response.PortalRunId
                || previous.EventId != response.EventId || (previous.Action is not null && previous.Action != response.Action)))) throw new InvalidOperationException();
            _snapshot = response; _online = true;
            _notice = response.Status == "Closed" ? "Evenement afgesloten. Stop deze run; historische resultaten zijn behouden." : "Server gecontroleerd. Een keuze is geen bewijs van endpointuitvoering of MDR-detectie.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || scope.IsCancellationRequested) { }
        catch { if (!scope.IsCancellationRequested && run == _run) { _online = false; _notice = "Portaal niet beschikbaar of koppeling ongeldig. Probeer opnieuw of bevestig expliciet handmatige voortgang. Geen nieuwe serverbevestiging."; } }
        finally { _busy = false; Changed(); }
    }
    private void Approve(bool manual)
    {
        if (manual ? !CanFallback : !CanApprove) return;
        _manual = manual; _approved = true;
        _notice = manual ? "Handmatig bevestigde voortgang — geen slachtofferactie of MDR-detectie vastgesteld." : "Ontvangen slachtofferkeuze handmatig bevestigd — geen MDR-detectie vastgesteld.";
        Changed();
    }
    public async Task<string> WaitForApprovalAsync(CancellationToken ct)
    {
        _approved = false; _manual = false; _waiting = true; _consent = false; Changed();
        try {
            while (!_approved) {
                ct.ThrowIfCancellationRequested();
                await RefreshAsync(ct);
                if (!_approved) await Task.Delay(500, ct);
            }
            return _manual ? "ManualFallback" : _snapshot?.Action?.ActionType == "ClickFixStopSelected" ? "VictimStopConfirmed" : "VictimChoiceConfirmed";
        }
        finally { _waiting = false; Changed(); }
    }
    public void Dispose() { _scope.Cancel(); _scope.Dispose(); client.Dispose(); }
}
