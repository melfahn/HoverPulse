[CmdletBinding()]
param([Parameter(Mandatory)]$Receiver,[Parameter(Mandatory)]$App,[Parameter(Mandatory)][IntPtr]$Window,
      [Parameter(Mandatory)][string]$PeerExecutable,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$Alternate)
$ErrorActionPreference='Stop'
$receiverSid=[ReleaseReceiver]::IntegritySid([Diagnostics.Process]::GetCurrentProcess())
$appSid=[ReleaseReceiver]::IntegritySid($App)
if ($receiverSid -ne 'S-1-16-8192' -or $appSid -ne 'S-1-16-4096') { throw "Unexpected receiver or candidate integrity: $receiverSid, $appSid." }
$peer=$null
try {
    [ReleaseReceiver]::Foreground($Receiver.Handle)
    $Receiver.Hold($Receiver.Handle,30,1300)
    $blockedEdges=$Receiver.Edges -join ','
    $controls=[ReleaseReceiver]::VisibleLabels($Window)
    if ($blockedEdges -or [ReleaseReceiver]::LeftDown()) { throw "Protected receiver saw unexpected input [$blockedEdges]." }
    if ($controls -contains 'Resume') {
        $resume=[ReleaseReceiver]::Button($Window,'Resume',0,$false)
        $Receiver.Hold($resume,4,1100)
    }
    $null=[ReleaseReceiver]::Button($Window,'Pause',0,$false)
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=(Get-Item -LiteralPath $PeerExecutable).FullName
    $start.UseShellExecute=$false
    $peer=[Diagnostics.Process]::Start($start)
    $peerSid=[ReleaseReceiver]::IntegritySid($peer)
    if ($peerSid -ne $appSid) { throw 'Positive-control receiver must run at candidate integrity.' }
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do {
        [ReleaseReceiver]::Pump(40)
        $peer.Refresh()
        if ($peer.HasExited -or $watch.Elapsed.TotalSeconds -gt 12) { throw 'Peer receiver failed to open.' }
    } until ($peer.MainWindowHandle -ne 0)
    $peerWindow=$peer.MainWindowHandle
    [ReleaseReceiver]::Foreground($peerWindow)
    $Receiver.Hold($peerWindow,0,1300)
    $peerCaption=[ReleaseReceiver]::ReadText($peerWindow)
    if ($peerCaption -ne 'Hover Click peer LD,LU' -or $Receiver.Edges.Count -or [ReleaseReceiver]::LeftDown()) {
        throw "Equal-integrity recovery differed: $peerCaption."
    }
    [pscustomobject]@{HigherTarget=$receiverSid;Candidate=$appSid;Peer=$peerSid;BlockedEdges=$blockedEdges;
        ControlsAfterBlock=$controls;PeerEdges='LD,LU';BackendAlternate=$Alternate.IsPresent} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'input-boundary.json')
} finally {
    if ($peer -and !$peer.HasExited) { $null=$peer.CloseMainWindow(); if (!$peer.WaitForExit(2500)) { $peer.Kill(); $peer.WaitForExit() } }
    if ($peer) { $peer.Dispose() }
}
