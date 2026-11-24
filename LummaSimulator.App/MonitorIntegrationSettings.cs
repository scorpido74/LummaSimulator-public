using System.IO;
using LummaSimulator.App.Infrastructure;
using LummaSimulator.Configuration;

namespace LummaSimulator.App.Services;

public sealed class MonitorIntegrationSettings : ObservableObject, ISessionMetadataClient
{
    private readonly MonitorCredentialStore _store;
    private readonly string? _environmentKey;
    private readonly string _defaultOrigin;
    private readonly Func<string, string, SessionMetadataClient> _createClient;
    private string _origin, _portalAddress, _status = "Monitor disconnected";
    private bool _hasKey, _busy, _disposed;
    private int _fetches;
    private DateTimeOffset? _success;
    private CancellationTokenSource? _cancel;
    private Task? _test;
    private string _grant="",_grantRun="";
    public void SetGrant(string run,string grant) {
        if(_disposed||!Guid.TryParseExact(run,"D",out var parsed)||!MonitorCredentialStore.ValidKey(grant))throw new ArgumentException("MonitorGrantFormat");
        _grantRun=parsed.ToString("D");_grant=grant;
    }
    public void ClearGrant(){_grantRun="";_grant="";}
    public MonitorIntegrationSettings(DesktopSettings settings, MonitorCredentialStore? store = null,
        Func<string, string, SessionMetadataClient>? createClient = null, Func<string?>? environmentKey = null)
    {
        _store = store ?? MonitorCredentialStore.ForSettings(settings);
        _environmentKey = environmentKey is null ? Environment.GetEnvironmentVariable("LUMMA_MONITOR_KEY") : environmentKey();
        _createClient = createClient ?? ((origin, key) => new(origin, key));
        _defaultOrigin = settings.PortalOrigin; _origin = _portalAddress = settings.PortalOrigin;
        TestCommand = new(() => { _test = TestAsync(); }, () => !_disposed && !_busy && _fetches == 0 && _hasKey);
        ReplaceCommand = new(() => { _status = "Enter the new monitor key"; Changed(); }, () => CanEdit);
        RemoveCommand = new(Remove, () => CanEdit);
        Reload();
    }
    public string Origin => _origin;
    public string PortalAddress { get => _portalAddress; set { if (CanEdit) { _portalAddress = value; Changed(); } } }
    public string Status => _status;
    public string KeyPresence => _hasKey ? "Yes" : "No";
    public string LastSuccess => _success?.ToLocalTime().ToString("g") ?? "—";
    public bool IsConfigured => _hasKey && !_disposed;
    public bool CanEdit => !_disposed && !_busy && _fetches == 0 && string.IsNullOrEmpty(_environmentKey);
    public bool IsBusy => _busy;
    public string Source => string.IsNullOrEmpty(_environmentKey) ? "DPAPI CurrentUser" : "Temporary environment override";
    public RelayCommand TestCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand RemoveCommand { get; }
    private void Changed()
    {
        foreach (var name in new[] { nameof(Origin), nameof(PortalAddress), nameof(Status), nameof(KeyPresence), nameof(LastSuccess), nameof(IsConfigured), nameof(CanEdit), nameof(IsBusy), nameof(Source) }) Notify(name);
        TestCommand.Refresh(); ReplaceCommand.Refresh(); RemoveCommand.Refresh();
    }
    private MonitorCredential? Effective()
    {
        if (!string.IsNullOrEmpty(_environmentKey)) {
            if (!MonitorCredentialStore.ValidKey(_environmentKey)) throw new MonitorKeyRejectedException();
            return new() { Origin = _defaultOrigin, Key = _environmentKey };
        }
        return _store.Load();
    }
    private void Reload()
    {
        try { var item = Effective(); _hasKey = item is not null; _origin = _portalAddress = item?.Origin ?? _defaultOrigin; _status = _hasKey ? "Monitor key present; not tested" : "Monitor disconnected"; }
        catch (MonitorKeyRejectedException) { _hasKey = false; _status = "Monitor key rejected or revoked"; }
        catch { _hasKey = false; _status = "Monitor credential storage unavailable"; }
        Changed();
    }
    public void Save(string key)
    {
        if (!CanEdit) return;
        ClearGrant();
        try { _store.Save(_portalAddress.Trim(), key); _success = null; Reload(); }
        catch { _status = "Monitor key could not be stored"; Changed(); }
    }
    private void Remove()
    {
        if (!CanEdit) return;
        ClearGrant();
        try { _store.Remove(); _success = null; Reload(); }
        catch { _status = "Monitor key could not be removed"; Changed(); }
    }
    private SessionMetadataClient Client(string? run=null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitorIntegrationSettings));
        var item = Effective() ?? throw new MonitorKeyRejectedException();
        if (_portalAddress.Trim() != item.Origin) throw new InvalidDataException("Save the new address first.");
        var client=_createClient(item.Origin,item.Key);if(Guid.TryParseExact(run,"D",out var parsed)&&parsed.ToString("D")==_grantRun)client.SetGrant(_grant);return client;
    }
    private async Task TestAsync()
    {
        if (_disposed || _busy || _fetches != 0) return;
        _busy = true; _cancel = new(); Changed();
        try { using var client = Client(); await client.TestAsync(_cancel.Token); _status = "Connected; monitor key verified"; _success = DateTimeOffset.Now; }
        catch (MonitorKeyRejectedException) { _status = "Monitor key rejected or revoked"; }
        catch (OperationCanceledException) { _status = "Monitor connection cancelled"; }
        catch { _status = "Monitor portal unavailable or configuration invalid"; }
        finally { _cancel.Dispose(); _cancel = null; _busy = false; Changed(); }
    }
    public async Task<SessionMetadata> FetchAsync(string run, CancellationToken token)
    {
        _fetches++; Changed();
        try { using var client = Client(run); var metadata = await client.FetchAsync(run, token); _status = "Connected; monitor key verified"; _success = DateTimeOffset.Now; Changed(); return metadata; }
        catch (MonitorKeyRejectedException) { _status = "Monitor key rejected or revoked"; Changed(); throw; }
        catch (SessionAccessException) { _status = "Monitor grant or active session missing"; Changed(); throw; }
        catch (OperationCanceledException) { _status = "Monitor connection cancelled"; Changed(); throw; }
        catch { _status = "Monitor portal unavailable or configuration invalid"; Changed(); throw; }
        finally { _fetches--; Changed(); }
    }
    public async Task StopAsync() { try { _cancel?.Cancel(); } catch (ObjectDisposedException) { } if (_test is not null) await _test; }
    public void Dispose() { _disposed = true; ClearGrant(); _cancel?.Cancel(); }
}
