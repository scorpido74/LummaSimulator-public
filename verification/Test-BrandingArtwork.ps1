# Asset decoding only. No application, process management, database or certificate changes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$repo=Split-Path $PSScriptRoot
$stream=[IO.File]::OpenRead((Join-Path $repo 'LummaSimulator.App/Assets/LummaSimulator.ico'))
try {
    $decoder=[Windows.Media.Imaging.IconBitmapDecoder]::new($stream,[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $sizes=@($decoder.Frames | ForEach-Object {$_.PixelWidth})
    if(($sizes -join ',') -ne '16,32,48,256') {throw 'Unexpected decoded ICO resolutions.'}
} finally {$stream.Dispose()}
$dictionary=[Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $repo 'LummaSimulator.App/Assets/Branding.xaml')))
if($dictionary['InfinigateLogo'] -isnot [Windows.Media.DrawingImage] -or $dictionary['InfinigateLogo'].Width -le 0) {throw 'Default vector logo cannot be decoded.'}
Write-Output 'PASS: Windows decodes all four ICO frames and the local Infinigate DrawingImage. No app/taskbar test performed.'
