using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages;
[EnableRateLimiting("demo")]
public sealed class SessionDemoModel:PageModel
{
    public string ViewId {get;private set;}="";
    public IActionResult OnGet(string id) { if(!Guid.TryParseExact(id,"D",out _))return NotFound();ViewId=id;return Page(); }
}
