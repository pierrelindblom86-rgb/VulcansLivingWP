$ErrorActionPreference = 'Continue'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinBtn {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(150);
    mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
  }
}
"@

function Get-Main {
  $p = Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  return $p
}

function State([string]$label) {
  $p = Get-Main
  if (-not $p) { Write-Host "$label : NO-MAIN-WINDOW"; return $null }
  $h = $p.MainWindowHandle
  $r = New-Object WinBtn+RECT
  [WinBtn]::GetWindowRect($h, [ref]$r) | Out-Null
  Write-Host ("$label : iconic=" + [WinBtn]::IsIconic($h) + " zoomed=" + [WinBtn]::IsZoomed($h) +
    " visible=" + [WinBtn]::IsWindowVisible($h) + " rect=" + $r.L + "," + $r.T + " " + ($r.R-$r.L) + "x" + ($r.B-$r.T))
  return $r
}

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Start-Process 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe' -ArgumentList '--applywall=C:\pwallpaper\test-videos\sample.mp4'
Start-Sleep 9

$r = State 'start'
if ($r -eq $null) { exit 1 }
$y = $r.T + 22
$xMin = $r.R - 109
$xMax = $r.R - 67
$xClose = $r.R - 23

# 1. Minimize
[WinBtn]::Click($xMin, $y); Start-Sleep 1
State 'after-minimize-click' | Out-Null
$h = (Get-Main).MainWindowHandle
[WinBtn]::ShowWindow($h, 9) | Out-Null; Start-Sleep 1
State 'after-restore' | Out-Null

# Button positions move when the window changes size, so recompute per click.
function ButtonX([int]$fromRight) { $c = State 'pos'; return @(($c.R - $fromRight), ($c.T + 22)) }

# 2. Maximize, then restore
$pos = ButtonX 67
[WinBtn]::Click($pos[0], $pos[1]); Start-Sleep 1
State 'after-maximize-click' | Out-Null
$pos = ButtonX 67
[WinBtn]::Click($pos[0], $pos[1]); Start-Sleep 1
State 'after-restore-click' | Out-Null

# 3. Close (should hide to tray when tray icon is enabled)
$pos = ButtonX 23
[WinBtn]::Click($pos[0], $pos[1]); Start-Sleep 2
$p = Get-Main
Write-Host ("after-close-click : process-alive=" + [bool](Get-Process VulcansLivingWP -ErrorAction SilentlyContinue) +
  " main-window-present=" + [bool]$p)
if ($p) { State 'after-close-state' | Out-Null }

# Wallpaper must still be attached to the desktop layer after all the state changes.
$tree = (& "$PSScriptRoot\desk-tree.ps1" *>&1) | Out-String
Write-Host ("wallpaper-attached-under-workerw=" + ($tree -match 'class=HwndWrapper\[(FluxWall|VulcansLivingWP)'))
Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
