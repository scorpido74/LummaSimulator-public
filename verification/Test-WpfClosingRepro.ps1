# Independent WPF framework reproduction, not a compiled LummaSimulator acceptance test.
# Run under Windows PowerShell -STA. No windows shown, apps/data/certificates touched or endpoints used.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,WindowsBase
if(-not [Threading.Tasks.Task]::CompletedTask.GetAwaiter().IsCompleted) { throw 'Expected synchronous task completion.' }
$state=@{ ReentrantRejected=$false; Events=0 }
$window=[Windows.Window]::new()
$handler=[ComponentModel.CancelEventHandler]{
    param($sender,$eventArgs)
    $state.Events++
    $eventArgs.Cancel=$true
    try { $sender.Close() }
    catch {
        $errorType=$_.Exception.GetBaseException()
        if($errorType -isnot [InvalidOperationException]) { throw }
        $state.ReentrantRejected=$true
    }
}
$window.add_Closing($handler)
try {
    $window.Close()
    if(-not $state.ReentrantRejected -or $state.Events -ne 1) { throw 'Expected nested Close rejection in one native Closing event.' }
} finally {
    $window.remove_Closing($handler)
    $window.Close()
}
Write-Output 'PASS: completed-task await does not yield; WPF rejects nested Close inside Closing. Independent .NET Framework reproduction only; no project build or production shutdown test.'
