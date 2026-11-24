using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-management")]
public class AuditModel(PresenterService service) : PageModel {
    public IReadOnlyList<PresenterAudit> Entries { get; private set; }=[];
    public async Task OnGetAsync(long before=0) => Entries=await service.AuditAsync(User,before,HttpContext.RequestAborted);
}
