# Invoked inside the reserved login by Test-GuestDelivery after its native action sequence.
[CmdletBinding()]
param([Parameter(Mandatory)][IntPtr]$Window, [Parameter(Mandatory)]$Receiver,
      [Parameter(Mandatory)][string]$OutputDirectory, [int]$ExpectedDpi)
$ErrorActionPreference = 'Stop'
$dpi = [int][ReleaseReceiver]::GetDpiForWindow($Window)
if ($dpi -le 0 -or ($ExpectedDpi -and $dpi -ne $ExpectedDpi)) { throw "Expected DPI $ExpectedDpi, observed $dpi." }
$order = 'Pause','Left','Double','Drag','Right','Setup','Exit','Move'
$sizes = @(@('Extra Small',44,52,56,5),@('Small',48,62,64,6),@('Medium',56,68,72,8),@('Large',64,76,80,10))
$themes = 'Black and gray','Light','Blue dark'
$fills = '292929','EDF2F9','26364D'
$lockedFills = '444444','DCEAFE','284983'
$pauseFills = 'DEDEDE','2354A2','A8CAFF'
$receipts = New-Object 'System.Collections.Generic.List[object]'
function Scale([int]$Value) { [int][Math]::Round($Value*$dpi/96.0,[MidpointRounding]::AwayFromZero) }
function Find-Control([string]$Name) { [ReleaseReceiver]::Button($Window,$Name,0,$false) }
function Select-Control([string]$Name) {
    $shell = [ReleaseReceiver]::ScreenBounds($Window)
    # This explicitly observed inert strip rearms consecutive dwells on the same command.
    $Receiver.HoldScreenPoint($Window,($shell.Left+[int]($shell.Width/2)),($shell.Top+(Scale 2)),240)
    $Receiver.Hold((Find-Control $Name),0,950)
}
function Select-Value([string]$Prefix,[string]$Value,[int]$Max) {
    for ($i=0; $i -le $Max; $i++) {
        $button = Find-Control $Prefix
        $text = [ReleaseReceiver]::ReadText($button)
        if ($text.Split([char]0xB7)[-1].Trim() -eq $Value) { return }
        if ($i -lt $Max) { Select-Control $Prefix }
    }
    throw "Could not select $Prefix $Value."
}
function Check-Pixel($Bitmap,[int]$X,[int]$Y,[string]$Expected) {
    $actual=$Bitmap.GetPixel($X,$Y).ToArgb() -band 0xffffff
    if ($actual -ne [Convert]::ToInt32($Expected,16)) { throw "Pixel mismatch at ($X,$Y): expected $Expected, saw $($actual.ToString('X6'))." }
}
function Read-Face([int]$Size,[int]$Theme,[bool]$Labels,[int]$Padding) {
    $m = $sizes[$Size]
    $shell = [ReleaseReceiver]::ScreenBounds($Window)
    $bounds = @()
    $inset = 1+$Padding
    $x = $inset
    foreach ($name in $order) {
        $button = Find-Control $name
        $actual = [ReleaseReceiver]::ScreenBounds($button)
        $width = if ($name -eq 'Pause') { $m[3] } else { $m[1] }
        $expected = [Drawing.Rectangle]::FromLTRB(($shell.Left+(Scale $x)),($shell.Top+(Scale $inset)),
            ($shell.Left+(Scale ($x+$width))),($shell.Top+(Scale ($inset+$m[2]))))
        if (!$actual.Equals($expected)) { throw "$name expected $expected, observed $actual." }
        [ReleaseReceiver]::CheckHit($button,$actual.Left,$actual.Top)
        [ReleaseReceiver]::CheckHit($button,($actual.Right-1),($actual.Bottom-1))
        if ($bounds.Count) { [ReleaseReceiver]::CheckHit($Window,($actual.Left-2),($actual.Top+8)) }
        $bounds += $actual
        $x += $width+$m[4]
    }
    $widthPx = Scale ($x-$m[4]+$inset)
    $heightPx = Scale (2*$inset+$m[2])
    if ($shell.Width -ne $widthPx -or $shell.Height -ne $heightPx) { throw 'Shell size mismatch.' }
    $bitmap = [ReleaseReceiver]::CaptureWindow($Window)
    try {
        Check-Pixel $bitmap ($bounds[0].Left-$shell.Left+7) ($bounds[0].Top-$shell.Top+12) $pauseFills[$Theme]
        Check-Pixel $bitmap ($bounds[1].Left-$shell.Left+7) ($bounds[1].Top-$shell.Top+12) $lockedFills[$Theme]
        Check-Pixel $bitmap ($bounds[2].Left-$shell.Left+7) ($bounds[2].Top-$shell.Top+12) $fills[$Theme]
        $bitmap.Save((Join-Path $OutputDirectory "design-$dpi-$Size-$Theme-$Labels-$Padding.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    $saved=Get-Content -LiteralPath (Join-Path $env:APPDATA 'HoverPulse\settings.json') -Raw | ConvertFrom-Json
    if ($saved.AppSize -ne $Size -or [int]$saved.Theme -ne $Theme -or [bool]$saved.ShowToolbarLabels -ne $Labels -or $saved.ToolbarPadding -ne $Padding) {
        throw 'Rendered preferences differ from saved preferences.'
    }
    $receipts.Add([pscustomobject]@{Dpi=$dpi;Size=$Size;Theme=$Theme;Labels=$Labels;Padding=$Padding;Width=$widthPx;Height=$heightPx})
}
# Double-dwell locking and held drag have distinct paint and Windows button state.
Select-Control 'Right'
Select-Control 'Right'
$shell = [ReleaseReceiver]::ScreenBounds($Window)
$right = [ReleaseReceiver]::ScreenBounds((Find-Control 'Right'))
$bitmap = [ReleaseReceiver]::CaptureWindow($Window)
try { Check-Pixel $bitmap ($right.Left-$shell.Left+7) ($right.Top-$shell.Top+12) $lockedFills[0] } finally { $bitmap.Dispose() }
Select-Control 'Left'
Select-Control 'Left'
Select-Control 'Drag'
$Receiver.Hold($Receiver.Handle,0,1200)
if (($Receiver.Edges -join ',') -ne 'LD' -or ![ReleaseReceiver]::LeftDown()) { throw 'Paint test failed to acquire drag.' }
$Receiver.Edges.Clear()
$drag = [ReleaseReceiver]::ScreenBounds((Find-Control 'Drag'))
$bitmap = [ReleaseReceiver]::CaptureWindow($Window)
try {
    Check-Pixel $bitmap ($drag.Left-$shell.Left+7) ($drag.Top-$shell.Top+12) 'F4F4F4'
    $bitmap.Save((Join-Path $OutputDirectory "held-drag-$dpi.png"),[Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }
$Receiver.Hold($Receiver.Handle,30,1200)
if (($Receiver.Edges -join ',') -ne 'LU' -or [ReleaseReceiver]::LeftDown()) { throw 'Paint test failed to drop the drag.' }
$Receiver.Edges.Clear()

foreach ($size in 1,2,3,0) {
    foreach ($theme in 0,1,2) {
        foreach ($labels in $false,$true) {
            Select-Control 'Setup'
            Select-Value 'Size' $sizes[$size][0] 3
            Select-Value 'Theme' $themes[$theme] 2
            $labelState = [ReleaseReceiver]::ReadText((Find-Control 'Labels'))
            if (($labelState -eq 'Labels on') -ne $labels) { Select-Control 'Labels' }
            Select-Control 'OK'
            Select-Control 'Right'
            Read-Face $size $theme $labels 4
        }
    }
}
Select-Control 'Setup'
Select-Control 'Padding +'
Select-Control 'Cancel'
Read-Face 0 2 $true 4
Select-Control 'Setup'
Select-Control 'Padding +'
Select-Control 'OK'
Read-Face 0 2 $true 5
$anchor=[ReleaseReceiver]::ScreenBounds((Find-Control 'Pause'))
Select-Control 'Pause'
$resume=[ReleaseReceiver]::ScreenBounds((Find-Control 'Resume'))
if (!$anchor.Equals($resume)) { throw 'Resume moved on collapse.' }
# Do not hover a shell gap while paused: off-toggle samples must stay frozen.
$Receiver.Hold((Find-Control 'Resume'),4,1200)
$null=Find-Control 'Move'
$beforeMove=[ReleaseReceiver]::ScreenBounds($Window)
Select-Control 'Move'
$null=Find-Control 'Place'
$point=[Windows.Forms.Cursor]::Position
$dx=Scale 44
$dy=Scale 100
$Receiver.HoldScreenPoint($Receiver.Handle,($point.X+$dx),($point.Y+$dy),1500)
$null=Find-Control 'Move'
$afterMove=[ReleaseReceiver]::ScreenBounds($Window)
if ($afterMove.X-$beforeMove.X -ne $dx -or $afterMove.Y-$beforeMove.Y -ne $dy) { throw 'Move/Place did not preserve the pointer offset.' }
Select-Control 'Exit'
Select-Control 'Cancel'
$null=Find-Control 'Pause'
Select-Control 'Exit'
Select-Control 'Exit Hover Click'
if ($Receiver.Edges.Count) { throw 'Internal toolbar operations delivered a desktop click.' }
$receipts | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'design.json')
