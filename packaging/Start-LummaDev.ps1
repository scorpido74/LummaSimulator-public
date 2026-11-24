# Developer launcher. Never provision TLS, reset data or stop unrelated processes.
param([switch]$PortalConsole, [string]$SourceRoot, [switch]$Stop,[switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'EventEdition/LauncherControl.ps1')
. (Join-Path $PSScriptRoot 'DevBuild.ps1')

function New-DevChildInfo([string]$file,[string]$arguments,[string]$monitorKey='',[string]$monitorHash='',[string]$stopFile='') {
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$file;$info.Arguments=$arguments
    $info.UseShellExecute=$false;$info.CreateNoWindow=$false
    $info.EnvironmentVariables.Remove('LUMMA_MONITOR_KEY');$info.EnvironmentVariables.Remove('Monitor__KeyHash');$info.EnvironmentVariables.Remove('LUMMA_PORTAL_STOP_FILE')
    if($monitorKey) {$info.EnvironmentVariables['LUMMA_MONITOR_KEY']=$monitorKey}
    if($monitorHash) {$info.EnvironmentVariables['Monitor__KeyHash']=$monitorHash}
    if($stopFile) {$info.EnvironmentVariables['LUMMA_PORTAL_STOP_FILE']=$stopFile}
    return $info
}
function Get-DevRecordPath([string]$repo) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try {$scope=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($repo.ToUpperInvariant()))).Replace('-','')} finally {$sha.Dispose()}
    return Join-Path (Assert-PrivateControlRoot) ('dev-'+$scope+'.json')
}
function Find-DevOwned($record,[string]$repo) {
    $source=[IO.Path]::GetFullPath($record.Source)
    if(-not $source.StartsWith((Join-Path $repo 'artifacts/dev-start-'),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $source -Leaf) -ne 'source') {throw 'Onbekende ontwikkelprocesregistratie.'}
    if(-not $record.PSObject.Properties['Role']){return $null}
    $expected=if($record.Role -eq 'App'){Join-Path $source 'LummaSimulator.App/bin/Release/net10.0-windows/LummaSimulator.App.exe'}else{Join-Path $source 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.exe'}
    if($record.Path -ne $expected -and ($record.Role -ne 'Portal' -or $record.Path -ne (Get-Command powershell.exe).Source)){return $null}
    $process=Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    Confirm-DemoProcess $process $record
}
function Save-DevProcess([string]$recordFile,$process,[string]$source,[string]$role,[string]$stopFile='') {
    $records=@(Read-DemoRecords $recordFile | Where-Object {Get-Process -Id $_.Id -ErrorAction SilentlyContinue})
    $records+=@([pscustomobject]@{Id=$process.Id;StartTicks=$process.StartTime.ToUniversalTime().Ticks.ToString();Path=$process.Path;Source=$source;Role=$role;StopFile=$stopFile})
    ConvertTo-Json -InputObject $records|Set-Content -LiteralPath $recordFile -Encoding UTF8
}

function Assert-Idle([int]$Port) {
    # Refuse rather than adopt/kill an installation whose ownership is unknown.
    $running = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -in @('LummaSimulator.Web.exe', 'LummaSimulator.App.exe') -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'LummaSimulator\.(Web|App)([\\/\s."'']|$)')
    })
    if ($running.Count) { throw 'LummaSimulator draait al. Sluit de bestaande portal en WPF eerst; niets is gestopt.' }
    if (@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).Count) {
        throw "HTTPS-poort $Port is bezet. Sluit de bekende toepassing zelf; niets is gestopt."
    }
}

function Assert-StablePaths([string]$Root) {
    # Relative overrides would change meaning in the isolated build. Fail closed.
    foreach ($name in @('Database__Path','Storage__DataDirectory','LUMMA_DATA_DIRECTORY','LUMMA_LOG_DIRECTORY','LUMMA_EXPORT_DIRECTORY')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value -and -not [IO.Path]::IsPathRooted([Environment]::ExpandEnvironmentVariables($value))) {
            throw 'Relatieve runtimepad-override gevonden. Laat de bestaande opslagconfiguratie beoordelen; geen apps gestart.'
        }
    }
    foreach ($relative in @('LummaSimulator.Web/appsettings.json','LummaSimulator.Web/appsettings.Development.json','LummaSimulator.App/LummaSimulator.App.settings.json')) {
        $config = Get-Content -LiteralPath (Join-Path $Root $relative) -Raw | ConvertFrom-Json
        foreach ($path in @('Database.Path','Storage.DataDirectory','DataDirectory','LogDirectory','ExportDirectory')) {
            $node = $config
            foreach ($part in $path.Split('.')) {
                if ($null -eq $node -or $part -notin $node.PSObject.Properties.Name) { $node = $null; break }
                $node = $node.$part
            }
            if ($node -and -not [IO.Path]::IsPathRooted([Environment]::ExpandEnvironmentVariables([string]$node))) {
                throw 'Relatief runtimepad in instellingen. Geen automatische verplaatsing of padwijziging toegestaan.'
            }
        }
    }
}

try {
    if ($PortalConsole) {
        Set-Location -LiteralPath $SourceRoot
        Write-Host 'Lumma Dev portal - stoppen: Ctrl+C, daarna dit venster sluiten.'
        & dotnet run --project LummaSimulator.Web --configuration Release --no-build --no-restore --launch-profile https
        $portalExit = $LASTEXITCODE
        Write-Host "Portal afgesloten (exitcode $portalExit). Sluit ook WPF en het startvenster."
        if($portalExit -ne 0) {Read-Host 'Enter om dit foutvenster te sluiten'}
        exit $portalExit
    }
    $repo = Split-Path $PSScriptRoot
    Set-Location -LiteralPath $repo
    if($Stop) {
        $recordFile=Get-DevRecordPath $repo
        if(-not (Test-Path -LiteralPath $recordFile)){Write-Host 'Geen geregistreerde ontwikkelprocessen.';exit 0}
        $children=@(foreach($record in @(Read-DemoRecords $recordFile)) {
            if(-not $record.PSObject.Properties['Source']){continue}
            $process=Find-DevOwned $record $repo;if(-not $process){continue}
            $stop=if($record.PSObject.Properties['StopFile']){$record.StopFile}else{''}
            [pscustomobject]@{Process=$process;Role=$record.Role;StopFile=$stop}
        })
        if($children.Count){Stop-DemoChildren $children}else{Write-Host 'Geen verifieerbare actieve ontwikkelprocessen.'}
        exit 0
    }
    # Keep compatibility inputs in this launcher only; builders must never inherit either value.
    $devMonitorKey=$env:LUMMA_MONITOR_KEY;$devMonitorHash=$env:Monitor__KeyHash
    Remove-Item Env:LUMMA_MONITOR_KEY,Env:Monitor__KeyHash,Env:LUMMA_PORTAL_STOP_FILE -ErrorAction SilentlyContinue
    if($BuildOnly){
        $copy=New-DevSourceCopy $repo
        Invoke-DevBuild $copy
        Write-Host "Geisoleerde build gereed: $($copy.Work). Geen applicaties of runtimegegevens gestart."
        exit 0
    }
    $portal=$null; $wpf=$null; $stopFile=$null
    $children=[Collections.Generic.List[object]]::new()
    Assert-InteractiveAppSession
    $mutex = [Threading.Mutex]::new($false, 'Local\LummaSimulator.DevLauncher')
    $owned = $false
    try {
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw 'Het ontwikkelstartvenster is al actief. Gebruik de bestaande vensters.' }
        $null = Get-Command dotnet -ErrorAction Stop
        $null = Get-Command git -ErrorAction Stop
        $sdks = & dotnet --list-sdks
        if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) { throw '.NET 10 SDK ontbreekt of is niet beschikbaar.' }
        $profile = (Get-Content -LiteralPath 'LummaSimulator.Web/Properties/launchSettings.json' -Raw | ConvertFrom-Json).profiles.https
        if ($profile.applicationUrl -ne 'https://localhost:7243' -or $profile.environmentVariables.ASPNETCORE_ENVIRONMENT -ne 'Development') {
            throw 'Het https-launchprofile is gewijzigd. Laat de ontwikkelstarter opnieuw beoordelen.'
        }
        Assert-Idle 7243
        Assert-StablePaths $repo
        Write-Host 'Actuele broncode bouwen in een nieuwe artifacts-map. Geen bestaande buildoutput wordt overschreven.'
        $copy=New-DevSourceCopy $repo
        $work=$copy.Work;$source=$copy.Source
        Invoke-DevBuild $copy
        Assert-Idle 7243
        $app = Join-Path $source 'LummaSimulator.App/bin/Release/net10.0-windows/LummaSimulator.App.exe'
        if (-not (Test-Path -LiteralPath $app)) { throw 'WPF-buildoutput ontbreekt; geen apps gestart.' }
        # Run the portal apphost directly, preserving the validated HTTPS profile and inherited overrides.
        $recordFile=Get-DevRecordPath $repo
        $stopFile=New-PortalStopFile
        $portalExe=Join-Path $source 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.exe'
        $portalInfo=New-DevChildInfo $portalExe '' '' $devMonitorHash $stopFile
        $portalInfo.CreateNoWindow=$true
        $portalInfo.WorkingDirectory=Split-Path $portalExe
        foreach($variable in $profile.environmentVariables.PSObject.Properties){$portalInfo.EnvironmentVariables[$variable.Name]=[string]$variable.Value}
        $portalInfo.EnvironmentVariables['ASPNETCORE_URLS']=$profile.applicationUrl
        $portal=[Diagnostics.Process]::Start($portalInfo)
        $children.Add([pscustomobject]@{Process=$portal;Role='Portal';StopFile=$stopFile})
        Save-DevProcess $recordFile $portal $source 'Portal' $stopFile
        $ready = $false
        for ($i = 0; $i -lt 40; $i++) {
            if ($portal.HasExited) { throw "Portal stopte (exitcode $($portal.ExitCode)). Controleer HTTPS, configuratie en opslag." }
            try {
                $request = [Net.HttpWebRequest]::Create('https://localhost:7243/Presenter/Login')
                $request.Proxy = $null; $request.AllowAutoRedirect = $false; $request.Timeout = 1000
                $response = $request.GetResponse()
                try { $ready = Test-PortalReadyResponse ([int]$response.StatusCode) $response.Headers['Location'] ([uri]$profile.applicationUrl) } finally { $response.Close() }
                if ($ready) { break }
            } catch { }
            Start-Sleep -Milliseconds 250
        }
        if (-not $ready) { throw 'Portal niet gereed via vertrouwd HTTPS. Controleer het bestaande certificaat, de configuratie en poort 7243.' }
        $appInfo=New-DevChildInfo $app '' $devMonitorKey
        $appInfo.WorkingDirectory=Split-Path $app
        $wpf=[Diagnostics.Process]::Start($appInfo)
        $children.Add([pscustomobject]@{Process=$wpf;Role='App';StopFile=''})
        Save-DevProcess $recordFile $wpf $source 'App'
        Wait-LummaAppWindow $wpf
        Start-Process 'https://localhost:7243'
        Write-Host 'Portal bereikbaar op https://localhost:7243. LummaApp gestart en het hoofdvenster is gereed. Stop met Start-LummaDev.bat stop.'
        Write-Host "Build bewaard in: $work"
        Wait-DemoConsole $children '.\Start-LummaDev.bat stop'
    } finally {
        try {if($children.Count){Stop-DemoChildren $children}}
        finally {if($owned){$mutex.ReleaseMutex()};$mutex.Dispose()}
    }
} catch {
    Write-Host 'FOUT: ontwikkelstart mislukt. Er worden geen processen geforceerd gestopt.' -ForegroundColor Red
    # Do not print raw exception data: configuration failures can contain secrets.
    if ($_.CategoryInfo.Category -eq 'OperationStopped') { Write-Host $_.Exception.Message }
    else { Write-Host 'Controleer bovenstaande uitvoer, SDK/NuGet-toegang, runtimepaden, bestaande processen en HTTPS-configuratie.' }
    exit 1
}
