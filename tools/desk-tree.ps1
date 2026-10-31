Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class T {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  public struct RECT { public int L, T, R, B; }
}
'@

function Dump-Hwnd($h, $indent) {
  $sb = New-Object System.Text.StringBuilder 256
  [T]::GetClassName($h, $sb, 256) | Out-Null
  $t = New-Object System.Text.StringBuilder 128
  [T]::GetWindowText($h, $t, 128) | Out-Null
  $r = New-Object T+RECT
  [T]::GetWindowRect($h, [ref]$r) | Out-Null
  $pad = ' ' * $indent
  Write-Host ("$pad" + "hwnd=" + $h + " class=" + $sb + " vis=" + [T]::IsWindowVisible($h) +
    " title='" + $t + "' rect=" + $r.L + "," + $r.T + " " + ($r.R - $r.L) + "x" + ($r.B - $r.T))
}

[T]::EnumWindows({ param($h, $l)
  $sb = New-Object System.Text.StringBuilder 256
  [T]::GetClassName($h, $sb, 256) | Out-Null
  $cls = $sb.ToString()
  $r = New-Object T+RECT
  [T]::GetWindowRect($h, [ref]$r) | Out-Null
  if (($cls -eq 'WorkerW' -or $cls -eq 'Progman') -and ($r.R - $r.L) -ge 1000) {
    Dump-Hwnd $h 0
    [T]::EnumChildWindows($h, { param($c, $l2)
      Dump-Hwnd $c 4
      return $true
    }, [IntPtr]::Zero) | Out-Null
  }
  return $true
}, [IntPtr]::Zero) | Out-Null
