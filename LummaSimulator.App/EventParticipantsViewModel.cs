using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using LummaSimulator.App.Infrastructure;
using LummaSimulator.App.Services;

namespace LummaSimulator.App.ViewModels;

public sealed record ParticipantRow(string Id, int Round, string Alias, string ConsentStatus, bool CanOpen)
{
    public string ConsentText => DesktopLanguage.Current["SessionConsent" + ConsentStatus];
}

public sealed class EventParticipantsViewModel : ObservableObject
{
    private readonly IParticipantSessionClient _client;
    private readonly Action<Uri> _openBrowser;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _scope = new();
    private AppConnection? _connection;
    private Task _operation = Task.CompletedTask, _consentTask = Task.CompletedTask;
    private Task? _monitor, _shutdown;
    private bool _busy, _waiting, _runActive, _consentGranted, _stopped;
    private long _selectionVersion, _rosterVersion;
    private RoundEvent? _event;
    private ParticipantRow? _participant;
    private string? _activeRun, _activeParticipant;
    private string _notice = "EventParticipantsConnectInfo";
    private string _rosterNotice = "";
    public ObservableCollection<RoundEvent> Events { get; } = [];
    public ObservableCollection<ParticipantRow> Participants { get; } = [];
    public bool IsConnected => _connection is { } c && c.ExpiresAt > _clock.GetUtcNow();
    public string? PortalOrigin=>_client.PortalOrigin;
    public async Task<RoundEvent?> ReadSelectedEventAsync(CancellationToken ct)
    {
        if(_stopped||!IsConnected||_rosterVersion==0||_rosterVersion!=_selectionVersion||_connection is not {} connection||_event is not {} selected)return null;
        var version=_selectionVersion;
        var available=await _client.EventsAsync(connection,ct);
        if(_stopped||!IsConnected||_connection!=connection||_selectionVersion!=version||_rosterVersion!=version||_event?.Id!=selected.Id)return null;
        return available.SingleOrDefault(e=>e.Id==selected.Id);
    }
    public bool IsWaiting => _waiting;
    public bool CanEdit => !_busy && !_waiting && !_runActive && !_stopped;
    public string Notice => DesktopLanguage.Current[_notice];
    public string RosterNotice => _rosterNotice.Length == 0 ? "" : DesktopLanguage.Current[_rosterNotice];
    public bool CanStart => IsConnected && CanEdit && SelectedScope;
    private bool SelectedScope => _event is not null && _participant is not null && _rosterVersion == _selectionVersion;
    public bool HasActiveConsent => IsConnected && SelectedScope && _consentGranted
        && _activeRun is not null && _participant is { ConsentStatus: "Granted", CanOpen: true } && _participant.Id == _activeParticipant;
    public bool CanOpen => !_busy && !_stopped && HasActiveConsent;
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RoundEvent? SelectedEvent
    {
        get => _event;
        set
        {
            if (!CanEdit || _event?.Id == value?.Id) return;
            _event = value is not null && Events.Contains(value) ? value : null;
            _participant = null; Participants.Clear(); _rosterVersion = 0; _consentGranted = false; _rosterNotice = "";
            Changed();
            if (_event is not null) _operation = SelectAsync(_event);
        }
    }
    public ParticipantRow? SelectedParticipant
    {
        get => _participant;
        set
        {
            if (!CanEdit) return;
            _participant = value is not null && Participants.Contains(value) ? value : null;
            Changed();
        }
    }
    public EventParticipantsViewModel(IParticipantSessionClient client, Action<Uri>? openBrowser = null, TimeProvider? clock = null)
    {
        _client = client; _clock = clock ?? TimeProvider.System;
        _openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        ConnectCommand = new(() => _ = ConnectAsync(), () => CanEdit);
        RefreshCommand = new(() => _ = RefreshAsync(), () => IsConnected && !_busy && !_waiting && !_stopped);
        OpenCommand = new(() => _ = OpenAsync(), () => CanOpen);
        DesktopLanguage.Current.PropertyChanged += OnLanguageChanged;
    }
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Notify(nameof(Notice));
        Notify(nameof(RosterNotice));
        var selected = _participant?.Id; var rows = Participants.ToArray(); Participants.Clear();
        foreach (var row in rows) Participants.Add(row with { });
        _participant = Participants.SingleOrDefault(r => r.Id == selected); Notify(nameof(SelectedParticipant));
    }
    private void Changed()
    {
        foreach (var name in new[] { nameof(IsConnected), nameof(IsWaiting), nameof(CanEdit), nameof(CanStart), nameof(CanOpen), nameof(HasActiveConsent), nameof(Notice), nameof(RosterNotice), nameof(SelectedEvent), nameof(SelectedParticipant) }) Notify(name);
        ConnectCommand.Refresh(); RefreshCommand.Refresh(); OpenCommand.Refresh();
    }
    public void SetRunActive(bool active) { _runActive = active; Changed(); }
    public Task ConnectAsync()
    {
        if (!CanEdit) return Task.CompletedTask;
        return _operation = ConnectCoreAsync();
    }
    // The receiver passes a short-lived portal grant, never browser cookies or local accounts.
    public async Task AttachAsync(AppConnection connection, string eventId)
    {
        if (!CanEdit) throw new InvalidOperationException("A participant demo is active.");
        _busy = true; Changed();
        try {
            var status = await _client.ConnectionAsync(connection, _scope.Token);
            if (status.Status != "Ready") throw new InvalidDataException();
            var events = await _client.EventsAsync(connection, _scope.Token);
            var selected = events.SingleOrDefault(e => e.Id == eventId) ?? throw new InvalidDataException();
            var previous = _connection;
            if (previous?.RequestId != connection.RequestId) {
                await RevokeRunAsync();
                if (previous is not null) await _client.DisconnectAsync(previous, _scope.Token);
                _selectionVersion = 0;
            }
            _connection = connection with { ExpiresAt = status.ExpiresAt };
            Events.Clear(); foreach (var item in events) Events.Add(item);
            _event = selected; _participant = null; Participants.Clear(); _rosterVersion = 0;
            var version = ++_selectionVersion;
            await _client.SelectAsync(_connection, eventId, version, _scope.Token);
            await LoadRoster(_connection, eventId, version, _scope.Token);
            _notice = "EventParticipantsReady";
            if (_monitor is null) _monitor = MonitorAsync(_scope.Token);
        }
        catch (Exception error) { ClearRoster(error); throw; }
        finally { _busy = false; Changed(); }
    }
    private async Task ConnectCoreAsync()
    {
        _busy = true; _event = null; _participant = null; Participants.Clear(); Events.Clear(); _rosterVersion = 0; _consentGranted = false; _rosterNotice = "";
        var ct = _scope.Token; var previous = _connection; _connection = null;
        _notice = "EventParticipantsConnecting"; Changed();
        AppConnection? pending = null;
        try
        {
            if (previous is not null) await _client.DisconnectAsync(previous, ct);
            pending = await _client.BeginAsync(ct); _openBrowser(_client.ApprovalUri(pending.RequestId));
            for (var attempt = 0; attempt < 110; attempt++)
            {
                ct.ThrowIfCancellationRequested(); var status = await _client.ConnectionAsync(pending, ct);
                if (status.Status == "Ready")
                {
                    _connection = pending with { ExpiresAt = status.ExpiresAt }; _selectionVersion = 0;
                    foreach (var item in await _client.EventsAsync(_connection, ct)) Events.Add(item);
                    _notice = "EventParticipantsSelect"; pending = null; return;
                }
                await Task.Delay(1000, ct);
            }
            _notice = "EventParticipantsUnavailable";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { _connection = null; _notice = "EventParticipantsUnavailable"; }
        finally
        {
            if (pending is not null) await DisconnectBestEffort(pending);
            _busy = false; Changed();
            if (IsConnected && !_stopped && _monitor is null) _monitor = MonitorAsync(ct);
        }
    }
    private async Task SelectAsync(RoundEvent item)
    {
        if (_connection is not { } connection) return;
        var version = ++_selectionVersion; _busy = true; Changed();
        try
        {
            await RevokeRunAsync();
            await _client.SelectAsync(connection, item.Id, version, _scope.Token);
            await LoadRoster(connection, item.Id, version, _scope.Token); _notice = "EventParticipantsReady";
        }
        catch (OperationCanceledException) when (_scope.IsCancellationRequested) { }
        catch (Exception error) { ClearRoster(error); }
        finally { _busy = false; Changed(); }
    }
    private void ClearRoster(Exception? error = null)
    {
        Participants.Clear(); _participant = null; _rosterVersion = 0; _consentGranted = false; _notice = "EventParticipantsUnavailable";
        _rosterNotice = error switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound } => "EventParticipantsDenied",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "EventParticipantsThrottled",
            JsonException or InvalidDataException => "EventParticipantsInvalidResponse",
            null => "EventParticipantsUnavailable",
            _ => "EventParticipantsLoadFailed"
        };
    }
    private async Task LoadRoster(AppConnection connection, string eventId, long version, CancellationToken ct)
    {
        var roster = await _client.RosterAsync(connection, eventId, ct);
        ct.ThrowIfCancellationRequested();
        if (_event?.Id != eventId || version != _selectionVersion || roster.EventId != eventId || roster.SelectionVersion != version) throw new InvalidDataException();
        var selected = _participant?.Id; Participants.Clear();
        foreach (var p in roster.Participants) Participants.Add(new(p.Id, p.Round, p.Alias, p.ConsentStatus, p.CanOpen));
        _participant = Participants.SingleOrDefault(p => p.Id == selected); _rosterVersion = version;
        _rosterNotice = roster.Participants.Count == 0 ? "EventParticipantsEmpty" : "";
        if (_consentGranted && _participant is not { ConsentStatus: "Granted", CanOpen: true }) _consentGranted = false;
        Changed();
    }
    public Task RefreshAsync()
    {
        if (_busy || _waiting || !IsConnected || _connection is null || _stopped) return Task.CompletedTask;
        return _operation = RefreshCoreAsync();
    }
    private async Task RefreshCoreAsync()
    {
        _busy = true; Changed();
        try {
            var selectedId = _event?.Id;
            var events = await _client.EventsAsync(_connection!, _scope.Token);
            Events.Clear(); foreach (var item in events) Events.Add(item);
            _event = events.SingleOrDefault(e => e.Id == selectedId);
            if (selectedId is not null && _event is null) throw new HttpRequestException("Event access revoked.", null, HttpStatusCode.Forbidden);
            if (_event is not null) await LoadRoster(_connection!, _event.Id, _selectionVersion, _scope.Token);
        }
        catch (OperationCanceledException) when (_scope.IsCancellationRequested) { }
        catch (Exception error) { ClearRoster(error); }
        finally { _busy = false; Changed(); }
    }
    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                if (!IsConnected) { ClearRoster(); Changed(); }
                else await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public Task<bool> WaitForConsentAsync(string runId, CancellationToken ct)
    {
        if (_busy || _waiting || _stopped || !IsConnected || !SelectedScope) return Task.FromResult(false);
        var task = WaitForConsentCoreAsync(runId, ct); _consentTask = task; return task;
    }
    private async Task<bool> WaitForConsentCoreAsync(string runId, CancellationToken ct)
    {
        var connection = _connection!; var item = _event!; var participant = _participant!; var version = _selectionVersion;
        _waiting = true; _consentGranted = false; _notice = "EventParticipantsWaiting"; Changed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _scope.Token);
        try
        {
            await RevokeRunAsync(); _activeRun = runId; _activeParticipant = participant.Id;
            var reply = await _client.RequestConsentAsync(connection, item.Id, participant.Id, runId, linked.Token);
            if (reply.Alias != participant.Alias || reply.Round != participant.Round) throw new InvalidOperationException();
            for (var attempt = 0; attempt < 120; attempt++)
            {
                linked.Token.ThrowIfCancellationRequested();
                await LoadRoster(connection, item.Id, version, linked.Token);
                if (_participant is { ConsentStatus: "Granted", CanOpen: true })
                {
                    _consentGranted = true; _notice = "EventParticipantsGranted"; return true;
                }
                if (_participant is null || _participant.ConsentStatus is "Declined" or "Revoked" or "Expired" or "None") break;
                await Task.Delay(1000, linked.Token);
            }
            _notice = "EventParticipantsNoConsent"; return false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
        catch { _notice = "EventParticipantsUnavailable"; return false; }
        finally
        {
            try { if (!_consentGranted) await RevokeRunAsync(); }
            catch { _notice = "EventParticipantsUnavailable"; }
            finally { _waiting = false; Changed(); }
        }
    }
    public Task OpenAsync()
    {
        if (!CanOpen) return Task.CompletedTask;
        return _operation = OpenCoreAsync();
    }
    private async Task OpenCoreAsync()
    {
        var connection = _connection!; var item = _event!; var participant = _participant!;
        _busy = true; Changed();
        try
        {
            var uri = await _client.OpenAsync(connection, item.Id, participant.Id, _scope.Token);
            _scope.Token.ThrowIfCancellationRequested();
            if (!HasActiveConsent) return;
            _openBrowser(uri);
        }
        catch (OperationCanceledException) when (_scope.IsCancellationRequested) { }
        catch { ClearRoster(); }
        finally { _busy = false; Changed(); }
    }
    private async Task RevokeRunAsync()
    {
        var run = _activeRun; _activeRun = null; _activeParticipant = null; _consentGranted = false; Changed();
        if (run is not null && _connection is { } connection)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.CancelAsync(connection, run, timeout.Token);
        }
    }
    public Task CancelRunAsync()
    {
        if (_activeRun is null || _stopped) return Task.CompletedTask;
        return _operation = CancelCoreAsync();
    }
    private async Task CancelCoreAsync()
    {
        _busy = true; Changed();
        try { await RevokeRunAsync(); }
        catch { ClearRoster(); }
        finally { _busy = false; Changed(); }
    }
    private async Task DisconnectBestEffort(AppConnection connection)
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await _client.DisconnectAsync(connection, timeout.Token); } catch { }
    }
    public Task StopAndDisconnectAsync() => _shutdown ??= StopAndDisconnectCoreAsync();
    private async Task StopAndDisconnectCoreAsync()
    {
        _stopped = true; _scope.Cancel(); Changed();
        try { await Task.WhenAll(_operation, _consentTask, _monitor ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        finally
        {
            if (_connection is { } connection) await DisconnectBestEffort(connection);
            _connection = null; _consentGranted = false; Changed();
            DesktopLanguage.Current.PropertyChanged -= OnLanguageChanged; _client.Dispose(); _scope.Dispose();
        }
    }
}
