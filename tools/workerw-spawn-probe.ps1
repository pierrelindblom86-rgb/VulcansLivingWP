# Diagnostic: replays one WM_SPAWNWORKER variant and reports whether Explorer then
# exposes the icons-host WorkerW. Used to decide which variant/retry the app needs.
param(
    [ValidateSet("none", "zero", "0xd")] [string]$Variant = "none",
    [int]$WaitMs = 1500
)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class SpawnProbe2
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    public static string Report()
    {
        var sb = new StringBuilder();
        int iconsHost = 0, emptyHost = 0, hiddenWorker = 0;
        EnumWindows((h, l) =>
        {
            if (Cls(h) != "WorkerW") return true;
            bool def = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
            RECT r; GetWindowRect(h, out r);
            bool big = (r.R - r.L) >= 1000;
            if (def) iconsHost++;
            else if (IsWindowVisible(h) && big) emptyHost++;
            else hiddenWorker++;
            return true;
        }, IntPtr.Zero);

        IntPtr progman = FindWindow("Progman", null);
        bool defInProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        sb.AppendFormat("WorkerW(icons)={0} WorkerW(empty,visible)={1} WorkerW(other)={2} DefViewInProgman={3}\n",
            iconsHost, emptyHost, hiddenWorker, defInProgman);
        return sb.ToString();
    }

    public static string Send(int variant)
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr res;
        if (variant == 0) return "0/0 -> " + SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 2000, out res).ToInt64();
        return "0xD/0x1 -> " + SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 2000, out res).ToInt64();
    }
}
'@

"before: " + [SpawnProbe2]::Report().Trim()
if ($Variant -eq "zero") { [SpawnProbe2]::Send(0) }
elseif ($Variant -eq "0xd") { [SpawnProbe2]::Send(1) }
Start-Sleep -Milliseconds $WaitMs
"after ${WaitMs}ms: " + [SpawnProbe2]::Report().Trim()
