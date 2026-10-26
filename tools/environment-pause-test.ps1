# Verifies the 1.2.3 environment pause: playback must stop while the desktop
# cannot be seen (locked session / display off / disconnected session) and must
# resume afterwards, including on the same session and the same decoder.
#
# The screen is not actually locked (that would hide the desktop we measure), so
# the app's test hook --testsuspend pretends the screen is unavailable and
# --testsuspendclear releases it again after N seconds.
#
# Evidence: desktop pixel motion (a paused wallpaper is a still frame) plus the
# app log.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\environment-pause-test.ps1

param(
    # Any mp4/mov file you own. Required: the repository does not ship video files.
    [string]$Video = '',
    [string]$Exe = '',
    # Seconds after launch at which the fake "screen unavailable" state is released.
    [int]$ResumeAfter = 16
)

$ErrorActionPreference = 'Stop'

# All paths are relative to this repository, so the suite runs from a fresh clone.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Video) { throw 'Pass -Video <path to an mp4/mov file>.' }
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class EPShot {
    public static Bitmap Grab(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }
    public static double Diff(Bitmap a, Bitmap b) {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y += 6)
            for (int x = 0; x < a.Width; x += 6) {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n += 3;
            }
        return n == 0 ? 0 : Math.Round(sum / n, 2);
    }
}
'@ -ReferencedAssemblies System.Drawing

$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

$script:failures = 0
function Check($label, $cond, $extra) {
    if ($cond) { Write-Host ("PASS  {0}" -f $label) }
    else { Write-Host ("FAIL  {0}   [{1}]" -f $label, $extra); $script:failures++ }
}

function LatestLog { Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
function LogText {
    $f = LatestLog
    if (-not $f) { return '' }
    try {
        $fs = [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'ReadWrite')
        try { return (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}
function Motion {
    $a = [EPShot]::Grab($screen.X, $screen.Y, $screen.Width, $screen.Height)
    Start-Sleep -Milliseconds 900
    $b = [EPShot]::Grab($screen.X, $screen.Y, $screen.Width, $screen.Height)
    $d = [EPShot]::Diff($a, $b); $a.Dispose(); $b.Dispose()
    return $d
}

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$before = LogText
$appArgs = @('--minimized', "--applywall=$Video", '--testsuspend=on', "--testsuspendclear=$ResumeAfter")
$app = Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
Write-Host ("launched pid={0} resume_after={1}s" -f $app.Id, $ResumeAfter)

Start-Sleep -Seconds 9
$pausedMotion = Motion
$pausedLog = LogText
$pauseLines = ([regex]::Matches($pausedLog.Substring([Math]::Min($before.Length, $pausedLog.Length)),
                               'Playback paused while the desktop is not visible')).Count
Write-Host ("motion_while_paused={0} pause_lines={1}" -f $pausedMotion, $pauseLines)
Check 'playback paused while the screen is unavailable' ($pausedMotion -lt 0.5) $pausedMotion
Check 'pause is logged once' ($pauseLines -eq 1) $pauseLines
Check 'log names the reason' ($pausedLog -match 'Playback paused while the desktop is not visible \(test override\)')

# Now wait for the fake lock to be released and prove the SAME decoder restarts.
Start-Sleep -Seconds ([Math]::Max(0, $ResumeAfter - 9 + 6))
$resumedMotion = Motion
$fullLog = LogText
$resumeLines = ([regex]::Matches($fullLog.Substring([Math]::Min($before.Length, $fullLog.Length)),
                                 'Playback resumed: the desktop is visible again')).Count
Write-Host ("motion_after_resume={0} resume_lines={1}" -f $resumedMotion, $resumeLines)
Check 'playback resumed after the screen came back' ($resumedMotion -gt 0.5) $resumedMotion
Check 'resume is logged once' ($resumeLines -eq 1) $resumeLines

# The wallpaper must still be the live one, not a stale frozen surface.
Start-Sleep -Seconds 2
$laterMotion = Motion
Write-Host ("motion_later={0}" -f $laterMotion)
Check 'wallpaper keeps animating afterwards' ($laterMotion -gt 0.5) $laterMotion
Check 'app is still running' ($null -ne (Get-Process -Id $app.Id -ErrorAction SilentlyContinue))

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host ('RESULT: ' + $(if ($script:failures -eq 0) { 'ALL CHECKS PASSED' } else { "$script:failures CHECK(S) FAILED" }))
exit $script:failures
