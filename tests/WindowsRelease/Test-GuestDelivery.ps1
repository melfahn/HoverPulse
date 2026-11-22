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
    [switch]$TraceDrag,
    [switch]$CheckReconnect,
    [switch]$CheckLock,
    [switch]$CheckMonitorRemoval,
    [switch]$CheckBoundaries,
    [switch]$MeasureMotion,
    [switch]$CheckDesign,
    [string]$RestoreDesignProfile,
    [switch]$CheckMixedDpi,
    [string]$RestrictedPeer,
    [ValidateRange(0,768)][int]$ExpectedDpi = 0,
    [ValidateRange(1200,60000)][int]$DragHoldMilliseconds = 1200,
    [ValidateRange(100,1200)][int]$DropDeadlineMilliseconds = 1200,
    [ValidateRange(0,48)][double]$SoakHours = 0
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot DeliveryCycles.ps1)
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
if (!$isReference -and $SoakHours -gt 0 -and !$ExpectedSHA256) { throw 'Candidate endurance requires the exact executable hash.' }
if ($DropDeadlineMilliseconds -ne 1200 -and ($env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $isReference -or !$SoakHours)) {
    throw 'An intentionally short drop deadline requires a disposable hosted candidate soak.'
}
if ($CheckOrderlyExit -and $isReference) { throw 'The duplicate-instance exit code is a candidate-only contract.' }
if ($CheckDesign -and ($CheckReconnect -or $CheckOrderlyExit -or $CheckForcedExit)) { throw 'The presentation run ends with hands-free exit and needs its own login run.' }
if ($CheckMixedDpi -and ($isReference -or $CheckDesign)) { throw 'Mixed-DPI checks require a separate candidate run.' }
if ($RestrictedPeer -and ($isReference -or $SoakHours -or $CheckDesign -or $CheckReconnect -or $CheckMixedDpi -or $CheckForcedExit -or $CheckOrderlyExit -or $CheckBoundaries -or $MeasureMotion -or $RestoreDesignProfile)) { throw 'The input-integrity check requires a separate candidate run.' }
if ($RestrictedPeer -and !$ExpectedSHA256) { throw 'The input-integrity fixture needs the expected candidate hash.' }
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
# Screen placement uses physical pixels. Boundary checks request and verify reference-space offsets.
$previousDpi = [ReleaseReceiver]::EnterPerMonitorDpi()
$receiver = New-Object ReleaseReceiver
$receiver.TraceDrag = $TraceDrag.IsPresent -or $SoakHours -gt 0
$traceVariable = 'HOVERPULSE_DRAG_TRACE_DIRECTORY'
if ($CheckDesign -or $CheckMixedDpi -or $CheckMonitorRemoval) {
    $receiver.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
    $receiver.Bounds = if ($CheckMixedDpi -or $CheckMonitorRemoval) { [Windows.Forms.SystemInformation]::VirtualScreen } else { [Windows.Forms.Screen]::PrimaryScreen.WorkingArea }
}
$app = $null
$start = $null
$passed = $false
$heldAfterKill = $null
$endurance = $null
$registered = $false
$run = @{App=$null; Window=[IntPtr]::Zero; Phase='startup'; TraceStart=0; TimelineStart=0;
         Attempts=0; Cycles=0; Failures=0; Restarts=0; Completed=$false; Passed=$false;
         FailureDir=$null; StopReason=$null; KnownDrag=$false; DragTraceStart=0}
$timer = [Diagnostics.Stopwatch]::StartNew()
function Open-TestToolbar {
    $run.Window = [IntPtr]::Zero
    $run.App = if ($start) { [Diagnostics.Process]::Start($start) } else { Start-Process -FilePath $exe.FullName -PassThru }
    $startup = [Diagnostics.Stopwatch]::StartNew()
    do {
        $run.App.Refresh()
        if ($run.App.HasExited) { throw "Toolbar exited during startup with code $($run.App.ExitCode)." }
        if ($startup.Elapsed.TotalSeconds -gt 30) { throw 'Timed out waiting for the visible toolbar.' }
        [ReleaseReceiver]::Pump(100)
        $run.Window = if ($isReference) { $run.App.MainWindowHandle } else { [ReleaseReceiver]::CandidateToolbar($run.App.Id) }
    } until ($run.Window -ne [IntPtr]::Zero)
}
function Add-Observation($Record, [string]$Key, [scriptblock]$Read) {
    try { $Record[$Key] = & $Read }
    catch { $Record["${Key}Error"] = $_.Exception.Message }
}
function Save-CycleFailure($Failure) {
    $detail = [ordered]@{Error=$Failure.Exception.Message; SHA256=$hash; Attempt=$run.Attempts;
        Cycle=$run.Cycles; Phase=$run.Phase; Utc=[datetime]::UtcNow.ToString('o');
        Qpc=[Diagnostics.Stopwatch]::GetTimestamp(); ReceiverEdges=@($receiver.Edges); KnownTestDrag=$run.KnownDrag}
    $folder = Join-Path $out ('failure-{0:D5}' -f [Math]::Max(1,$run.Failures))
    $null = New-Item -ItemType Directory -Path $folder
    $run.FailureDir = $folder
    Add-Observation $detail WindowsLeftDown { [ReleaseReceiver]::LeftDown() }
    Add-Observation $detail Pointer { [Windows.Forms.Cursor]::Position.ToString() }
    if ($run.App) { $detail.ProcessId = $run.App.Id }
    $receiver.Trace.GetRange($run.TraceStart, $receiver.Trace.Count - $run.TraceStart).ToArray() |
        Set-Content -LiteralPath (Join-Path $folder 'partial-cycle.txt')
    if ($receiver.TraceDrag) {
        $receiver.DragTimeline.GetRange($run.TimelineStart, $receiver.DragTimeline.Count - $run.TimelineStart).ToArray() |
            Set-Content -LiteralPath (Join-Path $folder 'receiver-at-failure.log')
    }
    $statePath = Join-Path $folder 'failure-state.json'
    $detail | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath
    if ($run.Window -ne [IntPtr]::Zero) {
        Add-Observation $detail ToolbarDpi { [ReleaseReceiver]::GetDpiForWindow($run.Window) }
        Add-Observation $detail ToolbarBounds { [ReleaseReceiver]::ScreenBounds($run.Window).ToString() }
        Add-Observation $detail ToolbarResponsive { [ReleaseReceiver]::Responds($run.Window) }
        Add-Observation $detail Screenshot {
            $bitmap = [ReleaseReceiver]::CaptureWindow($run.Window)
            try { $bitmap.Save((Join-Path $folder 'toolbar.png'),[Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
            'toolbar.png'
        }
        if (!$isReference) { Add-Observation $detail VisibleControls { [ReleaseReceiver]::VisibleLabels($run.Window) } }
    }
    $detail | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath
    if ($run.Failures -le 1) { Copy-Item -LiteralPath $statePath -Destination (Join-Path $out 'failure-state.json') }
}
function Observe-LateEdges {
    # Hold the destination and pump the receiver before any recovery action.
    $first = $receiver.Edges.Count
    $firstEvent = $receiver.DragTimeline.Count
    $late = [Diagnostics.Stopwatch]::StartNew()
    $samples = do {
        [pscustomobject]@{Qpc=[Diagnostics.Stopwatch]::GetTimestamp(); LeftDown=[ReleaseReceiver]::LeftDown();
            ToolbarResponsive=[ReleaseReceiver]::Responds($run.Window); ReceiverEdges=$receiver.Edges.Count - $first}
        [ReleaseReceiver]::Pump(75)
    } while ($late.ElapsedMilliseconds -lt 3000)
    @{Samples=@($samples); Edges=@($receiver.Edges.GetRange($first,$receiver.Edges.Count-$first).ToArray());
      LeftDown=[ReleaseReceiver]::LeftDown(); ObservationMilliseconds=$late.ElapsedMilliseconds} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run.FailureDir 'late-observation.json')
    $receiver.DragTimeline.GetRange($firstEvent,$receiver.DragTimeline.Count-$firstEvent).ToArray() |
        Set-Content -LiteralPath (Join-Path $run.FailureDir 'receiver-late.log')
}
function Recover-TestCycle {
    $recovery = [ordered]@{StartedUtc=[datetime]::UtcNow.ToString('o'); OldPid=$run.App.Id; NewPid=$null; Safe=$false}
    $interruption = [Diagnostics.Stopwatch]::StartNew()
    try {
        [ReleaseReceiver]::RequireInputDesktop()
        $first = $receiver.Edges.Count
        if (!$run.App.HasExited) {
            [ReleaseReceiver]::CloseToolbar($run.Window,$run.App.Id)
            $wait = [Diagnostics.Stopwatch]::StartNew()
            while (!$run.App.HasExited -and $wait.ElapsedMilliseconds -lt 2000) { [ReleaseReceiver]::Pump(50) }
            $recovery.ForcedToolbarExit = !$run.App.HasExited
            if (!$run.App.HasExited) { $run.App.Kill() }
            if (!$run.App.WaitForExit(2000)) { throw 'The failed candidate could not be retired.' }
        }
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            [ReleaseReceiver]::Pump(100)
            $active = @(Get-Process -Name HoverPulse,DwellClicker -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)
        } while ($active.Count -and $wait.ElapsedMilliseconds -lt 3000)
        $recovery.ProcessesStillPresent = @($active | ForEach-Object Id)
        $recovery.CandidateExitEdges = @($receiver.Edges.GetRange($first,$receiver.Edges.Count-$first).ToArray())
        if ($active.Count) { throw 'A clicker or its drag worker is still present in the test session.' }

        [ReleaseReceiver]::RequireInputDesktop()
        if ([ReleaseReceiver]::OtherButtonDown()) { throw 'Windows reports another mouse button held.' }
        $recovery.LeftDownAfterRetirement = [ReleaseReceiver]::LeftDown()
        $recovery.FixtureReleaseAttempted = $false
        if ($recovery.LeftDownAfterRetirement) {
            if (!$run.KnownDrag) { throw 'Windows reports a held left button without a verified test drag.' }
            $dragEdges = @($receiver.Trace.GetRange($run.DragTraceStart,$receiver.Trace.Count-$run.DragTraceStart) |
                Where-Object { $_ -match ' (LD|LU) ' })
            if ($dragEdges.Count -ne 1 -or $dragEdges[0] -notmatch ' LD ') {
                throw 'The held button cannot be attributed to the verified test drag.'
            }
            $first = $receiver.Edges.Count
            $recovery.FixtureReleaseAttempted = $true
            try { $receiver.ReleaseTestDrag() }
            finally { $recovery.FixtureReleaseEdges = @($receiver.Edges.GetRange($first,$receiver.Edges.Count-$first).ToArray()) }
            if (($recovery.FixtureReleaseEdges -join ',') -ne 'LU') { throw 'The receiver did not verify the fixture release.' }
        }
        if ([ReleaseReceiver]::LeftDown() -or [ReleaseReceiver]::OtherButtonDown()) { throw 'Windows still reports a held mouse button.' }
        if ($endurance.Elapsed.TotalHours -lt $SoakHours) {
            if ((Get-FileHash -LiteralPath $exe.FullName).Hash -ne $hash) { throw 'Candidate hash changed before restart.' }
            [ReleaseReceiver]::Foreground($receiver.Handle)
            $receiver.Edges.Clear()
            $receiver.Hold($receiver.Handle,-48,100)
            if ($receiver.Edges.Count -or [ReleaseReceiver]::LeftDown() -or [ReleaseReceiver]::OtherButtonDown()) {
                throw 'Unexpected input before candidate restart.'
            }
            try { Open-TestToolbar }
            finally {
                if ($run.App -and $run.App.Id -ne $recovery.OldPid) {
                    $run.Restarts++
                    $recovery.NewPid = $run.App.Id
                }
            }
            $receiver.Hold($receiver.Handle,-48,1200)
            $recovery.RestartEdges = @($receiver.Edges)
            if (($receiver.Edges -join ',') -ne 'LD,LU' -or [ReleaseReceiver]::LeftDown() -or [ReleaseReceiver]::OtherButtonDown()) {
                throw 'The restarted candidate did not deliver a clean left click to the receiver.'
            }
            $receiver.Edges.Clear()
        }
        $recovery.Safe = $true
    } catch {
        $recovery.StopReason = $_.Exception.Message
        throw
    } finally {
        $recovery.InterruptionSeconds = $interruption.Elapsed.TotalSeconds
        $recovery | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run.FailureDir 'recovery.json')
    }
}
try {
    if ($SoakHours -gt 0) { [ReleaseReceiver]::KeepAwake($true) }
    $receiver.TopMost = $true
    $receiver.Show()
    $receiver.Activate()
    [ReleaseReceiver]::Pump(250)
    [ReleaseReceiver]::RequireInputDesktop()
    if ([ReleaseReceiver]::LeftDown() -or [ReleaseReceiver]::OtherButtonDown()) { throw 'A mouse button is held before the test.' }
    $receiver.Hold($receiver.Handle,-48,100)
    $receiver.TopMost = $false
    if ($RestrictedPeer -or $TraceDrag) {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $exe.FullName
        $start.UseShellExecute = $false
        if ($TraceDrag) { $start.EnvironmentVariables[$traceVariable] = $out }
    }
    if ($RestrictedPeer) {
        $lowTemp = Join-Path $env:USERPROFILE ('AppData\LocalLow\HoverClick-QA\' + [Guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $lowTemp -Force
        & icacls $lowTemp /setintegritylevel '(OI)(CI)L' | Out-Null
        if ($LASTEXITCODE) { throw 'Could not prepare the confined runtime directory.' }
        $start.WorkingDirectory = $lowTemp
        $start.EnvironmentVariables['TEMP'] = $lowTemp
        $start.EnvironmentVariables['TMP'] = $lowTemp
        $start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $lowTemp
    }
    Open-TestToolbar
    $app = $run.App
    $window = $run.Window
    if ($RestrictedPeer) {
        & (Join-Path $PSScriptRoot Test-InputBoundary.ps1) -Receiver $receiver -App $app -Window $window -PeerExecutable $RestrictedPeer -OutputDirectory $out -Alternate:$Alternate
        $passed=$true
        return
    }
    if ($RestoreDesignProfile) {
        & (Join-Path $PSScriptRoot Test-ToolbarPresentation.ps1) -Window $window -Receiver $receiver -OutputDirectory $out -Restored
    }
    function Check-Edges([string]$Phase,[string]$Expected) {
        $actual = $receiver.Edges -join ','
        if ($actual -ne $Expected) { throw "Cycle $($run.Cycles), ${Phase}: expected [$Expected], received [$actual]." }
        $receiver.Edges.Clear()
    }
    function Select-Mode([string]$Label,[int]$Id,[int]$Offset = 0) {
        $run.Phase = $Label
        $button = [ReleaseReceiver]::Button($window,$Label,$Id,$isReference)
        $receiver.Hold($button,$Offset,1100)
        Check-Edges $Label ''
    }
    function Hold-ReceiverPhase([string]$Phase,[int]$Offset,[int]$Milliseconds,[string]$Expected) {
        $run.Phase = $Phase
        $receiver.Hold($receiver.Handle,$Offset,$Milliseconds)
        Check-Edges $Phase $Expected
    }
    # Prime the first position. Ignore only this startup interval.
    $receiver.Hold($receiver.Handle,-48,1200)
    $receiver.Edges.Clear()
    $endurance = [Diagnostics.Stopwatch]::StartNew()
    $runCycle = {
        $window = $run.Window
        $run.TraceStart = $receiver.Trace.Count
        $run.TimelineStart = $receiver.DragTimeline.Count
        $run.KnownDrag = $false
        Select-Mode 'Right' 1003
        Hold-ReceiverPhase 'right click' 0 1200 'RD,RU'
        Hold-ReceiverPhase 'restore left default' 30 1200 'LD,LU'
        Select-Mode 'Double' 1001
        Hold-ReceiverPhase 'double click' 0 1200 'LD,LU,LD,LU'
        Select-Mode 'Drag' 1002
        $run.DragTraceStart = $receiver.Trace.Count
        Hold-ReceiverPhase 'start drag' 30 $DragHoldMilliseconds 'LD'
        $run.KnownDrag = $true
        Select-Mode 'Pause' 1000
        Hold-ReceiverPhase 'paused dwell during drag' 0 1200 ''
        Select-Mode 'Resume' 1000 4
        $run.Phase = 'drop after resume'
        $receiver.Hold($receiver.Handle,30,$DropDeadlineMilliseconds)
        $heldAtDeadline = [ReleaseReceiver]::LeftDown()
        if ($receiver.TraceDrag) { $receiver.MarkDrag("drop-deadline attempt=$($run.Attempts) leftDown=$heldAtDeadline") }
        if ($heldAtDeadline) { throw "Cycle $($run.Cycles), Windows still reports a held left button at the drop deadline." }
        Check-Edges 'drop after resume' 'LU'
        $run.KnownDrag = $false
    }
    $progress = {
        if ($run.Attempts -eq 1 -or $run.Attempts % 10 -eq 0 -or $run.FailureDir) {
            @{Cycles=$run.Cycles; Attempts=$run.Attempts; Failures=$run.Failures; Restarts=$run.Restarts;
              ElapsedSeconds=$endurance.Elapsed.TotalSeconds; Utc=[DateTime]::UtcNow.ToString('o')} |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'progress.json')
            if ($SoakHours -gt 0 -and !$run.App.HasExited) {
                $run.App.Refresh()
                [pscustomobject]@{Cycle=$run.Cycles;Attempt=$run.Attempts;PID=$run.App.Id;Handles=$run.App.HandleCount;
                    PrivateBytes=$run.App.PrivateMemorySize64;CpuSeconds=$run.App.TotalProcessorTime.TotalSeconds;DelayedHolds=$receiver.DelayedHolds.Count} |
                    Export-Csv -NoTypeInformation -Append -LiteralPath (Join-Path $out 'resource-samples.csv')
            }
        }
        $run.FailureDir = $null
    }
    $recover = if ($SoakHours -gt 0 -and !$isReference) { { Recover-TestCycle } }
    Invoke-DeliveryCycles -State $run -RunCycle $runCycle -RecordProgress $progress -Recover $recover `
        -HasTime { $endurance.Elapsed.TotalHours -lt $SoakHours } -RecordFailure {
            param($failure)
            Save-CycleFailure $failure
            if ($receiver.TraceDrag -and $run.Phase -eq 'drop after resume') { Observe-LateEdges }
        }
    $app = $run.App
    $window = $run.Window
    if (!$run.Passed) { throw "Endurance finished with $($run.Failures) failed cycle(s). See the recorded failures and recovery receipts." }
    $run.Phase = 'post-cycle qualification'
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
    if (!$run.StopReason -and !$run.Completed) { $run.StopReason = $failure.Exception.Message }
    if (!$run.Failures) { Save-CycleFailure $failure }
    throw $failure
}
finally {
    $cleanupError = $null
    try {
        if ($run.App -and !$run.App.HasExited) {
            if ($run.Window -and !$run.StopReason) { [ReleaseReceiver]::CloseToolbar($run.Window,$run.App.Id) }
            if (!$run.App.WaitForExit(2500)) { $run.App.Kill(); $run.App.WaitForExit() }
        }
    } catch { $cleanupError = $_.Exception.Message; $passed = $false }
    $receiver.Trace | Set-Content -LiteralPath (Join-Path $out events.txt)
    if ($receiver.TraceDrag) {
        $receiver.DragTimeline | Set-Content -LiteralPath (Join-Path $out 'receiver-drag.log')
        [Diagnostics.Stopwatch]::Frequency | Set-Content -LiteralPath (Join-Path $out 'qpc-frequency.txt')
    }
    if ($receiver.DelayedHolds.Count) { $receiver.DelayedHolds | Set-Content -LiteralPath (Join-Path $out 'delayed-holds.txt') }
    @{ Product=$Product; SHA256=$hash; Passed=$passed; Host=$computer.Name; Model=$computer.Model;
       Windows=[Environment]::OSVersion.VersionString; BackendAlternate=$Alternate.IsPresent;
       Cycles=$run.Cycles; Attempts=$run.Attempts; FailedCycles=$run.Failures; CandidateRestarts=$run.Restarts;
       RequestedSoakHours=$SoakHours; SoakCompleted=$run.Completed; SoakElapsedSeconds=if ($endurance) {$endurance.Elapsed.TotalSeconds};
       StopReason=$run.StopReason; CleanupError=$cleanupError;
       ElapsedSeconds=$timer.Elapsed.TotalSeconds; ClickTime=2; MoveLimit=5; DragHoldMilliseconds=$DragHoldMilliseconds;
       DropDeadlineMilliseconds=$DropDeadlineMilliseconds;
       CheckedOrderlyExit=$CheckOrderlyExit.IsPresent; CheckedForcedExit=$CheckForcedExit.IsPresent;
       LeftDownAfterKill=$heldAfterKill; CheckedReconnect=$CheckReconnect.IsPresent; CheckedLock=$CheckLock.IsPresent; SessionEvents=@($receiver.SessionEvents);
       CheckedBoundaries=$CheckBoundaries.IsPresent; CheckedDesign=$CheckDesign.IsPresent; CheckedMixedDpi=$CheckMixedDpi.IsPresent;
       CheckedMonitorRemoval=$CheckMonitorRemoval.IsPresent; CheckedRestoredProfile=[bool]$RestoreDesignProfile; CheckedInputBoundary=[bool]$RestrictedPeer } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt
    if ($registered) { [void][ReleaseReceiver]::WTSUnRegisterSessionNotification($receiver.Handle) }
    $receiver.Dispose()
    if ($previousDpi) { [ReleaseReceiver]::RestoreDpi($previousDpi) }
    if ($SoakHours -gt 0) { [ReleaseReceiver]::KeepAwake($false) }
}
