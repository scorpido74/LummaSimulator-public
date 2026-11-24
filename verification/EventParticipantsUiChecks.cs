using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LummaSimulator.App;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

internal static class EventParticipantsUiChecks
{
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
    private static async Task Until(Func<bool> ready)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!ready()) await Task.Delay(20, timeout.Token); }
    internal static async Task RunAsync()
    {
        var client = new Client(); var opened = new List<Uri>(); var simulation = new Simulation();
        var vm = new MainViewModel(simulation, new MemoryLog(), eventParticipants: new(client, opened.Add));
        var window = new MainWindow(vm); window.Show();
        try
        {
            vm.ContinueToControlsCommand.Execute(null);
            var tabs = (TabControl)window.FindName("ScenarioTabs"); tabs.SelectedIndex = 2;
            vm.EventEditionEnabled = true; window.UpdateLayout();
            var connect = (Button)window.FindName("ConnectParticipantsButton");
            var events = (ComboBox)window.FindName("ParticipantEventChoice");
            var rows = (ListBox)window.FindName("EventParticipantsList");
            var start = (Button)window.FindName("StartParticipantDemoButton");
            var open = (Button)window.FindName("OpenParticipantButton");
            var rosterNotice = (TextBlock)window.FindName("ParticipantRosterNotice");
            Check(connect.IsEnabled && !start.IsEnabled && !open.IsEnabled, "WPF participant controls start disconnected with Start and Open disabled");
            Check(!((PasswordBox)window.FindName("EventPairingCode")).IsVisible, "Normal Event Edition flow shows no manual run/code entry");
            await vm.EventParticipants.ConnectAsync(); events.SelectedIndex = 0;
            await Until(() => vm.EventParticipants.CanEdit);
            var phasePriorWidth=window.Width;var phasePriorHeight=window.Height;
            tabs.SelectedIndex=0;
            foreach(var scale in new[]{1d,1.5d}){window.Width=1366/scale;window.Height=768/scale;window.UpdateLayout();Program.VerifyPhaseCardRows(window);}
            window.Width=phasePriorWidth;window.Height=phasePriorHeight;tabs.SelectedIndex=2;window.UpdateLayout();
            window.UpdateLayout();
            Check(rows.Items.Count == 0 && rosterNotice.Text == DesktopLanguage.Current["EventParticipantsEmpty"], "WPF distinguishes a successfully loaded empty roster from a load failure");
            client.Count = 1; await vm.EventParticipants.RefreshAsync(); window.UpdateLayout();
            Check(rows.Items.Count == 1 && vm.EventParticipants.Participants[0].ConsentStatus == "None" && !open.IsEnabled,
                "A newly arriving signed-in visitor is visible in WPF before any consent request");
            client.Count = 2; await vm.EventParticipants.RefreshAsync();
            await Until(() => rows.Items.Count == 2 && vm.EventParticipants.CanEdit);
            rows.SelectedIndex = 0; window.UpdateLayout();
            var selectedAlias=vm.EventParticipants.SelectedParticipant?.Alias;
            await vm.EventParticipants.RefreshAsync();window.UpdateLayout();
            Check(rows.Items.Count==2 && vm.EventParticipants.SelectedParticipant?.Alias==selectedAlias
                &&vm.EventParticipants.Participants.Any(p=>p.Alias==selectedAlias)&&!open.IsEnabled,
                "Lijst vernieuwen retains the exact temporary alias and does not grant consent");
            Check(ReferenceEquals(rows.ItemsSource, vm.EventParticipants.Participants) && start.IsEnabled && !open.IsEnabled
                && vm.EventParticipants.SelectedParticipant?.Alias == "Synthetic visitor one", "WPF event selection binds the two aliases, round and consent state to the real list");
            start.Command.Execute(null); await Until(() => vm.EventParticipants.IsWaiting);
            window.UpdateLayout();
            Check(simulation.Calls == 0 && !events.IsEnabled && !rows.IsEnabled && !open.IsEnabled, "WPF freezes scope and starts no phase while visitor consent is pending");
            client.Status = "Granted"; await Until(() => !vm.IsRunning);
            window.UpdateLayout();
            Check(simulation.Calls == 6 && open.IsEnabled, "Explicit synthetic visitor approval enables the existing six phases and Open button");
            open.Command.Execute(null); await Until(() => opened.Count == 2);
            Check(opened[1].AbsolutePath.StartsWith("/SessionDemo/", StringComparison.Ordinal) && opened[1].Fragment.StartsWith("#open=", StringComparison.Ordinal), "WPF Open uses the fixed temporary synthetic viewer route");
            window.Width = 1093; window.Height = 720;
            rows.BringIntoView(); window.UpdateLayout();
            var root = Path.Combine(Path.GetTempPath(), "LummaStep5Wpf", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var preview = Path.Combine(root, "event-participants.png"); using (var file = File.Create(preview)) encoder.Save(file);
            Console.WriteLine("PARTICIPANTS_PREVIEW=" + preview);
            client.Status = "Revoked"; await vm.EventParticipants.RefreshAsync(); window.UpdateLayout();
            Check(!open.IsEnabled && vm.EventParticipants.Participants[0].ConsentStatus == "Revoked", "WPF renders withdrawn consent and disables Open for an already approved alias");
            rows.SelectedIndex = 1; window.UpdateLayout();
            Check(!open.IsEnabled && vm.EventParticipants.SelectedParticipant?.ConsentStatus == "None", "WPF second alias never inherits the first alias's consent");
            vm.ResetCommand.Execute(null);
            await Until(() => vm.EventParticipants.CanEdit);
            foreach (var error in new Exception[] { new HttpRequestException("Synthetic forbidden", null, HttpStatusCode.Forbidden), new InvalidDataException("Synthetic invalid response"), new HttpRequestException("Synthetic offline") })
            {
                client.Error = error; await vm.EventParticipants.RefreshAsync(); window.UpdateLayout();
                var key = error is InvalidDataException ? "EventParticipantsInvalidResponse" : error is HttpRequestException { StatusCode: HttpStatusCode.Forbidden } ? "EventParticipantsDenied" : "EventParticipantsLoadFailed";
                Check(rows.Items.Count == 0 && rosterNotice.IsVisible && rosterNotice.Text == DesktopLanguage.Current[key] && !start.IsEnabled && !open.IsEnabled,
                    "WPF visibly distinguishes " + key + " and disables participant actions");
            }
            client.Error = null; await vm.EventParticipants.RefreshAsync(); window.UpdateLayout();
            Check(rows.Items.Count == 2 && rosterNotice.Text == "", "WPF refresh recovers from a load error and clears its error message");
            Console.WriteLine("WPF_TEST_PROCESS_ADMIN=" + new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
        }
        finally
        {
            await vm.ShutdownAsync(); window.Close();
            await Until(() => !window.IsVisible);
        }
    }
    private sealed class Simulation : ISimulationService
    {
        public int Calls;
        public Task ExecuteAsync(int phase, Action<string> report, CancellationToken ct) { Calls++; return Task.CompletedTask; }
    }
    private sealed class MemoryLog : IJsonLogService
    {
        public string DirectoryPath => Path.GetTempPath();
        public Task<string> SaveAsync(string run, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases) => Task.FromResult("memory-only");
    }
    private sealed class Client : IParticipantSessionClient
    {
        private readonly string _event = Guid.NewGuid().ToString("D"), _one = Guid.NewGuid().ToString("D"), _two = Guid.NewGuid().ToString("D");
        private long _version; public string Status = "None"; public int Count; public Exception? Error;
        public Task<AppConnection> BeginAsync(CancellationToken ct) => Task.FromResult(new AppConnection(Guid.NewGuid().ToString("D"), new string('A', 43), DateTimeOffset.UtcNow.AddMinutes(10)));
        public Uri ApprovalUri(string id) => new("https://localhost:7243/Presenter/AppConnection?requestId=" + id);
        public Task<AppConnectionStatus> ConnectionAsync(AppConnection c, CancellationToken ct) => Task.FromResult(new AppConnectionStatus("Ready", c.ExpiresAt));
        public Task<IReadOnlyList<RoundEvent>> EventsAsync(AppConnection c, CancellationToken ct) => Task.FromResult<IReadOnlyList<RoundEvent>>([new(_event, "Synthetic round event")]);
        public Task SelectAsync(AppConnection c, string eventId, long version, CancellationToken ct) { _version = version; return Task.CompletedTask; }
        public Task<RoundRoster> RosterAsync(AppConnection c, string eventId, CancellationToken ct)
        {
            if (Error is not null) return Task.FromException<RoundRoster>(Error);
            RoundParticipant[] participants = [new(_one, 1, "Synthetic visitor one", Status, Status == "Granted"), new(_two, 1, "Synthetic visitor two", "None", false)];
            return Task.FromResult(new RoundRoster(_event, _version, participants.Take(Count).ToArray()));
        }
        public Task<ConsentReply> RequestConsentAsync(AppConnection c, string eventId, string participantId, string runId, CancellationToken ct)
        { Status = "Pending"; return Task.FromResult(new ConsentReply(Guid.NewGuid().ToString("D"), "Pending", "Synthetic round event", 1, "Synthetic visitor one", DateTimeOffset.UtcNow.AddMinutes(2))); }
        public Task<Uri> OpenAsync(AppConnection c, string eventId, string participantId, CancellationToken ct) => Task.FromResult(new Uri("https://localhost:7243/SessionDemo/" + Guid.NewGuid().ToString("D") + "#open=" + new string('B', 43)));
        public Task DisconnectAsync(AppConnection c, CancellationToken ct) => Task.CompletedTask;
        public Task CancelAsync(AppConnection c, string runId, CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { }
    }
}
