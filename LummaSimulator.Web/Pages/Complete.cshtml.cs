using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LummaSimulator.Web.Pages;
public class CompleteModel : PageModel
{
    public IActionResult OnGet() => TempData["DemoDeleted"] as string == "yes" ? Page() : RedirectToPage("/Index");
}
