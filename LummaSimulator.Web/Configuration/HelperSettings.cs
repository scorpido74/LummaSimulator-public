namespace LummaSimulator.Web.Configuration;

// Provisioned by the installation procedure; never infer the app host from a visitor request.
public sealed class HelperSettings
{
    public bool Enabled { get; set; }
    public string? DownloadUrl { get; set; }
    public string? InstallationGuideUrl { get; set; }
    public static bool ValidLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.IsDefaultPort;
}
