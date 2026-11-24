# Read-only measurement of explicitly selected, freshly built package output.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ZipPath,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd('\','/')
$zip=Get-Item -LiteralPath $ZipPath
if(Test-Path -LiteralPath $ReportPath) { throw 'Rapport bestaat al; kies een nieuw pad.' }
$files=@(Get-ChildItem -LiteralPath $root -File -Recurse)
$components=@($files | Group-Object {
    $relative=$_.FullName.Substring($root.Length+1)
    if($relative -match '^Simulator[\\/]LocalTestServer[\\/]') { 'Simulator/LocalTestServer' }
    elseif($relative -match '^([^\\/]+)[\\/]') { $Matches[1] }
    else { 'Package metadata' }
} | ForEach-Object {
    $bytes=($_.Group | Measure-Object Length -Sum).Sum
    [pscustomobject]@{Component=$_.Name;Files=$_.Count;Bytes=$bytes;MB=[math]::Round($bytes/1000000,3)}
})
$duplicates=@($files | Group-Object Length | Where-Object Count -gt 1 | ForEach-Object {
    $_.Group | Group-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } |
        Where-Object Count -gt 1 | ForEach-Object {
            [pscustomobject]@{Copies=$_.Count;BytesPerCopy=$_.Group[0].Length;Paths=@($_.Group | ForEach-Object {$_.FullName.Substring($root.Length+1)})}
        }
})
[pscustomobject]@{
    Zip=$zip.FullName;ZipBytes=$zip.Length;ZipMB=[math]::Round($zip.Length/1000000,3)
    UnpackedBytes=($files | Measure-Object Length -Sum).Sum
    Components=$components
    RuntimeCopies=@($files | Where-Object Name -in @('coreclr.dll','hostfxr.dll','System.Private.CoreLib.dll') | ForEach-Object {$_.FullName.Substring($root.Length+1)})
    ReviewCandidates=@($files | Where-Object {$_.Extension -in @('.pdb','.dbg') -or $_.FullName.Substring($root.Length+1) -match '(^|[\\/])(tests|testresults|obj|linux[^\\/]*|osx[^\\/]*|win-(x86|arm64))([\\/]|$)'} | ForEach-Object {$_.FullName.Substring($root.Length+1)})
    IdenticalFiles=$duplicates
    Note='Identieke bestanden zijn meetgegevens, geen bewijs dat ze veilig kunnen worden verwijderd. MB = 1.000.000 bytes.'
} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
