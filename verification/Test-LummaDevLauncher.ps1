# Only synthetic preflight tests. Never launch apps, query live processes or open data.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'packaging/Start-LummaDev.ps1'), [ref]$tokens, [ref]$errors)
if ($errors) { throw ($errors | Out-String) }
foreach ($name in @('Assert-Idle','Assert-StablePaths')) {
    $definition = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Get-CimInstance { param($ClassName) return $script:fakeProcesses }
function Get-NetTCPConnection { param($State,$LocalPort,$ErrorAction) return $script:fakeListeners }
function Refused([scriptblock]$Action) {
    $blocked = $false
    try { & $Action } catch { $blocked = $true }
    if (-not $blocked) { throw 'Expected preflight refusal.' }
}
$fakeProcesses = @(); $fakeListeners = @()
Assert-Idle 7243
$fakeListeners = @([pscustomobject]@{LocalPort=7243})
Refused { Assert-Idle 7243 }
$fakeListeners = @()
foreach ($process in @(
    @{Name='LummaSimulator.Web.exe';CommandLine=''},
    @{Name='LummaSimulator.App.exe';CommandLine=''},
    @{Name='dotnet.exe';CommandLine='dotnet run --project LummaSimulator.Web --launch-profile https'},
    @{Name='dotnet.exe';CommandLine='dotnet "C:\project with spaces\LummaSimulator.Web.dll"'}
)) {
    $fakeProcesses = @([pscustomobject]$process)
    Refused { Assert-Idle 7243 }
}
$fakeProcesses = @([pscustomobject]@{Name='dotnet.exe';CommandLine='dotnet OtherApplication.dll'})
Assert-Idle 7243
$previous = $env:Database__Path
try {
    $env:Database__Path = 'relative/portal.db'
    Refused { Assert-StablePaths $repo }
} finally { $env:Database__Path = $previous }
$batch = Get-Content -LiteralPath (Join-Path $repo 'Start-LummaDev.bat') -Raw
if ($batch -notmatch '%~dp0packaging\\Start-LummaDev.ps1' -or $batch -match 'ExecutionPolicy|runas') { throw 'Batch wrapper changed its launch/security contract.' }
'PASS: parser, idle/foreign process, occupied port, relative path and batch wrapper checks (10 cases). No apps started.'
