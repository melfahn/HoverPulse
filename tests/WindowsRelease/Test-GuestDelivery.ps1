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
    foreach ($proc in $protected) {
        $owner = Invoke-CimMethod -InputObject $proc -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or !$owner.Sid -or $owner.Sid -eq $identity.User.Value -or $proc.SessionId -eq $session) {
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
$receiver = New-Object ReleaseReceiver
$app = $null
$passed = $false
$cycles = 0
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $receiver.Show()
    $receiver.Hold($receiver.Handle,-48,100)
    $app = Start-Process -FilePath $exe.FullName -PassThru
    do {
        $app.Refresh()
        if ($app.HasExited -or $timer.Elapsed.TotalSeconds -gt 20) { throw 'Toolbar startup failed' }
        [ReleaseReceiver]::Pump(100)
    } until ($app.MainWindowHandle -ne 0)
    $window = $app.MainWindowHandle
    function Select-Mode([string]$Label,[int]$Id,[int]$Offset = 0) {
        $button = [ReleaseReceiver]::Button($window,$Label,$Id,$isReference)
        $receiver.Hold($button,$Offset,1100)
    }
    # Prime the first position. Ignore only this startup interval.
    $receiver.Hold($receiver.Handle,-48,1200)
    $receiver.Edges.Clear()
    do {
        Select-Mode 'Right' 1003
        $receiver.Hold($receiver.Handle,0,1200)
        $receiver.Hold($receiver.Handle,30,1200)
        Select-Mode 'Double' 1001
        $receiver.Hold($receiver.Handle,0,1200)
        Select-Mode 'Drag' 1002
        $receiver.Hold($receiver.Handle,30,1200)
        Select-Mode 'Pause' 1000
        $receiver.Hold($receiver.Handle,0,1200)
        Select-Mode 'Resume' 1000 4
        $receiver.Hold($receiver.Handle,30,1200)
        $expected = 'RD,RU,LD,LU,LD,LU,LD,LU,LD,LU'
        if (($receiver.Edges -join ',') -ne $expected) { throw "Mismatch in cycle $cycles. Received $($receiver.Edges -join ',')" }
        $receiver.Edges.Clear()
        $cycles++
    } while ($timer.Elapsed.TotalHours -lt $SoakHours)
    $passed = $true
}
finally {
    if ($app -and !$app.HasExited) {
        $null = $app.CloseMainWindow()
        if (!$app.WaitForExit(2500)) { $app.Kill(); $app.WaitForExit() }
    }
    $receiver.Trace | Set-Content -LiteralPath (Join-Path $out events.txt)
    @{ Product=$Product; SHA256=$hash; Passed=$passed; Host=$computer.Name; Model=$computer.Model;
       Windows=[Environment]::OSVersion.VersionString; BackendAlternate=$Alternate.IsPresent;
       Cycles=$cycles; ElapsedSeconds=$timer.Elapsed.TotalSeconds; ClickTime=2; MoveLimit=5 } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt
    $receiver.Dispose()
}
