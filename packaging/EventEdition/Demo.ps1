param([ValidateSet('Setup','Start','Stop','Portal','App','Bootstrap','Firewall','ExportHelper')][string]$Action='Setup',
    [ValidateSet('Audit','Plan','Install','Remove')][string]$FirewallAction='Audit',
    [ValidateSet('Portal','AppPc','Combined')][string]$HostRole='Portal',
    [string[]]$LocalAddress,[string]$PortalOrigin,[string]$PortalProgram,[string[]]$DemoSubnet,
    [ValidateSet('Private','Domain')][string[]]$NetworkProfile,
    [string]$HelperReceiverSettingsPath,[string[]]$HelperSubnet,
    [string]$AppOrigin,[string]$PortalListenAddress,[string]$AppListenAddress,
    [ValidateSet('Both','Portal','App')][string]$Component='Both',[switch]$Detached)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($Action -eq 'Firewall'){
    $firewallParameters=@{Action=$FirewallAction}
    foreach($name in @('HostRole','LocalAddress','PortalOrigin','PortalProgram','DemoSubnet','NetworkProfile','HelperReceiverSettingsPath','HelperSubnet')){
        $value=Get-Variable -Name $name -ValueOnly
        if($value){$firewallParameters[$name]=$value}
    }
    & (Join-Path $PSScriptRoot 'Firewall.ps1') @firewallParameters
    exit
}
. (Join-Path $PSScriptRoot 'LauncherControl.ps1')
$packageRoot=$PSScriptRoot
$settingsPath=Join-Path $packageRoot 'event-edition.local.json'
$origin='https://localhost';$portalHost='localhost'
function Set-PortalOrigin([string]$Value) {
    $candidate=$null
    if(-not [uri]::TryCreate($Value,[UriKind]::Absolute,[ref]$candidate) -or $candidate.Scheme -ne 'https' -or -not $candidate.IsDefaultPort -or $Value -ne $candidate.GetLeftPart([UriPartial]::Authority) -or $candidate.UserInfo -or $candidate.Query -or $candidate.Fragment -or $candidate.HostNameType -ne 'Dns') { throw 'Gebruik een exacte HTTPS-origin met vaste DNS-naam en standaardpoort 443.' }
    $script:origin=$Value;$script:portalHost=$candidate.DnsSafeHost
}


function Assert-LocalWindows {
    if([Environment]::OSVersion.Version.Build -lt 22000 -or -not [Environment]::Is64BitOperatingSystem) { throw 'Windows 11 x64 is vereist.' }
    foreach($path in @($packageRoot,$env:LOCALAPPDATA)) {
        if($path.StartsWith('\\') -or ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($path))).DriveType -ne 'Fixed') { throw 'Gebruik een lokale vaste schijf, geen netwerkmap of gedeelde VM-map.' }
    }
    foreach($file in @('Portal/LummaSimulator.Web.exe','Simulator/LummaSimulator.App.exe','Simulator/LocalTestServer/LummaSimulator.LocalTestServer.exe')) {
        if(-not (Test-Path -LiteralPath (Join-Path $packageRoot $file) -PathType Leaf)) { throw "Onvolledig pakket: $file ontbreekt." }
    }
}
function Get-Certificate([string]$thumbprint) {
    $cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
    if(-not $cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date) -or $cert.GetNameInfo('DnsName',$false) -ne $portalHost) { throw 'Certificaat vereist een private key, geldigheid en de geconfigureerde DNS-naam.' }
    $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
    try { if(-not $chain.Build($cert)) { throw 'Het certificaat wordt niet vertrouwd. Volg de certificaatinstructies in de handleiding.' } } finally { $chain.Dispose() }
    $subject=$cert.GetNameInfo('SimpleName',$false)
    $matches=@(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.GetNameInfo('SimpleName',$false) -eq $subject })
    if($matches.Count -ne 1) { throw 'Gebruik een certificaat met een unieke CN; Kestrel selecteert op subject.' }
    return $cert
}
function Get-HelperPackage {
    $folder=Join-Path $packageRoot 'Portal/wwwroot/downloads'
    if(-not (Test-Path -LiteralPath $folder)){return}
    $files=@(Get-ChildItem -LiteralPath $folder -File -Filter 'LummaSimulator-PresenterHelper-v*-win-x64.zip')
    if($files.Count -ne 1){throw 'Precies één Presenter Helper-download is vereist.'}
    return $files[0]
}
function Get-CertificateBundleDirectory($package) {
    $hash=(Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    return Join-Path $runtimeRoot ("helper-downloads/$($certificate.Thumbprint)-$hash")
}
function Export-HelperPackage {
    $package=Get-HelperPackage
    if(-not $package){throw 'Presenter Helper-pakket ontbreekt.'}
    $destination=Get-CertificateBundleDirectory $package
    if(Test-Path -LiteralPath $destination){Write-Host "Helperpakket: $destination";return}
    $parent=Split-Path $destination
    $stage=Join-Path $parent ('pending-'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $stage -Force
    $zip=Join-Path $stage $package.Name
    Copy-Item -LiteralPath $package.FullName -Destination $zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression
    $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach($name in 'Portal.cer','PORTAL-VERTROUWEN.txt'){
            if($archive.GetEntry($name)){throw "Het basispakket bevat al $name."}
        }
        $entry=$archive.CreateEntry('Portal.cer').Open()
        try {
            $bytes=$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
            $entry.Write($bytes,0,$bytes.Length)
        } finally {$entry.Dispose()}
        $entry=[IO.StreamWriter]::new($archive.CreateEntry('PORTAL-VERTROUWEN.txt').Open())
        try {
            $entry.WriteLine("Portal: $origin")
            $entry.WriteLine("Certificaatthumbprint: $($certificate.Thumbprint)")
            $entry.WriteLine('Vergelijk de thumbprint via een kanaal van je netwerkbeheerder. Een publiek vertrouwde keten vereist geen handmatige installatie.')
            $entry.WriteLine('Laat bij interne PKI de juiste vertrouwde CA via de beheerprocedure installeren. Importeer dit servercertificaat alleen als expliciet goedgekeurd voor een zelfondertekende portal.')
        } finally {$entry.Dispose()}
    } finally {$archive.Dispose()}
    "$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)  $($package.Name)" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
    [IO.Directory]::Move($stage,$destination)
    Write-Host "Helperpakket: $destination"
}
function New-ChildInfo([string]$relative,[string]$arguments='',[string]$stopFile='') {
    $exe=Join-Path $packageRoot $relative
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$exe; $info.WorkingDirectory=Split-Path $exe; $info.Arguments=$arguments
    $info.UseShellExecute=$false; $info.CreateNoWindow=($Action -ne 'Bootstrap')
    # Never inherit deployment overrides or monitor credentials from another installation.
    foreach($key in @($info.EnvironmentVariables.Keys)) {
        if($key -match '^(ASPNETCORE_|DOTNET_|HTTP_PORTS$|HTTPS_PORTS$|URLS$|WebAuthn__|Kestrel__|Database__|Storage__|Experience__|ReverseProxy__|Monitor__|Presenter__|Helper__|DemoNetwork__|LUMMA_)') { $info.EnvironmentVariables.Remove($key) }
    }
    $values=@{
        ASPNETCORE_ENVIRONMENT='Production'; ASPNETCORE_URLS=$origin; AllowedHosts=$portalHost
        WebAuthn__RpId=$portalHost; WebAuthn__Origins__0=$origin; Experience__Hostname=$portalHost
        Database__Path=(Join-Path $runtimeRoot 'PasskeyPortal/portal.db'); Storage__DataDirectory=$runtimeRoot
        LUMMA_DATA_DIRECTORY=$runtimeRoot; LUMMA_LOG_DIRECTORY=(Join-Path $runtimeRoot 'Logs')
        LUMMA_EXPORT_DIRECTORY=(Join-Path $runtimeRoot 'Exports'); LUMMA_PORTAL_ORIGIN=$origin
    }
    if($relative -eq 'Portal/LummaSimulator.Web.exe') {
        $download=Get-HelperPackage
        if($download){
            $values.Helper__Enabled='true';$values.Helper__DownloadUrl=$origin+'/downloads/presenter-helper'
            $bundle=Get-CertificateBundleDirectory $download
            if(Test-Path -LiteralPath $bundle -PathType Container){$values.Helper__PackageDirectory=$bundle}
        }
        if((Get-Variable -Name settings -ErrorAction SilentlyContinue) -and 'Network' -in $settings.PSObject.Properties.Name){
            foreach($property in $settings.Network.PSObject.Properties){$values['DemoNetwork__'+$property.Name]=[string]$property.Value}
        }
        if($env:Monitor__KeyHash) { $values.Monitor__KeyHash=$env:Monitor__KeyHash }
        if($stopFile) { $values.LUMMA_PORTAL_STOP_FILE=$stopFile }
        $values.Kestrel__Certificates__Default__Subject=$certificate.GetNameInfo('SimpleName',$false)
        $values.Kestrel__Certificates__Default__Store='My'
        $values.Kestrel__Certificates__Default__Location='CurrentUser'
        $values.Kestrel__Certificates__Default__AllowInvalid='false'
    }
    if($relative -eq 'Simulator/LummaSimulator.App.exe' -and $env:LUMMA_MONITOR_KEY) { $values.LUMMA_MONITOR_KEY=$env:LUMMA_MONITOR_KEY }
    foreach($key in $values.Keys) { $info.EnvironmentVariables[$key]=[string]$values[$key] }
    return $info
}
function New-Child([string]$relative,[string]$arguments='',[string]$stopFile='') {
    return [Diagnostics.Process]::Start((New-ChildInfo $relative $arguments $stopFile))
}
function Read-Records {
    Read-DemoRecords $processFile
}
function Find-Owned($record) {
    if($record.Path -notin @((Join-Path $packageRoot 'Portal/LummaSimulator.Web.exe'),(Join-Path $packageRoot 'Simulator/LummaSimulator.App.exe'))) { return $null }
    $process=Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    Confirm-DemoProcess $process $record
}
function Start-Owned([string]$relative) {
    $exe=Join-Path $packageRoot $relative
    foreach($record in @(Read-Records)) {
        $existing=Find-Owned $record
        if($record.Path -eq $exe -and $existing){return $existing}
        if($record.Path -ne $exe -and (Split-Path $record.Path -Leaf) -eq (Split-Path $exe -Leaf)){
            $foreign=Confirm-DemoProcess (Get-Process -Id $record.Id -ErrorAction SilentlyContinue) $record
            if($foreign){throw 'Dit installatie-ID draait vanuit een andere pakketmap. Gebruik de stopopdracht in die map.'}
        }
    }
    $stopFile=if($relative -eq 'Portal/LummaSimulator.Web.exe') { New-PortalStopFile } else { '' }
    $child=New-Child $relative '' $stopFile
    $role=if($stopFile){'Portal'}else{'App'}
    $children.Add([pscustomobject]@{Process=$child;Role=$role;StopFile=$stopFile})
    try {
        $record=[pscustomobject]@{Id=$child.Id;StartTicks=$child.StartTime.ToUniversalTime().Ticks.ToString();Path=$exe;StopFile=$stopFile}
        $records=@(Read-Records | Where-Object {Get-Process -Id $_.Id -ErrorAction SilentlyContinue})+@($record)
        ConvertTo-Json -InputObject $records | Set-Content -LiteralPath $processFile -Encoding UTF8
    } catch {
        try {
            if($stopFile) { Request-PortalStop $stopFile $child }
            elseif(-not $child.HasExited) { $null=$child.CloseMainWindow(); $null=$child.WaitForExit(15000) }
        } catch { Write-Host 'Automatisch netjes afsluiten niet bevestigd. Sluit uitsluitend het eigen venster.' }
        throw 'Procesadministratie mislukt; geen geforceerde stop uitgevoerd.'
    }
    return $child
}
function Start-Portal {
    $exe=Join-Path $packageRoot 'Portal/LummaSimulator.Web.exe'
    $owned=@(Read-Records | Where-Object { $_.Path -eq $exe -and (Find-Owned $_) })
    if($owned.Count -eq 0) {
        Assert-LoopbackPortAvailable ([uri]$origin).Port
    }
    $child=Start-Owned 'Portal/LummaSimulator.Web.exe'
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        if($child.HasExited) { throw 'Portal is afgesloten. Controleer certificaat, configuratie en opslag.' }
        try {
            $request=[Net.HttpWebRequest]::Create("$origin/Presenter/Login")
            $request.Proxy=$null; $request.AllowAutoRedirect=$false; $request.Timeout=1000
            $response=$request.GetResponse()
            try { $ready=Test-PortalReadyResponse ([int]$response.StatusCode) $response.Headers['Location'] ([uri]$origin) } finally {$response.Close()}
            if($ready){break}
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if(-not $ready) { throw 'Portal niet bereikbaar via vertrouwd HTTPS. Controleer het certificaat, de listener en poort 443; start daarna Start-PasskeyPortal.cmd.' }
    Write-Host "Portal bereikbaar: $origin"
    Start-Process $origin/Presenter
}
function Start-LummaApp {
    Assert-InteractiveAppSession
    $child=Start-Owned 'Simulator/LummaSimulator.App.exe'
    Wait-LummaAppWindow $child
    Write-Host "LummaApp gestart (PID $($child.Id)). De portalverbinding gebruikt $origin; de gekoppelde functies vereisen bestaande goedkeuring."
}
function Start-ConfiguredComponents([string]$Role) {
    if($Role -ne 'App'){Start-Portal}
    if($Role -ne 'Portal'){Start-LummaApp}
}

$startGate=$null
$children=[Collections.Generic.List[object]]::new()
$keepChildren=$false
try {
    Assert-LocalWindows
    if($Action -eq 'Setup' -and -not (Test-Path -LiteralPath $settingsPath)) {
        if($PortalOrigin){Set-PortalOrigin $PortalOrigin}
        $thumbprint=''
        if($Component -ne 'App'){
            Write-Host "HTTPS is verplicht. Laat een beheerder eerst een voor $portalHost vertrouwd certificaat installeren."
            $thumbprint=(Read-Host 'Thumbprint uit Cert:\CurrentUser\My (geen private key invoeren)').Replace(' ','')
            $null=Get-Certificate $thumbprint
        }
        $installationSettings=@{Version=1;InstallationId=[guid]::NewGuid().ToString('D');CertificateThumbprint=$thumbprint;PortalOrigin=$origin;Component=$Component}
        if($AppOrigin -or $PortalListenAddress -or $AppListenAddress -or $DemoSubnet -or $HelperSubnet){
            if(([uri]$origin).IsLoopback -or -not $AppOrigin -or -not $PortalListenAddress -or -not $AppListenAddress -or -not $DemoSubnet -or -not $HelperSubnet -or -not $NetworkProfile -or $DemoSubnet.Count -ne 1 -or $HelperSubnet.Count -ne 1 -or $NetworkProfile.Count -ne 1){throw 'Geef een volledig netwerkplan met beide hosts, beide luister-IP-adressen, twee CIDRs en één profiel op.'}
            $appUri=$null
            if(-not [uri]::TryCreate($AppOrigin,[UriKind]::Absolute,[ref]$appUri) -or $appUri.Scheme -ne 'https' -or -not $appUri.IsDefaultPort -or $appUri.IsLoopback -or $AppOrigin -ne $appUri.GetLeftPart([UriPartial]::Authority) -or $appUri.UserInfo -or $appUri.Query -or $appUri.Fragment){throw 'De app-origin moet een exacte interne HTTPS-origin op 443 zijn.'}
            foreach($address in @($PortalListenAddress,$AppListenAddress)){$ip=$null;if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or [Net.IPAddress]::IsLoopback($ip) -or $ip.Equals([Net.IPAddress]::Any) -or $ip.Equals([Net.IPAddress]::IPv6Any)){throw 'Geef concrete niet-loopback luister-IP-adressen op.'}}
            # Reuse the exact firewall CIDR validator, without executing its entry point.
            $parseErrors=$null;$tokens=$null
            $firewallAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Firewall.ps1'),[ref]$tokens,[ref]$parseErrors)
            if($parseErrors){throw 'Firewallvalidatie kon niet worden gelezen.'}
            $validator=$firewallAst.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-Subnet'},$true)
            . ([scriptblock]::Create($validator.Extent.Text));Assert-Subnet $DemoSubnet;Assert-Subnet $HelperSubnet
            $installationSettings.Network=@{PortalOrigin=$origin;AppOrigin=$AppOrigin;PortalAddress=$PortalListenAddress;AppAddress=$AppListenAddress;DemoSubnet=$DemoSubnet[0];HelperSubnet=$HelperSubnet[0];Profile=$NetworkProfile[0]}
        }
        $json=$installationSettings | ConvertTo-Json -Depth 4
        # A concurrent setup must never replace another installation identity.
        $stream=[IO.File]::Open($settingsPath,'CreateNew','Write','None')
        $writer=New-Object IO.StreamWriter($stream)
        try { $writer.Write($json) } finally { $writer.Dispose() }
    }
    if(-not (Test-Path -LiteralPath $settingsPath)) { throw 'Voer eerst Setup-Demo.cmd uit.' }
    $settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $installedComponent=if('Component' -in $settings.PSObject.Properties.Name){$settings.Component}else{'Both'}
    if($installedComponent -eq 'App' -and $Action -in @('Portal','Bootstrap')){throw 'Deze installatie is alleen voor de app-pc; start de portal uitsluitend op de server.'}
    if('PortalOrigin' -in $settings.PSObject.Properties.Name){Set-PortalOrigin $settings.PortalOrigin}
    if($PortalOrigin -and $PortalOrigin -ne $origin){throw 'Bestaande origin wordt niet gewijzigd; hiervoor is een afzonderlijk WebAuthn-migratieplan nodig.'}
    $installation=[guid]::ParseExact($settings.InstallationId,'D')
    if($settings.Version -ne 1) { throw 'Onbekende configuratieversie.' }
    $runtimeRoot=Join-Path $env:LOCALAPPDATA "LummaSimulator/EventEdition/$installation"
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $processFile=Join-Path $runtimeRoot 'started-processes.json'
    $waitForeground=$false
    if($Action -in @('Start','Portal','App')){
        try {$startGate=[IO.File]::Open((Join-Path $runtimeRoot 'start-window.lock'),'OpenOrCreate','ReadWrite','None')}
        catch [IO.IOException]{throw 'Het startvenster van deze installatie is al actief. Gebruik daar Ctrl+C of Stop-Demo.cmd.'}
    }
    # Serialize starters/stoppers for this installation; never stop by process name.
    try {$lock=[IO.File]::Open((Join-Path $runtimeRoot 'launcher.lock'),'OpenOrCreate','ReadWrite','None')}
    catch [IO.IOException]{throw 'Het startvenster van deze installatie is actief. Gebruik daar Ctrl+C.'}
    try {
        if($Action -eq 'Stop') {
            foreach($record in @(Read-Records)) {
                $child=Find-Owned $record
                if(-not $child) { continue }
                $role=if($record.Path -like '*LummaSimulator.App.exe'){'App'}else{'Portal'}
                $stop=if($record.PSObject.Properties['StopFile']){$record.StopFile}else{''}
                $children.Add([pscustomobject]@{Process=$child;Role=$role;StopFile=$stop})
            }
            $keepChildren=$true
            if($children.Count){Stop-DemoChildren $children}else{Write-Host 'Geen verifieerbare actieve processen voor deze pakketmap.'}
        } else {
            # Offline WPF remains usable even if the portal certificate has expired.
            if($Action -ne 'App' -and $installedComponent -ne 'App') { $certificate=Get-Certificate $settings.CertificateThumbprint }
            switch($Action) {
                Setup { Write-Host "Configuratie gereed. Opslag: $runtimeRoot"; Write-Host 'Start Start-Demo.cmd. De opgeslagen rol bepaalt of de portal, LummaApp of beide starten. Registreer bij een nieuwe portalinstallatie via /Presenter/Setup.' }
                Bootstrap {
                    Write-Host 'Zelfde Windows-gebruiker en installatie vereist. Bestaande bootstrap controleert OS-beheerrechten; geen automatische elevatie.'
                    $child=New-Child 'Portal/LummaSimulator.Web.exe' '--bootstrap-administrator'; $child.WaitForExit()
                    if($child.ExitCode -ne 0) { throw 'Bootstrap geweigerd of mislukt. Bestaande accounts worden niet gereset.' }
                }
                Portal { Start-Portal }
                ExportHelper { if($installedComponent -eq 'App'){throw 'Gebruik ExportHelper op de portalhost.'}; Export-HelperPackage }
                App { Start-LummaApp }
                Start {
                    Start-ConfiguredComponents $installedComponent
                    $waitForeground=-not $Detached
                }
            }
            if($Action -in @('Portal','App') -or ($Action -eq 'Start' -and $Detached)){
                $keepChildren=$true
                Write-Host 'Achtergrondmodus. Stop deze installatie met .\Stop-Demo.cmd vanuit deze pakketmap.'
            }
        }
    } finally {$lock.Dispose()}
    if($waitForeground){Wait-DemoConsole $children '.\Stop-Demo.cmd'}
} catch { Write-Host "FOUT: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
finally {
    try {if(-not $keepChildren -and $children.Count){Stop-DemoChildren $children}}
    finally {if($startGate){$startGate.Dispose()}}
}
