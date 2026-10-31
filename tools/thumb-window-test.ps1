$ErrorActionPreference = 'Continue'
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinScan {
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static List<string> ForPid(uint target) {
    var found = new List<string>();
    EnumWindows((h, p) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == target && IsWindowVisible(h)) {
        var sb = new StringBuilder(256); GetClassName(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        found.Add(sb.ToString() + " " + (r.R-r.L) + "x" + (r.B-r.T));
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$thumbs = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\thumbs'
$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'

Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Get-ChildItem $thumbs -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host ("thumbs_before=" + (@(Get-ChildItem $thumbs -Filter *.png -ErrorAction SilentlyContinue)).Count)

$proc = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + 'C:\pwallpaper\test-videos\sample.mp4') -PassThru

$maxWindows = 0
$windowSets = @{}
for ($i = 0; $i -lt 70; $i++) {
    Start-Sleep -Milliseconds 150
    $wins = [WinScan]::ForPid([uint32]$proc.Id)
    if ($wins.Count -gt $maxWindows) { $maxWindows = $wins.Count }
    foreach ($w in $wins) { if (-not $windowSets.ContainsKey($w)) { $windowSets[$w] = 0 }; $windowSets[$w]++ }
}

$pngs = @(Get-ChildItem $thumbs -Filter *.png -ErrorAction SilentlyContinue)
Write-Host ("max_visible_top_level_windows=" + $maxWindows)
foreach ($k in ($windowSets.Keys | Sort-Object)) { Write-Host ("  window=" + $k + " samples=" + $windowSets[$k]) }
Write-Host ("thumbs_after=" + $pngs.Count)
foreach ($p in $pngs) { Write-Host ("  thumb=" + $p.Name + " bytes=" + $p.Length) }

$log = Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if ($log) {
    Write-Host '--- log tail ---'
    Get-Content $log.FullName -Tail 20
}
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
