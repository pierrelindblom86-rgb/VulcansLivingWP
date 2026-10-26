using System;
using System.Runtime.InteropServices;
using System.Threading;
using FluxWall.Common;

namespace FluxWall.Engine
{
    /// <summary>
    /// The single component that touches the sensitive WorkerW/Progman
    /// technique. Isolated here so failures can be detected and recovered from
    /// (Explorer restarts, different Windows builds, shell quirks).
    ///
    /// 1.2.2 notes (root cause of the "wallpaper disappears" bug):
    ///  - Explorer answers only the 0x052C / 0xD / 0x1 form of the spawn
    ///    request on current Windows 10 builds; the legacy 0/0 form is a no-op
    ///    (verified on 10.0.19044.7807). Sending only 0/0 left the desktop
    ///    without a wallpaper layer, so the old code fell back to Progman.
    ///  - Explorer creates that layer asynchronously, so a single enumeration
    ///    right after the send races it. We now poll for a short, bounded time.
    ///  - Progman is never a valid wallpaper host while SHELLDLL_DefView lives
    ///    in it: a child pinned at the bottom of Progman sits *below* the
    ///    opaque icon view, i.e. the video is invisible. We refuse that host
    ///    instead of reporting a wallpaper that nobody can see.
    /// </summary>
    public static class WorkerWInjector
    {
        const uint WM_SPAWNWORKER = 0x052C;

        /// <summary>How long we let Explorer create/move the wallpaper layer.</summary>
        const int SpawnTimeoutMs = 2000;
        const int HostPollAttempts = 12;
        const int HostPollDelayMs = 60;

        /// <summary>
        /// Returns a window suitable for hosting wallpaper child windows —
        /// creating it if Explorer has not done so already.
        /// Returns IntPtr.Zero when the current shell layout has no usable
        /// wallpaper layer (the caller must then not cover the desktop).
        /// </summary>
        public static IntPtr GetWallpaperHost()
        {
            var progman = Native.FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                Logger.Warn("Progman not found — Explorer may not be running.");
                return IntPtr.Zero;
            }

            // Fast path: the layer may already exist (another run, another app,
            // or a shell that kept it alive across a display change).
            var host = FindWallpaperHost();
            if (host != IntPtr.Zero)
            {
                Logger.Info("Wallpaper layer ready: " + Describe(host) + ".");
                return host;
            }

            // Ask Explorer to create the layer. Both variants are sent: the
            // 0xD/0x1 form is the one current builds answer, the legacy 0/0
            // form keeps older builds working, and a duplicate request simply
            // reuses the existing layer.
            SpawnLayer(progman, new IntPtr(0xD), new IntPtr(0x1));
            SpawnLayer(progman, IntPtr.Zero, IntPtr.Zero);

            for (int i = 0; i < HostPollAttempts; i++)
            {
                if (i > 0) Thread.Sleep(HostPollDelayMs);
                host = FindWallpaperHost();
                if (host != IntPtr.Zero)
                {
                    Logger.Info("Wallpaper layer created: " + Describe(host) + ".");
                    return host;
                }
            }

            Logger.Warn("No WorkerW wallpaper layer appeared; the desktop host is unavailable. " +
                        "The wallpaper is kept hidden instead of covering the desktop icons.");
            return IntPtr.Zero;
        }

        static void SpawnLayer(IntPtr progman, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                IntPtr result;
                Native.SendMessageTimeout(progman, WM_SPAWNWORKER, wParam, lParam,
                    Native.SMTO_NORMAL, SpawnTimeoutMs, out result);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("WM_SPAWNWORKER failed", e));
            }
        }

        /// <summary>
        /// Finds the empty WorkerW that sits directly below the icon host in
        /// z-order. Works for both known shell layouts:
        ///   top level:  WorkerW(icons) then WorkerW(wallpaper)
        ///   child:      Progman > WorkerW(icons), WorkerW(wallpaper)
        /// </summary>
        static IntPtr FindWallpaperHost()
        {
            var defView = FindDefView();
            if (defView == IntPtr.Zero) return IntPtr.Zero;

            var iconsHost = Native.GetAncestor(defView, Native.GA_PARENT);
            if (iconsHost == IntPtr.Zero) iconsHost = Native.GetParent(defView);
            if (iconsHost == IntPtr.Zero) return IntPtr.Zero;

            var iconsHostClass = Native.WindowClass(iconsHost);
            if (iconsHostClass != "WorkerW")
            {
                // SHELLDLL_DefView lives directly in Progman: no wallpaper slot
                // exists yet. Progman cannot host a visible wallpaper (see the
                // class remarks), so report "no host" and let the caller retry.
                return IntPtr.Zero;
            }

            var iconsHostParent = Native.GetAncestor(iconsHost, Native.GA_PARENT);
            var below = Native.FindWindowEx(iconsHostParent, iconsHost, "WorkerW", null);
            if (below == IntPtr.Zero || below == iconsHost)
            {
                Logger.Warn("WorkerW icon host has no wallpaper layer below it; retrying later.");
                return IntPtr.Zero;
            }
            return below;
        }

        /// <summary>The desktop icon view (a child window, never top-level).</summary>
        static IntPtr FindDefView()
        {
            var progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                var direct = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (direct != IntPtr.Zero) return direct;

                var nested = FindDescendant(progman, "SHELLDLL_DefView");
                if (nested != IntPtr.Zero) return nested;
            }

            // Spawned layout: the icon host lives next to Progman, not inside it.
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, lparam) =>
            {
                if (Native.WindowClass(hwnd) != "WorkerW") return true;
                var dv = Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv == IntPtr.Zero) return true;
                found = dv;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static IntPtr FindDescendant(IntPtr root, string className)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumChildWindows(root, (hwnd, lparam) =>
            {
                if (Native.WindowClass(hwnd) != className) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>True when the handle is a live WorkerW we may parent into.</summary>
        public static bool IsUsableHost(IntPtr host)
        {
            try
            {
                if (host == IntPtr.Zero || !Native.IsWindow(host)) return false;
                if (Native.WindowClass(host) != "WorkerW") return false;
                var progman = Native.FindWindow("Progman", null);
                return host != progman;
            }
            catch { return false; }
        }

        /// <summary>Short human-readable host description for the log.</summary>
        public static string Describe(IntPtr host)
        {
            try
            {
                Native.RECT r;
                Native.GetWindowRect(host, out r);
                return string.Format("WorkerW 0x{0:X} visible={1} rect={2},{3} {4}x{5}",
                    host.ToInt64(), Native.IsWindowVisible(host),
                    r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch { return "WorkerW 0x" + host.ToInt64().ToString("X"); }
        }

        /// <summary>
        /// Detects whether <paramref name="ourHwnd"/> lost its injected parent
        /// (Explorer restarted). Cheap; call on a low-frequency timer.
        /// NOTE: our windows keep WS_POPUP, so GetParent reports the OWNER.
        /// GetAncestor(GA_PARENT) is required to see the real parenting.
        /// </summary>
        public static bool InjectionLost(IntPtr ourHwnd, IntPtr expectedParent)
        {
            try
            {
                if (expectedParent == IntPtr.Zero) return false;
                if (!Native.IsWindow(ourHwnd)) return true;   // destroyed entirely
                if (!Native.IsWindow(expectedParent)) return true;
                return Native.GetAncestor(ourHwnd, Native.GA_PARENT) != expectedParent;
            }
            catch { return false; }
        }

        /// <summary>Reapplies parent + bottom z-order after a shell event.</summary>
        public static bool ReinjectionRepair(IntPtr ourHwnd, IntPtr host)
        {
            try
            {
                if (ourHwnd == IntPtr.Zero || host == IntPtr.Zero) return false;
                if (!Native.IsWindow(ourHwnd) || !Native.IsWindow(host)) return false;
                if (!Native.SetParent(ourHwnd, host))
                {
                    Logger.Warn("SetParent failed during re-injection (error " +
                                Marshal.GetLastWin32Error() + ").");
                    return false;
                }
                return Native.GetAncestor(ourHwnd, Native.GA_PARENT) == host;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }
    }
}
