# Reproducible neutral artwork, no external assets or network.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$destination=Join-Path (Split-Path $PSScriptRoot) 'LummaSimulator.App/Assets/LummaSimulator.ico'
$null=New-Item -ItemType Directory -Force -Path (Split-Path $destination)
$frames=@()
foreach($size in @(16,32,48,256)) {
    $canvas=[Drawing.Bitmap]::new(1024,1024);$g=[Drawing.Graphics]::FromImage($canvas);$g.SmoothingMode='AntiAlias'
    $navy=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#14263D'))
    $mint=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#53DDC5'),60)
    $white=[Drawing.Pen]::new([Drawing.Color]::White,76);$mint.LineJoin='Round';$white.LineJoin='Round'
    try {
        $g.Clear([Drawing.Color]::Transparent);$g.FillEllipse($navy,12,12,1000,1000)
        $g.DrawLines($mint,[Drawing.Point[]]@([Drawing.Point]::new(512,175),[Drawing.Point]::new(775,280),[Drawing.Point]::new(740,620),[Drawing.Point]::new(512,838),[Drawing.Point]::new(284,620),[Drawing.Point]::new(249,280),[Drawing.Point]::new(512,175)))
        $g.DrawLines($white,[Drawing.Point[]]@([Drawing.Point]::new(420,350),[Drawing.Point]::new(420,600),[Drawing.Point]::new(624,600)))
        $g.FillEllipse([Drawing.Brushes]::White,577,324,76,76)
        $bitmap=[Drawing.Bitmap]::new($size,$size);$scaled=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $scaled.InterpolationMode='HighQualityBicubic';$scaled.DrawImage($canvas,0,0,$size,$size)
            $stream=[IO.MemoryStream]::new()
            try {$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$frames+=,[byte[]]$stream.ToArray()} finally {$stream.Dispose()}
        } finally {$scaled.Dispose();$bitmap.Dispose()}
    } finally {$g.Dispose();$canvas.Dispose();$navy.Dispose();$mint.Dispose();$white.Dispose()}
}
$writer=[IO.BinaryWriter]::new([IO.File]::Create($destination))
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count);$offset=6+16*$frames.Count
    for($i=0;$i -lt $frames.Count;$i++) {
        $size=@(16,32,48,256)[$i];$dimension=if($size -eq 256){0}else{$size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset);$offset+=$frames[$i].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose()}
Write-Host 'Neutral LummaSimulator.ico generated (16,32,48,256).'
