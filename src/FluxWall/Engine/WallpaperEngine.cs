using System;
using System.Collections.Generic;
using System.IO;
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
        WallpaperPlan _incomingPlan;
        string _incomingTopology = "";
        WallpaperPlan _requestedPlan;
        int _retryCount;

        IntPtr _workerHost;
        string _appliedSignature = "";

        /// <summary>Plan that is currently on screen (null while nothing plays).</summary>
        WallpaperPlan _appliedPlan;
        readonly DispatcherTimer _recoveryTimer;
        readonly DispatcherTimer _autoSwitchTimer;
        readonly DispatcherTimer _swapTimer;
        readonly DispatcherTimer _displayTimer;
        readonly DispatcherTimer _retryTimer;
        readonly ScreenStateMonitor _screen = new ScreenStateMonitor();
        string _lastMonitorSignature = "";
        WallpaperPlan _retryPlan;
        readonly Random _random = new Random();
        string _previousWallpaperId = "";

        string _lastRequestSignature = "";
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
                var plan = _retryPlan;
                _retryPlan = null;
                if (plan == null || _windows.Count > 0) return;
                StartSwap(plan);
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
        /// Applies <paramref name="item"/> according to the current display mode:
        /// same-all and span change the one video on screen, while independent mode
        /// assigns it to the primary monitor only, so other monitors keep theirs.
        /// </summary>
        public void ApplyItemForCurrentMode(WallpaperItem item)
        {
            if (item == null) return;
            RunOnUi(() =>
            {
                if (item.IsMissing) { Logger.Warn("Activation skipped, file is missing: " + item.FilePath); return; }

                switch (_settings.DisplayMode)
                {
                    case DisplayMode.Independent:
                        var primary = MonitorService.Primary(MonitorService.All());
                        if (primary != null)
                        {
                            _settings.Assign(primary.Key, item.Id);
                            Logger.Info("Independent mode: assigned '" + item.FilePath +
                                        "' to " + primary.DeviceName + ".");
                        }
                        _settings.ActiveWallpaperId = item.Id;
                        break;

                    case DisplayMode.Span:
                        _settings.SpanWallpaperId = item.Id;
                        _settings.ActiveWallpaperId = item.Id;
                        break;

                    default:
                        _settings.ActiveWallpaperId = item.Id;
                        break;
                }
                SettingsStore.Save();
                StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()));
            });
        }

        /// <summary>Assigns a video to one monitor (Independent mode).</summary>
        public void AssignToMonitor(WallpaperItem item, string monitorKey)
        {
            RunOnUi(() =>
            {
                bool missing = false;
                if (item != null)
                {
                    try { missing = item.IsMissing || !File.Exists(item.FilePath); }
                    catch { missing = true; }
                }
                if (missing)
                {
                    // Do not store a choice that cannot play: the UI is rebuilt from
                    // the settings, so it would otherwise show a new selection while
                    // the old video kept running. Name the monitor and the file.
                    Logger.Warn("Per-monitor assignment skipped for " + monitorKey +
                                ": the selected file is missing: " + item.FilePath);
                    return;
                }

                _settings.Assign(monitorKey, item != null ? item.Id : "");
                if (item != null) _settings.ActiveWallpaperId = item.Id;
                Logger.Info("Monitor " + monitorKey + " -> " +
                            (item != null ? item.FilePath : "(cleared)") + ".");
                SettingsStore.Save();
                StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()));
            });
        }

        /// <summary>Clears one monitor's assignment without touching the others.</summary>
        public void ClearMonitor(string monitorKey)
        {
            RunOnUi(() =>
            {
                _settings.Assign(monitorKey, "");
                SettingsStore.Save();
                StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()));
            });
        }

        /// <summary>Switches display mode, keeping every saved assignment intact.</summary>
        public void SetDisplayMode(DisplayMode mode)
        {
            RunOnUi(() =>
            {
                if (_settings.DisplayMode == mode) return;
                _settings.DisplayMode = mode;
                SettingsStore.Save();
                Logger.Info("Display mode changed to " + mode + " (saved assignments are kept).");
                StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()));
            });
        }

        /// <summary>Sets which monitors span mode uses (empty = all monitors).</summary>
        public void SetSpanMonitors(IEnumerable<string> monitorKeys)
        {
            RunOnUi(() =>
            {
                _settings.SpanMonitors = monitorKeys != null
                    ? new List<string>(monitorKeys)
                    : new List<string>();
                SettingsStore.Save();
                if (_settings.DisplayMode == DisplayMode.Span)
                    StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()));
            });
        }

        /// <summary>Rebuilds and applies the plan for the current settings.</summary>
        public void ApplyCurrentLayout()
        {
            RunOnUi(() => StartSwap(WallpaperPlan.Build(_settings, _library, MonitorService.All()), true));
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

                // Assign first: the plan is derived from the settings, so this is
                // the single place that decides what each mode should store.
                switch (_settings.DisplayMode)
                {
                    case DisplayMode.Independent:
                        var primary = MonitorService.Primary(MonitorService.All());
                        if (primary != null) _settings.Assign(primary.Key, item.Id);
                        _settings.ActiveWallpaperId = item.Id;
                        break;
                    case DisplayMode.Span:
                        _settings.SpanWallpaperId = item.Id;
                        _settings.ActiveWallpaperId = item.Id;
                        break;
                    default:
                        _settings.ActiveWallpaperId = item.Id;
                        break;
                }

                var plan = WallpaperPlan.Build(_settings, _library, MonitorService.All());
                if (!force)
                {
                    if (string.Equals(plan.Signature, _appliedSignature, StringComparison.Ordinal) && AllWindowsHealthy())
                    {
                        Logger.Info("Wallpaper already active; ignoring duplicate activation of '" + item.FilePath + "'.");
                        SettingsStore.Save();
                        return;
                    }
                    if (string.Equals(plan.Signature, _lastRequestSignature, StringComparison.Ordinal) &&
                        (DateTime.UtcNow - _lastRequestUtc) < DuplicateWindow)
                    {
                        Logger.Info("Ignoring repeated activation request for '" + item.FilePath + "'.");
                        SettingsStore.Save();
                        return;
                    }
                    if (_incomingPlan != null && string.Equals(_incomingPlan.Signature, plan.Signature, StringComparison.Ordinal))
                    {
                        Logger.Info("Activation for '" + item.FilePath + "' is already in progress.");
                        SettingsStore.Save();
                        return;
                    }
                }

                _lastRequestSignature = plan.Signature;
                _lastRequestUtc = DateTime.UtcNow;
                SettingsStore.Save();
                StartSwap(plan);
            });
        }

        /// <summary>Creates the new surfaces; the previous wallpaper keeps playing until they are ready.</summary>
        void StartSwap(WallpaperPlan plan)
        {
            StartSwap(plan, false);
        }

        void StartSwap(WallpaperPlan plan, bool force)
        {
            CancelIncoming("a new wallpaper was requested");
            _requestedPlan = plan;

            if (plan == null || plan.IsEmpty)
            {
                Logger.Warn("Nothing to apply: " + (plan != null && plan.Note.Length > 0 ? plan.Note : "no usable wallpaper for this layout.") +
                            (_windows.Count > 0 ? " Keeping the current wallpaper." : " The desktop stays as it is."));
                return;
            }

            if (!force && string.Equals(plan.Signature, _appliedSignature, StringComparison.Ordinal) && AllWindowsHealthy())
            {
                Logger.Info("Layout is already on screen; nothing to change. " + plan);
                return;
            }

            var host = WorkerWInjector.GetWallpaperHost();
            if (host == IntPtr.Zero)
            {
                Logger.Warn("Cannot activate the wallpaper: the desktop wallpaper layer is unavailable. " +
                            (_windows.Count > 0 ? "Keeping the current wallpaper." : "The desktop stays as it is."));
                return;
            }

            var incoming = new List<WallpaperWindow>();
            foreach (var spec in plan.Surfaces)
            {
                try
                {
                    var w = new WallpaperWindow(spec.Item, _settings, spec.Monitor, spec.IsSpan);
                    w.MediaFailed += OnWindowFailed;
                    w.Opened += OnWindowOpened;
                    w.SetEnvironmentPaused(_environmentPaused);
                    if (w.Attach(host)) incoming.Add(w);
                    else w.Dispose();
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception("Creating the wallpaper surface for " + spec.Describe() + " failed", e));
                }
            }

            if (incoming.Count == 0)
            {
                Logger.Warn("No surface could be hosted; " +
                            (_windows.Count > 0 ? "keeping the current wallpaper." : "nothing is playing."));
                return;
            }

            _workerHost = host;
            _incoming = incoming;
            _incomingPlan = plan;
            _incomingTopology = plan.Signature;

            Logger.Info("Wallpaper loading: " + plan + " ; the previous wallpaper stays until the first frame.");

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
            var plan = _incomingPlan;
            var topology = _incomingTopology;
            _incoming = null;
            _incomingPlan = null;
            _incomingTopology = "";

            var previous = Current;
            var retired = _windows;
            _windows = incoming;

            foreach (var w in retired) DisposeWindow(w);

            var item = plan != null ? plan.PrimaryItem : null;
            if (previous != null && item != null && previous.Id != item.Id) _previousWallpaperId = previous.Id;

            _appliedSignature = topology;
            _appliedPlan = plan;
            _retryCount = 0;
            Current = item;
            if (item != null && plan != null && plan.Mode != DisplayMode.Independent)
                _settings.ActiveWallpaperId = item.Id;
            SettingsStore.Save();

            foreach (var w in _windows) w.ShowSurface();

            ArmAutoSwitch();
            var h = WallpaperApplied;
            if (h != null) h(item);

            Logger.Info("Wallpaper set: " + (plan != null ? plan.ToString() : "?") +
                        " on " + _windows.Count + " surface(s).");

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

            var failed = _incomingPlan;
            Logger.Warn("The new layout produced no frame within " + SwapTimeout.TotalSeconds +
                        "s; keeping the previous wallpaper. " + (failed != null ? failed.ToString() : ""));
            CancelIncoming("the new wallpaper did not start");

            if (failed != null && _windows.Count > 0 && _retryCount < 2)
            {
                _retryCount++;
                _retryPlan = failed;
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
            _incomingPlan = null;
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
                _requestedPlan = null;
                _appliedPlan = null;
                _retryCount = 0;
                foreach (var w in _windows) DisposeWindow(w);
                _windows.Clear();
                _appliedSignature = "";
                Current = null;
                _settings.ActiveWallpaperId = "";
                SettingsStore.Save();
                var h = WallpaperApplied;
                if (h != null) h(null);
                Logger.Info("Wallpaper stopped.");
            });
        }

        /// <summary>
        /// Re-applies the current layout (display change, Explorer restart, settings).
        /// The plan is rebuilt from the settings, so a display change picks up the
        /// new monitor geometry instead of replaying stale rectangles.
        /// </summary>
        public void Reapply()
        {
            RunOnUi(() =>
            {
                if (Current == null && _appliedPlan == null) return;
                var plan = WallpaperPlan.Build(_settings, _library, MonitorService.All());
                // Deferred so a caller inside a timer/dispatcher callback can
                // never re-enter the engine while it is closing windows.
                _dispatcher.BeginInvoke((Action)(() => StartSwap(plan, true)), DispatcherPriority.Background);
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
                        if (_requestedPlan != null && _windows.Count == 0) StartSwap(_requestedPlan);
                        return;
                    }

                    bool hostGone = _workerHost == IntPtr.Zero ||
                                    !Native.IsWindow(_workerHost) ||
                                    Native.WindowClass(_workerHost) != "WorkerW";
                    var plan = WallpaperPlan.Build(_settings, _library, MonitorService.All());
                    // While a swap is in flight, compare against the layout it
                    // is being built for. Comparing against the committed one
                    // made every display notification cancel and restart the
                    // pending activation.
                    var baseline = _incoming != null ? _incomingTopology : _appliedSignature;
                    bool layoutChanged = plan.Signature.Length > 0 &&
                                         !string.Equals(plan.Signature, baseline, StringComparison.Ordinal);

                    if (hostGone || layoutChanged || _windows.Count == 0)
                    {
                        Logger.Info("Rebuilding the wallpaper after a display/shell change (hostGone=" +
                                    hostGone + ", layoutChanged=" + layoutChanged +
                                    ", surfaces=" + _windows.Count + ").");
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

        void OnWindowFailed(WallpaperWindow window, WallpaperItem item, string message)
        {
            // While a swap is pending, the outgoing surfaces are still playing.
            // Replacing one monitor's video opens the new decoder next to the old
            // one, and Media Foundation can report a failure for the surface that
            // is about to be disposed (0xC00D11B1 when both briefly decode the
            // same file). The swap already has a replacement for it, so this must
            // not raise an error dialog: log it and let the commit finish.
            if (_incoming != null && window != null && !_incoming.Contains(window))
            {
                Logger.Info("Outgoing wallpaper surface for '" +
                            (item != null ? item.FilePath : "?") +
                            " failed while it was being replaced (ignored): " + message);
                return;
            }

            // A failed NEW surface must not replace a working wallpaper.
            if (_incoming != null && _incoming.Count > 0 && _incomingPlan != null &&
                item != null && _incomingPlan.PrimaryItem != null &&
                _incomingPlan.PrimaryItem.Id == item.Id)
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
                    if (_incoming == null && _retryCount < HowManyRetriesWhenNothingIsShowing)
                    {
                        var retryPlan = WallpaperPlan.Build(_settings, _library, MonitorService.All());
                        if (retryPlan.IsEmpty) return;
                        _retryCount++;
                        Logger.Info("Retrying wallpaper activation (attempt " + _retryCount + " of " +
                                    HowManyRetriesWhenNothingIsShowing + ").");
                        StartSwap(retryPlan);
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

                // 2. Display layout (or the assignments) changed? Compare against
                // the plan that is actually on screen. Comparing against the live
                // windows used to re-apply forever whenever one surface failed,
                // which showed up as repeated identical activations.
                //
                // Only rebuild the full plan when the monitor topology actually
                // changed; re-enumerating monitors and building a StringBuilder
                // signature every 5s while idle is pure allocation churn.
                var monitors = MonitorService.All();
                var monitorSignature = MonitorService.Signature(monitors);
                if (!string.Equals(monitorSignature, _lastMonitorSignature, StringComparison.Ordinal))
                {
                    _lastMonitorSignature = monitorSignature;
                    var plan = WallpaperPlan.Build(_settings, _library, monitors);
                    if (plan.Signature.Length > 0 && !plan.IsEmpty &&
                        !string.Equals(plan.Signature, _appliedSignature, StringComparison.Ordinal))
                    {
                        Logger.Info("Display layout changed; re-applying wallpaper. was=[" +
                                    _appliedSignature + "] now=[" + plan.Signature + "]");
                        Reapply();
                    }
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
                _retryPlan = null;
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
