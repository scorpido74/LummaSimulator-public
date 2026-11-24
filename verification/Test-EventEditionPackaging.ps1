# No application/database/certificate-store mutations. Tests launcher helpers with synthetic records.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path $PSScriptRoot
$scriptPath=Join-Path $repo 'packaging/EventEdition/Demo.ps1'
$parseErrors=$null; $tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$parseErrors)
if($parseErrors) { throw ($parseErrors | Out-String) }
$buildAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'packaging/Build-EventEdition.ps1'),[ref]$tokens,[ref]$parseErrors)
if($parseErrors) { throw ($parseErrors | Out-String) }
$count=0
function Check([bool]$condition,[string]$message) {
    if(-not $condition) { throw $message }; $script:count++; Write-Host "PASS: $message"
}
$pathValidator=$buildAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-ForbiddenPackagePath'},$true)
. ([scriptblock]::Create($pathValidator.Extent.Text))
$builderText=$buildAst.Extent.Text
Check ($builderText.Contains('Directory.Build.props') -and -not $builderText.Contains('v0.6.5-beta.1')) 'Builder reads the shared version without a legacy override'
Check ($builderText.Contains('if(-not $VerifiedSourceDirectory)')) 'Explicit verified source skips restore and verification'
Check ($builderText.Contains('Get-FileHash -LiteralPath $target') -and $builderText.Contains('Geverifieerde bron wijkt af')) 'Verified source must match compiled repository inputs'
Check ($builderText.Contains('RELEASE-NOTES.md') -and $builderText.Contains('VERSION.txt') -and $builderText.Contains('"$zip.sha256"')) 'Package includes release documentation, version and external ZIP checksum'
# Evaluate only argument construction with a recording stub: never invoke dotnet.
$runtimeAssignment=$buildAst.Find({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$runtimeArgs'},$true)
. ([scriptblock]::Create($runtimeAssignment.Extent.Text))
$source=Join-Path $repo 'artifacts/synthetic-runtime-source'
$work=Join-Path $repo 'artifacts/synthetic-runtime-work'
$recordedCommands=New-Object 'System.Collections.Generic.List[object]'
function Invoke-Dotnet([string[]]$Arguments,[string]$Log) { $recordedCommands.Add([pscustomobject]@{Arguments=$Arguments;Log=$Log}) }
foreach($command in $buildAst.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-Dotnet'},$true)) {
    if($command.CommandElements[1].Extent.Text -ne '$publishArgs') { . ([scriptblock]::Create($command.Extent.Text)) }
}
# Run the actual publish-loop header and argument assignments for BOTH projects,
# omitting file operations and replacing dotnet with the recording stub above.
$publishLoop=$buildAst.Find({param($node) $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -eq 'pair'},$true)
$header=$publishLoop.Extent.Text.Substring(0,$publishLoop.Body.Extent.StartOffset-$publishLoop.Extent.StartOffset)
$argumentStatements=@($publishLoop.Body.Statements | Where-Object {
    $_ -is [Management.Automation.Language.AssignmentStatementAst] -or
    ($_ -is [Management.Automation.Language.PipelineAst] -and $_.PipelineElements[0] -is [Management.Automation.Language.CommandAst] -and $_.PipelineElements[0].GetCommandName() -eq 'Invoke-Dotnet')
})
. ([scriptblock]::Create($header+'{'+(($argumentStatements | ForEach-Object {$_.Extent.Text}) -join "`n")+'}'))
Check ($recordedCommands.Count -eq 7) 'Capture restore, solution build, regression and all four project publishes without executing dotnet'
function Check-Arguments([int]$Index,[string[]]$Expected,[string]$Message) {
    $actual=$recordedCommands[$Index].Arguments
    Check ($actual.Count -eq $Expected.Count -and [string]::Join([char]0,$actual) -ceq [string]::Join([char]0,$Expected)) $Message
}
Check-Arguments 0 @('restore',(Join-Path $source 'LummaSimulator.slnx'),'--configfile',(Join-Path $source 'NuGet.Config'),'--locked-mode','--runtime','win-x64','-p:SelfContained=true','-p:AppendRuntimeIdentifierToOutputPath=false') 'Exact restore arguments: explicit source config, locked-mode and win-x64 retained'
Check-Arguments 1 @('build',(Join-Path $source 'LummaSimulator.slnx'),'-c','Release','--no-restore') 'Exact solution-build arguments: no RID or self-contained override'
Check-Arguments 2 @('test',(Join-Path $source 'tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj'),'-c','Release','--no-restore') 'Exact regression arguments: no RID or implicit restore'
foreach($index in @(1,2)) {
    $invalid=@($recordedCommands[$index].Arguments | Where-Object {$_ -match '^(--runtime($|=)|-r($|:|=)|[-/](p|property):RuntimeIdentifiers?=|--property:RuntimeIdentifiers?=)'})
    Check ($invalid.Count -eq 0) "RID is forbidden on build/test invocation $index"
}
$index=3
foreach($expectedPair in @(@('LummaSimulator.Web','Portal'),@('LummaSimulator.App','Simulator'),@('LummaSimulator.PresenterHelper','PresenterHelper'),@('LummaSimulator.HelperUpdater','HelperUpdater'))) {
    Check-Arguments $index @('publish',(Join-Path $source "$($expectedPair[0])/$($expectedPair[0]).csproj"),'-c','Release','--no-restore','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=None','-o',(Join-Path $work "publish/$($expectedPair[1])"),'--runtime','win-x64','-p:SelfContained=true','-p:AppendRuntimeIdentifierToOutputPath=false') "Exact $($expectedPair[0]) publish arguments: self-contained win-x64, no restore"
    $index++
}
foreach($lockPath in @('LummaSimulator.Web/packages.lock.json','tests/LummaSimulator.Web.Tests/packages.lock.json','tests/LummaSimulator.RegressionTests/packages.lock.json')) {
    $lock=Get-Content -LiteralPath (Join-Path $repo $lockPath) -Raw | ConvertFrom-Json
    $ridTargets=@($lock.dependencies.PSObject.Properties.Name | Where-Object { $_ -match '/' })
    Check ($lock.version -eq 1 -and $ridTargets.Count -gt 0 -and @($ridTargets | Where-Object { $_ -notmatch '/win-x64$' }).Count -eq 0) "Existing lockfile runtime agrees with package RID: $lockPath"
}
foreach($path in @('certificate.cer','certificate.crt','certificate.der','Page.cshtml','Window.xaml','Portal.runtimeconfig.dev.json','Portal.sourcelink.json')) {
    Check (Test-ForbiddenPackagePath $path) "Additional development/certificate path refused: $path"
}
foreach($path in @('Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','SQLitePCLRaw.bundle_e_sqlite3.dll','SQLitePCLRaw.provider.e_sqlite3.dll','e_sqlite3.dll','runtimes/win-x64/native/e_sqlite3.dll','Portal\Microsoft.Data.Sqlite.dll','Simulator/LocalTestServer/System.Private.CoreLib.dll','sqlite assets/Microsoft.Data.Sqlite.dll')) {
    Check (-not (Test-ForbiddenPackagePath $path)) "Required dependency allowed: $path"
}
foreach($path in @('monitor-integration.state','monitor-integration.state.managed','monitor-integration.state.previous','monitor-credential.dpapi')) {
    Check (Test-ForbiddenPackagePath $path) "Monitor runtime material refused: $path"
}
foreach($path in @('portal.db','portal.db-wal','portal.db-shm','portal.db-journal','portal.sqlite','portal.sqlite3','portal.sqlite-wal','portal.sqlite3-shm','Portal/data/PORTAL.SQLITE3','data\portal.db','portal.db.bak','portal.sqlite3.zip','private.key','private.pem','certificate.pfx','certificate.p12','private.key.backup','keys/key-example.xml','key-example.xml','secrets.json','credentials.json','.env','.env.local','appsettings.Local.json','appsettings.Production.Local.json','event-edition.local.json','.setup-verification-path','.setup-verification-example/file.dll','Portal\.setup-verification-example\file.dll','tests/Microsoft.Data.Sqlite.dll','obj/generated.dll','Logs/demo.log','App_Data/settings.json','event-edition.manifest.previous','source.cs','project.csproj','file.tmp','file.log','../Microsoft.Data.Sqlite.dll','C:\publish\Microsoft.Data.Sqlite.dll','/publish/e_sqlite3.dll')) {
    Check (Test-ForbiddenPackagePath $path) "Protected path refused: $path"
}
foreach($name in @('New-ChildInfo','Find-Owned','Set-PortalOrigin','Get-HelperPackage','Get-CertificateBundleDirectory')) {
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$packageRoot=Join-Path $repo 'verification/Package with spaces'
$runtimeRoot=Join-Path $repo 'verification/Synthetic storage with spaces'
$Action='Start'; Set-PortalOrigin 'https://localhost'
$certificate=[pscustomobject]@{}
$certificate | Add-Member ScriptMethod GetNameInfo { param($kind,$issuer) return 'LummaEvent-SyntheticOnly' }
$previousMonitor=$env:LUMMA_MONITOR_KEY
$previousDatabase=$env:Database__Path
$previousEndpoint=$env:Kestrel__Endpoints__Unexpected__Url
try {
    $env:LUMMA_MONITOR_KEY='synthetic-do-not-inherit'
    $env:Database__Path='synthetic-old-installation'
    $env:Kestrel__Endpoints__Unexpected__Url='http://0.0.0.0:9876'
    $info=New-ChildInfo 'Portal/LummaSimulator.Web.exe'
    Check ($info.FileName -eq (Join-Path $packageRoot 'Portal/LummaSimulator.Web.exe')) 'Executable path with spaces is passed as FileName, not shell text'
    Check (-not $info.UseShellExecute) 'No shell or SDK used to launch portal'
    Check ($info.WorkingDirectory -eq (Join-Path $packageRoot 'Portal')) 'Working directory independent from caller'
    Check ($info.EnvironmentVariables['Database__Path'] -eq (Join-Path $runtimeRoot 'PasskeyPortal/portal.db')) 'Explicit per-installation database overrides inherited path'
    Check (-not $info.EnvironmentVariables.ContainsKey('LUMMA_MONITOR_KEY')) 'Existing monitor secret is not inherited'
    Check (-not $info.EnvironmentVariables.ContainsKey('Kestrel__Endpoints__Unexpected__Url')) 'Inherited public listener is removed'
    Check ($info.EnvironmentVariables['ASPNETCORE_URLS'] -eq $origin -and $info.EnvironmentVariables['WebAuthn__RpId'] -eq 'localhost') 'HTTPS origin and RP ID are aligned'
    Check ($info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] -eq 'Production') 'Production validation retained; development user-secrets are not loaded'
    Check ($info.EnvironmentVariables['Kestrel__Certificates__Default__AllowInvalid'] -eq 'false') 'Invalid certificate use remains disabled'
    Set-PortalOrigin 'https://cybertruck.example.test'
    $networkInfo=New-ChildInfo 'Portal/LummaSimulator.Web.exe'
    Check ($networkInfo.EnvironmentVariables['ASPNETCORE_URLS'] -eq 'https://cybertruck.example.test' -and $networkInfo.EnvironmentVariables['WebAuthn__RpId'] -eq 'cybertruck.example.test' -and $networkInfo.EnvironmentVariables['AllowedHosts'] -eq 'cybertruck.example.test') 'Local DNS origin, RP-ID and allowed host stay aligned without fixed IP'
    $remoteApp=New-ChildInfo 'Simulator/LummaSimulator.App.exe'
    Check ($remoteApp.EnvironmentVariables['LUMMA_PORTAL_ORIGIN'] -eq $networkInfo.EnvironmentVariables['ASPNETCORE_URLS']) 'An explicitly configured external origin is shared with App and retained'
    foreach($invalidOrigin in @('http://cybertruck.example.test','https://cybertruck.example.test:7243','https://192.0.2.1','https://user@cybertruck.example.test','https://cybertruck.example.test/path')) {
        $refused=$false;try {Set-PortalOrigin $invalidOrigin}catch{$refused=$true}
        Check $refused 'Unsafe or unsupported installation origin refused'
    }
    Set-PortalOrigin 'https://localhost'
    $info=New-ChildInfo 'Simulator/LummaSimulator.App.exe'
    Check ($info.EnvironmentVariables['LUMMA_PORTAL_ORIGIN'] -eq 'https://localhost') 'Combined local App uses the loopback portal connection'
    Check ($info.EnvironmentVariables['LUMMA_DATA_DIRECTORY'] -eq $runtimeRoot) 'WPF uses own runtime data, independent from executable path'
    Check (-not $info.EnvironmentVariables.ContainsKey('Kestrel__Certificates__Default__Subject')) 'Offline WPF does not depend on portal certificate selection'
    $Action='Bootstrap'
    $info=New-ChildInfo 'Portal/LummaSimulator.Web.exe' '--bootstrap-administrator'
    Check ($info.Arguments -eq '--bootstrap-administrator' -and -not $info.CreateNoWindow) 'Bootstrap stays interactive and uses existing maintenance argument'
} finally {
    $env:LUMMA_MONITOR_KEY=$previousMonitor; $env:Database__Path=$previousDatabase; $env:Kestrel__Endpoints__Unexpected__Url=$previousEndpoint
}
# Mock process discovery; never inspect or stop an active application.
$sourceControl=Join-Path $repo 'packaging/EventEdition/LauncherControl.ps1'
. $sourceControl
$script:syntheticProcess=[pscustomobject]@{Id=12345;StartTime=[datetime]::UtcNow;Path=(Join-Path $packageRoot 'Portal/LummaSimulator.Web.exe');HasExited=$false}
function Get-Process { param($Id,$ErrorAction) if($Id -eq 12345) { return $script:syntheticProcess } }
$record=[pscustomobject]@{Id=12345;StartTicks=$syntheticProcess.StartTime.ToUniversalTime().Ticks.ToString();Path=$syntheticProcess.Path}
Check ($null -ne (Find-Owned $record)) 'Exact registered PID, timestamp and package executable are accepted'
$record.StartTicks='1'
Check ($null -eq (Find-Owned $record)) 'Reused PID with different timestamp is refused'
$record.StartTicks=$syntheticProcess.StartTime.ToUniversalTime().Ticks.ToString(); $record.Path='C:\Unrelated\Other.exe'
Check ($null -eq (Find-Owned $record)) 'Foreign executable paths in process records are refused'
$record.Path=$syntheticProcess.Path; $record.Id=54321
Check ($null -eq (Find-Owned $record)) 'Missing process is not replaced by a name lookup'
foreach($name in @('Setup-Demo','Start-Demo','Stop-Demo','Start-PasskeyPortal','Start-LummaSimulator','Bootstrap-Administrator')) {
    $cmd=Get-Content -LiteralPath (Join-Path $repo "packaging/EventEdition/$name.cmd") -Raw
    Check ($cmd.Contains('"%~dp0Demo.ps1"') -and $cmd -notmatch 'ExecutionPolicy|dotnet run') "$name resolves its own quoted script path without bypass"
}
Write-Host "$count packaging checks passed. Runtime/VM acceptance remains separate."
& powershell.exe -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'Test-MonitorLaunchers.ps1')
if($LASTEXITCODE -ne 0) { throw 'Monitor launcher checks failed.' }
# Run the actual process-start regression on .NET Framework/Windows PowerShell,
# even when the developer invokes the builder from PowerShell 7.
& powershell.exe -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'Test-PublishedProcessCompatibility.ps1')
if($LASTEXITCODE -ne 0) { throw 'Windows PowerShell published-process compatibility checks failed.' }
