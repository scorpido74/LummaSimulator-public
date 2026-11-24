using System.Text.Json;
using System.IO;
using LummaSimulator.Configuration;

namespace LummaSimulator.App.Services;

public sealed class DesktopSettings
{
    public string OrganizationName { get; set; } = "Infinigate";
    public string? LogoFile { get; set; }
    public string DefaultLanguage { get; set; } = "nl-NL";
    public string? DataDirectory { get; set; }
    public string? LogDirectory { get; set; }
    public string? ExportDirectory { get; set; }
    public string PortalOrigin { get; set; } = "https://localhost:7243";
    public bool UseRemotePortal { get; set; }
    public static DesktopSettings Load()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "LummaSimulator.App.settings.json");
        var settings = File.Exists(file) ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new() : new();
        // No raw key in settings, logs or exports. MonitorIntegrationSettings resolves DPAPI or explicit environment override.
        settings.PortalOrigin = Environment.GetEnvironmentVariable("LUMMA_PORTAL_ORIGIN") ?? settings.PortalOrigin;
        if (!string.IsNullOrWhiteSpace(settings.DataDirectory) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMMA_DATA_DIRECTORY")))
            Environment.SetEnvironmentVariable("LUMMA_DATA_DIRECTORY", RuntimePaths.DataRoot(settings.DataDirectory));
        return settings;
    }
}
