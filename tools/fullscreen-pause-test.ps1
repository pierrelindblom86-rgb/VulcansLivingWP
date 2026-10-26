# Verifies fullscreen auto-pause: a maximised borderless window must pause the
# wallpaper, and closing it must resume playback. Evidence comes from the app
# log (Fullscreen activity detected / ended) and from desktop pixel motion.
param(
    # Any mp4/mov file you own. Required: the repository does not ship video files.
    [string]$Video = '',
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'

# All paths are relative to this repository, so the suite runs from a fresh clone.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Video) { throw 'Pass -Video <path to an mp4/mov file>.' }
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class Shot {
    public static Bitmap Grab(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }
    public static double Diff(Bitmap a, Bitmap b) {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y += 4)
            for (int x = 0; x < a.Width; x += 4) {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n += 3;
            }
        return n == 0 ? 0 : Math.Round(sum / n, 2);
    }
}
'@ -ReferencedAssemblies System.Drawing

$exe = $Exe
$video = $Video
$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'

function Check($label, $cond) { Write-Host (("{0,-46} {1}" -f $label, $(if ($cond) { 'PASS' } else { 'FAIL' }))) }
function WallpaperMotion() {
    $s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $a = [Shot]::Grab(0, 0, $s.Width, $s.Height)
    Start-Sleep -Milliseconds 900
    $b = [Shot]::Grab(0, 0, $s.Width, $s.Height)
    $d = [Shot]::Diff($a, $b); $a.Dispose(); $b.Dispose()
    return $d
}

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$app = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $video) -PassThru
Start-Sleep -Seconds 10
# Send the app to the tray so it does not cover the desktop while we measure.
$app.Refresh()
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class Msg{[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}'
[void][Msg]::SendMessage($app.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$baseMotion = WallpaperMotion
Write-Host ('wallpaper_motion_before=' + $baseMotion)
Check 'wallpaper animates normally' ($baseMotion -gt 0.5)

# A borderless maximised window is what games use.
$fs = Start-Process powershell -ArgumentList '-NoProfile','-WindowStyle','Hidden','-File',(Join-Path $PSScriptRoot 'fullscreen-stay.ps1') -PassThru
Start-Sleep -Seconds 4
Add-Type -AssemblyName Microsoft.VisualBasic
$motion = WallpaperMotion
Write-Host ('wallpaper_motion_during_fullscreen=' + $motion)
Check 'wallpaper paused while fullscreen' ($motion -lt 0.5)

$fs.CloseMainWindow() | Out-Null
Start-Sleep -Milliseconds 500
Get-Process powershell -ErrorAction SilentlyContinue |
    Where-Object { $_.Id -ne $PID -and $_.StartTime -gt (Get-Date).AddMinutes(-3) -and $_.MainWindowTitle -eq 'Fullscreen Test' } |
    ForEach-Object { $_.Kill() }
Stop-Process -Id $fs.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 4
$resumed = WallpaperMotion
Write-Host ('wallpaper_motion_after_close=' + $resumed)
Check 'wallpaper resumed after fullscreen' ($resumed -gt 0.5)

$log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
$text = Get-Content $log.FullName -Raw
Check 'log: fullscreen detected' ($text -match 'Fullscreen activity detected')
Check 'log: fullscreen ended'     ($text -match 'Fullscreen activity ended')

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
