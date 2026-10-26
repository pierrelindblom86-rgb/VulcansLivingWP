param(
    [string]$Exe = '',
    # Any mp4/mov file you own. Required: the repository does not ship video files.
    [string]$Video = '',
    # Screenshots are written here; defaults to the temp folder.
    [string]$OutDir = ''
)

# All paths are relative to this repository, so the suite runs from a fresh clone.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Video) { throw 'Pass -Video <path to an mp4/mov file>.' }
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' }
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP 'VulcansLivingWP-tests\preview-play' }

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class FrameDiff {
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr h);
    public static Bitmap Grab(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }
    public static Bitmap Crop(Bitmap src, Rectangle r) { return src.Clone(r, src.PixelFormat); }
    public static double Diff(Bitmap a, Bitmap b) {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y += 2) {
            for (int x = 0; x < a.Width; x += 2) {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                n += 3;
            }
        }
        return n == 0 ? 0 : Math.Round(sum / n, 2);
    }
    public static double Brightness(Bitmap a) {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y += 3)
            for (int x = 0; x < a.Width; x += 3) {
                Color p = a.GetPixel(x, y); sum += p.R + p.G + p.B; n += 3;
            }
        return n == 0 ? 0 : Math.Round(sum / n, 1);
    }
}
'@ -ReferencedAssemblies System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $Exe -ArgumentList ('--applywall=' + $Video) -PassThru
Start-Sleep -Seconds 10

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Host 'window_not_found'; exit 2 }

[void][FrameDiff]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 900

$b = $win.Current.BoundingRectangle
$x = [int]$b.X; $y = [int]$b.Y; $w = [int]$b.Width; $h = [int]$b.Height
Write-Host ("window=" + $x + "," + $y + " " + $w + "x" + $h)

# Everything above the buttons row of the first card is the video area.
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Preview')
$btns = @()
for ($try = 0; $try -lt 30 -and $btns.Count -eq 0; $try++) {
    $btns = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) |
        Sort-Object { $_.Current.BoundingRectangle.X })
    if ($btns.Count -eq 0) { Start-Sleep -Milliseconds 500 }
}
Write-Host ('preview_buttons=' + $btns.Count)
if ($btns.Count -lt 1) { Write-Host 'no_preview_buttons'; exit 3 }

$r = $btns[0].Current.BoundingRectangle
# Screenshots are window-relative, so convert the screen-space button rect.
$cardRect = New-Object System.Drawing.Rectangle(
    [int]($r.X - $b.X - 10), [int]($r.Y - $b.Y - 130), 230, 125)
Write-Host ("card_region=" + $cardRect.X + "," + $cardRect.Y + " " + $cardRect.Width + "x" + $cardRect.Height)

$full0 = [FrameDiff]::Grab($x, $y, $w, $h)
$before = [FrameDiff]::Crop($full0, $cardRect)
$before.Save((Join-Path $OutDir 'card-before.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host ('card_brightness_before=' + [FrameDiff]::Brightness($before))

$btns[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1200

$frames = @()
for ($i = 0; $i -lt 4; $i++) {
    $full = [FrameDiff]::Grab($x, $y, $w, $h)
    $crop = [FrameDiff]::Crop($full, $cardRect)
    $crop.Save((Join-Path $OutDir ("card-after-" + $i + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , $crop
    Start-Sleep -Milliseconds 500
}

$diffs = @()
for ($i = 1; $i -lt $frames.Count; $i++) {
    $diffs += [FrameDiff]::Diff($frames[$i - 1], $frames[$i])
}
Write-Host ('frame_diffs=' + ($diffs -join ','))
Write-Host ('card_brightness_after=' + [FrameDiff]::Brightness($frames[$frames.Count - 1]))
$moving = ($diffs | Measure-Object -Maximum).Maximum -gt 0.5
Write-Host ('card_is_animating=' + $moving)
Write-Host ('top_level_windows=' + @([FrameDiff]::Grab(0,0,1,1) | Out-Null; (Get-Process VulcansLivingWP -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Measure-Object).Count))

$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
$log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host '--- log tail ---'
Get-Content $log.FullName -Tail 8

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
