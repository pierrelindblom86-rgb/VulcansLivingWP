# End-to-end verification for the 1.2.2 activation fix.
#
#   A) activates a wallpaper and proves it stays visible: the video window is a
#      visible child of a WorkerW wallpaper layer (not of Progman) and the
#      desktop pixels keep changing.
#   B) clicks the real "Apply" button twice on the already-active wallpaper and
#      proves no second activation happens.
#   C) with -ExplorerRestart: kills Explorer while playing and checks that the
#      app re-acquires the layer and keeps animating.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\wallpaper-activation-test.ps1

param(
    # Any mp4/mov file you own. Required: the repository does not ship video files.
    [string]$Video = '',
    # Defaults to this repository's Release build; point it at an installed copy to
    # verify a real installation instead.
    [string]$Exe = '',
    [switch]$ExplorerRestart,
    # Restart Explorer first, so the desktop starts without any WorkerW wallpaper
    # layer. This reproduces the 1.2.1 failure exactly.
    [switch]$FreshShell
)

$ErrorActionPreference = 'Stop'

# All paths are relative to this repository, so the suite runs from a fresh clone.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Video) { throw 'Pass -Video <path to an mp4/mov file>.' }
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

public static class Act
{
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    // "parentClass|visible" for this process' wallpaper surface, searched inside
    // every WorkerW/Progman container (the surface is a child, so a plain
    // top-level enumeration never sees it).
    public static string WallpaperSurface(int pid)
    {
        string result = "";
        var tops = new List<IntPtr>();
        EnumWindows((h, l) => { tops.Add(h); return true; }, IntPtr.Zero);
        foreach (var t in tops)
        {
            string tc = Cls(t);
            if (tc != "WorkerW" && tc != "Progman") continue;
            EnumChildWindows(t, (c, l) =>
            {
                uint cp; GetWindowThreadProcessId(c, out cp);
                if (cp == (uint)pid && Cls(c).StartsWith("HwndWrapper[VulcansLivingWP"))
                    result = Cls(GetAncestor(c, 1)) + "|" + (IsWindowVisible(c) ? "visible" : "hidden");
                return true;
            }, IntPtr.Zero);
            if (result != "") break;
        }
        return result;
    }

    public static int TopLevelFullscreenSurfaces(int pid)
    {
        int n = 0;
        var tops = new List<IntPtr>();
        EnumWindows((h, l) => { tops.Add(h); return true; }, IntPtr.Zero);
        foreach (var t in tops)
        {
            uint tp; GetWindowThreadProcessId(t, out tp);
            if (tp != (uint)pid || !IsWindowVisible(t)) continue;
            if (!Cls(t).StartsWith("HwndWrapper[VulcansLivingWP")) continue;
            RECT r; GetWindowRect(t, out r);
            if ((r.R - r.L) >= 1000 && (r.B - r.T) >= 700) n++;   // a desktop-sized window on top
        }
        return n;
    }

    public static double Motion(int x, int y, int w, int h, int gapMs)
    {
        using (var a = Grab(x, y, w, h))
        {
            System.Threading.Thread.Sleep(gapMs);
            using (var b = Grab(x, y, w, h))
                return Diff(a, b);
        }
    }

    static Bitmap Grab(int x, int y, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }

    static double Diff(Bitmap a, Bitmap b)
    {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y += 6)
            for (int x = 0; x < a.Width; x += 6)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n += 3;
            }
        return n == 0 ? 0 : Math.Round(sum / n, 2);
    }

    public static void CloseWindow(IntPtr h) { SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
'@ -ReferencedAssemblies System.Drawing

$exe = $Exe
$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

$script:failures = 0
function Check($label, $cond, $extra) {
    if ($cond) { Write-Host ("PASS  {0}" -f $label) }
    else { Write-Host ("FAIL  {0}   [{1}]" -f $label, $extra); $script:failures++ }
}
function LatestLog { Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
function LogText {
    # The app holds the log open for appending, so read it with a sharing mode
    # and tolerate the file not existing yet (first launch of the day).
    $f = LatestLog
    if (-not $f) { return '' }
    try {
        $fs = [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'ReadWrite')
        try {
            $sr = New-Object System.IO.StreamReader($fs)
            return $sr.ReadToEnd()
        } finally { $fs.Dispose() }
    } catch { return '' }
}
function Stop-App {
    Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.CloseMainWindow() | Out-Null } catch { }
    }
    Start-Sleep -Milliseconds 500
    Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}
function Start-App([string[]]$extraArgs, [switch]$KeepWindow) {
    $p = Start-Process -FilePath $exe -ArgumentList (@('--minimized') + $extraArgs) -PassThru
    for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
    if (-not $KeepWindow -and $p.MainWindowHandle -ne 0) { [Act]::CloseWindow($p.MainWindowHandle) | Out-Null }
    return $p
}
function FindButtons($procId) {
    $result = @()
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byPid = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byPid)
    if (-not $win) { return $result }
    $byType = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byType)) {
        if ($b.Current.Name -match 'Apply|Active|Preview') { $result += $b }
    }
    return $result
}
function InvokeTwice($element) {
    $invoke = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Milliseconds 120
    $invoke.Invoke()
}
function Delta($before) {
    $now = LogText
    if ($now.StartsWith($before)) { return $now.Substring($before.Length) }
    return $now
}

Write-Host "=== A: single activation ==="
Stop-App
if ($FreshShell) {
    Write-Host "restarting Explorer to start without a wallpaper layer..."
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 6
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    Start-Sleep -Seconds 8
}
$before = LogText
$app = Start-App @("--applywall=$Video")
Start-Sleep -Seconds 8
$text = Delta $before
$surface = [Act]::WallpaperSurface($app.Id)
$motion = [Act]::Motion($screen.X, $screen.Y, $screen.Width, $screen.Height, 900)
$topFullscreen = [Act]::TopLevelFullscreenSurfaces($app.Id)
$sets = ([regex]::Matches($text, 'Wallpaper set:')).Count

Write-Host "activation log:`n$text"
Write-Host "surface=$surface motion=$motion top_level_fullscreen=$topFullscreen set_lines=$sets"

Check 'wallpaper surface is a child of a WorkerW layer' ($surface -like 'WorkerW|*') $surface
Check 'wallpaper surface is visible' ($surface -like '*|visible') $surface
Check 'wallpaper layer was acquired or created' (($text -match 'Wallpaper layer ready') -or ($text -match 'Wallpaper layer created'))
if ($FreshShell) {
    Check 'layer was CREATED after a fresh Explorer start' ($text -match 'Wallpaper layer created') 'not created'
    Check 'log never mentions a Progman fallback' ($text -notmatch 'Progman fallback')
}
Check 'no desktop-sized window floats above everything' ($topFullscreen -eq 0) $topFullscreen
Check 'desktop shows moving video' ($motion -gt 0.5) $motion
Check 'exactly one activation for one request' ($sets -eq 1) $sets

Write-Host "=== B: two clicks on Apply while that wallpaper is active ==="
Stop-App
# The main window has to be on screen for UI automation, so this launch keeps it.
$app = Start-App @("--applywall=$Video") -KeepWindow
Start-Sleep -Seconds 7
$buttons = FindButtons $app.Id
$activeBtn = $buttons | Where-Object { $_.Current.Name -match 'Active' } | Select-Object -First 1
$otherBtn = $buttons | Where-Object { $_.Current.Name -match 'Apply' } | Select-Object -First 1
Check 'button of the active wallpaper was found' ($activeBtn -ne $null) 'not found'

# B1: the wallpaper is already playing - a double click must do nothing at all.
if ($activeBtn) {
    $before = LogText
    InvokeTwice $activeBtn
    Start-Sleep -Seconds 4
    $text = Delta $before
    $sets = ([regex]::Matches($text, 'Wallpaper set:')).Count
    $ignored = ([regex]::Matches($text, 'already active|Ignoring repeated|already in progress')).Count
    Write-Host "active-card log:`n$text"
    Write-Host "set_lines=$sets ignored_lines=$ignored"
    Check 'clicking the active wallpaper does not re-activate it' ($sets -eq 0) $sets
    Check 'the repeat request is logged' ($ignored -ge 1) $ignored
}

# B2: switching to another wallpaper - two quick clicks, one activation.
Check 'a second wallpaper is available for the switch test' ($otherBtn -ne $null) 'only one wallpaper in the library'
if ($otherBtn) {
    $before = LogText
    InvokeTwice $otherBtn
    Start-Sleep -Seconds 5
    $text = Delta $before
    $sets = ([regex]::Matches($text, 'Wallpaper set:')).Count
    $ignored = ([regex]::Matches($text, 'Ignoring repeated|already in progress|already active')).Count
    Write-Host "switch log:`n$text"
    Write-Host "set_lines=$sets ignored_lines=$ignored"
    Check 'two quick clicks switch the wallpaper exactly once' ($sets -eq 1) $sets
    Check 'the second click is logged as ignored' ($ignored -ge 1) $ignored
}

# D: a card preview must stay inside the card.
Write-Host "=== D: card preview open/close ==="
$previewBtn = $null
foreach ($b in (FindButtons $app.Id)) { if ($b.Current.Name -match 'Preview') { $previewBtn = $b; break } }
Check 'a Preview button was found' ($previewBtn -ne $null) 'not found'
if ($previewBtn) {
    $before = LogText
    $inv = $previewBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $inv.Invoke()
    Start-Sleep -Seconds 4
    $inv.Invoke()
    Start-Sleep -Seconds 3
    $text = Delta $before
    $sets = ([regex]::Matches($text, 'Wallpaper set:')).Count
    $previewed = ([regex]::Matches($text, 'Card preview opened')).Count
    Write-Host "preview log:`n$text"
    Write-Host "set_lines=$sets preview_opened=$previewed"
    Check 'preview opened inside the card' ($previewed -ge 1) $previewed
    Check 'preview did not replace the wallpaper' ($sets -eq 0) $sets
}

# Hide the UI again and confirm the wallpaper itself never stopped.
[Act]::CloseWindow($app.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 2
$motion = [Act]::Motion($screen.X, $screen.Y, $screen.Width, $screen.Height, 900)
Write-Host "motion=$motion"
Check 'wallpaper keeps playing after the clicks' ($motion -gt 0.5) $motion

if ($ExplorerRestart) {
    Write-Host "=== C: Explorer restart while playing ==="
    Stop-Process -Name explorer -Force
    Start-Sleep -Seconds 6
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    Start-Sleep -Seconds 12
    $text = LogText
    $noticed = $text -match 'Wallpaper injection lost|Explorer restart detected|Rebuilding the wallpaper'
    $surface = [Act]::WallpaperSurface($app.Id)
    $motion = [Act]::Motion($screen.X, $screen.Y, $screen.Width, $screen.Height, 900)
    Write-Host "after explorer restart: surface=$surface motion=$motion"
    Check 'app noticed the shell restart' $noticed
    Check 'wallpaper re-parented into a WorkerW layer' ($surface -like 'WorkerW|*') $surface
    Check 'wallpaper animates again after Explorer restart' ($motion -gt 0.5) $motion
}

Stop-App
Write-Host ('RESULT: ' + $(if ($script:failures -eq 0) { 'ALL CHECKS PASSED' } else { "$script:failures CHECK(S) FAILED" }))
exit $script:failures
