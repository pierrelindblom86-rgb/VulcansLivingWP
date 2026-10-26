using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using FluxWall.Common;
using FluxWall.Library;
using FluxWall.Settings;

namespace FluxWall.Engine
{
    /// <summary>
    /// Owns the set of live WallpaperWindows, mirrors them onto the target
    /// monitors, and centralises pause-state logic:
    ///     paused = manualPause || (autoPauseFullscreen && fullscreenActive)
    /// The engine is the ONLY component creating/closing video windows, so
    /// decoders are never left running behind the UI.
    ///
    /// 1.2.2: an activation is now idempotent (clicking Apply twice cannot
    /// start two decode chains), and a new wallpaper replaces the old one only
    /// after its first frame is decoded, so the desktop never drops back to the
    /// static Windows wallpaper — or flashes black — during a switch.
    /// </summary>
    public class WallpaperEngine : IDisposable
    {
        const int HowManyRetriesWhenNothingIsShowing = 6;
        static readonly TimeSpan SwapTimeout = TimeSpan.FromSeconds(5);
        static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(1200);

        readonly LibraryStore _library;
        readonly AppSettings _settings;
        readonly Dispatcher _dispatcher;

        List<WallpaperWindow> _windows = new List<WallpaperWindow>();
        List<WallpaperWindow> _incoming;
        WallpaperItem _incomingItem;
        string _incomingTopology = "";
        WallpaperItem _requestedItem;
        int _retryCount;

        IntPtr _workerHost;
        string _appliedTopology = "";
        readonly DispatcherTimer _recoveryTimer;
        readonly DispatcherTimer _autoSwitchTimer;
        readonly DispatcherTimer _swapTimer;
        readonly DispatcherTimer _displayTimer;
        readonly DispatcherTimer _retryTimer;
        readonly ScreenStateMonitor _screen = new ScreenStateMonitor();
        WallpaperItem _retryItem;
        readonly Random _random = new Random();
        string _previousWallpaperId = "";

        string _lastRequestItemId = "";
        DateTime _lastRequestUtc = DateTime.MinValue;

        bool _manualPaused;
        bool _fullscreenActive;
        string _fullscreenApp = "";
        bool _environmentPaused;

        public event Action ManualPauseChanged;
        public event Action<WallpaperItem> WallpaperApplied;
        public event Action<WallpaperItem, string> PlaybackFailed;

        public FullScreenWatcher Watcher { get; private set; }

        /// <summary>Lock / display-off / disconnect notifications from Windows.</summary>
        public ScreenStateMonitor Screen { get { return _screen; } }

        /// <summary>True while playback is stopped because nobody can see it.</summary>
        public bool EnvironmentPaused { get { return _environmentPaused; } }

        public WallpaperEngine(LibraryStore library, AppSettings settings)
        {
            _library = library;
            _settings = settings;
            _dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);

            Watcher = new FullScreenWatcher(() => _windows.ToArray());
            Watcher.FullscreenChanged += OnFullscreenChanged;
            _screen.Changed += RefreshEnvironmentPause;

            // Recovery / display-change checks are very cheap; 5s cadence.
            _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            _recoveryTimer.Tick += (s, e) => HealthCheck();

            // One-shot timer, re-armed after every change: a tick can never
            // overlap another one, and while waiting it costs nothing.
            _autoSwitchTimer = new DispatcherTimer(DispatcherPriority.Background);
            _autoSwitchTimer.Tick += (s, e) =>
            {
                _autoSwitchTimer.Stop();
                OnAutoSwitchTick();
            };

            // One-shot: if a new wallpaper never produces a frame (hung decoder,
            // no usable host) the previous wallpaper simply stays.
            _swapTimer = new DispatcherTimer(DispatcherPriority.Background);
            _swapTimer.Tick += (s, e) =>
            {
                _swapTimer.Stop();
                OnSwapTimeout();
            };

            // One-shot: display changes reach the shell asynchronously, so the
            // wallpaper is re-applied shortly after WM_DISPLAYCHANGE instead of
            // only on the next 5s health tick.
            _displayTimer = new DispatcherTimer(DispatcherPriority.Background);
            _displayTimer.Tick += (s, e) =>
            {
                _displayTimer.Stop();
                OnDisplaySettled();
            };

            // One-shot retry for a decoder that needed longer than the swap
            // window (rare, but it happens on old GPUs right after a display
            // change). Never retries more than twice.
            _retryTimer = new DispatcherTimer(DispatcherPriority.Background);
            _retryTimer.Interval = TimeSpan.FromSeconds(2);
            _retryTimer.Tick += (s, e) =>
            {
                _retryTimer.Stop();
                var item = _retryItem;
                _retryItem = null;
                if (item == null || _windows.Count > 0) return;
                StartSwap(item);
            };
        }

        /// <summary>True while an automatic wallpaper change is counting down.</summary>
        public bool AutoSwitchArmed { get { return _autoSwitchTimer.IsEnabled; } }

        public bool ManualPaused { get { return _manualPaused; } }

        /// <summary>Safe read-only view of the live window set (for probes/tests).</summary>
        public System.Collections.Generic.IReadOnlyList<WallpaperWindow> WindowsSnapshot
        {
            get
            {
                lock (_windows) { return _windows.ToArray(); }
            }
        }

        public WallpaperItem Current { get; private set; }
        public bool FullscreenActive { get { return _fullscreenActive; } }
        public string FullscreenApp { get { return _fullscreenApp; } }
        public bool IsSwapping { get { return _incoming != null; } }

        public bool IsPlaying
        {
            get { return Current != null && !_manualPaused && !(_settings.AutoPauseFullscreen && _fullscreenActive); }
        }

        #region Apply / stop

        public void Apply(WallpaperItem item)
        {
            Apply(item, false);
        }

        /// <summary>
        /// Activates <paramref name="item"/>. Unless <paramref name="force"/> is
        /// set, a request for the wallpaper that is already playing and healthy
        /// is ignored — that is what makes a double-click, a repeated tray
        /// command or a startup restore racing a manual pick harmless.
        /// </summary>
        public void Apply(WallpaperItem item, bool force)
        {
            if (item == null) return;
            RunOnUi(() =>
            {
                if (item.IsMissing)
                {
                    Logger.Warn("Activation skipped, file is missing: " + item.FilePath);
                    return;
                }

                if (!force)
                {
                    if (Current != null && Current.Id == item.Id && AllWindowsHealthy())
                    {
                        Logger.Info("Wallpaper already active; ignoring duplicate activation of '" + item.FilePath + "'.");
                        return;
                    }
                    if (_lastRequestItemId == item.Id &&
                        (DateTime.UtcNow - _lastRequestUtc) < DuplicateWindow)
                    {
                        Logger.Info("Ignoring repeated activation request for '" + item.FilePath + "'.");
                        return;
                    }
                    if (_incomingItem != null && _incomingItem.Id == item.Id)
                    {
                        Logger.Info("Activation for '" + item.FilePath + "' is already in progress.");
                        return;
                    }
                }

                _lastRequestItemId = item.Id;
                _lastRequestUtc = DateTime.UtcNow;
                StartSwap(item);
            });
        }

        /// <summary>Creates the new surfaces; the previous wallpaper keeps playing until they are ready.</summary>
        void StartSwap(WallpaperItem item)
        {
            CancelIncoming("a new wallpaper was requested");
            _requestedItem = item;

            var host = WorkerWInjector.GetWallpaperHost();
            if (host == IntPtr.Zero)
            {
                Logger.Warn("Cannot activate '" + item.FilePath + "': the desktop wallpaper layer is unavailable. " +
                            (_windows.Count > 0 ? "Keeping the current wallpaper." : "The desktop stays as it is."));
                return;
            }

            var monitors = MonitorService.Resolve(_settings, MonitorService.All());
            if (monitors.Count == 0)
            {
                Logger.Warn("Cannot activate '" + item.FilePath + "': no monitors were enumerated.");
                return;
            }

            var incoming = new List<WallpaperWindow>();
            foreach (var m in monitors)
            {
                try
                {
                    var w = new WallpaperWindow(item, _settings, m);
                    w.MediaFailed += OnWindowFailed;
                    w.Opened += OnWindowOpened;
                    w.SetEnvironmentPaused(_environmentPaused);
                    if (w.Attach(host)) incoming.Add(w);
                    else w.Dispose();
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception("Creating the wallpaper surface for " + m + " failed", e));
                }
            }

            if (incoming.Count == 0)
            {
                Logger.Warn("No monitor could host '" + item.FilePath + "'; " +
                            (_windows.Count > 0 ? "keeping the current wallpaper." : "nothing is playing."));
                return;
            }

            _workerHost = host;
            _incoming = incoming;
            _incomingItem = item;
            _incomingTopology = MonitorService.Signature(monitors);

            Logger.Info("Wallpaper loading: " + item.FilePath + " on " + incoming.Count +
                        " monitor(s); the previous wallpaper stays until the first frame.");

            // A surface can already have a frame (fast decoder); commit at once.
            if (TryCommit()) return;

            _swapTimer.Interval = SwapTimeout;
            _swapTimer.Start();
        }

        void OnWindowOpened()
        {
            RunOnUi(() => { TryCommit(); });
        }

        /// <summary>Commits the pending swap once every new surface has a decoded frame.</summary>
        bool TryCommit()
        {
            if (_incoming == null || _incoming.Count == 0) return false;
            foreach (var w in _incoming)
                if (!w.HasFirstFrame) return false;
            CommitSwap();
            return true;
        }

        void CommitSwap()
        {
            if (_incoming == null || _incoming.Count == 0) return;
            _swapTimer.Stop();

            var incoming = _incoming;
            var item = _incomingItem;
            var topology = _incomingTopology;
            _incoming = null;
            _incomingItem = null;
            _incomingTopology = "";

            var previous = Current;
            var retired = _windows;
            _windows = incoming;

            foreach (var w in retired) DisposeWindow(w);

            if (previous != null && previous.Id != item.Id) _previousWallpaperId = previous.Id;

            _appliedTopology = topology;
            _retryCount = 0;
            Current = item;
            _settings.ActiveWallpaperId = item.Id;
            SettingsStore.Save();

            foreach (var w in _windows) w.ShowSurface();

            ArmAutoSwitch();
            var h = WallpaperApplied;
            if (h != null) h(item);

            Logger.Info("Wallpaper set: " + item.FilePath + " on " + _windows.Count + " monitor(s).");

            // One collection after the decoders are released keeps long switch
            // sessions from holding native buffers, without stalling the commit.
            // Optimized: the runtime decides whether a blocking collection is
            // worth it. Forcing a full gen2 pass on a live wallpaper heap caused
            // a visible hitch on every switch, and MediaElement.Close() has
            // already released the decoder's native buffers by now.
            _dispatcher.BeginInvoke((Action)(() =>
            {
                try { GC.Collect(2, GCCollectionMode.Optimized, false); } catch { }
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>No new surface produced a frame in time: keep what was playing.</summary>
        void OnSwapTimeout()
        {
            if (_incoming == null) return;
            int ready = _incoming.Count(w => w.HasFirstFrame);
            if (ready > 0)
            {
                Logger.Warn("Only " + ready + " of " + _incoming.Count +
                            " wallpaper surfaces produced a frame; using the ones that did.");
                CommitSwap();
                return;
            }

            var item = _incomingItem;
            Logger.Warn("Wallpaper '" + (item != null ? item.FilePath : "?") +
                        "' produced no frame within " + SwapTimeout.TotalSeconds + "s; keeping the previous wallpaper.");
            CancelIncoming("the new wallpaper did not start");

            if (item != null && _windows.Count > 0 && _retryCount < 2)
            {
                _retryCount++;
                _retryItem = item;
                Logger.Info("Retrying that wallpaper once (attempt " + _retryCount + ").");
                _retryTimer.Start();
            }
        }

        void CancelIncoming(string reason)
        {
            _swapTimer.Stop();
            if (_incoming == null) return;
            var pending = _incoming;
            _incoming = null;
            _incomingItem = null;
            _incomingTopology = "";
            foreach (var w in pending) DisposeWindow(w);
            Logger.Info("Cancelled the pending wallpaper activation (" + reason + ").");
        }

        void DisposeWindow(WallpaperWindow w)
        {
            if (w == null) return;
            try { w.SetManualPaused(true); } catch { }
            try { w.Shutdown(); } catch (Exception e) { Logger.Error(e); }
            try { w.Dispose(); } catch { }
        }

        public void StopAll()
        {
            RunOnUi(() =>
            {
                _autoSwitchTimer.Stop();
                CancelIncoming("the wallpaper was stopped");
                _previousWallpaperId = "";
                _requestedItem = null;
                _retryCount = 0;
                foreach (var w in _windows) DisposeWindow(w);
                _windows.Clear();
                _appliedTopology = "";
                Current = null;
                _settings.ActiveWallpaperId = "";
                SettingsStore.Save();
                var h = WallpaperApplied;
                if (h != null) h(null);
                Logger.Info("Wallpaper stopped.");
            });
        }

        /// <summary>Re-applies the SAME item (display change, Explorer restart, settings).</summary>
        public void Reapply()
        {
            RunOnUi(() =>
            {
                if (Current == null) return;
                var item = Current;
                // Deferred so a caller inside a timer/dispatcher callback can
                // never re-enter the engine while it is closing windows.
                _dispatcher.BeginInvoke((Action)(() => Apply(item, true)), DispatcherPriority.Background);
            });
        }

        /// <summary>
        /// Called for WM_DISPLAYCHANGE. The shell rebuilds the desktop layer
        /// asynchronously, so the actual re-apply is delayed a little and
        /// debounced; the 5s health check stays as a backstop.
        /// </summary>
        public void OnDisplayConfigurationChanged()
        {
            RunOnUi(() =>
            {
                Logger.Info("Display configuration changed; re-checking the wallpaper layer shortly.");
                _displayTimer.Stop();
                _displayTimer.Interval = TimeSpan.FromMilliseconds(800);
                _displayTimer.Start();
            });
        }

        /// <summary>
        /// WM_DISPLAYCHANGE is only a hint to re-validate. Forcing a re-apply on
        /// every notification is dangerous: some drivers broadcast the message
        /// when a video swap chain starts, which would rebuild the wallpaper on
        /// each activation — the exact "repeated activation" behaviour.
        /// </summary>
        void OnDisplaySettled()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (Current == null)
                    {
                        if (_requestedItem != null && _windows.Count == 0) StartSwap(_requestedItem);
                        return;
                    }

                    bool hostGone = _workerHost == IntPtr.Zero ||
                                    !Native.IsWindow(_workerHost) ||
                                    Native.WindowClass(_workerHost) != "WorkerW";
                    var signature = MonitorService.Signature(
                        MonitorService.Resolve(_settings, MonitorService.All()));
                    // While a swap is in flight, compare against the layout it
                    // is being built for. Comparing against the committed one
                    // made every display notification cancel and restart the
                    // pending activation.
                    var baseline = _incoming != null ? _incomingTopology : _appliedTopology;
                    bool layoutChanged = signature.Length > 0 &&
                                         !string.Equals(signature, baseline, StringComparison.Ordinal);

                    if (hostGone || layoutChanged || _windows.Count == 0)
                    {
                        Logger.Info("Rebuilding the wallpaper after a display/shell change (hostGone=" +
                                    hostGone + ", layoutChanged=" + layoutChanged +
                                    ", windows=" + _windows.Count + ").");
                        Reapply();
                        return;
                    }

                    // Same layout and same host: nothing to rebuild, just verify.
                    HealthCheck();
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception("Display-change recovery failed", e));
                }
            });
        }

        /// <summary>True when every live surface is parented, visible and mid-playback.</summary>
        public bool AllWindowsHealthy()
        {
            if (_windows.Count == 0) return false;
            if (_workerHost == IntPtr.Zero) return false;
            foreach (var w in _windows)
            {
                if (w == null || w.Failed || !w.HasFirstFrame || !w.IsParented || !w.Visible) return false;
                if (WorkerWInjector.InjectionLost(w.Handle, _workerHost)) return false;
            }
            return true;
        }
        #endregion

        #region Pause / resume

        public void SetManualPaused(bool paused)
        {
            RunOnUi(() =>
            {
                if (_manualPaused == paused) return;
                _manualPaused = paused;
                foreach (var w in _windows) w.SetManualPaused(paused);
                if (_incoming != null) foreach (var w in _incoming) w.SetManualPaused(paused);
                if (paused) _autoSwitchTimer.Stop();
                else ArmAutoSwitch();
                var h = ManualPauseChanged;
                if (h != null) h();
                Logger.Info(paused ? "Playback paused manually." : "Playback resumed manually.");
            });
        }

        public void TogglePause()
        {
            SetManualPaused(!_manualPaused);
        }

        void OnFullscreenChanged(bool active, string title)
        {
            RunOnUi(() =>
            {
                _fullscreenActive = active;
                _fullscreenApp = title ?? "";
                UpdateFullscreenPause();
            });
        }

        void UpdateFullscreenPause()
        {
            bool pauseNow = _settings.AutoPauseFullscreen && _fullscreenActive;
            foreach (var w in _windows) w.SetFullscreenPaused(pauseNow);
            if (_incoming != null) foreach (var w in _incoming) w.SetFullscreenPaused(pauseNow);

            // Fullscreen pause wins over rotation: no wallpaper changes while
            // it is active, and the next countdown starts when playback resumes,
            // so a long game session cannot trigger a burst of changes.
            if (pauseNow || _manualPaused) _autoSwitchTimer.Stop();
            else ArmAutoSwitch();
        }

        #region Automatic wallpaper switching

        /// <summary>
        /// (Re)starts the single rotation timer from now. Safe to call for every
        /// setting change: it stops the old countdown first, so timers cannot
        /// accumulate, and it does nothing while rotation is off or paused.
        /// </summary>
        public void ArmAutoSwitch()
        {
            RunOnUi(() =>
            {
                _autoSwitchTimer.Stop();
                if (!_settings.AutoSwitchWallpapers) return;
                if (Current == null) return;
                if (_environmentPaused || _manualPaused || (_settings.AutoPauseFullscreen && _fullscreenActive))
                {
                    Logger.Info("Auto-switch: playback is paused; countdown starts when it resumes.");
                    return;
                }

                int minutes = _settings.EffectiveAutoSwitchMinutes;
                _autoSwitchTimer.Interval = TimeSpan.FromMinutes(minutes);
                _autoSwitchTimer.Start();
                Logger.Info("Auto-switch: next change in " + minutes + " min.");
            });
        }

        void OnAutoSwitchTick()
        {
            try
            {
                if (!_settings.AutoSwitchWallpapers) return;
                if (Current == null) return;

                if (_environmentPaused || _manualPaused || (_settings.AutoPauseFullscreen && _fullscreenActive))
                {
                    Logger.Info("Auto-switch skipped: playback is paused.");
                    ArmAutoSwitch();
                    return;
                }

                _library.RefreshMissing();
                var available = _library.Items
                    .Where(i => i != null && !i.IsMissing)
                    .ToList();
                var candidates = available
                    .Where(i => !string.Equals(i.Id, Current.Id, StringComparison.Ordinal))
                    .ToList();

                if (candidates.Count == 0)
                {
                    // Keep the countdown alive so a video imported later is
                    // picked up without the user having to toggle anything.
                    Logger.Info("Auto-switch: no other usable wallpaper; keeping the current one.");
                    ArmAutoSwitch();
                    return;
                }

                // With three or more choices, avoid bouncing straight back to
                // the wallpaper that played before the current one.
                if (candidates.Count > 1 && _previousWallpaperId.Length > 0)
                {
                    var withoutPrevious = candidates
                        .Where(i => !string.Equals(i.Id, _previousWallpaperId, StringComparison.Ordinal))
                        .ToList();
                    if (withoutPrevious.Count > 0) candidates = withoutPrevious;
                }

                var next = candidates[_random.Next(candidates.Count)];
                Logger.Info("Auto-switch: changing wallpaper to '" + next.FilePath + "'.");
                Apply(next);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Auto-switch failed", e));
                ArmAutoSwitch();
            }
        }

        #endregion
        #endregion

        /// <summary>Pushes fresh settings (audio/scaling) into live windows.</summary>
        public void ApplySettingsChange()
        {
            RunOnUi(() =>
            {
                foreach (var w in _windows)
                {
                    try { w.ApplySettings(_settings); } catch (Exception e) { Logger.Error(e); }
                }
            });
        }

        /// <summary>Lets the automation/tests drive one health pass.</summary>
        public void TickHealth()
        {
            RunOnUi(() => HealthCheck());
        }

        #region Events plumbing

        void OnWindowFailed(WallpaperItem item, string message)
        {
            // A failed NEW surface must not replace a working wallpaper.
            if (_incoming != null && _incoming.Count > 0 && _incomingItem != null &&
                item != null && _incomingItem.Id == item.Id)
            {
                CancelIncoming("decoding failed for the new wallpaper");
            }

            var h = PlaybackFailed;
            if (h != null) try { h(item, message); } catch { }
        }
        #endregion

        #region Health / shell recovery

        void HealthCheck()
        {
            try
            {
                // Also the battery fallback: PowerLineStatus is a cheap Win32
                // query, and it is only read when the option is switched on.
                RefreshEnvironmentPause();

                // Nothing is playing yet: this is the startup case where Explorer
                // was not ready when the saved wallpaper was restored. Retry a
                // few times instead of leaving the user with an empty desktop.
                if (_windows.Count == 0)
                {
                    if (_incoming == null && _requestedItem != null &&
                        !_requestedItem.IsMissing && _retryCount < HowManyRetriesWhenNothingIsShowing)
                    {
                        _retryCount++;
                        Logger.Info("Retrying wallpaper activation (attempt " + _retryCount + " of " +
                                    HowManyRetriesWhenNothingIsShowing + ").");
                        StartSwap(_requestedItem);
                    }
                    return;
                }

                if (_incoming != null) return;   // a swap is in progress

                // 1. Injected parent lost (Explorer restarted / layer rebuilt).
                var sample = _windows.FirstOrDefault(w => w.Window != null && w.IsParented);
                var liveHandle = sample != null ? Native.GetWindowLive(sample.Window) : IntPtr.Zero;
                if (sample != null && WorkerWInjector.InjectionLost(liveHandle, _workerHost))
                {
                    Logger.Warn(string.Format(
                        "Wallpaper injection lost (live={0:X}, ancestor={1:X}, host={2:X}). Re-injecting…",
                        liveHandle.ToInt64(), Native.GetAncestor(liveHandle, Native.GA_PARENT).ToInt64(), _workerHost.ToInt64()));
                    Reapply();
                    return;
                }

                // 2. Display layout changed? Compare against the topology this
                // wallpaper was actually applied to. Comparing against the live
                // windows used to re-apply forever whenever one surface failed,
                // which showed up as repeated identical activations.
                var want = MonitorService.Resolve(_settings, MonitorService.All());
                var signature = MonitorService.Signature(want);
                if (signature.Length > 0 && !string.Equals(signature, _appliedTopology, StringComparison.Ordinal))
                {
                    Logger.Info("Display layout changed; re-applying wallpaper. was=[" + _appliedTopology + "] now=[" + signature + "]");
                    Reapply();
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("HealthCheck failed", e));
            }
        }
        #endregion

        #region Environment pause (locked / display off / battery)

        static bool OnBattery()
        {
            try
            {
                return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
                       == System.Windows.Forms.PowerLineStatus.Offline;
            }
            catch { return false; }
        }

        /// <summary>
        /// Re-evaluates whether playback should stop because the desktop is not
        /// visible (locked session, display off, disconnected session, or the
        /// optional battery rule). Cheap: only touches the surfaces when the
        /// answer actually changes. Safe to call from any thread.
        /// </summary>
        public void RefreshEnvironmentPause()
        {
            RunOnUi(() =>
            {
                bool pause = _screen.Unavailable || (_settings.PauseOnBattery && OnBattery());
                if (pause == _environmentPaused) return;

                _environmentPaused = pause;
                foreach (var w in _windows) w.SetEnvironmentPaused(pause);
                if (_incoming != null) foreach (var w in _incoming) w.SetEnvironmentPaused(pause);

                if (pause)
                {
                    _autoSwitchTimer.Stop();
                    Logger.Info("Playback paused while the desktop is not visible (" + _screen.Describe() + ").");
                }
                else
                {
                    Logger.Info("Playback resumed: the desktop is visible again.");
                    ArmAutoSwitch();
                }
            });
        }
        #endregion

        public void Start()
        {
            RunOnUi(() =>
            {
                Watcher.Start();
                _screen.Start();
                RefreshEnvironmentPause();
                _recoveryTimer.Start();
            });
        }

        public void Stop()
        {
            RunOnUi(() =>
            {
                try { _recoveryTimer.Stop(); } catch { }
                try { _autoSwitchTimer.Stop(); } catch { }
                try { _swapTimer.Stop(); } catch { }
                try { _displayTimer.Stop(); } catch { }
                try { _retryTimer.Stop(); } catch { }
                _retryItem = null;
                try { Watcher.Dispose(); } catch { }
                try { _screen.Dispose(); } catch { }
                CancelIncoming("the application is closing");
                foreach (var w in _windows) DisposeWindow(w);
                _windows.Clear();
                Current = null;
            });
        }

        void RunOnUi(Action action)
        {
            if (_dispatcher == null)
            {
                try { action(); } catch (Exception e) { Logger.Error(e); }
                return;
            }
            if (_dispatcher.CheckAccess())
            {
                try { action(); } catch (Exception e) { Logger.Error(e); }
            }
            else
            {
                _dispatcher.Invoke(action, DispatcherPriority.Background);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public static class RectExt
    {
        public static bool RectsEqual(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Right == b.Right &&
                   a.Top == b.Top && a.Bottom == b.Bottom;
        }
    }
}
