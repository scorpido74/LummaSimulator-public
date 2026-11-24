using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LummaSimulator.App.ViewModels;
using LummaSimulator.Helper;

namespace LummaSimulator.App.Services;

public sealed class HelperReceiverSettings
{
    public bool Enabled { get; set; }
    public string Origin { get; set; } = "";
    public string PairingTokenHash { get; set; } = "";
    public static HelperReceiverSettings Load() {
        var path = Path.Combine(AppContext.BaseDirectory, "HelperReceiver.settings.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<HelperReceiverSettings>(File.ReadAllBytes(path)) ?? throw new InvalidDataException() : new();
    }
}
public sealed class HelperReceiver : IAsyncDisposable
{
    private readonly ReceiverHost _host;
    public HelperReceiver(MainViewModel vm, Dispatcher dispatcher)
    {
        var settings = HelperReceiverSettings.Load(); if (!settings.Enabled) throw new InvalidOperationException();
        _host = new(settings.Origin, () => { var current = HelperReceiverSettings.Load(); return current.Enabled ? current.PairingTokenHash : ""; },
            () => dispatcher.InvokeAsync(() => new ReceiverInfo(typeof(MainViewModel).Assembly.GetName().Version!.ToString(), [1], vm.IsRunning || vm.EventParticipants.HasActiveConsent)).Task,
            request => dispatcher.InvokeAsync(async () => {
                if (vm.IsRunning || vm.EventParticipants.HasActiveConsent || request.PortalOrigin != DesktopSettings.Load().PortalOrigin
                    ||request.PortalOrigin!=vm.EventParticipants.PortalOrigin) throw new InvalidDataException();
                vm.EventEditionEnabled = true; vm.LegacyEventLinkEnabled = false;
                await vm.EventParticipants.AttachAsync(new(request.ConnectionId, request.ConnectionKey, DateTimeOffset.UtcNow.AddMinutes(10)), request.EventId);
                var selected = vm.EventParticipants.SelectedEvent ?? throw new InvalidDataException();
                return new SelectionReceipt(selected.Id, selected.Name, LummaSimulator.Shared.ParticipantLink.EventPath(selected.Id));
            }).Task.Unwrap(), Path.Combine(AppContext.BaseDirectory, "HelperReleases"), hold => dispatcher.InvokeAsync(() => vm.TryHoldForHelperUpdate(hold)).Task,
            currentSelection:ct=>dispatcher.InvokeAsync(async()=>{
                if(!vm.EventEditionEnabled||vm.LegacyEventLinkEnabled)return new ReceiverQrState("Unavailable");
                var origin=vm.EventParticipants.PortalOrigin;
                if(origin is null||origin!=DesktopSettings.Load().PortalOrigin)return new ReceiverQrState("Unavailable");
                var selected=await vm.EventParticipants.ReadSelectedEventAsync(ct);
                if(selected is null||!vm.EventEditionEnabled||vm.LegacyEventLinkEnabled||origin!=DesktopSettings.Load().PortalOrigin)return new ReceiverQrState("Unavailable");
                return new ReceiverQrState("Ready",origin,selected.Id,selected.Name,LummaSimulator.Shared.ParticipantLink.EventPath(selected.Id));
            }).Task.Unwrap(),openParticipants:(eventId,ct)=>dispatcher.InvokeAsync(async()=>{
                var origin=vm.EventParticipants.PortalOrigin;
                if(!vm.EventEditionEnabled||vm.LegacyEventLinkEnabled||origin is null||origin!=DesktopSettings.Load().PortalOrigin)throw new InvalidDataException();
                var selected=await vm.EventParticipants.ReadSelectedEventAsync(ct);
                if(selected?.Id!=eventId||origin!=DesktopSettings.Load().PortalOrigin||ct.IsCancellationRequested)throw new InvalidDataException();
                var uri=LummaSimulator.Shared.ParticipantLink.ForEvent(origin,eventId);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
                return true;
            }).Task.Unwrap());
    }
    public void Start() => _host.Start();
    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
