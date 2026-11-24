using System.Diagnostics;
using System.Text.Json;

namespace LummaSimulator.Web.Tests;

internal static class PrizeBrowserChecks
{
    internal static async Task RunAsync(string repository,string temporaryRoot,string origin,string cookie,string spki,bool onboarding=false)
    {
        var browser=Environment.GetEnvironmentVariable("LUMMA_TEST_BROWSER")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe");
        if(!File.Exists(browser))throw new InvalidOperationException("Browser regression requires Chrome or LUMMA_TEST_BROWSER.");
        var info=new ProcessStartInfo("node") { UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        info.ArgumentList.Add(Path.Combine(repository,"tests","LummaSimulator.Web.Tests","prize-browser-form.mjs"));
        using var process=Process.Start(info)??throw new InvalidOperationException("Browser regression did not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{browser,temporaryRoot,origin,cookie,spki,onboarding}));
        process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { if(!process.HasExited)process.Kill(entireProcessTree:true); await process.WaitForExitAsync();
            Console.Error.WriteLine("Prize browser bounded wait failed. Completed result: " + await output + " Diagnostic: " + await error); throw; }
        var result=await output;
        if(process.ExitCode!=0)throw new InvalidOperationException("Browser fixture failed: "+await error);
        using var json=JsonDocument.Parse(result);var data=json.RootElement;
        if(onboarding){
            TestProgram.Check(data.GetProperty("cancelled").GetBoolean()&&data.GetProperty("inputPreserved").GetBoolean(),
                "Chrome onboarding keeps the confirmation checked and stays on onboarding after cancelling the passkey prompt: "+result.Trim());
            return;
        }
        TestProgram.Check(data.GetProperty("postPath").GetString()=="/Presenter/Prizes/New"
            && data.GetProperty("contentType").GetString()!.StartsWith("application/x-www-form-urlencoded"),
            "Browser submits the complete rendered event form to POST /Presenter/Prizes/New");
        TestProgram.Check(data.GetProperty("postedOrigin").GetString()==origin
            && data.GetProperty("postStatus").GetInt32()==302 && data.GetProperty("eventPage").GetBoolean(),
            "Browser event creation preserves trusted Origin and redirects to saved event: "+result.Trim());
        TestProgram.Check(data.GetProperty("responsive").GetBoolean()&&data.GetProperty("copyOk").GetBoolean(),
            "Browser Configuration fits network fields at 360px and copies the canonical helper link");
    }
}
