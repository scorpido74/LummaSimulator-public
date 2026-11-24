$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$qr=Get-Content (Join-Path $repo 'shared/ParticipantQr.cs') -Raw
$settings=Get-Content (Join-Path $repo 'LummaSimulator.Web/Configuration/WebAuthnSettings.cs') -Raw
$settings=$settings.Replace('using Fido2NetLib;','').Replace('namespace LummaSimulator.Web.Configuration;','namespace LummaSimulator.Web.Configuration {')+"`n}"
# POCO stub for the external return type; actual validation/link methods and QR encoder compile unchanged.
$stub='namespace Fido2NetLib { public sealed class Fido2Configuration {public string RPID {get;set;}="";public string RPName {get;set;}="";public System.Collections.Generic.HashSet<string> Origins {get;set;}=new();public int ChallengeSize {get;set;}public int Timeout {get;set;}public bool AllowCrossOriginRequests {get;set;}}}'
Add-Type -TypeDefinition ("#nullable enable`nusing Fido2NetLib;`n"+$qr+$settings+$stub)
$settings=[LummaSimulator.Web.Configuration.WebAuthnSettings]::new()
$settings.RpId='cybertruck.example.test';$settings.Origins=@('https://cybertruck.example.test');$settings.VisitorBaseUrl='https://cybertruck.example.test/portal'
$settings.Validate($false,'cybertruck.example.test')
$results=foreach($id in @('00000000-0000-0000-0000-000000000401','00000000-0000-0000-0000-000000000402')){
    $text=$settings.VisitorLink($id,'/management-only','prize').AbsoluteUri
    $encoded=[LummaSimulator.Shared.ParticipantQr]::Encode($text)
    $rows=for($y=0;$y -lt $encoded.Size;$y++){$row=[Text.StringBuilder]::new();for($x=0;$x -lt $encoded.Size;$x++){$null=$row.Append($(if($encoded.GetModule($x,$y)){'1'}else{'0'}))};$row.ToString()}
    @{Text=$text;Version=$encoded.Version;Rows=@($rows);Svg=$encoded.ToSvg()}
}
$rejected=0
foreach($value in @('http://cybertruck.example.test','https://other.example.test','https://192.0.2.1','https://cybertruck.example.test/portal?q=1','https://cybertruck.example.test/#x','https://user@cybertruck.example.test')){
    $settings.VisitorBaseUrl=$value;try{$settings.Validate($false,'cybertruck.example.test')}catch{$rejected++}
}
$settings.VisitorBaseUrl=$null
$fallback=$settings.VisitorLink('00000000-0000-0000-0000-000000000401','/existing-prefix','prize').AbsoluteUri
$local=[LummaSimulator.Web.Configuration.WebAuthnSettings]::new();$local.RpId='localhost';$local.Origins=@('https://localhost:7243');$local.Validate($true)
$localUrl=$local.VisitorLink('00000000-0000-0000-0000-000000000401').AbsoluteUri
@{Samples=@($results);Rejected=$rejected;Fallback=$fallback;Local=$localUrl;LocalOnly=[LummaSimulator.Shared.ParticipantLink]::LaptopOnly([uri]$localUrl)}|ConvertTo-Json -Depth 5 -Compress
