using System.Diagnostics;
using System.Text.Json;

namespace LummaSimulator.Web.Tests;

internal static class ClickFixBrowserChecks
{
    public static async Task RunAsync(string repository, string temporaryRoot, string origin, string cookie, string spki, bool paired = false)
    {
        var browser = Environment.GetEnvironmentVariable("LUMMA_TEST_BROWSER")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
        if (!File.Exists(browser)) throw new InvalidOperationException("ClickFix browser regression requires Chrome or LUMMA_TEST_BROWSER.");
        var info = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(repository, "tests", "LummaSimulator.Web.Tests", "clickfix-browser.mjs"));
        using var process = Process.Start(info) ?? throw new InvalidOperationException("ClickFix browser fixture did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var diagnosticPath = Path.Combine(temporaryRoot, "clickfix-browser-diagnostics.log");
        var browserVersion = FileVersionInfo.GetVersionInfo(browser).ProductVersion;
        Console.WriteLine($"ClickFix browser: {browser}; version: {browserVersion}");
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { browser, browserVersion, temporaryRoot, origin, cookie, spki, paired }));
        process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(150)); }
        catch
        {
            Console.Error.WriteLine("ClickFix browser fixture did not finish within its 150-second bounded wait.");
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var timeoutDiagnostics = await error;
            await File.WriteAllTextAsync(diagnosticPath, timeoutDiagnostics);
            Console.Error.WriteLine(timeoutDiagnostics);
            throw;
        }
        var fixtureDiagnostics = await error;
        await File.WriteAllTextAsync(diagnosticPath, fixtureDiagnostics);
        Console.WriteLine("ClickFix step diagnostics: " + diagnosticPath);
        if (process.ExitCode != 0)
        {
            // Emit the fixture's fixed assertion diagnostics before outer teardown assertions.
            var diagnostic = "ClickFix browser fixture failed: " + fixtureDiagnostics;
            Console.Error.WriteLine(diagnostic);
            throw new InvalidOperationException(diagnostic);
        }
        using var result = JsonDocument.Parse(await output);
        TestProgram.Check(result.RootElement.GetProperty("success").GetBoolean(), "Real browser: signed-in invitation, automatic fictional exercise, NL/EN, viewport reflow, server completion and reload");
        Console.WriteLine("ClickFix browser previews: " + temporaryRoot);
    }
}
