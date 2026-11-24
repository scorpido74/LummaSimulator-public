using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LummaSimulator.App.Services;
using LummaSimulator.Helper;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LummaSimulator.PresenterHelper;

public partial class MainWindow : Window
{
    private readonly HelperUserStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly HelperQrDisplay _qr = new();
    private readonly DispatcherTimer _qrWatchdog = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTimeOffset _qrUntil;
    private readonly string[] _arguments;
    private FileStream? _instance;
    private HelperProfile _profile = new();
    private ParticipantSessionClient? _portal;
    private AppReceiverClient? _receiver;
    private AppConnection? _connection;
    private HelperUpdateCoordinator? _updates;
    private string _installationIdentity="", _installationId="";
    private DateTimeOffset _heartbeatDue;
    private bool _busy, _compatible, _closing;
    public MainWindow(string[] arguments, HelperUserStore? store = null) {
        _store = store ?? new HelperUserStore();
        InitializeComponent(); _arguments = arguments;
        Loaded += async (_, _) => await InitializeAsync();
        _timer.Tick += async (_, _) => { if (!_busy) await RefreshAsync(); };
        _qrWatchdog.Tick += (_,_) => { if(ParticipantQrImage.Source is not null&&DateTimeOffset.UtcNow>=_qrUntil)ClearQr(); };
        _qrWatchdog.Start();
        Closed += (_, _) => { _closing = true; _timer.Stop(); _qrWatchdog.Stop(); _stop.Cancel(); _portal?.Dispose(); _receiver?.Dispose(); _instance?.Dispose(); _stop.Dispose(); };
    }
    private async Task InitializeAsync() {
        try {
            _store.EnsureRoot(); _instance = new FileStream(Path.Combine(_store.Root, "helper-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _installationIdentity=_store.InstallationIdentity();
            _installationId=new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(_installationIdentity)).AsSpan(0,16)).ToString("D");
            _profile = _store.LoadProfile(); Portal.Text = _profile.PortalOrigin; AppOrigin.Text = _profile.AppOrigin; Pin.Text = _profile.CertificateSha256; Publisher.Text = _profile.PublisherSha256;
            _updates = new(_store); _updates.StatusChanged += value => UpdateStatus.Text = value; Configure();
            if (_arguments.Length == 2 && _arguments[0] == "--health" && Guid.TryParseExact(_arguments[1], "N", out _)) {
                var root = Path.Combine(_store.Root, "install"); var journal = new HelperUpdateTransaction(root).Load();
                if (journal is { Phase: "AwaitingHealth" } && journal.Version == HelperProtocol.HelperVersion)
                    HelperUserStore.Atomic(Path.Combine(root, "health-" + _arguments[1] + ".json"), System.Text.Encoding.UTF8.GetBytes(HelperProtocol.HelperVersion));
            }
            _timer.Start(); await RefreshAsync(); await CheckUpdateAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Status.Text = "Helper gestart zonder verbinding. Controleer de gebruikersinstallatie en instellingen; mogelijk is al een helper geopend."; }
    }
    private void Configure() {
        ClearQr();
        _portal?.Dispose(); _portal = new(_profile.PortalOrigin, remote: true); _receiver?.Dispose(); _receiver = null; _compatible = false;
        var token = _store.LoadPairing();
        if (_profile.AppOrigin.Length > 0 && token is not null) _receiver = new(_profile.AppOrigin, _profile.CertificateSha256, token);
        Status.Text = "Helper gereed. Meld je aan bij de portal; de app mag tijdelijk onbereikbaar zijn.";
    }
    private async void OnLogin(object sender, RoutedEventArgs e) {
        if (_busy || _portal is null) return; _busy = true; LoginButton.IsEnabled = false;
        ClearQr();
        try {
            var compatibility = await _portal.CompatibilityAsync(_stop.Token);
            if (!compatibility.Protocols.Contains(HelperProtocol.Version)) { Status.Text = "Portal en helper zijn niet compatibel. Werk de helper bij voordat je een nieuwe gekoppelde demo start."; return; }
            if (_connection is not null) await _portal.DisconnectAsync(_connection, _stop.Token);
            _connection = await _portal.BeginAsync(_stop.Token); OpenBrowser(_portal.ApprovalUri(_connection.RequestId));
            Status.Text = "Meld je in de browser aan met je beveiligingssleutel en keur de helperverbinding goed.";
            for (var count = 0; count < 110; count++) {
                var state = await _portal.ConnectionAsync(_connection, _stop.Token);
                if (state.Status == "Ready") { _connection = _connection with { ExpiresAt = state.ExpiresAt }; _heartbeatDue=default; Status.Text = "Aangemeld. Kies een bevoegd evenement."; break; }
                await Task.Delay(1000, _stop.Token);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { _connection = null; Status.Text = "Aanmelden niet voltooid. Controleer de portalverbinding en probeer opnieuw."; }
        finally { _busy = false; LoginButton.IsEnabled = true; await RefreshAsync(); }
    }
    private void OnInvitation(object sender, RoutedEventArgs e) {
        try {
            var uri = new Uri(Invitation.Text.Trim()); var origin = HelperProtocol.Origin(_profile.PortalOrigin);
            var value = uri.Fragment.StartsWith("#invite=", StringComparison.Ordinal) ? uri.Fragment[8..] : "";
            if (uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority) || uri.AbsolutePath != "/Presenter/Enroll" || uri.Query.Length != 0 || !HelperProtocol.IsToken(value)) throw new InvalidDataException();
            OpenBrowser(uri); Status.Text = "Registreer in de portal vanuit de geldige administratoruitnodiging. Kies daarna Aanmelden bij portal.";
        }
        catch { Status.Text = "Gebruik de volledige geldige uitnodigingslink van je administrator voor deze portal."; }
        finally { Invitation.Clear(); }
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync() {
        if (_busy || _closing) return; _busy = true;
        // Healthy polls keep the image stable for scanning; an independent watchdog
        // clears its short display lease even when a network request is still pending.
        try {
            var pairing=_receiver is null?"Unconfigured":"Unknown";
            var health="Unknown";
            if (_receiver is not null) {
                try { var info = await _receiver.InfoAsync(_stop.Token); pairing="Confirmed"; _compatible = info.Protocols.Contains(HelperProtocol.Version); health=_compatible?"Healthy":"Incompatible"; if (!_compatible) {ClearQr();Status.Text = "App en helper zijn niet compatibel. Kies Controleren op updates; nieuwe gekoppelde demo’s zijn geblokkeerd.";} }
                catch (System.Net.Http.HttpRequestException e) when(e.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
                    pairing="Rejected";health="Rejected";_compatible=false;ClearQr();Status.Text="De App-receiver heeft de koppeling geweigerd.";
                }
                catch { health="Unreachable";_compatible = false; ClearQr(); Status.Text = "App niet bereikbaar. Controleer app-locatie, HTTPS en de ingerichte certificaatpin."; }
            }
            else { _compatible = false; Status.Text = "App-koppeling nog niet ingericht. Stel de app-locatie, certificaatpin en koppeling in via de beheerprocedure."; }
            if (_connection is not null && _portal is not null) {
                try {
                    var id = (Events.SelectedItem as RoundEvent)?.Id; var events = await _portal.EventsAsync(_connection, _stop.Token);
                    Events.ItemsSource = events; Events.SelectedItem = events.SingleOrDefault(e => e.Id == id);
                }
                catch (System.Net.Http.HttpRequestException e) when(e.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) { _connection = null; ClearQr(); Events.ItemsSource = null; Status.Text = "Portalverbinding of bevoegdheid vervallen. Meld je opnieuw aan."; }
                catch { ClearQr(); Events.ItemsSource=null; Status.Text="Portal tijdelijk niet bereikbaar."; }
                if(_connection is {} approved&&approved.ExpiresAt>DateTimeOffset.UtcNow&&DateTimeOffset.UtcNow>=_heartbeatDue)
                {
                    _heartbeatDue=DateTimeOffset.UtcNow.AddSeconds(15);
                    try{await _portal.HelperHeartbeatAsync(approved,_installationId,_installationIdentity,Environment.MachineName,HelperProtocol.HelperVersion,pairing,health,_stop.Token);}
                    catch(System.Net.Http.HttpRequestException){}
                    catch(OperationCanceledException){}
                }
            }
            OpenButton.IsEnabled = _compatible && _connection is not null && Events.SelectedItem is RoundEvent;
            if (_updates is not null && _receiver is not null) {
                if (await _updates.InstallIfIdleAsync(_receiver, _stop.Token)) { Close(); return; }
                UpdateStatus.Text = _updates.Status;
            }
            if(_compatible&&_receiver is not null&&_portal is not null&&_connection is {} connection&&connection.ExpiresAt>DateTimeOffset.UtcNow) {
                try {
                    var selected=await _receiver.CurrentSelectionAsync(_stop.Token);
                    var available=await _portal.EventsAsync(connection,_stop.Token);
                    if(_qr.Apply(_profile.PortalOrigin,selected,available))ShowQr();
                    else {ClearQr();Status.Text="Geen actieve gevalideerde WPF-evenementselectie. Kies een open evenement in WPF.";}
                }catch {ClearQr();Status.Text="QR niet beschikbaar. Controleer de koppeling en de evenementtoegang.";}
            }else ClearQr();
        }
        finally { _busy = false; }
    }
    private void OnEventChanged(object sender, SelectionChangedEventArgs e) => OpenButton.IsEnabled = !_busy && _compatible && _connection is not null && Events.SelectedItem is RoundEvent;
    private async void OnOpen(object sender, RoutedEventArgs e) {
        if (_busy || !_compatible || _receiver is null || _portal is null || _connection is null || Events.SelectedItem is not RoundEvent selected) return;
        _busy = true; OpenButton.IsEnabled = false;
        ClearQr();
        try {
            var available = await _portal.EventsAsync(_connection, _stop.Token); if (!available.Any(e => e.Id == selected.Id)) throw new InvalidDataException();
            var receipt = await _receiver.SelectAsync(new(1, _profile.PortalOrigin, _connection.RequestId, _connection.Key, selected.Id), _stop.Token);
            var expected = LummaSimulator.Shared.ParticipantLink.EventPath(selected.Id);
            if (receipt.EventId != selected.Id || receipt.VisitorPath != expected) throw new InvalidDataException();
            var opened=await _receiver.OpenParticipantsAsync(selected.Id,_stop.Token);
            if(!opened.Opened)throw new InvalidDataException();
            Status.Text = receipt.EventName + ": deelnemerspagina geopend op de gekoppelde demowerkplek. De bezoeker meldt zich zelf aan.";
        }
        catch { Status.Text = "Evenement niet gekoppeld. Controleer app, bevoegdheid en of een demo nog actief is."; }
        finally { _busy = false; await RefreshAsync(); }
    }
    private async void OnSave(object sender, RoutedEventArgs e) {
        if (_busy) return;
        ClearQr();
        try {
            HelperProtocol.Origin(Portal.Text.Trim()); var uri = HelperProtocol.Origin(AppOrigin.Text.Trim());
            if (!uri.IsDefaultPort || !HelperProtocol.IsHash(Pin.Text.Trim()) || Publisher.Text.Trim().Length > 0 && !HelperProtocol.IsHash(Publisher.Text.Trim())) throw new InvalidDataException();
            if (_connection is not null && _portal is not null) await _portal.DisconnectAsync(_connection, _stop.Token);
            _connection = null; Events.ItemsSource = null;
            _profile = new() { PortalOrigin = Portal.Text.Trim(), AppOrigin = AppOrigin.Text.Trim(), CertificateSha256 = Pin.Text.Trim(), PublisherSha256 = Publisher.Text.Trim() };
            if (Pairing.Password.Length > 0) _store.SavePairing(Pairing.Password);
            _store.SaveProfile(_profile); Configure(); await RefreshAsync();
        }
        catch { Status.Text = "Instellingen niet toegepast. Gebruik de waarden uit de beheerprocedure; TCP 443 en ingerichte trust zijn verplicht."; }
        finally { Pairing.Clear(); }
    }
    private async void OnUpdate(object sender, RoutedEventArgs e) => await CheckUpdateAsync();
    private async Task CheckUpdateAsync() {
        if (_busy || _receiver is null || _updates is null) return; _busy = true;
        ClearQr();
        try { UpdateStatus.Text = "Updatecontrole gestart…"; await _updates.CheckAsync(_receiver, _profile.PublisherSha256, _stop.Token); UpdateStatus.Text = _updates.Status; }
        finally { _busy = false; }
    }
    private static void OpenBrowser(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    private void ClearQr()
    {
        _qrUntil=DateTimeOffset.MinValue;_qr.Clear();ParticipantQrPanel.Visibility=Visibility.Collapsed;ParticipantQrImage.Source=null;
        ParticipantQrName.Text="";ParticipantQrLink.Text="";CopyParticipantLink.IsEnabled=false;
    }
    private void ShowQr()
    {
        if(_qr.Code is not {} code||_qr.Link is not {} link)return;
        int width=(code.Size+8)*6;var pixels=new byte[width*width];Array.Fill(pixels,(byte)255);
        for(int y=0;y<code.Size;y++)for(int x=0;x<code.Size;x++)if(code.GetModule(x,y))
            for(int dy=0;dy<6;dy++)for(int dx=0;dx<6;dx++)pixels[((y+4)*6+dy)*width+(x+4)*6+dx]=0;
        var bitmap=BitmapSource.Create(width,width,96,96,PixelFormats.Gray8,null,pixels,width);bitmap.Freeze();
        ParticipantQrImage.Source=bitmap;ParticipantQrName.Text=_qr.EventName;ParticipantQrLink.Text=link.AbsoluteUri;
        ParticipantQrLocal.Visibility=_qr.LaptopOnly?Visibility.Visible:Visibility.Collapsed;
        CopyParticipantLink.IsEnabled=true;ParticipantQrPanel.Visibility=Visibility.Visible;
        _qrUntil=DateTimeOffset.UtcNow.AddSeconds(4);
        if(_connection is {} connection&&connection.ExpiresAt<_qrUntil)_qrUntil=connection.ExpiresAt;
    }
    private void OnCopyParticipantLink(object sender,RoutedEventArgs e)
    {
        if(DateTimeOffset.UtcNow>=_qrUntil){ClearQr();return;}
        if(_qr.Link is not {} link)return;
        try{Clipboard.SetText(link.AbsoluteUri);Status.Text="Deelnemerslink gekopieerd.";}
        catch{Status.Text="Kopiëren niet gelukt. Selecteer de zichtbare deelnemerslink en kopieer deze zelf.";}
    }
}
