using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using FluxWall.Common;
using FluxWall.Engine;

namespace FluxWall
{
    /// <summary>
    /// Detects fullscreen applications reliably and event-driven:
    ///  - a WinEventHook fires only on foreground changes (no hot polling);
    ///  - a slow 2s fallback check covers apps which change geometry without
    ///    re-activating (rare, but costs almost nothing);
    ///  - a window counts as fullscreen when it covers its monitor completely
    ///    and lacks window chrome (distinguishes it from maximized windows).
    /// Our own windows are always ignored.
    /// </summary>
    public class FullScreenWatcher : IDisposable
    {
        readonly Func<IEnumerable<Engine.WallpaperWindow>> _ownWindows;
        IntPtr _hook;
        // Must stay referenced for the hook's lifetime: if the GC collects a
        // delegate that the OS still holds, the next foreground change crashes.
        Native.WinEventProc _winEventProc;
        System.Windows.Threading.DispatcherTimer _fallback;
        public bool Enabled { get; set; } = true;

        /// <summary>Raised only when the state actually flips. Non-UI thread!</summary>
        public event Action<bool, string> FullscreenChanged;

        public IReadOnlyList<string> LastForeground { get { return _lastNames; } }

        readonly object _gate = new object();
        bool _fsActive;
        string _fsWindowName;
        readonly List<string> _lastNames = new List<string>();

        public FullScreenWatcher(Func<IEnumerable<Engine.WallpaperWindow>> ownWindows)
        {
            _ownWindows = ownWindows ?? (Func<IEnumerable<Engine.WallpaperWindow>>)(() => new Engine.WallpaperWindow[0]);
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _winEventProc = OnWinEvent;
            _hook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            var hookOk = _hook != IntPtr.Zero;
            if (!hookOk)
                Logger.Warn("SetWinEventHook failed; falling back to timer-only detection.");

            // The WinEvent hook covers foreground changes. The fallback timer only
            // catches the rare case where a window resizes into fullscreen without
            // re-activating. Poll at 10s when the hook works, 2s when it doesn't.
            _fallback = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background);
            _fallback.Interval = hookOk
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromSeconds(2);
            _fallback.Tick += (s, e) => { Evaluate(); };
            _fallback.Start();
        }

        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            // WinEvent callbacks arrive on the installing thread only when the
            // hook is created with WINEVENT_OUTOFCONTEXT and the thread pumps
            // messages (the WPF dispatcher does). Cheap by design.
            // Exceptions must never escape into native code.
            try
            {
                if (idObject != 0 /* OBJID_WINDOW */) return;
                Evaluate();
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Foreground callback failed", e));
            }
        }

        /// <summary>Forces one check right now (used after settings changes etc.).</summary>
        public void Evaluate()
        {
            if (!Enabled) { SetState(false, null); return; }
            try
            {
                var fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || !Native.IsWindow(fg)) { SetState(false, null); return; }

                if (IsOurOwnWindow(fg) ||
                    Native.WindowClass(fg) == "Shell_TrayWnd" ||
                    Native.WindowClass(fg) == "WorkerW" ||
                    Native.WindowClass(fg) == "Progman")
                { SetState(false, null); return; }

                // Cloaked UWP windows (hidden Modern Apps,Courtus overlays) don't
                // render, so they are never a fullscreen app for our purposes.
                if (IsDwmCloaked(fg)) { SetState(false, null); return; }

                if (!IsFullscreenShape(fg)) { SetState(false, null); return; }

                SetState(true, Native.WindowTitle(fg));
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Fullscreen evaluation failed", e));
            }
        }

        bool IsOurOwnWindow(IntPtr hwnd)
        {
            try
            {
                var own = _ownWindows();
                foreach (var w in own)
                    if (w.Handle == hwnd) return true;
            }
            catch { }
            return false;
        }

        static bool IsFullscreenShape(IntPtr hwnd)
        {
            // NOTE: many borderless/Maximized-style windows (WPF, some games,
            // Electron) STILL carry WS_CAPTION/WS_THICKFRAME style bits, so the
            // style is not a reliable discriminator. The real discriminator is
            // geometry: a truly fullscreen window covers the full physical
            // monitor — including the taskbar strip. An ordinary maximized
            // window only covers the work area (taskbar stays visible).
            var rect = new Native.RECT();
            if (!Native.GetWindowRect(hwnd, out rect)) return false;

            var hmon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
            if (hmon == IntPtr.Zero) return false;

            var miex = new Native.MONITORINFOEX();
            miex.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (!Native.GetMonitorInfo(hmon, ref miex)) return false;

            bool coversMonitor =
                rect.Left <= miex.rcMonitor.Left && rect.Right >= miex.rcMonitor.Right &&
                rect.Top <= miex.rcMonitor.Top && rect.Bottom >= miex.rcMonitor.Bottom;

            bool coversWorkAreaOnly =
                rect.Left <= miex.rcWork.Left && rect.Right >= miex.rcWork.Right &&
                rect.Top <= miex.rcWork.Top && rect.Bottom >= miex.rcWork.Bottom &&
                (rect.Bottom <= miex.rcWork.Bottom + 2 || rect.Top >= miex.rcWork.Top - 2) &&
                !coversMonitor;
            if (coversWorkAreaOnly) return false;   // maximized, not fullscreen

            // Covering beyond the work area (taskbar zone) while also covering
            // the monitor counts as fullscreen, chrome bits or not.
            return coversMonitor;
        }

        static bool GetImportantStyle(IntPtr hwnd, out int style)
        {
            try
            {
                // GWL_STYLE uses 32-bit storage even on x64, so GetWindowLong is fine.
                style = (int)Native.GetWindowLong(hwnd, Native.GWL_STYLE);
                return true;
            }
            catch { style = 0; return false; }
        }

        static bool IsDwmCloaked(IntPtr hwnd)
        {
            try
            {
                int value;
                if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out value, 4) == 0 && value != 0)
                    return true;
            }
            catch { }
            return false;
        }

        public static bool IsDwmCloakedPublic(IntPtr hwnd) { return IsDwmCloaked(hwnd); }
        public static bool IsFullscreenShapePublic(IntPtr hwnd) { return IsFullscreenShape(hwnd); }

        void SetState(bool active, string title)
        {
            bool changed;
            string prev;
            lock (_gate)
            {
                changed = active != _fsActive;
                prev = _fsWindowName;
                _fsActive = active;
                _fsWindowName = title;
                lock (this) { _lastNames.Clear(); if (active && title != null) _lastNames.Add(title); }
            }
            if (changed)
            {
                Logger.Info(active
                    ? "Fullscreen activity detected: " + (title ?? "")
                    : "Fullscreen activity ended.");
                var h = FullscreenChanged;
                if (h != null) h(active, title ?? "");
            }
            else if (active && !string.Equals(title, prev, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("Fullscreen app changed: " + title);
            }
        }

        public void Dispose()
        {
            try { if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook); } catch { }
            _hook = IntPtr.Zero;
            _winEventProc = null;
            try { if (_fallback != null) _fallback.Stop(); } catch { }
        }
    }
}
