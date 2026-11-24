using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LummaSimulator.Web.Pages.Downloads;

public sealed class PresenterHelperModel(HelperDownload download) : PageModel
{
    public HelperPackage? Package { get; private set; }
    public void OnGet()=>Package=download.Current();
    public IActionResult OnGetZip()
    {
        var package=download.Current();
        return package is null?NotFound():new PhysicalFileResult(package.Path,"application/zip")
        {FileDownloadName=package.FileName,EnableRangeProcessing=true};
    }
}
