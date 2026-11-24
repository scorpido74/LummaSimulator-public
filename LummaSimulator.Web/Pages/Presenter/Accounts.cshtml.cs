using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace LummaSimulator.Web.Pages.Presenter;
[EnableRateLimiting("presenter-management")]
public class AccountsModel(PresenterService service) : PageModel
{
    public IReadOnlyList<PresenterAccount> Accounts { get; private set; }=[];
    public IReadOnlyList<PresenterKey> Keys { get; private set; }=[];
    public string? Target { get; private set; }
    public string? IssuedCode { get; private set; }
    public string? Notice { get; private set; }
    private async Task Load(string? target) { PresenterEndpoints.Browser(HttpContext);Accounts=await service.AccountsAsync(User,HttpContext.RequestAborted);Target=target;Keys=target is null?[]:await service.KeysAsync(User,target,HttpContext.RequestAborted); }
    public Task OnGetAsync(string? target) => Load(target);
    public async Task<IActionResult> OnPostCreateAsync(string login,string name,string role)
    {
        try { IssuedCode=(await service.CreateAccountAsync(User,login??"",name??"",role??"",HttpContext.RequestAborted)).Code; }
        catch(DemoRequestException e){Notice=e.Message;}
        await Load(null);return Page();
    }
    public async Task<IActionResult> OnPostUpdateAsync(string target,string role,bool active,bool confirmed)
    {
        try { if(!confirmed)throw new DemoRequestException("PresenterConfirm");await service.SetAccountAsync(User,target??"",role??"",active,HttpContext.RequestAborted); }
        catch(DemoRequestException e){TempData["AccountNotice"]=e.Message;}
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostInviteAsync(string target,bool confirmed)
    {
        try { if(!confirmed)throw new DemoRequestException("PresenterConfirm");IssuedCode=await service.InviteAsync(User,target??"",HttpContext.RequestAborted); }
        catch(DemoRequestException e){Notice=e.Message;}
        await Load(target);return Page();
    }
    public async Task<IActionResult> OnPostRemoveAsync(string target,string key,bool confirmed)
    {
        try { if(!confirmed)throw new DemoRequestException("PresenterConfirm");await service.RemoveKeyAsync(User,target??"",key??"",HttpContext.RequestAborted); }
        catch(DemoRequestException e){TempData["AccountNotice"]=e.Message;}
        return RedirectToPage(new{target});
    }
}
