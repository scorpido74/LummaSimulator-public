[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Audit','Plan','Install','Remove')][string]$Action='Audit',
    [ValidateSet('Portal','AppPc','Combined')][string]$HostRole='Portal',
    [string]$PortalOrigin,[string]$PortalProgram,[string[]]$DemoSubnet,
    [ValidateSet('Private','Domain')][string[]]$NetworkProfile,
    [string[]]$LocalAddress,
    [string]$HelperReceiverSettingsPath,[string[]]$HelperSubnet
)
$ErrorActionPreference='Stop'
$group='LummaSimulator v0.7 managed';$marker='LummaSimulator v0.7 scoped network access'
function Assert-Subnet([string[]]$Values){
    if(-not $Values -or $Values.Count -gt 8){throw 'Geef 1-8 expliciete netwerk-CIDRs op.'}
    foreach($value in $Values){
        $parts=$value.Split('/');$ip=$null;$prefix=0
        if($parts.Count -ne 2 -or -not [Net.IPAddress]::TryParse($parts[0],[ref]$ip) -or -not [int]::TryParse($parts[1],[ref]$prefix)){throw 'Alleen expliciete netwerk-CIDRs zijn toegestaan.'}
        $bits=$ip.GetAddressBytes().Length*8
        if($prefix -lt 1 -or $prefix -gt $bits -or [Net.IPAddress]::IsLoopback($ip)){throw 'Een Any- of loopbackbereik is geen demonetwerk.'}
        $bytes=$ip.GetAddressBytes()
        for($bit=$prefix;$bit -lt $bits;$bit++){if(($bytes[[int][Math]::Floor($bit/8)] -band (1 -shl (7-($bit%8)))) -ne 0){throw 'Gebruik het netwerkadres, geen hostadres, in de CIDR.'}}
    }
}
function Public-Origin([string]$Value){
    $uri=$null
    if(-not [uri]::TryCreate($Value,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $Value -ne $uri.GetLeftPart([UriPartial]::Authority)){throw 'Een exacte ingerichte HTTPS-origin is vereist.'}
    if($uri.IsLoopback -or $uri.Host.EndsWith('.localhost')){throw 'Een localhost-portal heeft geen inkomende demonetwerkregel nodig.'}
    return $uri
}
function Assert-Administrator {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Voer deze concrete beheeractie zelf uit in een verhoogde PowerShell. De demo blijft standaardgebruiker.'}
}
function Listener([int]$Port){
    $rows=@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop|Where-Object {-not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress))})
    if(-not $rows.Count){throw "Geen vereiste netwerklistener op TCP $Port aangetoond; geen regel toegevoegd."}
    return $rows
}
if($Action -eq 'Audit'){
    # Permission failures stop the audit. No alternative provider or elevation workaround.
    Get-NetConnectionProfile|Select-Object InterfaceAlias,NetworkCategory,IPv4Connectivity
    Get-NetTCPConnection -State Listen|Where-Object LocalPort -in @(443,7243)|Select-Object LocalAddress,LocalPort,OwningProcess
    Get-NetFirewallProfile -PolicyStore ActiveStore|Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction
    Get-NetFirewallRule -PolicyStore ActiveStore|Where-Object {$_.Group -eq $group -or $_.Enabled -eq 'True'}|ForEach-Object {
        $rule=$_;$port=$rule|Get-NetFirewallPortFilter
        if($rule.Group -eq $group -or $port.LocalPort -contains 'Any' -or @($port.LocalPort|Where-Object {$_ -in @('443','7243')}).Count){
            [pscustomobject]@{Name=$rule.Name;Group=$rule.Group;Direction=$rule.Direction;Action=$rule.Action;Profile=$rule.Profile;Protocol=$port.Protocol;LocalPort=$port.LocalPort;RemotePort=$port.RemotePort;LocalAddress=($rule|Get-NetFirewallAddressFilter).LocalAddress;RemoteAddress=($rule|Get-NetFirewallAddressFilter).RemoteAddress;Program=($rule|Get-NetFirewallApplicationFilter).Program}
        }
    }
    return
}
if($Action -eq 'Remove'){
    Assert-Administrator
    $owned=@(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue)
    foreach($rule in $owned){
        if($rule.Description -ne $marker -or $rule.Name -notlike 'LummaSimulator-v07-*'){throw 'Afwijkende eigenaarmarkering; niets verwijderd.'}
    }
    foreach($rule in $owned){
        if($rule.Description -ne $marker -or $rule.Name -notlike 'LummaSimulator-v07-*'){throw 'Afwijkende eigenaarmarkering; niets verwijderd.'}
        if($PSCmdlet.ShouldProcess($rule.Name,'Verwijder uitsluitend de projectfirewallregel')){$rule|Remove-NetFirewallRule}
    }
    return
}
if(-not $NetworkProfile){throw 'Geef expliciet Private en/of Domain op; Public wordt niet automatisch toegestaan.'}
if(-not $LocalAddress){throw 'Geef expliciete lokale luister-IP-adressen op; geen Any-adres.'}
foreach($address in $LocalAddress){$ip=$null;if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or [Net.IPAddress]::IsLoopback($ip) -or $ip.Equals([Net.IPAddress]::Any) -or $ip.Equals([Net.IPAddress]::IPv6Any)){throw 'Alleen concrete niet-loopback luister-IP-adressen zijn toegestaan.'}}
$plans=@()
if($HostRole -in @('Portal','Combined')){
    Assert-Subnet $DemoSubnet;$portal=Public-Origin $PortalOrigin
    if($portal.Port -notin @(443,7243)){throw 'Deze inrichting ondersteunt uitsluitend portal TCP 443 of 7243.'}
    if(-not [IO.Path]::IsPathRooted($PortalProgram) -or [IO.Path]::GetFileName($PortalProgram) -ne 'LummaSimulator.Web.exe'){throw 'Geef het absolute pad van de eigen LummaSimulator.Web.exe op.'}
    $program=[IO.Path]::GetFullPath($PortalProgram)
    $plans+= [pscustomobject]@{Name="LummaSimulator-v07-Portal-$($portal.Port)";Port=$portal.Port;Remote=$DemoSubnet;Program=$program;Receiver=$false}
}
if($HostRole -in @('AppPc','Combined')){
    if(-not $HelperReceiverSettingsPath){throw 'AppPc vereist een expliciet helperontvanger-instellingenpad.'}
    if(-not [IO.Path]::IsPathRooted($HelperReceiverSettingsPath)){throw 'Een expliciet absoluut helperinstellingenpad is vereist.'}
    Assert-Subnet $HelperSubnet
    $receiver=Get-Content -LiteralPath $HelperReceiverSettingsPath -Raw|ConvertFrom-Json
    if(-not $receiver.Enabled){throw 'De helperontvanger staat uit; geen inkomende 443-regel nodig.'}
    $receiverOrigin=Public-Origin $receiver.Origin
    if($receiverOrigin.Port -ne 443){throw 'De bestaande helperontvanger vereist HTTPS TCP 443.'}
    $plans+=[pscustomobject]@{Name='LummaSimulator-v07-Receiver-443';Port=443;Remote=$HelperSubnet;Program='System';Receiver=$true}
}
if($Action -eq 'Plan'){
    foreach($plan in $plans){[pscustomobject]@{Name=$plan.Name;Direction='Inbound';Protocol='TCP';LocalPort=$plan.Port;LocalAddress=$LocalAddress;RemoteAddress=$plan.Remote;Profile=$NetworkProfile;Program=$plan.Program;ListenerVerified=$false}}
    return
}
Assert-Administrator
foreach($address in $LocalAddress){
    $interfaces=@(Get-NetIPAddress -IPAddress $address -ErrorAction Stop)
    if(-not $interfaces.Count){throw 'Luister-IP bestaat niet op deze host.'}
    foreach($interface in $interfaces){
        $category=(Get-NetConnectionProfile -InterfaceIndex $interface.InterfaceIndex -ErrorAction Stop).NetworkCategory
        $effective=if($category -eq 'DomainAuthenticated'){'Domain'}else{[string]$category}
        if($effective -notin $NetworkProfile){throw 'Het effectieve netwerkprofiel past niet bij het expliciet opgegeven profiel. Geen regel gewijzigd.'}
    }
}
# Validate every listener/owned rule before mutating any rule.
foreach($plan in $plans){
    $listeners=@(Listener $plan.Port)
    $listeners=@($listeners|Where-Object {$_.LocalAddress -in $LocalAddress -or $_.LocalAddress -in @('0.0.0.0','::')})
    if(-not $listeners.Count){throw 'Geen listener voor de opgegeven lokale adressen.'}
    if($plan.Receiver){
        if(-not @($listeners|Where-Object OwningProcess -eq 4).Count){throw 'HTTP.sys-netwerklistener niet aangetoond; geen 443-regel toegevoegd.'}
        $state=& netsh.exe http show servicestate view=requestq
        if($LASTEXITCODE -ne 0){throw 'Actieve HTTP.sys-registratie kon niet worden gecontroleerd.'}
        $registered=($state -join "`n").ToUpperInvariant()
        $prefix=$receiverOrigin.GetLeftPart([UriPartial]::Authority).ToUpperInvariant()
        if(-not $registered.Contains($prefix+'/LUMMA-HELPER/') -and -not $registered.Contains($prefix+':443/LUMMA-HELPER/')){throw 'Geen actieve helper-URL-registratie aangetoond; geen regel toegevoegd.'}
    }else{
        if(-not (Test-Path -LiteralPath $program -PathType Leaf)){throw 'Portalprogramma bestaat niet.'}
        $owned=@($listeners|Where-Object {(Get-Process -Id $_.OwningProcess -ErrorAction Stop).Path -eq $program})
        if(-not $owned.Count){throw 'De netwerklistener hoort niet bij het opgegeven portalprogramma.'}
    }
    $existing=Get-NetFirewallRule -Name $plan.Name -ErrorAction SilentlyContinue
    if($existing -and ($existing.Group -ne $group -or $existing.Description -ne $marker)){throw 'Bestaande gelijknamige regel is niet van dit project.'}
}
foreach($plan in $plans){
    if(-not $PSCmdlet.ShouldProcess($plan.Name,'Installeer/werk de begrensde projectregel bij')){continue}
    $existing=Get-NetFirewallRule -Name $plan.Name -ErrorAction SilentlyContinue
    if($existing){
        $existing|Set-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -Profile $NetworkProfile -EdgeTraversalPolicy Block
        $existing|Get-NetFirewallPortFilter|Set-NetFirewallPortFilter -Protocol TCP -LocalPort $plan.Port
        $existing|Get-NetFirewallAddressFilter|Set-NetFirewallAddressFilter -LocalAddress $LocalAddress -RemoteAddress $plan.Remote
        if($plan.Program){$existing|Get-NetFirewallApplicationFilter|Set-NetFirewallApplicationFilter -Program $plan.Program}
    }else{
        $ruleArguments=@{Name=$plan.Name;DisplayName=$plan.Name;Group=$group;Description=$marker;Direction='Inbound';Action='Allow';Protocol='TCP';LocalPort=$plan.Port;LocalAddress=$LocalAddress;RemoteAddress=$plan.Remote;Profile=$NetworkProfile;Enabled='True';EdgeTraversalPolicy='Block'}
        if($plan.Program){$ruleArguments.Program=$plan.Program}
        New-NetFirewallRule @ruleArguments|Out-Null
    }
}
