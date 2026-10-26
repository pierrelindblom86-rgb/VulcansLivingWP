using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.Library;
using FluxWall.Settings;
using FluxWall.UI;
using FluxWall.Visualizer;

namespace FluxWall
{
    /// <summary>
    /// The one object that owns every service and threads them together at
    /// startup. Created on the main (STA) thread.
    /// </summary>
    public class FluxWallApp
    {
        public LibraryStore Library { get; private set; }
        public ThumbnailService Thumbs { get; private set; }
        public WallpaperEngine Engine { get; private set; }
        public TrayService Tray { get; private set; }
        public MainWindow Main { get; private set; }
        public VisualizerService Visualizer { get; private set; }
        public AppSettings Settings { get { return SettingsStore.Current; } }

        /// <summary>The UI thread dispatcher (services marshal their callbacks here).</summary>
        public System.Windows.Threading.Dispatcher Dispatcher
        {
            get
            {
                return _app != null
                    ? _app.Dispatcher
                    : System.Windows.Threading.Dispatcher.CurrentDispatcher;
            }
        }

        Application _app;

        public HotkeyService Hotkeys { get; private set; }

        public void Run(string[] args)
        {
            _app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown, // tray keeps us alive
            };
            _app.DispatcherUnhandledException += (s, e) =>
            {
                Logger.Error(e.Exception);
                MessageBox.Show(
                    "Something went wrong inside VulcansLivingWP:\n" +
                    (e.Exception.Message ?? "") +
                    "\n\nIt has been logged. You can find the log in the app's \"About\" tab.",
                    "VulcansLivingWP", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            // Log crashes from any thread (background workers, native callbacks)
            // so an unhandled exception leaves a stack trace in the log.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error(ex ?? new Exception("Unhandled non-exception object"));
            };

            Library = new LibraryStore();
            Library.Load();

            Thumbs = new ThumbnailService(Library);

            Engine = new WallpaperEngine(Library, Settings);
            Engine.PlaybackFailed += OnPlaybackFailed;

            // Optional audio visualiser. Created before the UI so the settings page
            // and tray can talk to it; nothing is captured until it is enabled.
            Visualizer = new VisualizerService(this);
            Engine.Watcher.FullscreenChanged += (active, title) =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() => Visualizer?.SetFullscreenActive(active)),
                        System.Windows.Threading.DispatcherPriority.Normal);
                }
                catch (Exception e) { Logger.Error(e); }
            };

            Tray = new TrayService(this);

            Main = new MainWindow(this);
            Main.Init(this);

            Hotkeys = new HotkeyService(this);

            // Lets the installer/uninstaller stop us without opening any window.
            ShutdownSignal.Listen(() =>
            {
                try
                {
                    Logger.Info("Shutdown requested by the installer; exiting cleanly.");
                    _app.Dispatcher.BeginInvoke(new Action(ExitClean));
                }
                catch (Exception e) { Logger.Error(e); }
            });

            ApplyStartWithWindows();
            ApplyHotkey();
            ApplyLibraryStateFromSettings();

            // Auto-restore the wallpaper the user last applied, but skip
            // when --applywall already set one (avoids a double apply).
            bool hasApplyWallArg = Array.Exists(args ?? new string[0],
                a => a != null && a.StartsWith("--applywall=", StringComparison.OrdinalIgnoreCase));
            if (!hasApplyWallArg && Settings.RestoreLastWallpaper && !string.IsNullOrEmpty(Settings.ActiveWallpaperId))
            {
                var item = Library.Find(Settings.ActiveWallpaperId);
                if (item != null)
                {
                    // In independent mode the saved per-monitor assignments are the
                    // source of truth. Applying the "last wallpaper" here would
                    // assign it to the primary monitor, so a restart silently
                    // replaced that screen's own choice. Restore the saved layout
                    // instead; every other mode activates the one global video.
                    if (Settings.DisplayMode == DisplayMode.Independent)
                        Engine.ApplyCurrentLayout();
                    else
                        Engine.Apply(item);
                }
                else
                    Logger.Warn("Saved wallpaper " + Settings.ActiveWallpaperId + " no longer exists in the library.");
            }

            Engine.Start();
            Visualizer.Apply();
            Thumbs.RequestAll();
            Main.LibraryPage.RefreshCards();
            Main.UpdateStateChip();

            // Optional GitHub update check. Delayed so it can never delay the
            // first frame of the wallpaper or the window appearing, and rate
            // limited to once every 12 hours by UpdateService itself. It runs
            // even when checks are switched off: the service then reports the
            // "disabled" state without touching the network, which is how the
            // About card stops waiting for an answer that would never come.
            var updateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8),
            };
            updateTimer.Tick += (s, e) =>
            {
                updateTimer.Stop();
                try { Updates.UpdateService.StartupCheck(null); }
                catch (Exception ex) { Logger.Error(ex); }
            };
            updateTimer.Start();

            if (!Settings.FirstRunDone)
            {
                try
                {
                    Tray?.ShowFirstRunBalloon();
                    Settings.FirstRunDone = true;
                    SettingsStore.Save();
                }
                catch { }
            }

            // --stopwall is a silent one-shot command: remove the wallpaper and
            // exit. It must run before the window is shown, otherwise the window
            // flashes for anyone (or any script) using it.
            if (Array.Exists(args ?? new string[0], a => a != null &&
                a.Equals("--stopwall", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Logger.Info("Stop requested; removing wallpaper and exiting.");
                    Visualizer?.Dispose();
                    Engine?.StopAll();
                    Engine?.Dispose();
                    Tray?.Dispose();
                    Hotkeys?.Dispose();
                }
                finally
                {
                    Environment.Exit(0);
                }
            }

            bool minimized = Array.IndexOf(args, "--tray") >= 0 || Array.IndexOf(args, "--minimized") >= 0;
            if (!(minimized && !Settings.FirstRunDone) && !(minimized && Settings.StartMinimized))
                _DispatcherShowMain(minimized);
            else if (minimized)
                Logger.Info("--minimized passed; keeping window hidden.");

            ApplyCommandLine(args);

            _app.Run();
        }

        /// <summary>
        /// Optional automation switches (documented in README):
        ///   --applywall=path   import (if needed) + apply the video, then run
        ///   --stopwall         remove the active wallpaper and exit (no window)
        ///   --shutdown         ask a running instance to exit (used by Setup)
        ///   --autostop=secs    exit cleanly after N seconds (for scripted tests)
        /// </summary>
        public void ApplyCommandLine(string[] args)
        {
            try
            {
                Common.Logger.Info("ApplyCommandLine: " +
                    (args == null ? "(null)" : string.Join(" | ", args)));
                string apply = null;
                bool stop = false;
                int autoStop = 0;
                int probeDelay = 0;
                bool? testSuspend = null;
                int testSuspendClear = 0;
                foreach (var raw in args ?? new string[0])
                {
                    var a = raw.ToLowerInvariant();
                    if (a.StartsWith("--applywall=")) apply = raw.Substring(12);
                    else if (a == "--stopwall") stop = true;
                    else if (a.StartsWith("--testsuspend="))
                        testSuspend = a.Substring(14).Trim() == "on";
                    else if (a.StartsWith("--testsuspendclear="))
                        int.TryParse(a.Substring(19), out testSuspendClear);
                    else if (a.StartsWith("--autostop="))
                        int.TryParse(a.Substring(11), out autoStop);
                    else if (a.StartsWith("--probe="))
                        int.TryParse(a.Substring(8), out probeDelay);
                }
                if (probeDelay > 0 && apply != null)
                {
                    Common.Logger.Info("Probe timer armed for t+" + probeDelay + "s.");
                    var t = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(probeDelay) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        Common.Logger.Info("Probe timer fired.");
                        RunSelfProbe();
                    };
                    t.Start();
                }
                if (testSuspend.HasValue)
                {
                    Common.Logger.Info("Test hook: pretending the screen is " +
                        (testSuspend.Value ? "unavailable" : "available") + ".");
                    Engine.Screen.ForceUnavailable(testSuspend.Value);
                }
                if (testSuspendClear > 0)
                {
                    // Lets a script verify the pause AND the resume of the very
                    // same session, which is the part that touches the decoder.
                    var t = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(testSuspendClear) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        Common.Logger.Info("Test hook: screen available again.");
                        Engine.Screen.ForceUnavailable(false);
                    };
                    t.Start();
                }
                if (apply != null)
                {
                    var added = Library.AddFiles(new[] { apply });
                    string fullApply = apply;
                    try { fullApply = System.IO.Path.GetFullPath(apply); } catch { }
                    var item = added.Count > 0 ? added[0] :
                               System.Linq.Enumerable.FirstOrDefault(Library.Items, i =>
                                   string.Equals(i.FilePath, fullApply, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        Engine.Apply(item);
                    }
                }
                if (stop) Engine.StopAll();
                if (autoStop > 0)
                {
                    var t = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(autoStop),
                    };
                    t.Tick += (s, e) => { t.Stop(); Logger.Info("--autostop reached; exiting cleanly."); ExitClean(); };
                    t.Start();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        void _DispatcherShowMain(bool minimized)
        {
            ShowMainWindow();
        }

        /// <summary>
        /// Captures the live wallpaper window from inside the process (PrintWindow
        /// with PW_RENDERFULLCONTENT), plus a plain screen grab for comparison.
        /// Used by the automated test suite; writes PNGs under the app folder.
        /// </summary>
        void RunSelfProbe()
        {
            Common.Logger.Info("RunSelfProbe entered.");
            try
            {
                // Hide the main window so the bare wallpaper layer is exposed.
                Main?.TryHide();

                // Let DWM settle for a beat.
                var settle = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
                settle.Tick += (s2, e2) =>
                {
                    settle.Stop();
                    try { ProbeCapture(); } catch (Exception ex) { Common.Logger.Error(ex); }
                };
                settle.Start();
            }
            catch (Exception e)
            {
                Common.Logger.Error(new Exception("Self-probe failed", e));
            }
        }

        void ProbeCapture()
        {
            try
            {
                foreach (var w in Engine.WindowsSnapshot)
                {
                    var hwnd = w.Handle;
                    if (hwnd == IntPtr.Zero || w.Window == null) continue;

                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();

                    // 1. PrintWindow the wallpaper window itself
                    Native.GetWindowRect(hwnd, out var r);
                    int pw = r.Right - r.Left, ph = r.Bottom - r.Top;
                    if (pw > 0 && ph > 0)
                    {
                        using (var bmp = new System.Drawing.Bitmap(pw, ph))
 using (var g = System.Drawing.Graphics.FromImage(bmp))
                        {
                            var hdc = g.GetHdc();
                            bool ok = CaptureNative.PrintWindowWithContent(hwnd, hdc, 2);
                            g.ReleaseHdc(hdc);
                            var f1 = System.IO.Path.Combine(Common.Paths.Root, "probe-printwindow.png");
                            bmp.Save(f1, System.Drawing.Imaging.ImageFormat.Png);
                            Common.Logger.Info("PROBE PrintWindow ok=" + ok + " size=" +
                                new System.IO.FileInfo(f1).Length + " rect=" + pw + "x" + ph + " took " + sw.ElapsedMilliseconds + "ms");
                        }
                    }

                    // 2. Raw screen copy of the monitor region (what the user sees)
                    using (var bmp2 = new System.Drawing.Bitmap(pw, ph))
 using (var g2 = System.Drawing.Graphics.FromImage(bmp2))
                    {
                        g2.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(pw, ph));
                        var f2 = System.IO.Path.Combine(Common.Paths.Root, "probe-screen.png");
                        bmp2.Save(f2, System.Drawing.Imaging.ImageFormat.Png);
                        Common.Logger.Info("PROBE screen copy " + new System.IO.FileInfo(f2).Length + " bytes");
                    }
                }
            }
            catch (Exception e)
            {
                Common.Logger.Error(new Exception("Self-probe failed", e));
            }
        }

        #region Exposed services for UI
        public void ShowMainWindow()
        {
            try
            {
                Main.Show();
            }
            catch (Exception e) { Logger.Error(e); }
        }

        public void ExitClean()
        {
            try
            {
                // Stop capture first: it must never outlive the window or the app.
                Visualizer?.Dispose();
                Visualizer = null;
            }
            catch (Exception e) { Logger.Error(e); }
            try
            {
                SettingsStore.Save();
                Engine.StopAll();
                Engine.Dispose();
            }
            catch (Exception e) { Logger.Error(e); }
            try { Tray.Dispose(); } catch { }
            try { Hotkeys.Dispose(); } catch { }
            try { _app.Shutdown(); } catch { }
            Environment.Exit(0);
        }

        public void ApplyStartWithWindows()
        {
            try
            {
                const string keyName = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (var key = Registry.CurrentUser.OpenSubKey(keyName, true))
                {
                    if (key == null) return;
                    if (Settings.StartWithWindows)
                        key.SetValue("VulcansLivingWP", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --minimized");
                    else if (key.GetValue("VulcansLivingWP") != null)
                        key.DeleteValue("VulcansLivingWP");
                }
                SettingsStore.Save();
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Start-with-Windows update failed", e));
            }
        }

        public void ApplyHotkey()
        {
            try { Hotkeys?.Apply(Settings.PauseHotkey); } catch (Exception e) { Logger.Error(e); }
        }

        public void WatcherUpdate()
        {
            if (Engine == null || Engine.Watcher == null) return;
            Engine.Watcher.Enabled = Settings.AutoPauseFullscreen;
            if (!Settings.AutoPauseFullscreen)
            {
                if (Engine.FullscreenActive) { /* engine already computes */ }
            }
            Engine.Reapply();
        }

        /// <summary>Replays the saved last wallpaper (tray menu helper).</summary>
        public void RestoreLast()
        {
            try
            {
                if (string.IsNullOrEmpty(Settings.ActiveWallpaperId)) return;
                Library.RefreshMissing();
                var item = Library.Find(Settings.ActiveWallpaperId);
                if (item != null && !item.IsMissing)
                    Engine.Apply(item);
                else
                    MessageBox.Show("The previously applied wallpaper isn't available right now.",
                        "VulcansLivingWP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception e) { Logger.Error(e); }
        }

        void OnPlaybackFailed(WallpaperItem item, string message)
        {
            Main?.Win?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    MessageBox.Show(message, "VulcansLivingWP — playback problem",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            });
        }

        public void ApplyLibraryStateFromSettings()
        {
            // If the saved wallpaper is missing from the library, clear it.
            if (string.IsNullOrEmpty(Settings.ActiveWallpaperId)) return;
            var item = Library.Find(Settings.ActiveWallpaperId);
            if (item == null || item.IsMissing)
            {
                Settings.ActiveWallpaperId = "";
                SettingsStore.Save();
            }
        }

        public void OpenLogFolder()
        {
            try
            {
                if (!Directory.Exists(Logger.LogFolder)) Directory.CreateDirectory(Logger.LogFolder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Logger.LogFolder + "\"");
            }
            catch { }
        }
        #endregion
    }
}
