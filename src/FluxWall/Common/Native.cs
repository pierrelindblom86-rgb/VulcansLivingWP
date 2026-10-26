using System;
using System.Runtime.InteropServices;
using System.Text;

// ReSharper disable FieldCanBeMadeReadOnly.Global
namespace FluxWall.Common
{
    /// <summary>
    /// Minimal Win32 interop surface used by the wallpaper engine, fullscreen
    /// watcher and tray support. Every member is deliberately tiny — the
    /// version-sensitive desktop integration lives in Engine/, not here.
    /// </summary>
    public static class Native
    {
        #region Constants
        public const uint WM_USER = 0x0400;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_CLOSE = 0x0010;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_MINIMIZE = 0x20000000L;
        public const long WS_CHILD = 0x40000000L;

        public const uint WS_EX_TOOLWINDOW = 0x00000080u;
        public const uint WS_EX_TRANSPARENT = 0x00000020u;   // click-through
        public const uint WS_EX_NOACTIVATE = 0x08000000u;
        public const uint WS_EX_TOPMOST = 0x00000008u;
        public const uint SWP_FRAMECHANGED = 0x0020u;
        public const uint SWP_SHOWWINDOW = 0x0040u;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        public const int MONITOR_DEFAULTTONEAREST = 2;

        public const int EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const int WINEVENT_OUTOFCONTEXT = 0x0000;
        public const int WINEVENT_SKIPOWNPROCESS = 0x0002;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;

        public const uint SMTO_NORMAL = 0x0000;

        public const int DWMWA_CLOAKED = 14;

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        #endregion

        #region Basic window APIs
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SendMessageTimeout(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint relation);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        public const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetParent(IntPtr child, IntPtr parent);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Style bits are 32-bit even on x64. Declaring them as long made the
        // marshaller read the full 64-bit return register and could smuggle
        // undefined high bits into the extended style.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        #endregion

        #region Monitors
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clipRect,
            MonitorEnumProc proc, IntPtr data);
        #endregion

        #region WinEvent / DPI / DWM
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(int min, int max, IntPtr module,
            WinEventProc proc, uint pid, uint thread, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr,
            out int value, int size);
        #endregion

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string message);

        /// <summary>Current real HWND of a WPF window (0 on failure).</summary>
        public static IntPtr GetWindowLive(System.Windows.Window w)
        {
            try { return new System.Windows.Interop.WindowInteropHelper(w).Handle; }
            catch { return IntPtr.Zero; }
        }

        #region Hotkeys
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        #endregion

        /// <summary>Returns the window title, never throws.</summary>
        public static string WindowTitle(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                return sb.ToString();
            }
            catch { return ""; }
        }

        public static string WindowClass(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                GetClassName(hwnd, sb, 256);
                return sb.ToString();
            }
            catch { return ""; }
        }
    }
}
