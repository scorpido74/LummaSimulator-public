using LummaSimulator.App.ViewModels;
using LummaSimulator.App.Services;
using System.Net;
using System.Text.Json;

internal static class EventEditionChecks
{
    public static async Task RunAsync()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
        var fake = new FakeClient();
        using var view = new EventEditionViewModel(fake);
        var run = Guid.NewGuid().ToString("D"); var portal = Guid.NewGuid().ToString("D");
        view.SetRun(run); view.Code = new string('A', 43);
        fake.Value = new(run, portal, Guid.NewGuid().ToString("D"), "Synthetic event", "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null);
        using (var transport = new EventEditionClient("https://localhost:7243", new TestHandler(async request => {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/event-edition/poll/" + run
                && request.Headers.GetValues("X-Lumma-Event-Code").Single() == new string('A', 43) && !request.Headers.Contains("Cookie"), "Event client sends only run-scoped GET and separate code, never visitor cookies");
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(fake.Value)) });
        }))) Check((await transport.PollAsync(run, new string('A', 43), default)).PortalRunId == portal, "Event client validates bounded typed response");
        using (var mixed = new EventEditionClient("https://localhost:7243", new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(fake.Value with { WpfRunId = Guid.NewGuid().ToString("D") })) })))) {
            var rejected = false; try { await mixed.PollAsync(run, new string('A', 43), default); } catch { rejected = true; }
            Check(rejected, "Event client refuses data from another WPF run");
        }
        foreach (var origin in new[] { "http://localhost:7243", "https://other.invalid", "https://localhost:7243/path", "https://localhost:7243?key=synthetic" }) {
            var rejected = false; try { using var invalid = new EventEditionClient(origin); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Event client refuses insecure or non-allowlisted origin");
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var waiting = view.WaitForApprovalAsync(cancel.Token);
        await Task.Delay(30);
        Check(!waiting.IsCompleted && !view.CanApprove, "Event gate never automatically advances without a victim choice");
        fake.Value = fake.Value with { Action = new(portal, "ClickFixFollowSimulated", 1, DateTimeOffset.UtcNow) };
        await view.RefreshAsync(cancel.Token);
        view.Consent = true;
        Check(view.CanApprove && !waiting.IsCompleted, "Received victim action still requires explicit operator approval");
        var text = view.ActionText;
        await view.RefreshAsync(cancel.Token); Check(text == view.ActionText, "Duplicate polling does not reprocess an action");
        fake.Fail = true; await view.RefreshAsync(cancel.Token);
        Check(!view.CanApprove && view.CanFallback, "Network loss cannot turn stale data into current server confirmation");
        fake.Fail = false; await view.RefreshAsync(cancel.Token);
        Check(view.CanApprove && !view.CanFallback, "Temporary network recovery restores checked action without duplication");
        view.ApproveCommand.Execute(null);
        Check(await waiting == "VictimChoiceConfirmed", "Explicit confirmation releases the event gate");
        await view.RefreshAsync(cancel.Token);
        Check(view.ConfirmationText.Contains("handmatig bevestigd"), "Later polling cannot erase explicit presenter confirmation");
        view.SetRun(Guid.NewGuid().ToString("D"));
        Check(!view.ActionText.Contains("ClickFixFollowSimulated"), "New WPF run clears previous visitor state and code");
        var fallback = view.WaitForApprovalAsync(cancel.Token);
        view.Consent = true; view.FallbackCommand.Execute(null);
        Check(await fallback == "ManualFallback" && view.Notice.Contains("geen slachtofferactie"), "Explicit fallback is labeled separately from received victim activity");

        var recorder = new CountingSimulation();
        var vm = new MainViewModel(recorder, new JsonLogService(), eventEdition: new EventEditionViewModel(new FakeClient { Fail = true })) { EventEditionEnabled = true, LegacyEventLinkEnabled = true };
        vm.PrepareEventRunCommand.Execute(null); var prepared = vm.EventEdition.RunId;
        vm.StartCommand.Execute(null);
        while (!vm.EventEdition.IsWaiting) await Task.Delay(20, cancel.Token);
        Check(vm.DemoRunId == prepared && vm.IsRunning && recorder.Calls == 0, "WPF prepares stable paired run and performs no phase/endpoint action before approval");
        vm.EventEdition.Consent = true; vm.EventEdition.FallbackCommand.Execute(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (vm.IsRunning) await Task.Delay(20, timeout.Token);
        Check(recorder.Calls == 6 && vm.Status == "Completed", "Explicit manual fallback retains all six existing phases");
        Check(vm.Entries.Any(e => e.Message.Contains("Fictieve Lumma Stealer-installatie"))
            && vm.Entries.Any(e => e.Message.Contains("Onthulling:")), "Installation and reveal are explicitly fictional presentation records");
        vm.ResetCommand.Execute(null);
        Check(vm.EventEdition.RunId == "" && vm.DemoRunId == "", "Existing reset also clears Event Edition run state");
        await vm.ShutdownAsync();

        var stoppedClient = new FakeClient();
        var stoppedRecorder = new CountingSimulation();
        var stoppedVm = new MainViewModel(stoppedRecorder, new JsonLogService(), eventEdition: new EventEditionViewModel(stoppedClient));
        stoppedVm.IsLiveTelemetry = true;
        stoppedVm.EventEditionEnabled = true;
        stoppedVm.LegacyEventLinkEnabled = true;
        Check(!stoppedVm.IsLiveTelemetry, "Enabling paired ClickFix clears LIVE mode");
        stoppedVm.IsLiveTelemetry = true;
        Check(!stoppedVm.IsLiveTelemetry, "Paired ClickFix cannot enable LIVE mode");
        stoppedVm.PrepareEventRunCommand.Execute(null);
        var stoppedRun = stoppedVm.EventEdition.RunId;
        stoppedClient.Value = new(stoppedRun, portal, Guid.NewGuid().ToString("D"), "Synthetic stop", "Active", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), new(portal, "ClickFixStopSelected", 1, DateTimeOffset.UtcNow));
        stoppedVm.EventEdition.Code = new string('A', 43);
        stoppedVm.StartCommand.Execute(null);
        while (!stoppedVm.EventEdition.IsWaiting) await Task.Delay(20, cancel.Token);
        await stoppedVm.EventEdition.RefreshAsync(cancel.Token);
        stoppedVm.EventEdition.Consent = true;
        stoppedVm.EventEdition.ApproveCommand.Execute(null);
        while (stoppedVm.IsRunning) await Task.Delay(20, cancel.Token);
        Check(stoppedRecorder.Calls == 0 && stoppedVm.Status.StartsWith("Stopped"), "Presenter-confirmed visitor stop starts neither installation nor phases");
        await stoppedVm.ShutdownAsync();
    }
    private sealed class FakeClient : IEventEditionClient
    {
        public EventSnapshot? Value; public bool Fail;
        public Task<EventSnapshot> PollAsync(string run, string code, CancellationToken ct) => Fail ? Task.FromException<EventSnapshot>(new HttpRequestException()) : Task.FromResult(Value!);
        public void Dispose() { }
    }
    private sealed class TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class CountingSimulation : ISimulationService
    {
        public int Calls;
        public Task ExecuteAsync(int phase, Action<string> report, CancellationToken ct) { Calls++; return Task.CompletedTask; }
    }
}
