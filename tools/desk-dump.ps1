Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
'@

$pm = [W]::FindWindow('Progman', $null)
Write-Host ("Progman=" + $pm + " vis=" + [W]::IsWindowVisible($pm))
$dv = [W]::FindWindowEx($pm, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
Write-Host ("  Progman DefView=" + $dv)

[W]::EnumWindows({ param($h, $l)
  $sb = New-Object System.Text.StringBuilder 256
  [W]::GetClassName($h, $sb, 256) | Out-Null
  $cls = $sb.ToString()
  if ($cls -eq 'WorkerW' -or $cls.Contains('FluxWall')) {
    $dv2 = [W]::FindWindowEx($h, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
    $p = [W]::GetAncestor($h, 1)
    $vis = [W]::IsWindowVisible($h)
    $t = New-Object System.Text.StringBuilder 128
    [W]::GetWindowText($h, $t, 128) | Out-Null
    $r = New-Object W+RECT
    [W]::GetWindowRect($h, [ref]$r) | Out-Null
    Write-Host ("hwnd=" + $h + " class=" + $cls + " vis=" + $vis + " parent=" + $p +
      " hasDefView=" + ($dv2 -ne [IntPtr]::Zero) + " title='" + $t + "'" +
      " rect=" + $r.L + "," + $r.T + " " + ($r.R - $r.L) + "x" + ($r.B - $r.T))
  }
  return $true
}, [IntPtr]::Zero) | Out-Null
