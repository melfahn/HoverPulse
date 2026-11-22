# Run in a disposable guest or in a separately reserved physical test login.
# Start each reference/backend combination from a fresh guest profile.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('DisposableGuestName')][string]$TestComputerName,
    [string]$DedicatedTestAccount,
    [Parameter(Mandatory)][ValidateSet('Reference','Candidate')][string]$Product,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedSHA256,
    [switch]$Alternate,
    [switch]$CheckOrderlyExit,
    [switch]$CheckForcedExit,
    [switch]$CheckReconnect,
    [switch]$CheckLock,
    [switch]$CheckMonitorRemoval,
    [switch]$CheckBoundaries,
    [switch]$MeasureMotion,
    [switch]$CheckDesign,
    [string]$RestoreDesignProfile,
    [switch]$CheckMixedDpi,
    [ValidateRange(0,768)][int]$ExpectedDpi = 0,
    [ValidateRange(1200,60000)][int]$DragHoldMilliseconds = 1200,
    [ValidateRange(0,48)][double]$SoakHours = 0
)
$ErrorActionPreference = 'Stop'
$computer = Get-CimInstance Win32_ComputerSystem
if ($computer.Name -ne $TestComputerName) { throw 'Wrong test computer.' }
$hostFile = Join-Path $PSScriptRoot 'TestHosts.local.psd1'
$hostRules = if (Test-Path -LiteralPath $hostFile) { Import-PowerShellDataFile -LiteralPath $hostFile } else { @{} }
if ($computer.Name -in $hostRules.ProtectedHosts) { throw 'This computer is protected from release testing.' }
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($session -eq 0 -or ![Environment]::UserInteractive) { throw 'An interactive test login is required.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($DedicatedTestAccount) {
    if ($computer.Name -notin $hostRules.PhysicalTestHosts -or $identity.Name -ne $DedicatedTestAccount) {
        throw 'Physical test mode requires the expected computer and the named test login.'
    }
    # A protected user may keep their original logon disconnected during the test.
    # Never take input in their logon or reuse their per-user settings.
    $protected = @(Get-CimInstance Win32_Process -Filter "Name='mstsc.exe' OR Name='DwellClicker.exe'")
    $reservedSid = if ($hostRules.TestAccountSids) { $hostRules.TestAccountSids[$computer.Name] }
    if ($reservedSid -and $identity.User.Value -ne $reservedSid) {
        throw 'Test login SID does not match the reserved account.'
    }
    foreach ($proc in $protected) {
        if ($proc.SessionId -eq $session) { throw 'The test session contains an active RDP client or clicker.' }
        # Limited users cannot resolve process owners in other logons.
        # The administrator may pin the SID of a separate, dedicated test account.
        if ($reservedSid) { continue }
        $owner = Invoke-CimMethod -InputObject $proc -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or !$owner.Sid -or $owner.Sid -eq $identity.User.Value) {
            throw 'Test login isolation could not be verified.'
        }
    }
} elseif ($computer.Model -notmatch 'Virtual Machine|VMware|VirtualBox|KVM|QEMU|HVM|Parallels') {
    throw 'Specify a dedicated physical test login or use a named disposable guest.'
}
if (Get-Process -Name DwellClicker,HoverPulse -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session) {
    throw 'Another clicker is running in the test session.'
}
$exe = Get-Item -LiteralPath $Executable
$hash = (Get-FileHash -LiteralPath $exe.FullName).Hash
if ($ExpectedSHA256 -and $hash -ne $ExpectedSHA256) { throw 'The staged executable differs from the expected build.' }
$isReference = $Product -eq 'Reference'
if ($CheckOrderlyExit -and $isReference) { throw 'The duplicate-instance exit code is a candidate-only contract.' }
if ($CheckDesign -and ($CheckReconnect -or $CheckOrderlyExit -or $CheckForcedExit)) { throw 'The presentation run ends with hands-free exit and needs its own login run.' }
if ($CheckMixedDpi -and ($isReference -or $CheckDesign)) { throw 'Mixed-DPI checks require a separate candidate run.' }
if ($RestoreDesignProfile -and ($isReference -or $CheckDesign -or $CheckMixedDpi)) { throw 'Restore a saved design in a separate candidate run.' }
if ($CheckOrderlyExit -and $CheckForcedExit) { throw 'Select one exit test per run.' }
if ($CheckLock -and !$CheckReconnect) { throw 'Lock testing requires an authenticated reconnect coordinator.' }
if ($CheckMonitorRemoval -and !$CheckReconnect) { throw 'Monitor removal requires a reconnect to a single-display client.' }
if ($isReference -and $hash -ne 'CB8D235299FFAB67C950DC6D96B00CBD269C0691B43CADE368C7735F5B95CA15') {
    throw 'Reference hash mismatch.'
}
$out = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$receipt = Join-Path $out 'delivery.json'
if (Test-Path -LiteralPath $receipt) { throw 'Use a fresh output directory for each run.' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot Receiver.cs) -ReferencedAssemblies System.Windows.Forms,System.Drawing
if ($isReference) {
    $key = 'HKCU:\Software\DwellClicker\DwellClicker\DwellClicker V2'
    if (Test-Path $key) { throw 'Use a fresh reference test profile.' }
    New-Item -Path $key -Force | Out-Null
    $profile = @{ClickTime=2;MoveLimit=5;ClickingType2KXP=[int]$Alternate.IsPresent;DoSound=0;SaveX=0;SaveY=24;DefaultAction=1;AppSize=1;FloatMode=2}
    foreach ($name in $profile.Keys) { New-ItemProperty -Path $key -Name $name -Value $profile[$name] -PropertyType DWord | Out-Null }
} else {
    $folder = Join-Path $env:APPDATA HoverPulse
    $file = Join-Path $folder 'settings.json'
    if (Test-Path $folder) { throw 'Use a fresh candidate test profile.' }
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    if ($RestoreDesignProfile) {
        Copy-Item -LiteralPath $RestoreDesignProfile -Destination $file
    } else {
        @{SchemaVersion=2;ClickTime=2;MoveLimit=5;AlternateInput=$Alternate.IsPresent;ToolbarX=0;ToolbarY=24} |
            ConvertTo-Json | Set-Content -LiteralPath $file -Encoding UTF8
    }
}
# Boundary offsets and logged target coordinates are physical pixels in every suite.
$previousDpi = [ReleaseReceiver]::EnterPerMonitorDpi()
$receiver = New-Object ReleaseReceiver
if ($CheckDesign -or $CheckMixedDpi -or $CheckMonitorRemoval) {
    $receiver.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
    $receiver.Bounds = if ($CheckMixedDpi -or $CheckMonitorRemoval) { [Windows.Forms.SystemInformation]::VirtualScreen } else { [Windows.Forms.Screen]::PrimaryScreen.WorkingArea }
}
$app = $null
$passed = $false
$heldAfterKill = $null
$cycles = 0
$registered = $false
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    if ($SoakHours -gt 0) { [ReleaseReceiver]::KeepAwake($true) }
    $receiver.Show()
    $receiver.Hold($receiver.Handle,-48,100)
    $app = Start-Process -FilePath $exe.FullName -PassThru
    $startup = [Diagnostics.Stopwatch]::StartNew()
    do {
        $app.Refresh()
        if ($app.HasExited) { throw "Toolbar exited during startup with code $($app.ExitCode)." }
        if ($startup.Elapsed.TotalSeconds -gt 30) { throw 'Timed out waiting for the visible toolbar.' }
        [ReleaseReceiver]::Pump(100)
        $window = if ($isReference) { $app.MainWindowHandle } else { [ReleaseReceiver]::CandidateToolbar($app.Id) }
    } until ($window -ne 0)
    if ($RestoreDesignProfile) {
        & (Join-Path $PSScriptRoot Test-ToolbarPresentation.ps1) -Window $window -Receiver $receiver -OutputDirectory $out -Restored
    }
    function Check-Edges([string]$Phase,[string]$Expected) {
        $actual = $receiver.Edges -join ','
        if ($actual -ne $Expected) { throw "Cycle $cycles, ${Phase}: expected [$Expected], received [$actual]." }
        $receiver.Edges.Clear()
    }
    function Select-Mode([string]$Label,[int]$Id,[int]$Offset = 0) {
        $button = [ReleaseReceiver]::Button($window,$Label,$Id,$isReference)
        $receiver.Hold($button,$Offset,1100)
        Check-Edges $Label ''
    }
    # Prime the first position. Ignore only this startup interval.
    $receiver.Hold($receiver.Handle,-48,1200)
    $receiver.Edges.Clear()
    $endurance = [Diagnostics.Stopwatch]::StartNew()
    do {
        Select-Mode 'Right' 1003
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'right click' 'RD,RU'
        $receiver.Hold($receiver.Handle,30,1200)
        Check-Edges 'restore left default' 'LD,LU'
        Select-Mode 'Double' 1001
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'double click' 'LD,LU,LD,LU'
        Select-Mode 'Drag' 1002
        $receiver.Hold($receiver.Handle,30,$DragHoldMilliseconds)
        Check-Edges 'start drag' 'LD'
        Select-Mode 'Pause' 1000
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'paused dwell during drag' ''
        Select-Mode 'Resume' 1000 4
        $receiver.Hold($receiver.Handle,30,1200)
        Check-Edges 'drop after resume' 'LU'
        $cycles++
        if ($cycles -eq 1 -or $cycles % 10 -eq 0) {
            @{Cycles=$cycles; ElapsedSeconds=$endurance.Elapsed.TotalSeconds; Utc=[DateTime]::UtcNow.ToString('o')} |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'progress.json')
            if ($SoakHours -gt 0) {
                $app.Refresh()
                [pscustomobject]@{Cycle=$cycles;Handles=$app.HandleCount;PrivateBytes=$app.PrivateMemorySize64;
                    CpuSeconds=$app.TotalProcessorTime.TotalSeconds;DelayedHolds=$receiver.DelayedHolds.Count} |
                    Export-Csv -NoTypeInformation -Append -LiteralPath (Join-Path $out 'resource-samples.csv')
            }
        }
    } while ($endurance.Elapsed.TotalHours -lt $SoakHours)
    if ($MeasureMotion) {
        $observations = foreach ($origin in 0,1,2) {
            foreach ($delta in 4,5,6,7,8) {
                foreach ($axis in 'X','Y') {
                    $receiver.Hold($receiver.Handle,40,850)
                    Check-Edges 'measurement rearm' 'LD,LU'
                    $receiver.HoldAt($receiver.Handle,$origin,$origin,850)
                    Check-Edges 'measurement baseline' 'LD,LU'
                    $before = [ReleaseReceiver]::CursorForWindow($window)
                    $dx = if ($axis -eq 'X') { $delta } else { 0 }
                    $dy = if ($axis -eq 'Y') { $delta } else { 0 }
                    $receiver.HoldAt($receiver.Handle,($origin+$dx),($origin+$dy),850)
                    $after = [ReleaseReceiver]::CursorForWindow($window)
                    $edges = $receiver.Edges -join ','
                    if ($edges -notin '','LD,LU') { throw "Unexpected movement measurement edges [$edges]." }
                    $receiver.Edges.Clear()
                    [pscustomobject]@{Origin=$origin;Axis=$axis;PhysicalDelta=$delta;ObservedX=$after.X-$before.X;
                        ObservedY=$after.Y-$before.Y;Edges=$edges;ToolbarDpi=[ReleaseReceiver]::GetDpiForWindow($window)}
                }
            }
        }
        $observations | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'motion.json')
    }
    if ($CheckBoundaries) {
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'boundary baseline' 'LD,LU'
        $origin = [ReleaseReceiver]::CursorForWindow([IntPtr]::Zero)
        $receiver.HoldReferencePoint($receiver.Handle,($origin.X+4),($origin.Y+4),1200)
        Check-Edges 'four pixels on both axes' ''
        $receiver.HoldReferencePoint($receiver.Handle,($origin.X+8),($origin.Y+8),1200)
        Check-Edges 'two four-pixel steps from the baseline' ''
        $receiver.HoldReferencePoint($receiver.Handle,($origin.X+13),($origin.Y+8),1200)
        Check-Edges 'five pixels on x' 'LD,LU'
        $receiver.HoldReferencePoint($receiver.Handle,($origin.X+13),($origin.Y+3),1200)
        Check-Edges 'minus five pixels on y' 'LD,LU'
        $receiver.HoldReferencePoint($receiver.Handle,($origin.X+13),($origin.Y+3),3000)
        Check-Edges 'stationary after dispatch' ''
        Select-Mode 'Pause' 1000
        $anchor = [ReleaseReceiver]::CursorForWindow([IntPtr]::Zero)
        $toggle = [ReleaseReceiver]::Button($window,'Resume',1000,$isReference)
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'off the paused toggle' ''
        $receiver.HoldReferencePoint($toggle,($anchor.X + 1),$anchor.Y,1200)
        Check-Edges 'one pixel on toggle' ''
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'one-pixel paused rearm stays latched' ''
        $receiver.HoldReferencePoint($toggle,($anchor.X + 3),$anchor.Y,1200)
        Check-Edges 'two further pixels on toggle' ''
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'two-pixel paused rearm resumes' 'LD,LU'
    }
    if ($CheckDesign) {
        if ($isReference) { throw 'Design checks require the Hover Click candidate.' }
        & (Join-Path $PSScriptRoot Test-ToolbarPresentation.ps1) -Window $window -Receiver $receiver -OutputDirectory $out -ExpectedDpi $ExpectedDpi
        Check-Edges 'internal design and Setup actions' ''
        if (!$app.WaitForExit(5000) -or $app.ExitCode -ne 0) { throw 'Confirmed hands-free exit failed.' }
    }
    if ($CheckMixedDpi) {
        & (Join-Path $PSScriptRoot Test-ToolbarPresentation.ps1) -Window $window -Receiver $receiver -OutputDirectory $out -MixedDpi
        Check-Edges 'moving between monitors' ''
    }
    if ($CheckReconnect) {
        if ($CheckMonitorRemoval) {
            $second = [Windows.Forms.Screen]::AllScreens | Where-Object { !$_.Primary } | Select-Object -First 1
            if (!$second) { throw 'A second Windows display is required for removal.' }
            Select-Mode 'Move' 0
            $receiver.HoldScreenPoint($receiver.Handle,($second.WorkingArea.Left+[int]($second.WorkingArea.Width/2)),($second.WorkingArea.Top+160),1600)
            if (!$second.WorkingArea.Contains([ReleaseReceiver]::ScreenBounds($window))) { throw 'Toolbar did not move onto the display to remove.' }
        }
        Select-Mode 'Pause' 1000
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'paused before RDP disconnect' ''
        if ($CheckLock) {
            $registered = [ReleaseReceiver]::WTSRegisterSessionNotification($receiver.Handle,0)
            if (!$registered -or ![ReleaseReceiver]::LockWorkStation()) { throw 'Could not lock the test login.' }
            $lockWait = [Diagnostics.Stopwatch]::StartNew()
            while (!$receiver.SessionEvents.Contains(7)) {
                if ($lockWait.Elapsed.TotalSeconds -ge 15) { throw 'Windows did not confirm session lock.' }
                [ReleaseReceiver]::Pump(50)
            }
        }
        @{ProcessId=$app.Id; SessionId=$session; Width=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Width;
          Displays=[Windows.Forms.Screen]::AllScreens.Count; Toolbar=[ReleaseReceiver]::ScreenBounds($window).ToString()} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'ready-for-reconnect.json')
        $reconnect = [Diagnostics.Stopwatch]::StartNew()
        while (!(Test-Path -LiteralPath (Join-Path $out 'reconnected'))) {
            if ($app.HasExited -or $reconnect.Elapsed.TotalMinutes -ge 4) { throw 'RDP reconnect did not complete.' }
            [ReleaseReceiver]::Pump(100)
        }
        if ($CheckMonitorRemoval) {
            $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
            if ([Windows.Forms.Screen]::AllScreens.Count -ne 1 -or !$area.Contains([ReleaseReceiver]::ScreenBounds($window))) {
                throw 'Resume is not reachable on the remaining display.'
            }
            $receiver.Bounds = $area
            [ReleaseReceiver]::Pump(100)
        }
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'paused across reconnect' ''
        if ($CheckLock -and !$receiver.SessionEvents.Contains(8)) { throw 'Windows did not confirm session unlock.' }
        Select-Mode 'Resume' 1000 4
        $receiver.Hold($receiver.Handle,30,1200)
        Check-Edges 'first dwell after RDP reconnect' 'LD,LU'
    }
    if ($CheckOrderlyExit) {
        $duplicate = Start-Process -FilePath $exe.FullName -PassThru
        try {
            if (!$duplicate.WaitForExit(5000) -or $duplicate.ExitCode -ne 1) {
                throw 'The second candidate instance did not exit with its single-instance status.'
            }
        } finally {
            if (!$duplicate.HasExited) { $duplicate.Kill(); $duplicate.WaitForExit() }
            $duplicate.Dispose()
        }
        if ($app.HasExited) { throw 'Launching a second instance closed the first instance.' }
        Check-Edges 'launch second instance' ''
    }
    if ($CheckOrderlyExit -or $CheckForcedExit) {
        Select-Mode 'Drag' 1002
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'start drag before exit' 'LD'
        if (![ReleaseReceiver]::LeftDown()) { throw 'The drag did not hold the Windows left button.' }
        if ($CheckForcedExit) { $app.Kill() } else { [ReleaseReceiver]::CloseToolbar($window,$app.Id) }
        if (!$app.WaitForExit(5000)) { throw 'Process exit timed out.' }
        [ReleaseReceiver]::Pump(200)
        if ($CheckForcedExit) {
            $heldAfterKill = [ReleaseReceiver]::LeftDown()
            if ($heldAfterKill) {
                $receiver.ReleaseTestDrag()
                throw 'Forced termination left the Windows button held. The receiver released the test drag.'
            }
        }
        Check-Edges 'exit with drag held' 'LU'
        if ([ReleaseReceiver]::LeftDown()) { throw 'Windows still reports a held left button after exit.' }
    }
    $passed = $true
}
catch {
    $failure = $_
    $detail = [ordered]@{Error=$failure.Exception.Message; Cycle=$cycles; Utc=[datetime]::UtcNow.ToString('o')}
    try {
        $detail.WindowsLeftDown = [ReleaseReceiver]::LeftDown()
        $detail.Pointer = [Windows.Forms.Cursor]::Position.ToString()
        if ($app -and !$app.HasExited -and $window) {
            $detail.ToolbarDpi = [ReleaseReceiver]::GetDpiForWindow($window)
            $detail.ToolbarBounds = [ReleaseReceiver]::ScreenBounds($window).ToString()
            if (!$isReference) { $detail.VisibleControls = [ReleaseReceiver]::VisibleLabels($window) }
            $bitmap = [ReleaseReceiver]::CaptureWindow($window)
            try { $bitmap.Save((Join-Path $out 'failure-toolbar.png'),[Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
        }
    } catch { $detail.InspectionError = $_.Exception.Message }
    $detail | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $out 'failure-state.json')
    throw $failure
}
finally {
    if ($app -and !$app.HasExited) {
        if ($window) { [ReleaseReceiver]::CloseToolbar($window,$app.Id) }
        if (!$app.WaitForExit(2500)) { $app.Kill(); $app.WaitForExit() }
    }
    $receiver.Trace | Set-Content -LiteralPath (Join-Path $out events.txt)
    if ($receiver.DelayedHolds.Count) { $receiver.DelayedHolds | Set-Content -LiteralPath (Join-Path $out 'delayed-holds.txt') }
    @{ Product=$Product; SHA256=$hash; Passed=$passed; Host=$computer.Name; Model=$computer.Model;
       Windows=[Environment]::OSVersion.VersionString; BackendAlternate=$Alternate.IsPresent;
       Cycles=$cycles; ElapsedSeconds=$timer.Elapsed.TotalSeconds; ClickTime=2; MoveLimit=5; DragHoldMilliseconds=$DragHoldMilliseconds;
       CheckedOrderlyExit=$CheckOrderlyExit.IsPresent; CheckedForcedExit=$CheckForcedExit.IsPresent;
       LeftDownAfterKill=$heldAfterKill; CheckedReconnect=$CheckReconnect.IsPresent; CheckedLock=$CheckLock.IsPresent; SessionEvents=@($receiver.SessionEvents);
       CheckedBoundaries=$CheckBoundaries.IsPresent; CheckedDesign=$CheckDesign.IsPresent; CheckedMixedDpi=$CheckMixedDpi.IsPresent;
       CheckedMonitorRemoval=$CheckMonitorRemoval.IsPresent; CheckedRestoredProfile=[bool]$RestoreDesignProfile } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt
    if ($registered) { [void][ReleaseReceiver]::WTSUnRegisterSessionNotification($receiver.Handle) }
    $receiver.Dispose()
    if ($previousDpi) { [ReleaseReceiver]::RestoreDpi($previousDpi) }
    if ($SoakHours -gt 0) { [ReleaseReceiver]::KeepAwake($false) }
}
