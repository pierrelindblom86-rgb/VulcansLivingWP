# Dumps the Windows desktop window hierarchy (Progman / WorkerW / SHELLDLL_DefView)
# so wallpaper-host problems can be diagnosed without attaching a debugger.
# Read-only: it never sends WM_SPAWNWORKER or reparents anything.

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopProbe
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint rel);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    public static List<string> Dump()
    {
        var lines = new List<string>();
        IntPtr progman = FindWindow("Progman", null);
        lines.Add("Progman = 0x" + progman.ToInt64().ToString("X") +
                  (progman == IntPtr.Zero ? " (MISSING)" : ""));

        int topWorkerW = 0, defViewUnderWorkerW = 0;
        var tops = new List<IntPtr>();
        EnumWindows((h, l) => { tops.Add(h); return true; }, IntPtr.Zero);
        lines.Add("--- top-level windows in z-order ---");
        foreach (var h in tops)
        {
            string c = Cls(h);
            if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd")
            {
                bool hasDefView = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
                if (c == "WorkerW") { topWorkerW++; if (hasDefView) defViewUnderWorkerW++; }
                lines.Add(string.Format("  {0,-14} 0x{1:X10} visible={2} defview-child={3}",
                    c, h.ToInt64(), IsWindowVisible(h), hasDefView));
            }
        }
        lines.Add("top-level WorkerW=" + topWorkerW + " with DefView=" + defViewUnderWorkerW);

        if (progman != IntPtr.Zero)
        {
            lines.Add("--- Progman children (z-order) ---");
            var kids = new List<IntPtr>();
            EnumChildWindows(progman, (h, l) => { kids.Add(h); return true; }, IntPtr.Zero);
            foreach (var h in kids)
            {
                RECT wr, cr; GetWindowRect(h, out wr); GetClientRect(h, out cr);
                uint pid; GetWindowThreadProcessId(h, out pid);
                lines.Add(string.Format("  {0,-16} 0x{1:X10} parent=0x{2:X8} visible={3} pid={4} " +
                          "win={5},{6} {7}x{8} client={9}x{10}",
                    Cls(h), h.ToInt64(), GetParent(h).ToInt64(), IsWindowVisible(h), pid,
                    wr.L, wr.T, wr.R - wr.L, wr.B - wr.T, cr.R - cr.L, cr.B - cr.T));
            }
        }

        IntPtr defView = FindWindow("SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero && progman != IntPtr.Zero)
            defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        lines.Add("SHELLDLL_DefView = 0x" + defView.ToInt64().ToString("X"));
        if (defView != IntPtr.Zero)
        {
            IntPtr parent = GetParent(defView);      // real parent for any style
            IntPtr anc = GetAncestor(defView, 1);    // GA_PARENT
            lines.Add("  parent=0x" + parent.ToInt64().ToString("X") + " (" + Cls(parent) + ")" +
                      "  GA_PARENT=0x" + anc.ToInt64().ToString("X") + " (" + Cls(anc) + ")");
            IntPtr below = GetWindow(parent, 2 /* GW_HWNDNEXT */);
            if (below != IntPtr.Zero)
                lines.Add("  next sibling below parent: 0x" + below.ToInt64().ToString("X") + " (" + Cls(below) + ")");
        }
        return lines;
    }
}
'@

[DesktopProbe]::Dump() | ForEach-Object { $_ }
