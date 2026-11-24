using LummaSimulator.Web.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LummaSimulator.Web.Pages.Presenter;
public sealed class HelperModel(IConfiguration configuration,LummaSimulator.Web.Services.HelperDownload download) : PageModel
{
    public string? DownloadUrl { get; private set; }
    public string? GuideUrl { get; private set; }
    public void OnGet() {
        var settings = configuration.GetSection("Helper").Get<HelperSettings>() ?? new();
        if(download.Current() is not null)DownloadUrl=Url.Page("/Downloads/PresenterHelper");
        else if(settings.Enabled && HelperSettings.ValidLink(settings.DownloadUrl)) DownloadUrl=settings.DownloadUrl;
        if (HelperSettings.ValidLink(settings.InstallationGuideUrl)) GuideUrl = settings.InstallationGuideUrl;
    }
}
