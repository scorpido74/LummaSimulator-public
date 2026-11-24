$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$qrSource=Get-Content -LiteralPath (Join-Path $repo 'shared/ParticipantQr.cs') -Raw
$clientSource=Get-Content -LiteralPath (Join-Path $repo 'LummaSimulator.App/ParticipantSessionClient.cs') -Raw
$protocolSource=Get-Content -LiteralPath (Join-Path $repo 'LummaSimulator.Helper.Core/HelperProtocol.cs') -Raw
$eventRecord=[regex]::Match($clientSource,'public sealed record RoundEvent\([^;]+;').Value
$stateRecord=[regex]::Match($protocolSource,'public sealed record ReceiverQrState\([^;]+;').Value
$policy=Get-Content -LiteralPath (Join-Path $repo 'LummaSimulator.Helper.Core/HelperQrDisplay.cs') -Raw
$policy=[regex]::Replace($policy,'(?m)^using [^;]+;\r?$','').Replace('namespace LummaSimulator.Helper;','namespace LummaSimulator.Helper {')+"`n}"
# Compile the actual pure source and actual protocol DTO declarations in memory; no projects, packages, listeners or runtime data.
Add-Type -TypeDefinition ("#nullable enable`nusing System.IO;using LummaSimulator.App.Services;using LummaSimulator.Shared;`n"+$qrSource+"`nnamespace LummaSimulator.App.Services {"+$eventRecord+"}`nnamespace LummaSimulator.Helper {"+$stateRecord+"}`n"+$policy)
$samples=@()
foreach($length in @(1,18,33,54,79,107,135,155,193,231)){$samples+=('A'*$length)}
$id='00000000-0000-0000-0000-000000000401'
foreach($origin in @('https://localhost:7243','https://portal.example.test')){$samples+=[LummaSimulator.Shared.ParticipantLink]::ForEvent($origin,$id).AbsoluteUri}
$samples+=[LummaSimulator.Shared.ParticipantLink]::ForEvent('https://cybertruck.example.test',$id,'/portal','experience').AbsoluteUri
$samples+=[LummaSimulator.Shared.ParticipantLink]::ForEvent('https://cybertruck.example.test','00000000-0000-0000-0000-000000000402','/portal','experience').AbsoluteUri
$results=foreach($text in $samples){
    $qr=[LummaSimulator.Shared.ParticipantQr]::Encode($text)
    $rows=for($y=0;$y -lt $qr.Size;$y++){
        $row=[Text.StringBuilder]::new()
        for($x=0;$x -lt $qr.Size;$x++){$null=$row.Append($(if($qr.GetModule($x,$y)){'1'}else{'0'}))}
        $row.ToString()
    }
    @{Text=$text;Version=$qr.Version;Rows=@($rows);Svg=$qr.ToSvg()}
}
$rejected=0
foreach($bad in @('http://localhost','https://localhost/path','https://user:pass@localhost','https://localhost?x=1')){
    try{$null=[LummaSimulator.Shared.ParticipantLink]::ForEvent($bad,$id)}catch{$rejected++}
}
$other='00000000-0000-0000-0000-000000000402';$origin='https://portal.example.test'
$allowed=[LummaSimulator.App.Services.RoundEvent[]]@([LummaSimulator.App.Services.RoundEvent]::new($id,'Synthetic A'),[LummaSimulator.App.Services.RoundEvent]::new($other,'Synthetic B'))
$display=[LummaSimulator.Helper.HelperQrDisplay]::new()
$first=[LummaSimulator.Helper.ReceiverQrState]::new('Ready',$origin,$id,'Untrusted',[LummaSimulator.Shared.ParticipantLink]::EventPath($id))
$second=[LummaSimulator.Helper.ReceiverQrState]::new('Ready',$origin,$other,'Untrusted',[LummaSimulator.Shared.ParticipantLink]::EventPath($other))
$initial=$display.Apply($origin,$first,$allowed) -and $display.EventName -eq 'Synthetic A'
$changed=$display.Apply($origin,$second,$allowed) -and $display.EventName -eq 'Synthetic B' -and $display.Link.AbsoluteUri.EndsWith($other)
$closed=$false;try{$null=$display.Apply($origin,$second,[LummaSimulator.App.Services.RoundEvent[]]@())}catch{$closed=$true}
$cleared=$closed -and $null -eq $display.Link -and $null -eq $display.Code
$null=$display.Apply($origin,$first,$allowed)
$disconnected=(-not $display.Apply($origin,[LummaSimulator.Helper.ReceiverQrState]::new('Unavailable',$null,$null,$null,$null),$allowed)) -and $null -eq $display.Link
@{Samples=@($results);Rejected=$rejected;LaptopOnly=[LummaSimulator.Shared.ParticipantLink]::LaptopOnly([uri]'https://localhost:7243');PublicOnly=[LummaSimulator.Shared.ParticipantLink]::LaptopOnly([uri]'https://portal.example.test');Policy=@{Initial=$initial;Changed=$changed;Closed=$cleared;Disconnected=$disconnected}}|ConvertTo-Json -Depth 6 -Compress
