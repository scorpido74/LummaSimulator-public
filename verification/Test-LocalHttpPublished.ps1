param([string]$PublishDirectory = (Join-Path $PSScriptRoot 'bin/local-http-publish'))
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not auto-load this .NET Framework assembly.
Add-Type -AssemblyName System.Net.Http
function New-PublishedServerStartInfo([string]$PublishDirectory, [ValidateRange(1,65535)][int]$Port) {
    $serverDirectory = Join-Path ([System.IO.Path]::GetFullPath($PublishDirectory)) 'LocalTestServer'
    $start = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $serverDirectory 'LummaSimulator.LocalTestServer.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $serverDirectory
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Only a validated integer and a fixed switch enter the argument string.
    # Executable/working-directory paths remain separate properties, including spaces.
    $start.Arguments = $Port.ToString([Globalization.CultureInfo]::InvariantCulture) + ' --managed'
    return $start
}
$probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$start = New-PublishedServerStartInfo $PublishDirectory $port
$child = [System.Diagnostics.Process]::Start($start)
$http = $null
try {
    $child.BeginOutputReadLine()
    $child.BeginErrorReadLine()
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $http = [System.Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromSeconds(2)
    $identity = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($child.HasExited) { throw 'Published receiver exited before readiness.' }
        $nonce = [Guid]::NewGuid().ToString('N')
        try {
            $reply = $http.GetStringAsync("http://127.0.0.1:$port/lumma-health?nonce=$nonce").GetAwaiter().GetResult()
            $identity = $reply | ConvertFrom-Json
            break
        } catch { Start-Sleep -Milliseconds 100 }
    }
    if ($null -eq $identity -or $identity.Product -ne 'LummaSimulator.LocalTestServer' -or $identity.Protocol -ne 1 -or $identity.Nonce -ne $nonce -or $identity.Pid -ne $child.Id -or $identity.Port -ne $port) { throw 'Published receiver identity mismatch.' }
    'PASS: Published Windows apphost starts without dotnet run and responds with the expected identity.'
    $runId = [Guid]::NewGuid().ToString('D')
    $body = @{ DemoRunId=$runId; Browser='Fictional Browser'; Profile='Demo profile'; Url='https://example.invalid/demo'; Username='demo@example.invalid'; Password='FAKE-PASSWORD-ONLY'; Cookie='FAKE-COOKIE-ONLY'; Token='FAKE-TOKEN-ONLY' } | ConvertTo-Json -Compress
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "http://127.0.0.1:$port/lumma-demo")
    $request.Headers.Add('X-Lumma-DemoRunId', $runId)
    $request.Content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, 'application/json')
    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 200 -or ($response.Headers.GetValues('X-Lumma-TestServer') | Select-Object -First 1) -ne "LummaSimulator.LocalTestServer/1/$($identity.Instance)") { throw 'Published synthetic POST failed or changed identity.' }
        'PASS: Published receiver accepts a correlated synthetic POST.'
    } finally { $response.Dispose(); $request.Dispose() }
    $invalid = [System.Net.Http.StringContent]::new('{}', [System.Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $http.PostAsync("http://127.0.0.1:$port/lumma-demo", $invalid).GetAwaiter().GetResult()
        try { if ([int]$response.StatusCode -ne 400) { throw 'Invalid synthetic payload was accepted.' } } finally { $response.Dispose() }
        'PASS: Published receiver refuses an unrecognized payload.'
    } finally { $invalid.Dispose() }
    $child.StandardInput.WriteLine('stop')
    $child.StandardInput.Close()
    if (-not $child.WaitForExit(5000) -or $child.ExitCode -ne 0) { throw 'Published receiver did not shut down cleanly.' }
    'PASS: Published receiver exits cleanly; no child process remains.'
} finally {
    # Only this exact retained child handle is eligible for test cleanup.
    if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $child.Dispose()
    if ($null -ne $http) { $http.Dispose() }
}
