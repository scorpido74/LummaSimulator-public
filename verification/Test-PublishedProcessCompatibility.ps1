# Run with Windows PowerShell 5.1. Compiles only a synthetic .NET Framework
# argument/stdin probe, never starts an installed portal or opens a database.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path $PSScriptRoot
$count=0
function Check([bool]$condition,[string]$message) {
    if(-not $condition) { throw $message }; $script:count++; Write-Host "PASS: $message"
}
$serverAst=$null
foreach($folder in @('packaging','verification')) {
    foreach($script in Get-ChildItem -LiteralPath (Join-Path $repo $folder) -Filter '*.ps1' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        $tokens=$null; $parseErrors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$parseErrors)
        Check (-not $parseErrors) "Script parses in current PowerShell: $($script.Name)"
        $unsupported=$ast.FindAll({param($node)
            $node -is [Management.Automation.Language.MemberExpressionAst] -and
            $node.Member.Extent.Text -in @('ArgumentList','WaitForExitAsync','GetRelativePath','Environment')
        },$true)
        Check ($unsupported.Count -eq 0) "No newer process/path APIs: $($script.Name)"
        if($script.Name -eq 'Test-LocalHttpPublished.ps1') { $serverAst=$ast }
    }
}
$helper=$serverAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-PublishedServerStartInfo'},$true)
. ([scriptblock]::Create($helper.Extent.Text))
Add-Type -AssemblyName System.Net.Http
$client=[System.Net.Http.HttpClient]::new()
$client.Dispose()
Check $true 'System.Net.Http loads explicitly in Windows PowerShell'
$publish=Join-Path $repo ('artifacts/process-compatibility-'+[guid]::NewGuid().ToString('N')+'/Publish with spaces')
$server=Join-Path $publish 'LocalTestServer'
New-Item -ItemType Directory -Force -Path $server | Out-Null
$exe=Join-Path $server 'LummaSimulator.LocalTestServer.exe'
Add-Type -OutputAssembly $exe -OutputType ConsoleApplication -TypeDefinition @'
using System;
public static class PublishedProcessArgumentProbe {
    public static int Main(string[] args) {
        Console.WriteLine(args.Length);
        foreach (var arg in args) Console.WriteLine(arg);
        Console.WriteLine(Environment.CurrentDirectory);
        Console.Out.Flush();
        Console.Error.WriteLine("synthetic-stderr");
        return Console.ReadLine() == "stop" ? 0 : 1;
    }
}
'@
$info=New-PublishedServerStartInfo $publish 54321
Check ($info.Arguments -eq '54321 --managed') 'Validated port and fixed managed switch use Arguments'
Check ($info.FileName -eq $exe -and $info.WorkingDirectory -eq $server) 'Executable and working-directory paths with spaces remain separate'
Check (-not $info.UseShellExecute -and $info.RedirectStandardInput -and $info.RedirectStandardOutput -and $info.RedirectStandardError) 'Shell disabled and all private pipes retained'
$child=[Diagnostics.Process]::Start($info)
try {
    # Read asynchronously with deadlines: a startup/argument regression must not hang.
    $lines=@()
    for($i=0;$i -lt 4;$i++) {
        $read=$child.StandardOutput.ReadLineAsync()
        if(-not $read.Wait(5000)) { throw 'Synthetic published-process startup timed out.' }
        $lines+= $read.Result
    }
    Check ($lines[0] -eq '2' -and $lines[1] -eq '54321' -and $lines[2] -eq '--managed') 'Real child receives exactly two intact arguments'
    Check ($lines[3] -eq $server) 'Real child starts in the directory containing spaces'
    $child.StandardInput.WriteLine('stop'); $child.StandardInput.Close()
    Check ($child.WaitForExit(5000) -and $child.ExitCode -eq 0) 'Managed stdin stop reaches only this retained child and exits cleanly'
    Check ($child.StandardError.ReadToEnd().Trim() -eq 'synthetic-stderr') 'Redirected stderr remains available'
} finally {
    if(-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $child.Dispose()
}
foreach($invalid in @('0','65536','54321 --unexpected')) {
    $rejected=$false
    try { $null=New-PublishedServerStartInfo $publish $invalid } catch { $rejected=$true }
    Check $rejected "Invalid or injected port is refused: $invalid"
}
Write-Host "$count process compatibility checks passed; synthetic probe only, not published sidecar HTTP acceptance."
