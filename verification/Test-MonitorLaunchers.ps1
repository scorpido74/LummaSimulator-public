# Synthetic helper tests only: no apps, databases, secrets, certificates or active processes.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path $PSScriptRoot
$checks=0
function Check([bool]$ok,[string]$message){if(-not $ok){throw $message};$script:checks++;Write-Output ('PASS: '+$message)}
function Import-Function([string]$file,[string]$name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $file),[ref]$tokens,[ref]$errors)
    if($errors){throw 'PowerShell syntax error'}
    $definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    return [scriptblock]::Create($definition.Extent.Text)
}
. (Import-Function 'packaging/Start-LummaDev.ps1' 'New-DevChildInfo')
. (Import-Function 'packaging/EventEdition/Demo.ps1' 'New-ChildInfo')
. (Import-Function 'packaging/EventEdition/Demo.ps1' 'Get-HelperPackage')
. (Import-Function 'packaging/EventEdition/Demo.ps1' 'Get-CertificateBundleDirectory')
$beforeKey=$env:LUMMA_MONITOR_KEY;$beforeHash=$env:Monitor__KeyHash;$beforeStop=$env:LUMMA_PORTAL_STOP_FILE
try {
    $env:LUMMA_MONITOR_KEY='synthetic-key-not-a-credential';$env:Monitor__KeyHash='synthetic-hash';$env:LUMMA_PORTAL_STOP_FILE='synthetic-inherited-stop'
    $portal=New-DevChildInfo 'portal.exe' '--synthetic' '' 'synthetic-hash' 'synthetic-stop'
    $app=New-DevChildInfo 'app.exe' '' 'synthetic-key-not-a-credential'
    Check (-not $portal.EnvironmentVariables.ContainsKey('LUMMA_MONITOR_KEY')) 'Dev portal cannot inherit WPF raw key'
    Check ($portal.EnvironmentVariables['Monitor__KeyHash'] -eq 'synthetic-hash') 'Dev portal retains explicit legacy hash'
    Check (-not $app.EnvironmentVariables.ContainsKey('Monitor__KeyHash') -and -not $app.EnvironmentVariables.ContainsKey('LUMMA_PORTAL_STOP_FILE')) 'Dev WPF does not inherit portal hash or stop control'
    Check ($app.EnvironmentVariables['LUMMA_MONITOR_KEY'] -eq 'synthetic-key-not-a-credential') 'Dev WPF supports explicit compatibility override'
    Check ($portal.Arguments -eq '--synthetic' -and $app.Arguments -eq '') 'Dev arguments never contain monitor inputs'
    $packageRoot=Join-Path $repo 'artifacts/synthetic-package';$runtimeRoot=Join-Path $repo 'artifacts/synthetic-runtime';$origin='https://localhost';$portalHost='localhost';$Action='Start'
    $certificate=New-Object psobject
    $certificate|Add-Member ScriptMethod GetNameInfo {param($kind,$issuer)return 'localhost'}
    $portal=New-ChildInfo 'Portal/LummaSimulator.Web.exe' '' 'synthetic-stop'
    $app=New-ChildInfo 'Simulator/LummaSimulator.App.exe'
    Check (-not $portal.EnvironmentVariables.ContainsKey('LUMMA_MONITOR_KEY') -and $portal.EnvironmentVariables['Monitor__KeyHash'] -eq 'synthetic-hash') 'Edition portal retains explicit hash without raw key'
    Check ($app.EnvironmentVariables['LUMMA_MONITOR_KEY'] -eq 'synthetic-key-not-a-credential' -and -not $app.EnvironmentVariables.ContainsKey('Monitor__KeyHash')) 'Edition WPF receives only explicit raw compatibility key'
    Check (-not $app.EnvironmentVariables.ContainsKey('LUMMA_PORTAL_STOP_FILE')) 'Edition WPF cannot inherit portal stop control'
    Check ($portal.Arguments -eq '' -and $app.Arguments -eq '') 'Edition arguments contain no monitor inputs'
    $env:LUMMA_MONITOR_KEY=$null;$env:Monitor__KeyHash=$null
    $portal=New-ChildInfo 'Portal/LummaSimulator.Web.exe'
    Check (-not $portal.EnvironmentVariables.ContainsKey('Monitor__KeyHash')) 'Unconfigured Edition no longer writes an empty overriding monitor hash'
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Request-PortalStop')
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Test-PortalReadyResponse')
    Check (Test-PortalReadyResponse 200 '' 'https://localhost:7243') 'Installed local portal readiness accepts Login 200'
    Check (Test-PortalReadyResponse 302 '/Presenter/Setup' 'https://localhost:7243') 'Fresh local portal readiness accepts exact Setup redirect before App startup'
    Check (-not (Test-PortalReadyResponse 302 'https://other.invalid/Presenter/Setup' 'https://localhost:7243')) 'Unrelated listener redirect is not accepted as portal readiness'
    Check (-not (Test-PortalReadyResponse 503 '' 'https://localhost:7243')) 'Unavailable portal is reported by readiness check'
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Assert-LoopbackPortAvailable')
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    try {
        $port=$listener.LocalEndpoint.Port
        $occupied=$false
        try { Assert-LoopbackPortAvailable $port } catch { $occupied=$_.Exception.Message -like "*poort $port is bezet*" }
        Check $occupied 'An actually occupied loopback port fails with its number and a recovery action'
    } finally { $listener.Stop() }
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Wait-LummaAppWindow')
    $noApp=[pscustomobject]@{HasExited=$true;ExitCode=42;MainWindowHandle=[IntPtr]::Zero}
    $noApp|Add-Member ScriptMethod Refresh {}
    $failed=$false
    try{Wait-LummaAppWindow $noApp}catch{$failed=$_.Exception.Message -like '*42*Start-LummaSimulator.cmd*'}
    Check $failed 'App startup failure gives its exit code and supported retry command'
    . (Import-Function 'packaging/EventEdition/Demo.ps1' 'Start-Owned')
    $appPath=Join-Path $packageRoot 'Simulator/LummaSimulator.App.exe'
    function Read-Records { [pscustomobject]@{Path=$appPath} }
    function Find-Owned($record) { [pscustomobject]@{Id=42} }
    function New-Child { throw 'Repeated startup must not spawn a child' }
    Check ((Start-Owned 'Simulator/LummaSimulator.App.exe').Id -eq 42) 'Repeated startup reuses the registered App process'
    . (Import-Function 'packaging/EventEdition/Demo.ps1' 'Start-ConfiguredComponents')
    function Start-Portal {'Portal'}
    function Start-LummaApp {'App'}
    Check (((Start-ConfiguredComponents 'Both') -join ',') -eq 'Portal,App') 'Default combined startup launches portal and App once'
    Check (((Start-ConfiguredComponents 'Portal') -join ',') -eq 'Portal' -and ((Start-ConfiguredComponents 'App') -join ',') -eq 'App') 'Explicit split installations keep their selected component'
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Read-DemoRecords')
    . (Import-Function 'packaging/EventEdition/LauncherControl.ps1' 'Confirm-DemoProcess')
    $fixture=Join-Path $repo ('artifacts/launcher-record-test-'+[guid]::NewGuid().ToString('N')+'.json')
    '[]' | Set-Content -LiteralPath $fixture
    Check (@(Read-DemoRecords $fixture).Count -eq 0) 'Windows PowerShell empty process list has no Path access'
    '[[{"Path":"C:\\Fixture\\App.exe","Id":17,"StartTicks":"100"}],{"Id":18},{"Path":"C:\\Fixture\\Portal.exe","Id":19,"StartTicks":"100"}]' | Set-Content -LiteralPath $fixture
    $valid=@(Read-DemoRecords $fixture 3>$null)
    Check ($valid.Count -eq 2 -and $valid[0].Id -eq 17 -and $valid[1].Id -eq 19) 'Legacy nested JSON is flattened, while pathless records are skipped'
    Check ($null -eq (Confirm-DemoProcess ([pscustomobject]@{HasExited=$true}) $valid[0])) 'Exited process can be stopped repeatedly'
    Check ($null -eq (Confirm-DemoProcess ([pscustomobject]@{HasExited=$false;Path=$null}) $valid[0] 3>$null)) 'Unreadable process path never authorizes a stop'
    function Assert-PrivateControlRoot {return (Join-Path $repo 'artifacts/synthetic-control')}
    $script:waits=0
    $fake=[pscustomobject]@{HasExited=$true}
    $fake|Add-Member ScriptMethod WaitForExit {param($milliseconds)$script:waits++;return $true}
    Request-PortalStop (Join-Path (Assert-PrivateControlRoot) ('portal-stop-'+('a'*32)+'.request')) $fake
    Check ($waits -eq 0) 'Repeated stop of exited owned process is harmless and writes nothing'
    $denied=$false;try{Request-PortalStop (Join-Path $repo 'foreign.request') $fake}catch{$denied=$true}
    Check $denied 'Stop request cannot target an arbitrary file'
    foreach($file in @('packaging/Start-LummaDev.ps1','packaging/EventEdition/Demo.ps1')){
        $source=Get-Content -LiteralPath (Join-Path $repo $file) -Raw
        Check ($source -notmatch '\.Kill\(|Stop-Process') ('No forced termination in '+$file)
    }
} finally {$env:LUMMA_MONITOR_KEY=$beforeKey;$env:Monitor__KeyHash=$beforeHash;$env:LUMMA_PORTAL_STOP_FILE=$beforeStop}
Write-Output "$checks monitor launcher checks passed. Real startup/shutdown remains a separate acceptance test."
