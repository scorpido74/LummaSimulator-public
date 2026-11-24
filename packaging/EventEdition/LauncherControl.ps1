# Fixed user-private local control folder; no network listener or secrets in control records.
function Test-PortalReadyResponse([int]$Status,[string]$Location,[uri]$Origin) {
    if($Status -eq 200){return $true}
    if($Status -ne 302 -or -not $Location){return $false}
    $target=$null
    if(-not [uri]::TryCreate($Origin,$Location,[ref]$target)){return $false}
    return $target.GetLeftPart([UriPartial]::Authority) -eq $Origin.GetLeftPart([UriPartial]::Authority) -and
        $target.AbsolutePath -eq '/Presenter/Setup' -and -not $target.Query -and -not $target.Fragment
}
function Assert-InteractiveAppSession {
    if(-not [Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0){
        throw 'LummaApp vereist een interactieve Windows-sessie. Start Start-LummaSimulator.cmd als gebruiker op de app-pc.'
    }
}
function Assert-LoopbackPortAvailable([int]$Port) {
    $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
    try { $probe.Start() }
    catch [Net.Sockets.SocketException] { throw "HTTPS-poort $Port is bezet. Controleer welke toepassing luistert en start opnieuw zodra de poort vrij is." }
    finally { $probe.Stop() }
}
function Wait-LummaAppWindow($Process) {
    for($i=0;$i -lt 32;$i++){
        $Process.Refresh()
        if($Process.HasExited){throw "LummaApp stopte (exitcode $($Process.ExitCode)). Start Start-LummaSimulator.cmd opnieuw en controleer de app-instellingen."}
        if($Process.MainWindowHandle -ne [IntPtr]::Zero){return}
        Start-Sleep -Milliseconds 250
    }
    throw 'LummaApp draait, maar het hoofdvenster is niet gereed. Controleer het bestaande venster en de app-instellingen voordat je opnieuw start.'
}
function Request-LummaAppStop($Process) {
    for($i=0;$i -lt 32;$i++){
        $Process.Refresh()
        if($Process.HasExited){return}
        if($Process.CloseMainWindow()){
            if($Process.WaitForExit(15000)){return}
            break
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'LummaApp bevestigde het afsluiten niet. Sluit het eigen App-venster en probeer daarna de stopopdracht opnieuw.'
}
function Stop-DemoChildren($Children) {
    $failures=@()
    foreach($child in @($Children | Sort-Object {if($_.Role -eq 'App'){0}else{1}})){
        try {
            if($child.Process.HasExited){continue}
            if($child.Role -eq 'App'){Request-LummaAppStop $child.Process}
            else{Request-PortalStop $child.StopFile $child.Process}
            Write-Host "$($child.Role) netjes afgesloten."
        } catch {$failures+=@($_.Exception.Message)}
    }
    if($failures.Count){throw ($failures -join [Environment]::NewLine)}
}
function Read-DemoRecords([string]$Path) {
    if(-not (Test-Path -LiteralPath $Path -PathType Leaf)){return}
    $decoded=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    # Windows PowerShell 5.1 emits JSON arrays as a single pipeline object.
    function Unwrap-DemoRecords($Value,[int]$Depth=0){
        if($Depth -gt 16){Write-Warning 'Procesregistratie is te diep genest; ongeldige tak overgeslagen.';return}
        if($Value -is [array]){foreach($item in $Value){Unwrap-DemoRecords $item ($Depth+1)}}
        elseif($null -ne $Value){$Value}
    }
    foreach($row in @(Unwrap-DemoRecords $decoded)){
        $processId=0
        if($null -eq $row -or -not $row.PSObject.Properties['Path'] -or -not $row.PSObject.Properties['Id'] -or
            -not $row.PSObject.Properties['StartTicks'] -or $row.Path -isnot [string] -or -not $row.Path -or
            -not [int]::TryParse([string]$row.Id,[ref]$processId) -or $processId -le 0){
            Write-Warning 'Ongeldig procesrecord overgeslagen; eigendom is niet vast te stellen.'
            continue
        }
        $row
    }
}
function Confirm-DemoProcess($Process,$Record) {
    if(-not $Process){return $null}
    try {
        if($Process.HasExited){return $null}
        if(-not $Process.Path){Write-Warning "Procespad van PID $($Record.Id) is niet beschikbaar; proces overgeslagen.";return $null}
        if($Process.Path -eq $Record.Path -and $Process.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$Record.StartTicks){return $Process}
    } catch {Write-Warning "PID $($Record.Id) is niet toegankelijk; proces overgeslagen."}
    return $null
}
function Wait-DemoConsole($Children,[string]$StopCommand) {
    Write-Host "Ctrl+C stopt de onderdelen die dit venster heeft gestart. Stopopdracht: $StopCommand"
    $keyboard=$false
    if(-not [Console]::IsInputRedirected){try{$previous=[Console]::TreatControlCAsInput;[Console]::TreatControlCAsInput=$true;$keyboard=$true}catch{}}
    try {
        $last=''
        do {
            $running=$false
            $status=foreach($child in $Children){
                $child.Process.Refresh()
                if($child.Process.HasExited){"$($child.Role) gestopt ($($child.Process.ExitCode))"}
                else{$running=$true;"$($child.Role) actief (PID $($child.Process.Id))"}
            }
            $line=$status -join ' | '
            if($line -ne $last){Write-Host $line;$last=$line}
            if(-not $running){break}
            if($keyboard -and [Console]::KeyAvailable){
                $key=[Console]::ReadKey($true)
                if($key.Key -eq 'C' -and ($key.Modifiers -band [ConsoleModifiers]::Control)){break}
            }
            Start-Sleep -Milliseconds 250
        } while($true)
    } finally {if($keyboard){[Console]::TreatControlCAsInput=$previous}}
}
function Get-PortalControlRoot {
    $root=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'LummaSimulator/LauncherControl-v1'))
    $drive=[IO.Path]::GetPathRoot($root)
    if($root.StartsWith('\\') -or ([IO.DriveInfo]::new($drive)).DriveType -ne 'Fixed') { throw 'Lokale controlmap vereist.' }
    $current=$drive
    foreach($part in $root.Substring($drive.Length).Split([IO.Path]::DirectorySeparatorChar)) {
        if(-not $part) { continue };$current=Join-Path $current $part
        if((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Controlmap met reparsepunt geweigerd.' }
    }
    return $root
}
function Assert-PrivateControlRoot {
    $root=Get-PortalControlRoot
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    if(-not (Test-Path -LiteralPath $root)) {
        $null=New-Item -ItemType Directory -Path $root -Force
        $acl=New-Object Security.AccessControl.DirectorySecurity
        $acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
        Set-Acl -LiteralPath $root -AclObject $acl
    }
    $actual=Get-Acl -LiteralPath $root
    if($actual.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $sid.Value) { throw 'Controlmap heeft een andere eigenaar.' }
    foreach($rule in $actual.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -ne $sid.Value) { throw 'Controlmap is niet gebruikersgebonden.' }
    }
    return $root
}
function New-PortalStopFile {
    return Join-Path (Assert-PrivateControlRoot) ('portal-stop-'+[guid]::NewGuid().ToString('N')+'.request')
}
function Request-PortalStop([string]$Path,$Process) {
    $root=Assert-PrivateControlRoot
    $full=[IO.Path]::GetFullPath($Path)
    if((Split-Path $full) -ne $root -or (Split-Path $full -Leaf) -notmatch '^portal-stop-[a-f0-9]{32}\.request$') { throw 'Ongeldig portalstoprecord; niets gestopt.' }
    if($Process.HasExited) { return }
    if(-not (Test-Path -LiteralPath $full)) {
        $stream=[IO.File]::Open($full,'CreateNew','Write','None')
        try {$stream.WriteByte(1);$stream.Flush($true)} finally {$stream.Dispose()}
    }
    if(-not $Process.WaitForExit(15000)) { throw 'Portal bevestigde netjes afsluiten niet. Geen geforceerde beëindiging uitgevoerd.' }
}
