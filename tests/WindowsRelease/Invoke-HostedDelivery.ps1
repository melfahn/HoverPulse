[CmdletBinding()]
param([switch]$OnTestDesktop)
$ErrorActionPreference = 'Stop'
if ($env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Expected a disposable hosted runner' }
$workspace = (Get-Item -LiteralPath $env:GITHUB_WORKSPACE).FullName
$receipts = Join-Path $workspace 'artifacts\receipts'
if (!$OnTestDesktop) {
    Add-Type -Path (Join-Path $PSScriptRoot 'HostedDesktop.cs')
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + $PSCommandPath + '" -OnTestDesktop'
    $code = [HostedDesktop]::Run("$PSHOME\powershell.exe", $arguments, $workspace)
    if ($code) {
        $failure = Join-Path $receipts 'hosted-error.txt'
        if (Test-Path $failure) { throw (Get-Content $failure -Raw) }
        throw "Hosted receiver exited with code $code"
    }
    return
}
try {
    $exe = (Get-Item -LiteralPath (Join-Path $workspace 'artifacts\publish\HoverPulse.exe')).FullName
    $build = Get-Content (Join-Path $receipts 'build.json') -Raw | ConvertFrom-Json
    $hash = ($build.Files | Where-Object Name -eq HoverPulse.exe).SHA256
    $profileDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) HoverPulse
    foreach ($backend in 'legacy','alternate') {
        $out = Join-Path $receipts $backend
        & (Join-Path $PSScriptRoot 'Test-GuestDelivery.ps1') -TestComputerName $env:COMPUTERNAME -Product Candidate -Executable $exe -ExpectedSHA256 $hash -OutputDirectory $out -Alternate:($backend -eq 'alternate') -TraceDrag -CheckForcedExit
        $receipt = Get-Content (Join-Path $out 'delivery.json') -Raw | ConvertFrom-Json
        if (!$receipt.Passed -or $receipt.SHA256 -ne $hash -or $receipt.LeftDownAfterKill -ne $false) { throw "Invalid $backend delivery receipt" }
        Move-Item -LiteralPath $profileDir -Destination (Join-Path $env:RUNNER_TEMP "hoverclick-profile-$backend")
        $out = Join-Path $receipts "$backend-orderly"
        & (Join-Path $PSScriptRoot 'Test-GuestDelivery.ps1') -TestComputerName $env:COMPUTERNAME -Product Candidate -Executable $exe -ExpectedSHA256 $hash -OutputDirectory $out -Alternate:($backend -eq 'alternate') -TraceDrag -CheckOrderlyExit -DragHoldMilliseconds 7000
        $receipt = Get-Content (Join-Path $out 'delivery.json') -Raw | ConvertFrom-Json
        if (!$receipt.Passed -or $receipt.SHA256 -ne $hash -or !$receipt.CheckedOrderlyExit) { throw "Invalid $backend orderly-exit receipt" }
        Move-Item -LiteralPath $profileDir -Destination (Join-Path $env:RUNNER_TEMP "hoverclick-profile-$backend-orderly")
    }
    # The captured profile needs several 100 ms samples to drop. A 100 ms deadline
    # deliberately fails before the native left-up and exercises recovery on this executable.
    $out = Join-Path $receipts 'short-deadline'
    $failure = $null
    try {
        & (Join-Path $PSScriptRoot 'Test-GuestDelivery.ps1') -TestComputerName $env:COMPUTERNAME -Product Candidate -Executable $exe -ExpectedSHA256 $hash -OutputDirectory $out -SoakHours 0.009 -DropDeadlineMilliseconds 100 -TraceDrag
    } catch { $failure = $_ }
    if (!$failure) { throw 'The intentionally short deadline should fail qualification.' }
    $receipt = Get-Content (Join-Path $out 'delivery.json') -Raw | ConvertFrom-Json
    $late = Get-Content (Join-Path $out 'failure-00001/late-observation.json') -Raw | ConvertFrom-Json
    $recovery = Get-Content (Join-Path $out 'failure-00001/recovery.json') -Raw | ConvertFrom-Json
    if ($receipt.SHA256 -ne $hash -or $receipt.Passed -or !$receipt.SoakCompleted -or $receipt.Attempts -lt 2 -or
        $receipt.FailedCycles -ne $receipt.Attempts -or $receipt.CandidateRestarts -lt 1 -or
        ($late.Edges -join ',') -ne 'LU' -or $late.LeftDown -or !$recovery.Safe -or $recovery.FixtureReleaseAttempted -or
        !(Test-Path -LiteralPath (Join-Path $out 'failure-00001/toolbar.png'))) {
        throw "The short-deadline run did not record a late native release and safely continue. $($failure.Exception.Message)"
    }
} catch {
    ($_|Out-String) + $_.ScriptStackTrace | Set-Content (Join-Path $receipts 'hosted-error.txt')
    throw
}
