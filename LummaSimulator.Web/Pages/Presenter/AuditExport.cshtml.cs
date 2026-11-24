using LummaSimulator.Web.Services.AuditExport;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LummaSimulator.Web.Pages.Presenter;
[Authorize(Policy=PresenterService.Policy)]
[EnableRateLimiting("presenter-management")]
public sealed class AuditExportModel(IdentityAuditExportStatusStore status) : PageModel
{
    public IReadOnlyList<IdentityAuditExportStatus> Exporters {get;private set;}=[];
    public void OnGet() => Exporters=status.Snapshot();
}
