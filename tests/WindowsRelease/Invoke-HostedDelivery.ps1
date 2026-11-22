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
        & (Join-Path $PSScriptRoot 'Test-GuestDelivery.ps1') -TestComputerName $env:COMPUTERNAME -Product Candidate -Executable $exe -ExpectedSHA256 $hash -OutputDirectory $out -Alternate:($backend -eq 'alternate') -CheckForcedExit
        $receipt = Get-Content (Join-Path $out 'delivery.json') -Raw | ConvertFrom-Json
        if (!$receipt.Passed -or $receipt.SHA256 -ne $hash -or $receipt.LeftDownAfterKill -ne $false) { throw "Invalid $backend delivery receipt" }
        Move-Item -LiteralPath $profileDir -Destination (Join-Path $env:RUNNER_TEMP "hoverclick-profile-$backend")
    }
} catch {
    ($_|Out-String) + $_.ScriptStackTrace | Set-Content (Join-Path $receipts 'hosted-error.txt')
    throw
}
