using System.Net;
using System.Text.Json;
using LummaSimulator.App;
using LummaSimulator.App.Models;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

internal static class EventParticipantsChecks
{
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }
    public static async Task RunAsync()
    {
        await TransportAsync();
        var client = new FakeClient(); var browsers = new List<Uri>();
        var participants = new EventParticipantsViewModel(client, browsers.Add);
        var simulation = new CountingSimulation();
        var vm = new MainViewModel(simulation, new MemoryLog(), eventParticipants: participants);
        Check(!vm.EventEditionEnabled && vm.StartCommand.CanExecute(null) && client.Requests == 0, "Offline mode remains the default and creates no participant connection");
        vm.EventEditionEnabled = true;
        Check(!vm.StartCommand.CanExecute(null), "Participant mode requires an event and active participant before Start");
        vm.StartCommand.Execute(null);
        Check(!vm.IsRunning && simulation.Calls == 0, "Programmatic Start cannot bypass participant selection");
        await participants.ConnectAsync();
        Check(browsers.Count == 1 && browsers[0].AbsolutePath == "/Presenter/AppConnection" && !browsers[0].AbsoluteUri.Contains(client.Connection.Key), "App sign-in opens the fixed approval page without exposing its app key");
        participants.SelectedEvent = participants.Events[0];
        await Until(() => participants.CanEdit);
        Check(participants.Participants.Count == 2 && participants.Participants.All(p => p.Round == 1 && !p.CanOpen)
            && participants.Participants.Select(p => p.Alias).Distinct().Count() == 2, "Event Edition lists two round aliases with separate consent status");
        Check((await participants.ReadSelectedEventAsync(default))?.Id==participants.SelectedEvent?.Id,"Paired helper reads the validated current WPF event without selecting a participant or granting consent");
        DesktopLanguage.Current.Select("en-GB");
        Check(participants.Participants[0].ConsentText == "Not requested", "Participant consent status uses the English desktop resources");
        DesktopLanguage.Current.Select("nl-NL");
        Check(participants.Participants[0].ConsentText == "Niet gevraagd", "Participant consent status switches to Dutch without changing identity");
        participants.SelectedParticipant = participants.Participants[0];
        vm.StartCommand.Execute(null);
        await Until(() => participants.IsWaiting && client.Requests == 1);
        Check(simulation.Calls == 0 && !participants.CanOpen && !participants.CanEdit && Guid.TryParseExact(client.LastRun, "D", out _), "Start generates the run ID and waits for explicit visitor consent before any phase");
        participants.SelectedParticipant = participants.Participants[1];
        Check(participants.SelectedParticipant?.Id == client.FirstId, "Selection cannot change while visitor consent is pending");
        client.FirstStatus = "Granted";
        await Until(() => simulation.Calls == 1 && participants.CanOpen);
        await participants.OpenAsync();
        Check(browsers.Count == 2 && browsers[1].AbsolutePath.StartsWith("/SessionDemo/", StringComparison.Ordinal)
            && browsers[1].Query == "" && browsers[1].Fragment.StartsWith("#open=", StringComparison.Ordinal), "Only the approved run opens a temporary synthetic viewer with no manual code entry");
        client.FirstStatus = "Revoked"; await participants.RefreshAsync();
        await Until(() => !vm.IsRunning);
        Check(!participants.CanOpen && simulation.Calls == 1 && vm.Status == "Stopped" && client.CancelledRuns.Contains(client.LastRun!), "Withdrawal disables Open, cancels the running simulation and revokes its run grant");
        await participants.OpenAsync();
        Check(browsers.Count == 2, "Withdrawn consent cannot open a new browser view");
        vm.ResetCommand.Execute(null);
        participants.SelectedParticipant = participants.Participants.Single(p => p.Id == client.SecondId);
        client.DeclineSecond = true;
        vm.StartCommand.Execute(null); await Until(() => !vm.IsRunning);
        Check(simulation.Calls == 1 && vm.Status == "Stopped · no visitor consent" && !participants.CanOpen, "The declining second alias starts no phase and cannot be opened");
        vm.ResetCommand.Execute(null); simulation.Hold = false;
        participants.SelectedParticipant = participants.Participants.Single(p => p.Id == client.FirstId);
        client.AutoGrantFirst = true;
        vm.StartCommand.Execute(null); await Until(() => !vm.IsRunning);
        Check(simulation.Calls == 7 && vm.Status == "Completed" && participants.CanOpen, "Approved participant mode retains all six safe simulation phases");
        vm.ResetCommand.Execute(null); await Until(() => participants.CanEdit);
        Check(!participants.CanOpen && client.CancelledRuns.Contains(client.LastRun!), "Reset immediately closes local access and cancels the temporary server run");
        participants.SelectedEvent = participants.Events[1]; await Until(() => participants.CanEdit);
        Check(participants.Participants.Count == 1 && participants.Participants[0].Alias == "Foreign demo alias" && !participants.CanOpen, "Changing event replaces its participant scope and never inherits consent");
        client.MixedRoster = true; await participants.RefreshAsync();
        Check(participants.Participants.Count == 0 && !participants.CanStart && !participants.CanOpen, "A mismatched event response clears the list and fails closed");
        Check(vm.Entries.All(e => !e.Message.Contains(client.Connection.Key)), "Participant keys and synthetic open tokens are absent from simulation logs");
        await vm.ShutdownAsync();
        Check(client.Disposed && client.Disconnects == 1 && !participants.ConnectCommand.CanExecute(null), "Shutdown waits for participant polling and disconnects before disposing transport");

        var pendingClient = new FakeClient { PendingConnection = true };
        var pending = new EventParticipantsViewModel(pendingClient, _ => { });
        var connecting = pending.ConnectAsync();
        await pending.StopAndDisconnectAsync(); await connecting;
        Check(pendingClient.Disposed && pendingClient.Disconnects == 1 && !pending.IsConnected, "Closing during browser approval cancels the pending connection without late resurrection");
        var unavailableClient=new FakeClient{DeclineSecond=true,FailCancel=true};
        var unavailable=new EventParticipantsViewModel(unavailableClient,_=>{});
        await unavailable.ConnectAsync();unavailable.SelectedEvent=unavailable.Events[0];await Until(()=>unavailable.CanEdit);
        unavailable.SelectedParticipant=unavailable.Participants.Single(p=>p.Id==unavailableClient.SecondId);
        Check(!await unavailable.WaitForConsentAsync(Guid.NewGuid().ToString("D"),default)&&!unavailable.IsWaiting&&unavailable.CanEdit&&!unavailable.CanOpen,
            "Failed cancellation leaves no local access and never strands the app in a permanent consent wait");
        await unavailable.StopAndDisconnectAsync();
    }
    private static async Task TransportAsync()
    {
        var id = Guid.NewGuid().ToString("D"); var eventId = Guid.NewGuid().ToString("D"); var participantId = Guid.NewGuid().ToString("D");
        var connection = new AppConnection(id, new string('A', 43), DateTimeOffset.UtcNow.AddMinutes(10));
        using var transport = new ParticipantSessionClient("https://localhost:7243", new Handler(request =>
        {
            Check(request.RequestUri!.AbsolutePath == $"/api/local/participant-demo/connections/{id}/events/{eventId}/participants"
                && request.Headers.GetValues("X-Lumma-App-Key").Single() == connection.Key && request.Headers.GetValues("X-Lumma-App-Protocol").Single() == "1"
                && !request.Headers.Contains("Cookie") && !request.Headers.Contains("Origin"), "Participant client uses the scoped native contract and a separate app key, never visitor cookies");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new RoundRoster(eventId, 1, [new(participantId, 1, "Synthetic alias", "None", false)]))) };
        }));
        Check((await transport.RosterAsync(connection, eventId, default)).Participants.Count == 1, "Participant client validates its bounded typed roster");
        foreach (var invalid in new[] { "http://localhost:7243", "https://other.invalid", "https://localhost:7243/path", "https://localhost:7243?code=synthetic" })
        {
            var rejected = false; try { using var client = new ParticipantSessionClient(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Participant client rejects a noncanonical or unapproved portal origin");
        }
        using var mixed = new ParticipantSessionClient("https://localhost:7243", new Handler(_ => new(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new RoundRoster(Guid.NewGuid().ToString("D"), 1, []))) }));
        var denied = false; try { await mixed.RosterAsync(connection, eventId, default); } catch (System.IO.InvalidDataException) { denied = true; }
        Check(denied, "Participant transport refuses a response belonging to another event");
        using var empty = new ParticipantSessionClient("https://localhost:7243", new Handler(_ => new(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new RoundRoster(eventId, 1, []))) }));
        Check((await empty.RosterAsync(connection, eventId, default)).Participants.Count == 0, "Participant transport preserves an actual successful empty response");
        using var refused = new ParticipantSessionClient("https://localhost:7243", new Handler(_ => new(HttpStatusCode.Forbidden)));
        var forbidden = false; try { await refused.RosterAsync(connection, eventId, default); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden) { forbidden = true; }
        Check(forbidden, "Participant transport preserves HTTP refusal as a failure rather than an empty roster");
        foreach (var payload in new[] { "{\"eventId\":\"" + eventId + "\",\"selectionVersion\":1,\"participants\":null}",
            "{\"eventId\":\"" + eventId + "\",\"selectionVersion\":1,\"participants\":[null]}" })
        {
            using var invalid = new ParticipantSessionClient("https://localhost:7243", new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload) }));
            var invalidReply = false; try { await invalid.RosterAsync(connection, eventId, default); } catch (System.IO.InvalidDataException) { invalidReply = true; }
            Check(invalidReply, "Missing participant data is reported as an invalid response, never an empty roster");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
    private sealed class CountingSimulation : ISimulationService
    {
        public int Calls; public bool Hold = true;
        public async Task ExecuteAsync(int phase, Action<string> report, CancellationToken ct)
        { Calls++; if (Hold) await Task.Delay(Timeout.Infinite, ct); }
    }
    private sealed class MemoryLog : IJsonLogService
    {
        public string DirectoryPath => System.IO.Path.GetTempPath();
        public Task<string> SaveAsync(string run, string status, IEnumerable<LogEntry> entries, IEnumerable<SimulationPhase> phases) => Task.FromResult("memory-only");
    }
    private sealed class FakeClient : IParticipantSessionClient
    {
        public AppConnection Connection = new(Guid.NewGuid().ToString("D"), new string('A', 43), DateTimeOffset.UtcNow.AddMinutes(10));
        public readonly string FirstId = Guid.NewGuid().ToString("D"), SecondId = Guid.NewGuid().ToString("D");
        private readonly RoundEvent[] _events = [new(Guid.NewGuid().ToString("D"), "Synthetic event A"), new(Guid.NewGuid().ToString("D"), "Synthetic event B")];
        public string FirstStatus = "None", SecondStatus = "None";
        public int Requests, Disconnects; public string? LastRun; public bool DeclineSecond, AutoGrantFirst, MixedRoster, PendingConnection, Disposed, FailCancel;
        public List<string> CancelledRuns = []; private long _version;
        public Task<AppConnection> BeginAsync(CancellationToken ct) => Task.FromResult(Connection);
        public Uri ApprovalUri(string id) => new("https://localhost:7243/Presenter/AppConnection?requestId=" + id);
        public Task<AppConnectionStatus> ConnectionAsync(AppConnection c, CancellationToken ct) => Task.FromResult(new AppConnectionStatus(PendingConnection ? "Pending" : "Ready", c.ExpiresAt));
        public Task<IReadOnlyList<RoundEvent>> EventsAsync(AppConnection c, CancellationToken ct) => Task.FromResult<IReadOnlyList<RoundEvent>>(_events);
        public Task SelectAsync(AppConnection c, string eventId, long version, CancellationToken ct) { _version = version; return Task.CompletedTask; }
        public Task<RoundRoster> RosterAsync(AppConnection c, string eventId, CancellationToken ct)
        {
            IReadOnlyList<RoundParticipant> rows = eventId == _events[0].Id
                ? [new(FirstId, 1, "Synthetic alias one", FirstStatus, FirstStatus == "Granted"), new(SecondId, 1, "Synthetic alias two", SecondStatus, false)]
                : [new(Guid.NewGuid().ToString("D"), 1, "Foreign demo alias", "None", false)];
            return Task.FromResult(new RoundRoster(MixedRoster ? _events[0].Id : eventId, _version, rows));
        }
        public Task<ConsentReply> RequestConsentAsync(AppConnection c, string eventId, string participantId, string runId, CancellationToken ct)
        {
            Requests++; LastRun = runId;
            if (participantId == FirstId) FirstStatus = AutoGrantFirst ? "Granted" : "Pending";
            else SecondStatus = DeclineSecond ? "Declined" : "Pending";
            return Task.FromResult(new ConsentReply(Guid.NewGuid().ToString("D"), "Pending", "Synthetic event A", 1, participantId == FirstId ? "Synthetic alias one" : "Synthetic alias two", DateTimeOffset.UtcNow.AddMinutes(2)));
        }
        public Task<Uri> OpenAsync(AppConnection c, string eventId, string participantId, CancellationToken ct)
        {
            if (eventId != _events[0].Id || participantId != FirstId || FirstStatus != "Granted") throw new InvalidOperationException();
            return Task.FromResult(new Uri("https://localhost:7243/SessionDemo/" + Guid.NewGuid().ToString("D") + "#open=" + new string('B', 43)));
        }
        public Task CancelAsync(AppConnection c, string runId, CancellationToken ct) { CancelledRuns.Add(runId); return FailCancel?Task.FromException(new HttpRequestException("Synthetic cancellation failure")):Task.CompletedTask; }
        public Task DisconnectAsync(AppConnection c, CancellationToken ct) { Disconnects++; return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}
