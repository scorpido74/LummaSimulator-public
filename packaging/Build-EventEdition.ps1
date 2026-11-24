# Developer-only builder. The resulting package needs neither SDK nor NuGet.
param(
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '../dist'),
    # Explicit operator assertion: this isolated source has already passed Release/regression.
    # Reuse its existing RID assets without performing any restore or repeating verification.
    [string]$VerifiedSourceDirectory,
    [switch]$PhysicalAcceptance
)
if($PhysicalAcceptance -and $VerifiedSourceDirectory) { throw 'Fysieke acceptatie vereist een nieuwe bronkopie en locked restore.' }
$version='v'+([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.Version
# RID restore supplies both framework and win-x64 assets. Solution build/tests
# must remain RID-neutral (NETSDK1134); only individual publishes select the RID.
$runtimeArgs=@('--runtime','win-x64','-p:SelfContained=true','-p:AppendRuntimeIdentifierToOutputPath=false')
$ErrorActionPreference='Stop'
Remove-Item Env:LUMMA_MONITOR_KEY,Env:Monitor__KeyHash,Env:LUMMA_PORTAL_STOP_FILE -ErrorAction SilentlyContinue
function Test-ForbiddenPackagePath([string]$RelativePath) {
    # Match path segments and complete file suffixes, not namespace components
    # such as the '.Sqlite.' in Microsoft.Data.Sqlite.dll.
    $path=$RelativePath.Replace('\','/')
    if([string]::IsNullOrWhiteSpace($path) -or $path -match '^/|:|(^|/)\.\.(/|$)') { return $true }
    if($path -match '(^|/)(keys|App_Data|Logs|obj|bin|tests|verification|artifacts|node_modules)(/|$)|monitor-integration\.state|monitor-credential\.') { return $true }
    if($path -match '(^|/)\.setup-verification[^/]*(/|$)|event-edition\.manifest') { return $true }
    $name=($path -split '/')[-1]
    # Database files, SQLite sidecars and recognizable backups/archives.
    if($name -match '\.(db|sqlite|sqlite3)(-(wal|shm|journal))?(\.(bak|backup|old|tmp|gz|zip))?$') { return $true }
    if($name -match '\.(zip|7z|bak|backup|pfx|p12|pem|key|cer|crt|der|cs|csproj|cshtml|xaml|tmp|log)(\.(bak|backup|old|tmp|gz|zip))?$') { return $true }
    if($name -match '\.runtimeconfig\.dev\.json$|\.sourcelink\.json$') { return $true }
    if($name -match '^(secrets|credentials)\.json$|^\.env($|\.)|^key-.*\.xml$|^id_(rsa|ed25519)($|\.)|^event-edition\.local\.json$|^appsettings\.(.*\.)?Local\.json$|^HelperReceiver\.settings\.json$|^helper\.settings\.json$|\.dpapi$') { return $true }
    return $false
}
$repo=Split-Path $PSScriptRoot
Write-Host ('Builder account: '+[Security.Principal.WindowsIdentity]::GetCurrent().Name)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$packageLabel=if($PhysicalAcceptance){'LummaSimulator-PhysicalAcceptance-TEST'}else{'LummaSimulator-EventEdition'}
$zip=Join-Path $output "$packageLabel-$version-win-x64.zip"
if((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath "$zip.sha256")) { throw 'ZIP of checksum bestaat al; kies een nieuwe outputmap. Geen overschrijving.' }
& (Join-Path $repo 'verification/Test-EventEditionPackaging.ps1')
if(-not $?) { throw 'Launcherchecks mislukt.' }
& (Join-Path $repo 'verification/Test-ReleaseNetworkPlan.ps1')
if(-not $?) { throw 'Netwerkplanchecks mislukt.' }
$work=Join-Path $repo ('artifacts/event-edition-package-'+[guid]::NewGuid().ToString('N'))
$source=Join-Path $work 'source'
$stageName=if($PhysicalAcceptance){"LummaSimulator TEST - Physical Acceptance $version"}else{"LummaSimulator Event Edition $version"}
$stage=Join-Path $work $stageName
if($VerifiedSourceDirectory) {
    $source=(Resolve-Path -LiteralPath $VerifiedSourceDirectory -ErrorAction Stop).Path
    if($source.TrimEnd('\','/') -eq $repo.TrimEnd('\','/')) { throw 'Gebruik een geisoleerde geverifieerde bronkopie, niet de actieve projectmap.' }
}
if(Test-Path -LiteralPath $stage){throw 'Staging bestaat al; geen hergebruik of opschoning.'}
New-Item -ItemType Directory -Force -Path $source,$stage,$output | Out-Null
# Copy current source only, never existing build output, ignored runtime data or local copies.
$files=& git -C $repo ls-files --cached --others --exclude-standard
if($LASTEXITCODE -ne 0) { throw 'Git-inventarisatie mislukt.' }
$copiedFiles=@{}
foreach($file in $files) {
    if($file -notmatch '^(LummaSimulator\.(App|Web|Helper\.Core|HelperUpdater|PresenterHelper)/|shared/|tools/|tests/|verification/|docs/|packaging/|LummaSimulator\.slnx$|Directory\.[^/]+$|NuGet\.Config$|global\.json$|LICENSE[^/]*$|README\.md$)') { continue }
    if($file -match '(^|/)(bin|obj|artifacts|node_modules)/|\.setup-verification|\.(db|sqlite|sqlite3|pfx|p12|pem|key)(\.|$)|event-edition\.manifest|monitor-integration\.state|monitor-credential\.|appsettings\..*Local\.json') { throw "Onverwacht bronbestand: $file" }
    if(-not (Test-Path -LiteralPath (Join-Path $repo $file) -PathType Leaf)){continue}
    $target=Join-Path $source $file
    if($VerifiedSourceDirectory) {
        # Packaging/docs may have changed since verification; compiled inputs may not.
        if($file -match '^(LummaSimulator\.(App|Web|Helper\.Core|HelperUpdater|PresenterHelper)/|shared/|tools/|Directory\.|global\.json$)') {
            if(-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo $file)).Hash) { throw "Geverifieerde bron wijkt af: $file" }
        }
        continue
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $target
    if($PhysicalAcceptance) { $copiedFiles[$file]=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash }
}
if($PhysicalAcceptance) {
    $sourceCommit=& git -C $repo rev-parse HEAD
    if($LASTEXITCODE -ne 0) { throw 'Git-commit voor het testpakket kon niet worden vastgesteld.' }
    $workingTree=@(& git -C $repo -c core.quotepath=false status --porcelain=v1 --untracked-files=all --no-renames)
    if($LASTEXITCODE -ne 0) { throw 'Git-status voor het testpakket kon niet worden vastgesteld.' }
    $workingTree | Set-Content -LiteralPath (Join-Path $work 'working-tree-status.txt') -Encoding UTF8
    $inventory=Join-Path $stage 'SOURCE-SHA256SUMS.txt'
    $copiedFiles.Keys | Sort-Object | ForEach-Object { '{0}  {1}' -f $copiedFiles[$_],$_ } |
        Set-Content -LiteralPath $inventory -Encoding UTF8
    @('TESTPAKKET - ALLEEN VOOR FYSIEKE ACCEPTATIE',"Commit: $sourceCommit",
        'Bron: lokale werkboom. SOURCE-SHA256SUMS.txt bevat de exacte invoerhashes.',
        'Controles bij geldige ZIP en checksum: packaging, launchers, netwerkplan, locked restore, Release-build, publish, bestandsmanifest en sidecar uit de uitgepakte ZIP.',
        'Open: volledige geautomatiseerde regressie en fysieke acceptatie.',
        'Fysiek: App, Portal, Helper-download en pairing, twee volledige rondes en herstart.') |
        Set-Content -LiteralPath (Join-Path $stage 'PHYSICAL-ACCEPTANCE-TEST.txt') -Encoding UTF8
}
function Invoke-Dotnet([string[]]$Arguments,[string]$Log) {
    $logPath=Join-Path $work $Log
    $PSNativeCommandUseErrorActionPreference=$false
    & dotnet @Arguments *> $logPath
    $exitCode=$LASTEXITCODE
    if($exitCode -eq 0) { return }
    $diagnostics=@(Select-String -LiteralPath $logPath -Pattern @(
        '^\s*(?:Unhandled exception\.|(?:EXEC\s*:\s*)?error\s*:|FAIL(?::|ED\b))',
        ':\s+error\s*(?:[A-Z]+\d+\s*)?:',
        '^\s+at\s+.*\s+in\s+.*:line\s+\d+'
    ) | Select-Object -First 24 -ExpandProperty Line)
    $message="dotnet $($Arguments[0]) mislukt met exitcode $exitCode. Volledig log: $logPath"
    if($diagnostics.Count) { $message+="`n"+($diagnostics -join "`n") }
    throw $message
}
@{Account=[Security.Principal.WindowsIdentity]::GetCurrent().Name;Source=$source;RestoreConfigFile=(Join-Path $source 'NuGet.Config');LockedRestore=$true;PublishNoRestore=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $work 'build-context.json') -Encoding UTF8
if(-not $VerifiedSourceDirectory) {
    Invoke-Dotnet (@('restore',(Join-Path $source 'LummaSimulator.slnx'),'--configfile',(Join-Path $source 'NuGet.Config'),'--locked-mode') + $runtimeArgs) 'restore.log'
    Invoke-Dotnet @('build',(Join-Path $source 'LummaSimulator.slnx'),'-c','Release','--no-restore') 'build.log'
    if($PhysicalAcceptance) {
        Write-Host 'TESTPAKKET: volledige regressie staat open. Alleen voor fysieke acceptatie.'
    } else {
        $env:LUMMA_TEST_WEB_DLL=Join-Path $source 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'
        try { Invoke-Dotnet @('test',(Join-Path $source 'tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj'),'-c','Release','--no-restore') 'regression.log' }
        finally { Remove-Item Env:LUMMA_TEST_WEB_DLL -ErrorAction SilentlyContinue }
    }
} else { Write-Host 'Bestaande lokale Release/regressieverificatie gebruikt; geen restore uitgevoerd. Publishacceptatie blijft verplicht.' }
foreach($pair in @(@('LummaSimulator.Web','Portal'),@('LummaSimulator.App','Simulator'),@('LummaSimulator.PresenterHelper','PresenterHelper'),@('LummaSimulator.HelperUpdater','HelperUpdater'))) {
    $project=$pair[0]; $folder=$pair[1]
    $publish=Join-Path $work "publish/$folder"
    $publishArgs=@('publish',(Join-Path $source "$project/$project.csproj"),'-c','Release','--no-restore','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=None','-o',$publish) + $runtimeArgs
    Invoke-Dotnet $publishArgs "publish-$folder.log"
    foreach($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        $relative=$file.FullName.Substring($publish.Length+1)
        if(Test-ForbiddenPackagePath $relative) { throw "Verboden pakketbestand: $relative" }
        if($relative -match '\.pdb$|(^|[\\/])appsettings(\.[^\\/]+)?\.json$|web\.config$') { continue }
        $targetFolder=if($folder -eq 'HelperUpdater'){'PresenterHelper'}else{$folder}
        $target=Join-Path $stage "$targetFolder/$relative"
        if(Test-Path -LiteralPath $target){
            if((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Conflicterende helperdependency: $relative"}
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
# Ship known non-secret defaults; the launcher supplies explicit per-installation paths/TLS.
@{
    AllowedHosts='localhost'; WebAuthn=@{RpId='localhost';Origins=@('https://localhost')}
    Experience=@{Hostname='localhost';DefaultLanguage='nl-NL';AvailableLanguages=@('nl-NL','en-GB')}
    Logging=@{LogLevel=@{Default='Warning'}}
    Helper=@{Enabled=$true;DownloadUrl="https://localhost/downloads/LummaSimulator-PresenterHelper-$version-win-x64.zip"}
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'Portal/appsettings.json') -Encoding UTF8
@{OrganizationName='Infinigate';LogoFile=$null;DefaultLanguage='nl-NL';DataDirectory=$null;LogDirectory=$null;ExportDirectory=$null;PortalOrigin='https://localhost'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'Simulator/LummaSimulator.App.settings.json') -Encoding UTF8
$packageInput=if($PhysicalAcceptance){$source}else{$repo}
foreach($name in @('Demo.ps1','LauncherControl.ps1','Firewall.ps1','Setup-Demo.cmd','Start-Demo.cmd','Stop-Demo.cmd','Start-PasskeyPortal.cmd','Start-LummaSimulator.cmd','Bootstrap-Administrator.cmd','Start-PresenterHelper.cmd')) {
    Copy-Item -LiteralPath (Join-Path $packageInput "packaging/EventEdition/$name") -Destination $stage
}
foreach($name in @('README.md','RELEASE-NOTES.md')) { Copy-Item -LiteralPath (Join-Path $packageInput "packaging/EventEdition/$name") -Destination $stage }
@($version, ('Commit: '+(& git -C $repo rev-parse --short=12 HEAD)), 'Source: working tree (may include uncommitted changes)') | Set-Content -LiteralPath (Join-Path $stage 'VERSION.txt') -Encoding UTF8
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $packageInput 'docs/references/attack-navigator-LICENSE.txt') -Destination (Join-Path $stage 'docs')
foreach($name in @('LICENSE','LICENSE.md','LICENSE.txt')) { if(Test-Path -LiteralPath (Join-Path $packageInput $name)) { Copy-Item -LiteralPath (Join-Path $packageInput $name) -Destination $stage } }
foreach($folder in @('Portal','Simulator','Simulator/LocalTestServer','PresenterHelper')) {
    foreach($required in @('coreclr.dll','hostfxr.dll')) { if(-not (Test-Path -LiteralPath (Join-Path $stage "$folder/$required"))) { throw "Self-contained runtime ontbreekt: $folder/$required" } }
}
# One portable presenter package; the updater and shared runtime remain internal dependencies.
foreach($required in @('LummaSimulator.PresenterHelper.exe','LummaSimulator.HelperUpdater.exe','LummaSimulator.Helper.Core.dll')){
    if(-not (Test-Path -LiteralPath (Join-Path $stage "PresenterHelper/$required"))){throw "Helperdependency ontbreekt: $required"}
}
$helperStage=Join-Path $work 'helper-package'
$null=New-Item -ItemType Directory -Path $helperStage
Copy-Item -LiteralPath (Join-Path $stage 'PresenterHelper') -Destination $helperStage -Recurse
Copy-Item -LiteralPath (Join-Path $stage 'Start-PresenterHelper.cmd') -Destination $helperStage
$helperExe=Get-Item -LiteralPath (Join-Path $stage 'PresenterHelper/LummaSimulator.PresenterHelper.exe')
([Diagnostics.FileVersionInfo]::GetVersionInfo($helperExe.FullName).ProductVersion -split '\+')[0] | Set-Content -LiteralPath (Join-Path $helperStage 'HELPER-VERSION.txt') -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $packageInput 'packaging/EventEdition/HELPER-README.md') -Destination (Join-Path $helperStage 'README.md')
foreach($name in @('LICENSE','LICENSE.md','LICENSE.txt')){if(Test-Path -LiteralPath (Join-Path $stage $name)){Copy-Item -LiteralPath (Join-Path $stage $name) -Destination $helperStage}}
$downloads=Join-Path $stage 'Portal/wwwroot/downloads';$null=New-Item -ItemType Directory -Force -Path $downloads
$helperZip=Join-Path $downloads "LummaSimulator-PresenterHelper-$version-win-x64.zip"
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $helperStage | ForEach-Object FullName) -DestinationPath $helperZip -CompressionLevel Optimal
"$((Get-FileHash -LiteralPath $helperZip -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($helperZip))"|Set-Content -LiteralPath "$helperZip.sha256" -Encoding ASCII
if(-not (Get-ChildItem -LiteralPath (Join-Path $stage 'Portal') -Recurse -Filter 'e_sqlite3.dll')) { throw 'Native SQLite ontbreekt.' }
& (Join-Path $source 'verification/Test-LocalHttpPublished.ps1') -PublishDirectory (Join-Path $stage 'Simulator') *> (Join-Path $work 'published-sidecar.log')
if(-not $?) { throw 'Gepubliceerde sidecartest mislukt.' }
foreach($file in Get-ChildItem -LiteralPath $stage -File -Recurse){
    $relative=$file.FullName.Substring($stage.Length+1)
    if((Test-ForbiddenPackagePath $relative) -and $file.FullName -ne $helperZip){throw "Onverwachte inhoud in eindstaging: $relative"}
}
$manifest=Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.FullName.Substring($stage.Length+1)
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding UTF8
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
# Verify the generated archive from a new location; never reuse an installed tree.
$unpacked=Join-Path $work 'unpacked'
Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
$unpackedRoot=Join-Path $unpacked (Split-Path $stage -Leaf)
foreach($line in $manifest){
    $expected=$line.Substring(0,64);$relative=$line.Substring(66)
    $file=Join-Path $unpackedRoot $relative
    if(-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected){throw "ZIP-roundtrip mislukt: $relative. Pakket niet geaccepteerd."}
}
& (Join-Path $source 'verification/Test-LocalHttpPublished.ps1') -PublishDirectory (Join-Path $unpackedRoot 'Simulator') *> (Join-Path $work 'unpacked-sidecar.log')
if(-not $?) { throw 'Uitgepakte sidecartest mislukt. Pakket niet geaccepteerd.' }
$zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
$writtenHash=(Get-Content -LiteralPath "$zip.sha256" -Raw).Split(' ')[0]
if($writtenHash -cne (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Definitieve ZIP wijkt af van het SHA-256-bestand. Pakket niet geaccepteerd.'}
Write-Host "ZIP: $zip"
Write-Host "SHA-256: $zip.sha256"
Write-Host "Build/testlogs: $work"
