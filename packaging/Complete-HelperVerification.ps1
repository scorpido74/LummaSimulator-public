# Metadata-only finalization. Reuses the existing green test run; never reruns or cleans tests.
param(
    [Parameter(Mandatory=$true)][string]$ResultDirectory,
    [string]$RepositoryDirectory
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryDirectory)) { $RepositoryDirectory = Split-Path $PSScriptRoot }
$work = (Resolve-Path -LiteralPath $ResultDirectory).ProviderPath
$repo = (Resolve-Path -LiteralPath $RepositoryDirectory).ProviderPath
$source = Join-Path $work 'source'
$attempt = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$log = Join-Path $work ('finalization-' + $attempt + '.log')
function Record([string]$Line) { Add-Content -LiteralPath $log -Value $Line -Encoding UTF8; Write-Host $Line }
function SourcePath([string]$Root, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)|:') { throw "Invalid source path: $Relative" }
    return Join-Path $Root $Relative
}
try {
    Record ('RESULT_DIRECTORY=' + $work)
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw 'Expected the result directory containing source and result.txt, not a telemetry directory or source itself.' }
    $resultFile = Join-Path $work 'result.txt'
    $results = Get-Content -LiteralPath $resultFile
    foreach ($name in @('RESTORE','BUILD_SIDECAR','BUILD_WEB','BUILD_ALL','HELPER_CHECKS','SESSION_DEMO_CHECKS','PRIZE_ROUND_CHECKS','SESSION_DEMO_HTTP_CHECKS','PARTICIPANTS_CHECKS','PARTICIPANTS_UI','HELPER_UI','FULL_REGRESSION')) {
        $last = @($results | Where-Object { $_ -match ('^' + $name + '_EXITCODE=') }) | Select-Object -Last 1
        if ($last -ne ($name + '_EXITCODE=0')) { throw "Missing green prerequisite: $name" }
    }
    $snapshot = @{}
    $json = Get-Content -LiteralPath (Join-Path $work 'worktree-snapshot.json') -Raw | ConvertFrom-Json
    foreach ($property in $json.PSObject.Properties) { $snapshot[$property.Name] = [string]$property.Value }
    if ($snapshot.Count -eq 0) { throw 'Empty source snapshot.' }
    $tested = @{}
    $verifierOnlyChanges = @()
    $verifierPaths = @('packaging/Verify-HelperDevelopment.ps1','packaging/Complete-HelperVerification.ps1')
    foreach ($path in $snapshot.Keys) {
        $current = (Get-FileHash -LiteralPath (SourcePath $repo $path) -Algorithm SHA256).Hash
        if ($snapshot[$path] -ne $current) {
            if ($path -notin $verifierPaths) { throw "Worktree changed since tests: $path" }
            $verifierOnlyChanges += $path
        }
        $tested[$path] = (Get-FileHash -LiteralPath (SourcePath $source $path) -Algorithm SHA256).Hash
        if ($tested[$path] -ne $snapshot[$path] -and $path -notlike '*packages.lock.json') { throw "Test source changed: $path" }
    }
    # Detect new compiled inputs too. The new metadata-only verifier is deliberately outside this set.
    $currentInputs = @(& git -C $repo ls-files --cached --others --exclude-standard) | Where-Object {
        $_ -match '^(LummaSimulator\.[^/]+/|shared/|tools/|tests/|verification/|Directory\.[^/]+$|global.json$)' -and $_ -notmatch '(^|/)(bin|obj|artifacts|node_modules)/'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Current source inventory failed.' }
    foreach ($path in $currentInputs) { if (!$snapshot.ContainsKey($path)) { throw "New untested input: $path" } }
    Record ('SOURCE_HASHES_MATCH=' + $snapshot.Count)
    # Lockfiles belong next to known project files. Never enumerate bin/obj/test-data, even to skip them later.
    $lockChanges = @()
    $projects = @($snapshot.Keys | Where-Object { $_ -like '*.csproj' } | Sort-Object)
    if ($projects.Count -eq 0) { throw 'No projects in source snapshot.' }
    foreach ($project in $projects) {
        $relative = (Join-Path (Split-Path $project) 'packages.lock.json').Replace('\','/')
        $file = SourcePath $source $relative
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        $tested[$relative] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if (!$snapshot.ContainsKey($relative) -or $snapshot[$relative] -ne $tested[$relative]) { $lockChanges += $relative }
        Record ('LOCKFILE=' + $file + '; LENGTH=' + $file.Length)
    }
    $manifestPath = Join-Path $work ('tested-source-hashes-' + $attempt + '.json')
    $reviewPath = Join-Path $work ('lockfiles-for-review-' + $attempt + '.txt')
    $verifierHashPath = Join-Path $work ('finalizer-hashes-' + $attempt + '.json')
    $tested | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    @($lockChanges) | Set-Content -LiteralPath $reviewPath -Encoding UTF8
    $verifierHashes = @{}
    foreach ($path in $verifierPaths) { $verifierHashes[$path] = (Get-FileHash -LiteralPath (SourcePath $repo $path)).Hash }
    $verifierHashes | ConvertTo-Json | Set-Content -LiteralPath $verifierHashPath -Encoding UTF8
    $final = @('TESTS_GREEN=True', 'COMPILED_SOURCE_MATCH=True', ('WORKTREE_SNAPSHOT_UNCHANGED=' + ($verifierOnlyChanges.Count -eq 0)),
        ('REVIEWED_VERIFIER_ONLY_CHANGES=' + ($verifierOnlyChanges -join ',')), ('LOCKFILES_FOR_REVIEW=' + $lockChanges.Count),
        ('TESTED_SOURCE_MANIFEST=' + $manifestPath), ('LOCKFILES_REVIEW_LIST=' + $reviewPath), 'VERIFIER_FINALIZATION_EXITCODE=0', 'VERIFICATION_COMPLETE=True')
    foreach ($line in $final) { Record $line }
    Add-Content -LiteralPath $resultFile -Value $final
} catch {
    Record ('VERIFIER_FINALIZATION_EXITCODE=1; ERROR=' + $_.Exception.Message)
    Record ('SCRIPT_LINE=' + $_.InvocationInfo.ScriptLineNumber)
    throw
} finally { Record ('FINALIZATION_LOG=' + $log) }
