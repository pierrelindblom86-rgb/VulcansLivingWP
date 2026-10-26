using System;
using System.Diagnostics;
using System.Threading;

namespace FluxWall.Common
{
    /// <summary>
    /// Tiny named-event channel that lets the installer/uninstaller ask a running
    /// instance to exit without starting the normal UI.
    ///
    /// The uninstaller used to run <c>VulcansLivingWP.exe --stopwall</c>, which is
    /// just the normal app: it opened the main window (and, when an instance was
    /// already running, only showed the "already running" message box). Both are
    /// wrong during uninstall. <c>--shutdown</c> is handled before the UI exists,
    /// signals this event, and waits for the single-instance mutex to be released.
    /// </summary>
    public static class ShutdownSignal
    {
        public const string MutexName = "VulcansLivingWP-SingleInstance";
        public const string EventName = "VulcansLivingWP-Shutdown";

        /// <summary>
        /// Asks any running instance to exit and waits until it has.
        /// Returns 0 when nothing is left running, or 3 on timeout.
        /// </summary>
        public static int RequestAndWait(int timeoutMs)
        {
            if (!IsRunning()) return 0;

            try
            {
                EventWaitHandle existing;
                if (EventWaitHandle.TryOpenExisting(EventName, out existing))
                {
                    using (existing) existing.Set();
                }
                else
                {
                    Logger.Info("Shutdown requested, but the running instance has no shutdown channel (older build).");
                }
            }
            catch (Exception e)
            {
                Logger.Info("Could not signal shutdown: " + e.Message);
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!IsRunning()) return 0;
                Thread.Sleep(150);
            }
            return 3;
        }

        /// <summary>True while another process owns the single-instance mutex.</summary>
        public static bool IsRunning()
        {
            bool createdNew;
            try
            {
                using (var probe = new Mutex(true, MutexName, out createdNew))
                {
                    if (createdNew)
                    {
                        try { probe.ReleaseMutex(); } catch { }
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                // If the kernel object cannot be probed we cannot prove it is gone.
                return true;
            }
        }

        /// <summary>
        /// Subscribes <paramref name="onShutdown"/> to the channel. The callback
        /// runs on a thread-pool thread, so callers must marshal to their UI.
        /// </summary>
        public static EventWaitHandle Listen(Action onShutdown)
        {
            try
            {
                var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                ThreadPool.RegisterWaitForSingleObject(
                    handle,
                    (state, timedOut) =>
                    {
                        if (timedOut) return;
                        try { onShutdown(); }
                        catch (Exception e) { Logger.Error(e); }
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
                return handle;
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Shutdown channel unavailable.", e));
                return null;
            }
        }
    }
}
