# Source/build-only route shared by the normal launcher and isolated verification.
function New-DevSourceCopy([string]$Repo) {
    $null=Get-Command dotnet -ErrorAction Stop;$null=Get-Command git -ErrorAction Stop
    $sdks=& dotnet --list-sdks
    if($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')){throw '.NET 10 SDK ontbreekt.'}
    $work=Join-Path $Repo ('artifacts/dev-start-'+[guid]::NewGuid().ToString('N'))
    $source=Join-Path $work 'source';$null=New-Item -ItemType Directory -Path $source
    Write-Host "Buildmap: $work"
    $files=& git -C $Repo ls-files --cached --others --exclude-standard
    if($LASTEXITCODE -ne 0){throw 'Git-bronselectie mislukt.'}
    foreach($file in $files){
        if($file -notmatch '^(LummaSimulator\.(App|Web|Helper\.Core|HelperUpdater|PresenterHelper)/|shared/|tools/|tests/|verification/|LummaSimulator\.slnx$|Directory\.[^/]+$|NuGet\.Config$|global\.json$)'){continue}
        if($file -match '(^|/)(bin|obj|artifacts|node_modules|keys)/|\.(db|sqlite|sqlite3|pfx|p12|pem|key)(\.|-|$)|event-edition\.manifest|monitor-integration\.state|monitor-credential\.|\.setup-verification|appsettings\..*Local\.json|(^|/)(secrets|credentials)\.json$|(^|/)key-.*\.xml$|(^|/)\.env($|\.)'){
            throw 'Beschermd bestand in bronselectie. Niets wordt opgeschoond.'
        }
        $inputPath=Join-Path $Repo $file
        if(-not (Test-Path -LiteralPath $inputPath -PathType Leaf)){continue} # Respect a local deletion.
        $target=Join-Path $source $file
        $null=New-Item -ItemType Directory -Force -Path (Split-Path $target)
        Copy-Item -LiteralPath $inputPath -Destination $target
    }
    [xml]$solution=Get-Content -LiteralPath (Join-Path $source 'LummaSimulator.slnx')
    foreach($project in $solution.SelectNodes('//Project')){
        if(-not (Test-Path -LiteralPath (Join-Path $source $project.Path))){throw "Solutionproject ontbreekt in de bronkopie: $($project.Path)"}
    }
    return [pscustomobject]@{Work=$work;Source=$source}
}
function Invoke-DevBuild($Copy){
    Push-Location -LiteralPath $Copy.Source
    $previousErrorPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
    try{
        & dotnet restore LummaSimulator.slnx --configfile (Join-Path $Copy.Source 'NuGet.Config') --locked-mode '-p:RuntimeIdentifiers=win-x64' 2>&1 | Tee-Object -FilePath (Join-Path $Copy.Work 'restore.log') | Out-Host
        if($LASTEXITCODE -ne 0){throw 'Locked restore mislukt; zie restore.log in de nieuwe buildmap. Geen apps gestart.'}
        # The sidecar is transitive in the solution. Prepare its Release output before the App copy target runs.
        & dotnet build tools/LummaSimulator.LocalTestServer/LummaSimulator.LocalTestServer.csproj --configuration Release --no-restore 2>&1 | Tee-Object -FilePath (Join-Path $Copy.Work 'sidecar-build.log') | Out-Host
        if($LASTEXITCODE -ne 0){throw 'Release-build van LocalTestServer mislukt; zie sidecar-build.log. Geen apps gestart.'}
        & dotnet build LummaSimulator.slnx --configuration Release --no-restore 2>&1 | Tee-Object -FilePath (Join-Path $Copy.Work 'build.log') | Out-Host
        if($LASTEXITCODE -ne 0){throw 'Build mislukt; geen apps gestart.'}
    }finally{$ErrorActionPreference=$previousErrorPreference;Pop-Location}
}
