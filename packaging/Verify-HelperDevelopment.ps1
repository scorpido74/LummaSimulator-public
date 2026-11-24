# Run in ordinary, non-elevated Windows PowerShell. Never starts the installed portal.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$work = Join-Path $env:TEMP ('LummaHelperVerification_' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$configuration = 'HelperVerify_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$previousWeb = $env:LUMMA_TEST_WEB_DLL
$previousData = $env:LUMMA_DATA_DIRECTORY
$previousLogs = $env:LUMMA_LOG_DIRECTORY
$previousExports = $env:LUMMA_EXPORT_DIRECTORY
$previousMonitor = $env:LUMMA_MONITOR_KEY
$results = @()
New-Item -ItemType Directory -Path $source | Out-Null
function Invoke-Checked([string]$Name, [string[]]$Arguments) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $work ($Name + '.log')) | Out-Host
    $code = $LASTEXITCODE
    $ErrorActionPreference = $oldPreference
    $line = $Name.ToUpperInvariant().Replace('-', '_') + '_EXITCODE=' + $code
    Write-Host $line
    Add-Content -LiteralPath (Join-Path $work 'result.txt') -Value $line
    if ($code -ne 0) { throw "$Name failed; see its log. No commit or package created." }
}
try {
    if ((& git -C $repo branch --show-current) -ne 'feature/v0.7-cybertruck') { throw 'Unexpected branch.' }
    if ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in ordinary, non-elevated PowerShell to verify standard-user operation.' }
    $paths = @(& git -C $repo ls-files --cached --others --exclude-standard) | Where-Object {
        $_ -match '^(LummaSimulator\.(App|Web|Helper.Core|PresenterHelper|HelperUpdater)/|shared/|tools/|tests/|verification/|packaging/|docs/|Directory\.[^/]+$|LummaSimulator.slnx$|NuGet.Config$|global.json$|LICENSE[^/]*$)' -and $_ -notmatch '(^|/)(bin|obj|artifacts|node_modules)/'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
    $snapshot = @{}
    foreach ($path in $paths) {
        if ($path -match '(?i)\.(db|sqlite|pfx|p12|pem|key|dpapi)$|helper\.settings\.json|HelperReceiver\.settings\.json|appsettings\..*Local\.json') { throw "Protected material in source inventory: $path" }
        $input = Join-Path $repo $path; $output = Join-Path $source $path
        $snapshot[$path] = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash
        New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
        Copy-Item -LiteralPath $input -Destination $output
        if ($snapshot[$path] -ne (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash) { throw "Copy mismatch: $path" }
    }
    $snapshot | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'worktree-snapshot.json') -Encoding UTF8
    Write-Host "SOURCE=$source CONFIGURATION=$configuration"
    # Restore intentionally generates/updates lockfiles for new project references in the isolated copy only.
    Invoke-Checked 'restore' @('restore', (Join-Path $source 'LummaSimulator.slnx'), '-p:RuntimeIdentifiers=win-x64', '-p:RestoreDisableParallel=true', '-m:1', '/nr:false')
    $buildFlags = @('--configuration', $configuration, '--no-restore', '-m:1', '/nr:false', '/p:UseSharedCompilation=false')
    Invoke-Checked 'build-sidecar' (@('build', (Join-Path $source 'tools/LummaSimulator.LocalTestServer/LummaSimulator.LocalTestServer.csproj')) + $buildFlags)
    Invoke-Checked 'build-web' (@('build', (Join-Path $source 'LummaSimulator.Web/LummaSimulator.Web.csproj')) + $buildFlags)
    foreach ($path in @("tools/LummaSimulator.LocalTestServer/bin/$configuration/net10.0/LummaSimulator.LocalTestServer.deps.json", "tools/LummaSimulator.LocalTestServer/bin/$configuration/net10.0/LummaSimulator.LocalTestServer.runtimeconfig.json", "LummaSimulator.Web/obj/$configuration/net10.0/ref/LummaSimulator.Web.dll")) {
        if (!(Test-Path -LiteralPath (Join-Path $source $path))) { throw "Build output missing: $path" }
    }
    $regression = Join-Path $source 'tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj'
    Invoke-Checked 'build-all' (@('build', $regression) + $buildFlags)
    $env:LUMMA_TEST_WEB_DLL = Join-Path $source "LummaSimulator.Web/bin/$configuration/net10.0/LummaSimulator.Web.dll"
    $dependency = Join-Path $source "tests/LummaSimulator.Web.Tests/bin/$configuration/net10.0/LummaSimulator.Web.dll"
    if ((Get-FileHash -LiteralPath $env:LUMMA_TEST_WEB_DLL).Hash -ne (Get-FileHash -LiteralPath $dependency).Hash) { throw 'Portal dependency hash mismatch.' }
    $env:LUMMA_DATA_DIRECTORY = Join-Path $work 'targeted-test-data'
    $env:LUMMA_LOG_DIRECTORY = 'Logs'; $env:LUMMA_EXPORT_DIRECTORY = 'Exports'; $env:LUMMA_MONITOR_KEY = ''
    $runner = Join-Path $source "tests/LummaSimulator.Web.Tests/bin/$configuration/net10.0/LummaSimulator.Web.Tests.dll"
    Invoke-Checked 'helper-checks' @((Join-Path $source "tests/LummaSimulator.Helper.Tests/bin/$configuration/net10.0-windows/LummaSimulator.Helper.Tests.dll"))
    foreach ($selection in @('--session-demo-checks', '--prize-round-checks', '--session-demo-http-checks')) { Invoke-Checked $selection.TrimStart('-') @($runner, $selection) }
    Invoke-Checked 'participants-checks' @((Join-Path $source "tests/LummaSimulator.SmokeTests/bin/$configuration/net10.0/LummaSimulator.SmokeTests.dll"), '--event-participants-checks')
    $wpf = Join-Path $source "verification/bin/$configuration/net10.0-windows/Verification.dll"
    Invoke-Checked 'participants-ui' @($wpf, '--event-participants-ui-checks')
    Invoke-Checked 'helper-ui' @($wpf, '--presenter-helper-ui-checks')
    $data = Join-Path $source "tests/LummaSimulator.RegressionTests/bin/$configuration/test-data"
    if (Test-Path -LiteralPath $data) { throw 'Full regression data already exists; refuse reuse.' }
    Push-Location -LiteralPath $source
    try { Invoke-Checked 'full-regression' (@('test', $regression) + $buildFlags) } finally { Pop-Location }
    & (Join-Path $PSScriptRoot 'Complete-HelperVerification.ps1') -ResultDirectory $work -RepositoryDirectory $repo
} catch {
    Add-Content -LiteralPath (Join-Path $work 'result.txt') -Value ('VERIFICATION_STOPPED=' + $_.Exception.Message)
    Write-Host ('VERIFICATION_STOPPED=' + $_.Exception.Message)
} finally {
    $env:LUMMA_TEST_WEB_DLL = $previousWeb; $env:LUMMA_DATA_DIRECTORY = $previousData; $env:LUMMA_LOG_DIRECTORY = $previousLogs
    $env:LUMMA_EXPORT_DIRECTORY = $previousExports; $env:LUMMA_MONITOR_KEY = $previousMonitor
    Write-Host "RESULT_DIRECTORY=$work"
}
