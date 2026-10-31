param(
    [string]$Exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe',
    [string]$Video = 'C:\pwallpaper\test-videos\sample.mp4',
    [string]$OutDir = 'C:\pwallpaper\tmp\preview-in-card-test'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClickNative {
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

New-Item -Path $OutDir -ItemType Directory -Force | Out-Null
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $Exe -ArgumentList ('--applywall=' + $Video) -PassThru
Start-Sleep -Seconds 12

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $win) { Write-Host 'window_not_found'; exit 2 }
[void][ClickNative]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 800

$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Preview')
$btns = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond))
Write-Host ('preview_buttons=' + $btns.Count)
if ($btns.Count -lt 1) { Write-Host 'no_preview_buttons'; exit 3 }

# left-most Preview button = first card (black-cat)
$first = @($btns | Sort-Object { $_.Current.BoundingRectangle.X })[0]
$r = $first.Current.BoundingRectangle
Write-Host ('first_preview_rect=' + [int]$r.X + ',' + [int]$r.Y + ',' + [int]$r.Width + ',' + [int]$r.Height)

$clicked = $false
try {
    $first.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $clicked = $true
} catch { Write-Host ('invoke_failed=' + $_.Exception.Message) }
if (-not $clicked) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    [ClickNative]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)
    [ClickNative]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
    Write-Host 'clicked_via_mouse_fallback'
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
# thumbnail area sits above the Preview button
$thumb = New-Object System.Drawing.Rectangle([int]($r.X - 70), [int]($r.Y - 160), 210, 115)

function Grab($name, $rect) {
    $bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $screen.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $name))
    $crop = $bmp.Clone($rect, $bmp.PixelFormat)
    $bmp.Dispose()
    $crop.Save((Join-Path $OutDir ($name -replace '\.png$', '_thumb.png')))
    return ,$crop
}

Start-Sleep -Seconds 4
$a = Grab 'card_t0.png' $thumb
Start-Sleep -Milliseconds 1500
$b = Grab 'card_t1.png' $thumb
Start-Sleep -Milliseconds 1500
$c = Grab 'card_t2.png' $thumb

function Diff($x, $y) {
    $sum = [double]0; $n = [double]0
    for ($i = 0; $i -lt $x.Width; $i += 4) {
        for ($j = 0; $j -lt $x.Height; $j += 4) {
            $p = $x.GetPixel($i, $j); $q = $y.GetPixel($i, $j)
            $sum += [math]::Abs($p.R - $q.R) + [math]::Abs($p.G - $q.G) + [math]::Abs($p.B - $q.B)
            $n++
        }
    }
    return [math]::Round($sum / [double]$n, 2)
}
$d1 = Diff $a $b
$d2 = Diff $b $c
Write-Host ('thumb_diff_t0_t1=' + $d1 + ' thumb_diff_t1_t2=' + $d2)
$playing = ($d1 -gt 1) -or ($d2 -gt 1)
Write-Host ('in_card_video_playing=' + $playing)

$winsNow = @(Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.ProcessName -eq 'FluxWall' })
Write-Host ('fluxwall_top_windows=' + $winsNow.Count)

$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
$log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host '--- log tail ---'
Get-Content $log.FullName -Tail 15
