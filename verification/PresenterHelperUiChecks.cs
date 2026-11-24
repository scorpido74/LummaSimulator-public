using System.IO;
using System.Windows.Controls;
using LummaSimulator.Helper;

internal static class PresenterHelperUiChecks
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LummaHelperUi", Guid.NewGuid().ToString("N"));
        var window = new LummaSimulator.PresenterHelper.MainWindow([], new HelperUserStore(root)); window.Show();
        try {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var status = (TextBlock)window.FindName("Status"); var open = (Button)window.FindName("OpenButton");
            if (!window.IsVisible || string.IsNullOrWhiteSpace(status.Text) || open.IsEnabled) throw new InvalidOperationException("Offline helper must show a connection status and block a linked demo.");
            Console.WriteLine("PASS: Actual helper window starts offline with visible status and no linked-demo authority");
            if (((TextBox)window.FindName("Portal")).Text != "https://localhost" || !((Button)window.FindName("LoginButton")).IsEnabled) throw new InvalidOperationException("Presenter login must remain available while the app is offline.");
            Console.WriteLine("PASS: Helper retains portal login while the app receiver is unconfigured/unreachable");
            if(((System.Windows.FrameworkElement)window.FindName("ParticipantQrPanel")).IsVisible
                ||((Image)window.FindName("ParticipantQrImage")).Source is not null||((Button)window.FindName("CopyParticipantLink")).IsEnabled)
                throw new InvalidOperationException("Offline helper must remove the participant QR and its copy action.");
            Console.WriteLine("PASS: Offline helper shows no stale participant QR or copyable link");
            if (File.Exists(Path.Combine(root, "helper-pairing.dpapi"))) throw new InvalidOperationException("Startup must not fabricate pairing.");
            Console.WriteLine("PASS: First helper startup creates no local presenter, event or pairing token");
        }
        finally { window.Close(); }
    }
}
