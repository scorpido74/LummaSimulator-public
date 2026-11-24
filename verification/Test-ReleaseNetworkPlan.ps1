# Pure planning checks. No listener, firewall, certificate, URLACL or runtime-data changes.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$work=Join-Path $repo ('artifacts/network-plan-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $work
$settings=Join-Path $work 'synthetic-receiver.json'
@{Enabled=$true;Origin='https://app.demo.example'}|ConvertTo-Json|Set-Content -LiteralPath $settings
$script=Join-Path $repo 'packaging/EventEdition/Firewall.ps1'
$count=0
function Check($condition,$name){if(-not $condition){throw $name};$script:count++;Write-Output "PASS: $name"}
$portal=@(& $script -Action Plan -HostRole Portal -PortalOrigin 'https://portal.demo.example' -PortalProgram (Join-Path $work 'LummaSimulator.Web.exe') -DemoSubnet '192.0.2.0/24' -LocalAddress '192.0.2.10' -NetworkProfile Private)
Check ($portal.Count -eq 1 -and $portal[0].LocalPort -eq 443 -and $portal[0].Program.EndsWith('LummaSimulator.Web.exe')) 'Portal plan scopes port 443 to its executable'
Check ($portal[0].LocalAddress -contains '192.0.2.10' -and $portal[0].RemoteAddress -contains '192.0.2.0/24' -and -not $portal[0].ListenerVerified) 'Plan has explicit local and remote addresses and claims no verified listener'
$app=@(& $script -Action Plan -HostRole AppPc -HelperReceiverSettingsPath $settings -HelperSubnet '192.0.2.50/32' -LocalAddress '192.0.2.20' -NetworkProfile Domain)
Check ($app.Count -eq 1 -and $app[0].LocalPort -eq 443 -and $app[0].Program -eq 'System') 'App-pc plan requires only the HTTP.sys receiver, not a portal'
$backend=@(& $script -Action Plan -HostRole Portal -PortalOrigin 'https://portal.demo.example:7243' -PortalProgram (Join-Path $work 'LummaSimulator.Web.exe') -DemoSubnet '192.0.2.50/32' -LocalAddress '192.0.2.10' -NetworkProfile Private)
Check ($backend[0].LocalPort -eq 7243) 'Explicit portal 7243 stays separate from helper 443'
foreach($bad in @('0.0.0.0','::','127.0.0.1','Any')){
    $denied=$false
    try{& $script -Action Plan -HostRole AppPc -HelperReceiverSettingsPath $settings -HelperSubnet '192.0.2.50/32' -LocalAddress $bad -NetworkProfile Private|Out-Null}catch{$denied=$true}
    Check $denied "Unscoped/loopback local address is refused: $bad"
}
$denied=$false
try{& $script -Action Plan -HostRole AppPc -HelperReceiverSettingsPath $settings -HelperSubnet '0.0.0.0/0' -LocalAddress '192.0.2.20' -NetworkProfile Private|Out-Null}catch{$denied=$true}
Check $denied 'Any remote subnet is refused'
Write-Output "$count network plan checks passed. No effective firewall rule was changed."
