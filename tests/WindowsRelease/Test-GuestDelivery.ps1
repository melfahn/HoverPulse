# Run in a disposable guest or in a separately reserved physical test login.
# Start each reference/backend combination from a fresh guest profile.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('DisposableGuestName')][string]$TestComputerName,
    [string]$DedicatedTestAccount,
    [Parameter(Mandatory)][ValidateSet('Reference','Candidate')][string]$Product,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Alternate,
    [switch]$CheckOrderlyExit,
    [switch]$CheckForcedExit,
    [switch]$CheckReconnect,
    [switch]$CheckBoundaries,
    [switch]$CheckDesign,
    [ValidateRange(0,768)][int]$ExpectedDpi = 0,
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
$isReference = $Product -eq 'Reference'
if ($CheckOrderlyExit -and $isReference) { throw 'The duplicate-instance exit code is a candidate-only contract.' }
if ($CheckOrderlyExit -and $CheckForcedExit) { throw 'Select one exit test per run.' }
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
    @{SchemaVersion=2;ClickTime=2;MoveLimit=5;AlternateInput=$Alternate.IsPresent;ToolbarX=0;ToolbarY=24} |
        ConvertTo-Json | Set-Content -LiteralPath $file -Encoding UTF8
}
$previousDpi = if ($CheckDesign) { [ReleaseReceiver]::EnterPerMonitorDpi() }
$receiver = New-Object ReleaseReceiver
if ($CheckDesign) {
    $receiver.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
    $receiver.Bounds = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
}
$app = $null
$passed = $false
$heldAfterKill = $null
$cycles = 0
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
        $receiver.Hold($receiver.Handle,30,1200)
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
        }
    } while ($endurance.Elapsed.TotalHours -lt $SoakHours)
    if ($CheckBoundaries) {
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'boundary baseline' 'LD,LU'
        $receiver.HoldAt($receiver.Handle,4,4,1200)
        Check-Edges 'four pixels on both axes' ''
        $receiver.HoldAt($receiver.Handle,8,8,1200)
        Check-Edges 'two four-pixel steps from the baseline' ''
        $receiver.HoldAt($receiver.Handle,13,8,1200)
        Check-Edges 'five pixels on x' 'LD,LU'
        $receiver.HoldAt($receiver.Handle,13,3,1200)
        Check-Edges 'minus five pixels on y' 'LD,LU'
        $receiver.HoldAt($receiver.Handle,13,3,3000)
        Check-Edges 'stationary after dispatch' ''
        Select-Mode 'Pause' 1000
        $anchor = [Windows.Forms.Cursor]::Position
        $toggle = [ReleaseReceiver]::Button($window,'Resume',1000,$isReference)
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'off the paused toggle' ''
        $receiver.HoldScreenPoint($toggle,($anchor.X + 1),$anchor.Y,1200)
        Check-Edges 'one pixel on toggle' ''
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'one-pixel paused rearm stays latched' ''
        $receiver.HoldScreenPoint($toggle,($anchor.X + 3),$anchor.Y,1200)
        Check-Edges 'two further pixels on toggle' ''
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'two-pixel paused rearm resumes' 'LD,LU'
    }
    if ($CheckDesign) {
        if ($isReference) { throw 'Design checks require the Hover Click candidate.' }
        & (Join-Path $PSScriptRoot Test-ToolbarPresentation.ps1) -Window $window -Receiver $receiver -OutputDirectory $out -ExpectedDpi $ExpectedDpi
        Check-Edges 'internal design and Setup actions' ''
    }
    if ($CheckReconnect) {
        Select-Mode 'Pause' 1000
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'paused before RDP disconnect' ''
        @{ProcessId=$app.Id; SessionId=$session; Width=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Width} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'ready-for-reconnect.json')
        $reconnect = [Diagnostics.Stopwatch]::StartNew()
        while (!(Test-Path -LiteralPath (Join-Path $out 'reconnected'))) {
            if ($app.HasExited -or $reconnect.Elapsed.TotalMinutes -ge 4) { throw 'RDP reconnect did not complete.' }
            [ReleaseReceiver]::Pump(100)
        }
        $receiver.Hold($receiver.Handle,0,1200)
        Check-Edges 'paused across reconnect' ''
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
finally {
    if ($app -and !$app.HasExited) {
        if ($window) { [ReleaseReceiver]::CloseToolbar($window,$app.Id) }
        if (!$app.WaitForExit(2500)) { $app.Kill(); $app.WaitForExit() }
    }
    $receiver.Trace | Set-Content -LiteralPath (Join-Path $out events.txt)
    @{ Product=$Product; SHA256=$hash; Passed=$passed; Host=$computer.Name; Model=$computer.Model;
       Windows=[Environment]::OSVersion.VersionString; BackendAlternate=$Alternate.IsPresent;
       Cycles=$cycles; ElapsedSeconds=$timer.Elapsed.TotalSeconds; ClickTime=2; MoveLimit=5;
       CheckedOrderlyExit=$CheckOrderlyExit.IsPresent; CheckedForcedExit=$CheckForcedExit.IsPresent;
       LeftDownAfterKill=$heldAfterKill; CheckedReconnect=$CheckReconnect.IsPresent;
       CheckedBoundaries=$CheckBoundaries.IsPresent; CheckedDesign=$CheckDesign.IsPresent } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt
    $receiver.Dispose()
    if ($previousDpi) { [ReleaseReceiver]::RestoreDpi($previousDpi) }
    if ($SoakHours -gt 0) { [ReleaseReceiver]::KeepAwake($false) }
}
