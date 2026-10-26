# End-to-end checks for automatic wallpaper switching.
#
#   A. enabled, two wallpapers, 1 minute  -> exactly one change, after ~60s,
#      the desktop picture really changes, the new wallpaper is remembered
#   B. disabled                            -> no change at all
#   C. one wallpaper only                  -> graceful "nothing to switch to"
#   D. fullscreen app                      -> no change while paused, one change
#                                             only after playback resumes
#
# The library and settings are replaced for the duration and restored afterwards.
#
# Run against the installed app with -Exe 'C:\Program Files (x86)\...'.
param(
    [string]$Exe = '',
    # The rotation test needs two different videos you own. Required.
    [string]$VideoA = '',
    [string]$VideoB = ''
)

$ErrorActionPreference = 'Stop'

# All paths are relative to this repository, so the suite runs from a fresh clone.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' }
if (-not $VideoA -or -not $VideoB) { throw 'Pass -VideoA and -VideoB <paths to two mp4/mov files>.' }

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes

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

$appDir    = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP'
$libFile   = Join-Path $appDir 'VulcansLivingWP-library.xml'
$setFile   = Join-Path $appDir 'VulcansLivingWP-settings.xml'
$logDir    = Join-Path $appDir 'logs'
$backup    = Join-Path $env:TEMP 'VulcansLivingWP-tests\auto-switch-backup'
$videoA    = $VideoA
$videoB    = $VideoB
$idA       = 'autotest0000000000000000000000a'
$idB       = 'autotest0000000000000000000000b'

$script:failures = 0
function Check($label, $cond, $detail = '') {
    if ($cond) { Write-Host (("{0,-56} PASS {1}" -f $label, $detail)) }
    else { $script:failures++; Write-Host (("{0,-56} FAIL {1}" -f $label, $detail)) }
}

function Stop-App {
    Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

function Write-Library([string[]]$ids, [string[]]$paths) {
    $items = for ($i = 0; $i -lt $ids.Count; $i++) {
        '  <WallpaperItem><Id>' + $ids[$i] + '</Id><FilePath>' +
        [System.Security.SecurityElement]::Escape($paths[$i]) +
        '</FilePath><AddedAt>' + (Get-Date).ToString('o') + '</AddedAt></WallpaperItem>'
    }
    $xml = "<?xml version=`"1.0`"?>`r`n" +
           "<ArrayOfWallpaperItem xmlns:xsd=`"http://www.w3.org/2001/XMLSchema`" " +
           "xmlns:xsi=`"http://www.w3.org/2001/XMLSchema-instance`">`r`n" +
           ($items -join "`r`n") + "`r`n</ArrayOfWallpaperItem>"
    [IO.File]::WriteAllText($libFile, $xml, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-Setting([string]$name, [string]$value) {
    [xml]$xml = Get-Content $setFile -Raw
    $node = $xml.DocumentElement.SelectSingleNode($name)
    if (-not $node) {
        $node = $xml.CreateElement($name)
        [void]$xml.DocumentElement.AppendChild($node)
    }
    $node.InnerText = $value
    $xml.Save($setFile)
}

function Get-Setting([string]$name) {
    [xml]$xml = Get-Content $setFile -Raw
    $node = $xml.DocumentElement.SelectSingleNode($name)
    if ($node) { return $node.InnerText } else { return $null }
}

function Configure([bool]$enabled, [int]$minutes, [string]$activeId, [string[]]$ids, [string[]]$paths) {
    Write-Library $ids $paths
    Set-Setting 'AutoSwitchWallpapers' ([string]$enabled).ToLower()
    Set-Setting 'AutoSwitchMinutes' ([string]$minutes)
    Set-Setting 'ActiveWallpaperId' $activeId
}

$logFile = $null
function Start-App([string]$applyVideo) {
    $script:logFile = Join-Path $logDir ('VulcansLivingWP-' + (Get-Date).ToString('yyyy-MM-dd') + '.log')
    $script:logFrom = if (Test-Path $script:logFile) { (Get-Item $script:logFile).Length } else { 0 }
    $argv = @()
    if ($applyVideo) { $argv = @('--applywall=' + $applyVideo) }
    return Start-Process -FilePath $Exe -ArgumentList $argv -PassThru
}

function New-Log {
    if (-not (Test-Path $script:logFile)) { return '' }
    $bytes = [IO.File]::ReadAllBytes($script:logFile)
    if ($bytes.Length -le $script:logFrom) { return '' }
    return [Text.Encoding]::UTF8.GetString($bytes, $script:logFrom, $bytes.Length - $script:logFrom)
}

function Count-Log([string]$pattern) {
    return ([regex]::Matches((New-Log), $pattern)).Count
}

function First-ChangeDelay {
    $text = New-Log
    $arm = [regex]::Matches($text, '(?m)^(\S+ \S+) \[INFO \] Auto-switch: next change in 1 min\.')
    $chg = [regex]::Matches($text, '(?m)^(\S+ \S+) \[INFO \] Auto-switch: changing wallpaper to')
    if ($arm.Count -eq 0 -or $chg.Count -eq 0) { return -1 }
    $t1 = [datetime]::ParseExact($chg[0].Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', $null)
    $t0 = $null
    foreach ($m in $arm) {
        $t = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', $null)
        if ($t -le $t1) { $t0 = $t }
    }
    if ($null -eq $t0) { return -1 }
    return [math]::Round(($t1 - $t0).TotalSeconds, 1)
}

function Corner-Grab { return [Shot]::Grab(0, 0, 400, 200) }

function Click-ApplyCard([int]$procId) {
    $AE = [System.Windows.Automation.AutomationElement]
    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
    $win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
    if (-not $win) { return $false }
    $nameCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Apply')
    $btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    if (-not $btn) { return $false }
    try {
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return $true
    } catch { return $false }
}

# ---------------------------------------------------------------- setup
Stop-App
if (-not (Test-Path $libFile)) { throw 'no library.xml to back up' }
Copy-Item $libFile (Join-Path $backup 'run-library.xml') -Force
Copy-Item $setFile (Join-Path $backup 'run-settings.xml') -Force

try {
    # ------------------------------------------------------------ A: enabled
    Write-Host "`n=== A. enabled, 2 wallpapers, 1 minute ==="
    Configure $true 1 $idA @($idA, $idB) @($videoA, $videoB)
    $proc = Start-App $videoA
    Start-Sleep -Seconds 12
    $before = Corner-Grab

    $switched = $false
    for ($i = 0; $i -lt 70 -and -not $switched; $i++) {
        Start-Sleep -Seconds 1
        if ((Count-Log 'Auto-switch: changing wallpaper to') -ge 1) { $switched = $true }
    }
    Start-Sleep -Seconds 3
    $after = Corner-Grab
    $desktopDiff = [Shot]::Diff($before, $after)
    $before.Dispose(); $after.Dispose()

    $changes = Count-Log 'Auto-switch: changing wallpaper to'
    $delay = First-ChangeDelay
    $active = Get-Setting 'ActiveWallpaperId'
    $text = New-Log

    Check 'rotation armed at 1 minute'        ((Count-Log 'Auto-switch: next change in 1 min\.') -gt 0)
    Check 'exactly one change happened'       ($changes -eq 1) ("count=$changes")
    Check 'change fired after ~60s'           ($delay -ge 59 -and $delay -le 72) ("delay=${delay}s")
    Check 'desktop picture changed'           ($desktopDiff -gt 1.0) ("pixel_diff=$desktopDiff")
    Check 'new wallpaper is the other video'  ($active -eq $idB) ("active=$active")
    Check 'new wallpaper loaded on desktop'   ($text -match 'Wallpaper parented into desktop host')
    Check 'no ERROR in log'                   ($text -notmatch '\[ERROR\]')
    Check 'app still running'                 ($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue))

    # ------------------------------------- A2: manual pick while armed
    Write-Host "`n=== A2. manual selection during rotation ==="
    $changesBefore = Count-Log 'Auto-switch: changing wallpaper to'
    $armsBefore = Count-Log 'Auto-switch: next change in 1 min\.'
    $clicked = Click-ApplyCard $proc.Id
    Check 'manual Apply button clicked'       $clicked
    Start-Sleep -Seconds 3
    $active = Get-Setting 'ActiveWallpaperId'
    Check 'manual pick applies that wallpaper' ($active -eq $idA) ("active=$active")
    Check 'manual pick re-arms the countdown'  ((Count-Log 'Auto-switch: next change in 1 min\.') -gt $armsBefore)
    Start-Sleep -Seconds 67
    $active = Get-Setting 'ActiveWallpaperId'
    $changesNow = Count-Log 'Auto-switch: changing wallpaper to'
    Check 'rotation continues after manual pick' ($active -eq $idB) ("active=$active")
    Check 'exactly one more change'           ($changesNow -eq ($changesBefore + 1)) ("count=$changesNow")

    # --------------------------------------------------------- B: disabled
    Write-Host "`n=== B. disabled ==="
    Stop-App
    Configure $false 1 $idA @($idA, $idB) @($videoA, $videoB)
    $proc = Start-App $videoA
    Start-Sleep -Seconds 70
    $active = Get-Setting 'ActiveWallpaperId'
    Check 'no change while disabled'          ((Count-Log 'Auto-switch: changing wallpaper to') -eq 0)
    Check 'wallpaper unchanged'               ($active -eq $idA) ("active=$active")
    Check 'no rotation armed'                 ((Count-Log 'Auto-switch: next change') -eq 0)
    Check 'app still running'                 ($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue))

    # ------------------------------------------------------ C: single item
    Write-Host "`n=== C. single wallpaper ==="
    Stop-App
    Configure $true 1 $idA @($idA) @($videoA)
    $proc = Start-App $videoA
    Start-Sleep -Seconds 70
    Check 'nothing to rotate to is handled'   ((Count-Log 'no other usable wallpaper') -ge 1)
    Check 'single wallpaper stays applied'    ((Get-Setting 'ActiveWallpaperId') -eq $idA)
    Check 'no ERROR in log'                   ((New-Log) -notmatch '\[ERROR\]')
    Check 'app still running'                 ($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue))

    # ------------------------------------------------------- D: fullscreen
    Write-Host "`n=== D. fullscreen pause has priority ==="
    Stop-App
    Configure $true 1 $idA @($idA, $idB) @($videoA, $videoB)
    $proc = Start-App $videoA
    Start-Sleep -Seconds 10
    $fs = Start-Process powershell -ArgumentList '-NoProfile','-WindowStyle','Hidden','-File',
        (Join-Path $PSScriptRoot 'fullscreen-stay.ps1') -PassThru
    Start-Sleep -Seconds 3
    Start-Sleep -Seconds 70
    $duringFs = Count-Log 'Auto-switch: changing wallpaper to'
    Check 'no change while fullscreen'        ($duringFs -eq 0) ("count=$duringFs")

    Get-Process powershell -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -eq 'Fullscreen Test' } |
        ForEach-Object { $_.Kill() }
    Stop-Process -Id $fs.Id -Force -ErrorAction SilentlyContinue
    $resumedAt = Get-Date
    Start-Sleep -Seconds 20
    $early = Count-Log 'Auto-switch: changing wallpaper to'
    Check 'no instant change after resume'    ($early -eq 0) ("count=$early")

    Start-Sleep -Seconds 55
    $late = Count-Log 'Auto-switch: changing wallpaper to'
    Check 'one change after the fresh minute' ($late -eq 1) ("count=$late")
    Check 'app still running'                 ($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue))

    # ------------------------------------------------- shutdown cleanliness
    Write-Host "`n=== E. shutdown ==="
    & $Exe --shutdown | Out-Null
    $exited = $false
    for ($i = 0; $i -lt 15 -and -not $exited; $i++) {
        Start-Sleep -Seconds 1
        $exited = $null -eq (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
    }
    Check 'app exits cleanly on --shutdown'   $exited
    Check 'no ERROR at shutdown'              ((New-Log) -notmatch '\[ERROR\]')
}
finally {
    Stop-App
    Copy-Item (Join-Path $backup 'run-library.xml') $libFile -Force
    Copy-Item (Join-Path $backup 'run-settings.xml') $setFile -Force
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'RESULT: all checks passed' }
else { Write-Host ("RESULT: {0} check(s) failed" -f $script:failures) }
Write-Host 'DONE'
