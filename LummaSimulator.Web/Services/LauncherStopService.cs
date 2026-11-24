using LummaSimulator.Configuration;

namespace LummaSimulator.Web.Services;
// Local user-owned launcher signalling only. No HTTP endpoint, listener or process lookup.
public sealed class LauncherStopService(IConfiguration configuration, IHostApplicationLifetime lifetime, ILogger<LauncherStopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var requested = configuration["LUMMA_PORTAL_STOP_FILE"];
        if (string.IsNullOrEmpty(requested)) return;
        try {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LummaSimulator", "LauncherControl-v1");
            var path = UserPrivateFiles.LocalPath(requested);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)
                || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "\\Aportal-stop-[a-f0-9]{32}\\.request\\z")) throw new IOException("LauncherControl");
            UserPrivateFiles.Verify(root, true);
            while (!stoppingToken.IsCancellationRequested) {
                if (File.Exists(path)) {
                    UserPrivateFiles.Verify(path, false);
                    if (new FileInfo(path).Length != 1 || File.ReadAllBytes(path)[0] != 1) throw new IOException("LauncherControl");
                    lifetime.StopApplication(); return;
                }
                await Task.Delay(250, stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception) { logger.LogWarning("Local launcher control is unavailable; no forced shutdown performed."); }
    }
}
