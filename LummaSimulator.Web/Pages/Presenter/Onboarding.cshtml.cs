using LummaSimulator.Web.Services;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Pages.Presenter;
[Authorize(Policy=PresenterService.AdminPolicy)]
[EnableRateLimiting("presenter-management")]
public sealed class OnboardingModel(PresenterOnboarding onboarding,PresenterService presenters,ILogger<OnboardingModel> logger) : PageModel
{
    public OnboardingState State {get;private set;}=new(false,false,false,null);
    public int Keys {get;private set;}
    public string? ErrorKey {get;private set;}
    public bool StorageUnavailable {get;private set;}
    public string EventInput {get;private set;}="";
    public bool CertificateChecked {get;private set;}
    public bool Confirmed {get;private set;}

    private async Task LoadAsync()
    {
        State=await onboarding.StateAsync(HttpContext.RequestAborted);
        if(!State.Existing&&!State.Completed)Keys=(await presenters.KeysAsync(User,null,HttpContext.RequestAborted)).Count;
    }

    private void StorageError(Exception error)
    {
        logger.LogError("Onboarding storage operation failed. ErrorType={ErrorType} SqliteCode={SqliteCode}",error.GetType().Name,(error as SqliteException)?.SqliteErrorCode);
        StorageUnavailable=true;
        ErrorKey="OnboardingStorageError";
        Response.StatusCode=503;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        try { await LoadAsync(); }
        catch(Exception error) when(error is SqliteException or IOException or UnauthorizedAccessException){StorageError(error);return Page();}
        if(State.Existing||State.Completed)return RedirectToPage("/Presenter/Events/Index");
        PresenterEndpoints.Browser(HttpContext);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? action,string? name,bool certificateConfirmed,bool confirmed)
    {
        var interactive=Request.Headers["X-Lumma-Onboarding"]=="1";
        EventInput=name??"";
        CertificateChecked=certificateConfirmed;
        Confirmed=confirmed;
        try
        {
            await LoadAsync();
            if(State.Existing||State.Completed)return RedirectToPage("/Presenter/Events/Index");
            if(!ModelState.IsValid||!confirmed||action=="hosting"&&!certificateConfirmed)
                return Failure(400,action=="hosting"?"OnboardingHostingInput":"OnboardingConfirmInput");
            if(action=="finish"&&!State.Ready)return Failure(409,"OnboardingRequirementsMissing");
            await onboarding.ChangeAsync(User,action??"",EventInput,Request.IsHttps,certificateConfirmed,HttpContext.RequestAborted);
            if(action=="finish")return RedirectToPage("/Presenter/Events/Index");
            TempData["OnboardingSuccess"]=action=="hosting"?"OnboardingHostingSaved":"OnboardingEventSaved";
            return RedirectToPage();
        }
        catch(DemoRequestException error)
        {
            var key=error.Message switch {
                "PresenterReauthenticate"=>"PresenterReauthenticate",
                "PresenterDenied"=>"OnboardingAdminRequired",
                _ when action=="event"=>"OnboardingEventInput",
                _ when action=="finish"=>"OnboardingRequirementsMissing",
                _=>"OnboardingHostingInput"
            };
            return Failure(key=="OnboardingAdminRequired"?403:key=="PresenterReauthenticate"?409:400,key);
        }
        catch(Exception error) when(error is SqliteException or IOException or UnauthorizedAccessException)
        {
            StorageError(error);
            return Failure(503,"OnboardingStorageError");
        }

        IActionResult Failure(int status,string key)
        {
            if(interactive)return new JsonResult(new{errorKey=key}){StatusCode=status};
            Response.StatusCode=status;
            ErrorKey=key;
            return Page();
        }
    }
}
