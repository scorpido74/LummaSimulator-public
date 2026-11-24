using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LummaSimulator.App;
using LummaSimulator.App.Models;
using LummaSimulator.App.ViewModels;
using LummaSimulator.App.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (args.Contains("--event-participants-ui-checks")) { await EventParticipantsUiChecks.RunAsync(); return; }
                if (args.Contains("--presenter-helper-ui-checks")) { await PresenterHelperUiChecks.RunAsync(); return; }
                await WindowShutdownChecks.RunAsync();
                if (args.Contains("--shutdown-only")) return;
                await EventParticipantsUiChecks.RunAsync();
                await PresenterHelperUiChecks.RunAsync();
                var fixtureDirectory = Path.Combine(Path.GetTempPath(), "LummaWpfVerification", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixtureDirectory);
                var window = new MainWindow(new MainViewModel(new SimulationService(), new JsonLogService(fixtureDirectory)));
                window.Show();
                var vm = (MainViewModel)window.DataContext;
                Check(window.Title.Contains("Lumma Attack Simulator"), "Window title");
                Check(vm.Phases.Count == 6, "Six phases");
                Check(window.Icon is not null && window.ResellerLogo is not null && window.OrganizationName == "Infinigate", "Embedded neutral icon and default local Infinigate branding load");
                var language = DesktopLanguage.Current;
                language.Select("nl-NL");
                Check(language.Translate("Completed") == "Voltooid" && language.Translate("User Execution") == "Uitvoering door gebruiker", "Dutch status and phase rendering");
                language.Select("en-GB");
                Check(language.Translate("Van phishing naar sessierisico") == "From phishing to session risk", "English scenario rendering");
                Check(vm.Phases[0].TechniqueId == "T1204" && vm.Status == "Ready" && !vm.IsRunning, "Language selection does not change canonical state or start activity");
                vm.ResetCommand.Execute(null);
                Check(language.IsEnglish, "Language survives demo reset without a new preference file");
                language.Select("nl-NL");
                window.UpdateLayout();
                var introduction = (System.Windows.Controls.Border)window.FindName("ScenarioIntroduction");
                var overview = (System.Windows.Controls.ItemsControl)window.FindName("ScenarioPhaseOverview");
                var roles = (System.Windows.Controls.ItemsControl)window.FindName("ScenarioRoles");
                var startScenario = (System.Windows.Controls.Button)window.FindName("StartScenarioButton");
                Check(introduction.IsVisible && !vm.IsRunning && vm.DemoRunId == "", "Scenario introduction is initial view and performs no automatic execution");
                Check(ReferenceEquals(overview.ItemsSource, vm.Phases) && overview.Items.Count == 6, "Scenario overview reuses actual six phases and ATT&CK mapping");
                Check(roles.Items.Count == 3 && vm.Scenario.LearningPoints.Count >= 3 && !string.IsNullOrWhiteSpace(vm.Scenario.AttackerGoal), "Scenario presents three roles, attacker goal and learning points");
                Check(ReferenceEquals(startScenario.Command, vm.StartCommand), "Scenario start uses existing guarded command");
                var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                preview.Render(window);
                var image = new System.Windows.Media.Imaging.PngBitmapEncoder();
                image.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
                using (var output = File.Create(Path.Combine(fixtureDirectory, "scenario-introduction.png"))) image.Save(output);
                Console.WriteLine("Scenario preview: " + Path.Combine(fixtureDirectory, "scenario-introduction.png"));
                vm.ContinueToControlsCommand.Execute(null); window.UpdateLayout();
                Check(!introduction.IsVisible && !vm.IsRunning, "Presenter can choose existing controls without starting a scenario");
                foreach (var pixels in new[] { (1366d,768d), (1920d,1080d), (2560d,1440d) })
                foreach (var scale in new[] { 1d,1.25d,1.5d,2d })
                {
                    // Effective DIP viewport only; this does not change Windows DPI or emulate a monitor.
                    window.Width = pixels.Item1 / scale; window.Height = pixels.Item2 / scale;
                    window.UpdateLayout();
                    var expected = window.ActualWidth - 64;
                    Check(window.PhaseColumns == (expected >= 1150 ? 3 : expected >= 760 ? 2 : 1), "Responsive phase columns at effective DIP viewport " + pixels + " / " + scale);
                    Check(window.ResizeMode == ResizeMode.CanResize && window.UseLayoutRounding,
                        "Resizable DIP layout retains text sizing and layout rounding");
                    VerifyPhaseCardRows(window);
                    var assessment = (System.Windows.Controls.Border)window.FindName("AssessmentPanel");
                    var toggle=(System.Windows.Controls.Button)window.FindName("ToggleAssessmentButton");
                    Check(assessment.Visibility==Visibility.Collapsed,"Assessment starts closed at effective DIP viewport");
                    var workspace=(System.Windows.Controls.Grid)window.FindName("WorkspaceGrid");
                    Check(workspace.ColumnDefinitions[1].ActualWidth==0,"Closed assessment reserves no right column");
                    var phase=vm.SelectedPhase;var product=vm.SelectedProduct;
                    toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));window.UpdateLayout();
                    Check(assessment.IsVisible&&System.Windows.Controls.Grid.GetRow(assessment)==4,"Button opens the existing assessment below full-width cards");
                    toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));window.UpdateLayout();
                    Check(assessment.Visibility==Visibility.Collapsed&&vm.SelectedPhase==phase&&vm.SelectedProduct==product,"Closing assessment retains selected fields");
                }
                window.Width = 1280; window.Height = 900; window.UpdateLayout();
                vm.ShowScenarioCommand.Execute(null); window.UpdateLayout();
                Check(introduction.IsVisible, "Presenter can return to scenario introduction while idle");
                startScenario.Command.Execute(null); window.UpdateLayout();
                Check(!introduction.IsVisible && !vm.ShowScenarioCommand.CanExecute(null), "Scenario starts existing run and cannot interrupt it with overview");
                Check(vm.IsRunning && !vm.StartCommand.CanExecute(null), "Start command");
                await Task.Delay(400);
                await vm.StopAndWaitAsync();
                Check(vm.Status == "Stopped" && vm.Phases[0].Status == PhaseStatus.Failed, "Stop command");
                vm.ResetCommand.Execute(null);
                Check(vm.Progress == 0 && vm.Phases.All(p => p.Status == PhaseStatus.Pending), "Reset command");
                vm.StartCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (vm.IsRunning && DateTime.UtcNow < deadline) await Task.Delay(100);
                Check(!vm.IsRunning && vm.Status == "Completed" && vm.Progress == 100, "Full simulation");
                Check(vm.Phases.All(p => p.Status == PhaseStatus.Completed), "All phases completed");
                var latest = Directory.GetFiles(vm.LogDirectory, "*.json").OrderBy(File.GetLastWriteTimeUtc).Last();
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(latest));
                Check(json.RootElement.GetProperty("safeSimulation").GetBoolean(), "Safe JSON logging");
                Check(json.RootElement.GetProperty("status").GetString() == "Completed", "Final JSON status");
                Check(json.RootElement.GetProperty("phases").GetArrayLength() == 6, "JSON phases");
                Check(vm.Phases[0].TechniqueId == "T1204" && vm.Phases[4].TechniqueId == "T1074.001", "ATT&CK card mappings");
                Check(vm.Phases.All(p => p.Assessments.All(a => a.Detection == DetectionState.NotEvaluated)), "No automatic detections");
                vm.SelectedPhase = vm.Phases[0];
                vm.SelectedProduct = SecurityProducts.All[0];
                vm.SelectedAssessment = AssessmentKind.Observed;
                Check(!vm.ConfirmAssessmentCommand.CanExecute(null), "Explicit presenter acknowledgement required");
                vm.AssessmentConfirmed = true;
                Check(vm.ConfirmAssessmentCommand.CanExecute(null), "Explicit acknowledgement enables presenter command; no actual product confirmation is fabricated by this test");
                vm.AssessmentConfirmed = false;
                vm.ExportAttackCommand.Execute(null);
                await vm.StopAndWaitAsync();
                Check(vm.ExportStatus.StartsWith("ATT&CK layer saved:"), "WPF Navigator export command");
                var tabs = (System.Windows.Controls.TabControl)window.FindName("ScenarioTabs");
                Check(tabs.Items.Count == 4, "Independent ATT&CK, session-monitor, Event Edition and integration settings views");
                tabs.SelectedIndex = 1;
                window.UpdateLayout();
                Check(vm.SessionMonitor.State == MonitorSessionState.NotEvaluated && vm.SessionMonitor.Techniques.All(t => t.Activity == ActivityState.NotEvaluated), "Opening monitor tab performs no request or automatic session exposure");
                using var portProbe=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);
                portProbe.Start();var port=((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
                vm.IsLiveTelemetry=true;vm.LocalPort=port.ToString();
                var checkbox=(System.Windows.Controls.CheckBox)window.FindName("LocalHttpCheckBox");
                checkbox.IsChecked=true;await vm.LocalHttpUpdate;window.UpdateLayout();
                var label=(System.Windows.Controls.TextBlock)window.FindName("LocalHttpStatusLabel");
                Check(vm.LocalHttpReady && label.Text.Contains("127.0.0.1:"+port),"WPF checkbox starts receiver and renders actual readiness");
                using var http=LummaSimulator.LocalTesting.LocalTestServerProtocol.CreateClient();
                var identity=await LummaSimulator.LocalTesting.LocalTestServerProtocol.ProbeAsync(http,port,default);
                Check(identity is not null,"WPF receiver has the expected identity");
                window.Close();
                var closeDeadline=DateTime.UtcNow.AddSeconds(8);
                while(window.IsVisible && DateTime.UtcNow<closeDeadline)await Task.Delay(20);
                var exited=false;try {using var child=System.Diagnostics.Process.GetProcessById(identity!.Pid);exited=child.HasExited;}catch(ArgumentException){exited=true;}
                Check(!window.IsVisible && exited && !vm.LocalHttpReady,"Closing an idle WPF window waits for receiver shutdown and leaves no child process");
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }

    internal static void VerifyPhaseCardRows(MainWindow window)
    {
        var cardsControl=(System.Windows.Controls.ItemsControl)window.FindName("PhaseCardsControl");
        var cards=Descendants<System.Windows.Controls.Border>(cardsControl).Where(c=>c.Name=="Card").ToArray();
        Check(cards.Length==6,"Six phase cards remain present");
        foreach(var expanded in new[]{false,true,false}){
            foreach(var details in Descendants<System.Windows.Controls.Expander>(cardsControl).Where(e=>e.Name=="PhaseDetailsExpander"))details.IsExpanded=expanded;
            window.UpdateLayout();
            foreach(var row in cards.Chunk(window.PhaseColumns)){
                var firstHeight=row[0].ActualHeight;var bottoms=new List<double>();
                foreach(var card in row){Check(Math.Abs(card.ActualHeight-firstHeight)<1,"Phase cards share row height with details open or closed");
                    var details=Descendants<System.Windows.Controls.Expander>(card).Single(e=>e.Name=="PhaseDetailsExpander");
                    var header=Descendants<System.Windows.Controls.Primitives.ToggleButton>(details).First();
                    bottoms.Add(header.TransformToAncestor(cardsControl).Transform(new Point(0,header.ActualHeight)).Y);
                }
                Check(bottoms.Max()-bottoms.Min()<1,"Phase detail headers align at the row bottom");
            }
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }
}
