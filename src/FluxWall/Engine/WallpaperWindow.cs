using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FluxWall.Common;
using FluxWall.Library;
using FluxWall.Settings;

namespace FluxWall.Engine
{
    /// <summary>
    /// One video surface per monitor. The window is created hidden, parented
    /// into Explorer's wallpaper WorkerW *before* it is ever shown, and only
    /// revealed once the decoder has produced its first frame — so switching
    /// wallpapers can neither flash black nor cover the desktop icons.
    /// </summary>
    public class WallpaperWindow : IDisposable
    {
        readonly WallpaperItem _item;
        readonly AppSettings _settings;
        readonly MediaElement _media;
        readonly MonitorInfoLite _monitor;
        bool _unableToPlay;
        IntPtr _injectedHost;
        bool _hostReady;
        bool _closed;

        IntPtr _ourHwnd;
        bool _revealQueued;
        System.Windows.Threading.DispatcherTimer _revealFallback;

        public IntPtr Handle { get { return _ourHwnd; } }
        public IntPtr MonitorHandle { get { return _monitor.Handle; } }
        public WallpaperItem Item { get { return _item; } }
        public string MonitorName { get { return _monitor.DeviceName; } }
        /// <summary>True when this surface covers several monitors as one picture.</summary>
        public bool IsSpan { get { return _isSpan; } }
        public bool Visible { get; private set; }
        /// <summary>True once the window has been moved under the desktop icons.</summary>
        public bool IsParented { get; private set; }
        /// <summary>True when the decoder reported an opened video for this surface.</summary>
        public bool HasFirstFrame { get; private set; }
        public bool Failed { get { return _unableToPlay; } }
        /// <summary>Screen rectangle this surface occupies, in desktop pixels.</summary>
        public Native.RECT MonitorRect { get { return _monitor.ScreenRect; } }
        /// <summary>Playback position of this surface (used by the layout tests).</summary>
        public TimeSpan Position { get { return _media != null ? _media.Position : TimeSpan.Zero; } }
        public TimeSpan Duration
        {
            get
            {
                try
                {
                    return _media != null && _media.NaturalDuration.HasTimeSpan
                        ? _media.NaturalDuration.TimeSpan
                        : TimeSpan.Zero;
                }
                catch { return TimeSpan.Zero; }
            }
        }

        /// <summary>Raised once the video has opened on this surface.</summary>
        public event Action Opened;
        /// <summary>
        /// Raised if Media Foundation reports the file cannot be played. The
        /// surface is included so the engine can tell a failing new surface from
        /// an outgoing one that fails during replacement.
        /// </summary>
        public event Action<WallpaperWindow, WallpaperItem, string> MediaFailed;

        readonly bool _isSpan;

        public WallpaperWindow(WallpaperItem item, AppSettings settings, MonitorInfoLite monitor)
            : this(item, settings, monitor, false)
        {
        }

        /// <param name="isSpan">
        /// True for the single surface that covers several monitors at once. It
        /// uses the span fit setting instead of the per-monitor scaling mode.
        /// </param>
        public WallpaperWindow(WallpaperItem item, AppSettings settings, MonitorInfoLite monitor, bool isSpan)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (monitor == null) throw new ArgumentNullException("monitor");

            _item = item;
            _settings = settings ?? new AppSettings();
            _monitor = monitor;
            _isSpan = isSpan;

            _media = new MediaElement
            {
                Source = new Uri(item.FilePath),
                UnloadedBehavior = MediaState.Close,
                LoadedBehavior = MediaState.Manual,
                IsMuted = !_settings.PlayWallpaperAudio,
                Volume = 1.0,
                Stretch = Stretch.UniformToFill,   // Fill by default; see ApplyScaling
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ScrubbingEnabled = false,
            };

            var root = new Grid { Background = Brushes.Black, ClipToBounds = true };
            root.Children.Add(_media);

            Window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Topmost = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Background = Brushes.Black,
                Content = root,
            };

            // Play only once the window and its MediaElement are loaded; calling
            // Play before that is silently ignored and the video never opens.
            Window.Loaded += (s, e) =>
            {
                try { if (!_closed) _media.Play(); }
                catch (Exception ex) { Logger.Error(new Exception("Play on load failed", ex)); }
            };
            _media.MediaOpened += OnMediaOpened;
            _media.MediaEnded += OnMediaEnded;
            _media.MediaFailed += OnMediaFailed;
            _media.SizeChanged += (s, e) => Guarded(ApplyScaling, "media size change");
            Window.SizeChanged += (s, e) => Guarded(ApplyScaling, "window size change");
        }

        public Window Window { get; private set; }

        void Guarded(Action action, string what)
        {
            // WPF can still raise layout/media callbacks for a window that is
            // being torn down. Never let such a callback reach the dispatcher.
            if (_closed) return;
            try { action(); }
            catch (Exception e) { Logger.Error(new Exception("Wallpaper surface failed during " + what, e)); }
        }

        #region Lifecycle

        /// <summary>
        /// Creates the HWND (hidden), positions it over the target monitor,
        /// parents it into the wallpaper WorkerW and starts playback.
        /// Nothing is painted on the desktop by this step: the window is shown
        /// only after the first decoded frame (see OnMediaOpened).
        /// Returns false when the surface could not be placed, so the engine
        /// never reports a wallpaper that is not actually on screen.
        /// Call on the app's dispatcher thread.
        /// </summary>
        public bool Attach(IntPtr workerHost)
        {
            try
            {
                if (_closed || Window == null)
                {
                    Logger.Warn("Attach skipped: the wallpaper surface for '" + _item.FilePath + "' is already closed.");
                    return false;
                }

                if (!WorkerWInjector.IsUsableHost(workerHost))
                {
                    Logger.Warn("Attach refused for '" + _item.FilePath + "': no usable desktop host (host=0x" +
                                workerHost.ToInt64().ToString("X") + ").");
                    return false;
                }

                var r = _monitor.ScreenRect;
                double dpi = GetDpiScale();

                // Size through WPF properties first: the framework's own layout
                // pass would otherwise override the native rectangle we set below.
                Window.Left = r.Left / dpi;
                Window.Top = r.Top / dpi;
                Window.Width = Math.Max(1.0, (r.Right - r.Left) / dpi);
                Window.Height = Math.Max(1.0, (r.Bottom - r.Top) / dpi);

                // Show() is required: it creates the HWND AND loads the WPF content
                // (Loaded, MediaElement). EnsureHandle alone leaves the content
                // unloaded, so the video never opens. Hiding again in the same
                // dispatcher turn means the black window is never composited.
                if (!Window.IsVisible) Window.Show();
                _ourHwnd = new WindowInteropHelper(Window).Handle;
                if (_ourHwnd == IntPtr.Zero)
                {
                    Logger.Warn("Attach failed for '" + _item.FilePath + "': the window has no HWND.");
                    return false;
                }
                Native.ShowWindow(_ourHwnd, Native.SW_HIDE);

                ApplyWindowStyles(_ourHwnd);
                _injectedHost = workerHost;
                _hostReady = true;

                if (!ParentIntoHost())
                {
                    _hostReady = false;
                    return false;
                }

                // The window stays hidden until its first frame; the fallback
                // reveal covers decoders that refuse to start on a hidden
                // surface, so playback can never stall silently.
                StartRevealFallback();
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Attach failed for '" + _item.FilePath + "'", e));
                var h = MediaFailed;
                if (h != null) h(this, _item, "VulcansLivingWP could not place the wallpaper on the desktop.");
                return false;
            }
        }

        double GetDpiScale()
        {
            try
            {
                var ps = PresentationSource.FromVisual(Window);
                if (ps != null && ps.CompositionTarget != null)
                {
                    double dpi = ps.CompositionTarget.TransformToDevice.M11;
                    if (dpi > 0) return dpi;
                }
            }
            catch { }
            return 1.0;
        }

        /// <summary>No taskbar entry, never steals focus, never activates.</summary>
        void ApplyWindowStyles(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            int exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
                exStyle | (int)Native.WS_EX_TOOLWINDOW | (int)Native.WS_EX_NOACTIVATE);
        }

        /// <summary>
        /// Moves the window under the desktop icons (child of the WorkerW layer),
        /// positioned relative to that host's origin. The window stays hidden.
        /// </summary>
        bool ParentIntoHost()
        {
            if (!_hostReady || _ourHwnd == IntPtr.Zero) return false;
            if (!Native.IsWindow(_ourHwnd)) return false;
            if (!Native.IsWindow(_injectedHost))
            {
                Logger.Warn("Desktop host 0x" + _injectedHost.ToInt64().ToString("X") + " is gone before parenting.");
                return false;
            }

            if (!Native.SetParent(_ourHwnd, _injectedHost))
            {
                Logger.Warn(string.Format(
                    "SetParent into the wallpaper layer failed (error {0}, hwnd=0x{1:X}, host=0x{2:X}).",
                    Marshal.GetLastWin32Error(), _ourHwnd.ToInt64(), _injectedHost.ToInt64()));
                return false;
            }

            var child = Native.GetAncestor(_ourHwnd, Native.GA_PARENT);
            if (child != _injectedHost)
            {
                Logger.Warn(string.Format(
                    "Wallpaper window is not a child of the desktop host (parent=0x{0:X}, host=0x{1:X}).",
                    child.ToInt64(), _injectedHost.ToInt64()));
                return false;
            }

            ApplyHostRelativeRect();
            IsParented = true;
            Logger.Info("Wallpaper parented into desktop host before playback (" +
                        WorkerWInjector.Describe(_injectedHost) + ").");
            return true;
        }

        /// <summary>
        /// Safety net: some Media Foundation decoders only start presenting once
        /// their window is visible. If no frame arrives quickly we reveal the
        /// surface anyway, which keeps that decoder running instead of failing
        /// silently. While a previous wallpaper is still playing the new window
        /// sits *below* it in the host, so this costs no visible flash.
        /// </summary>
        void StartRevealFallback()
        {
            StopRevealFallback();
            _revealFallback = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background);
            _revealFallback.Interval = TimeSpan.FromMilliseconds(900);
            _revealFallback.Tick += (s, e) =>
            {
                StopRevealFallback();
                if (_closed || HasFirstFrame || !IsParented) return;
                Logger.Warn("Revealing the wallpaper surface before its first frame on " +
                            _monitor.DeviceName + " (decoder did not start on a hidden window).");
                ShowSurface();
            };
            _revealFallback.Start();
        }

        void StopRevealFallback()
        {
            if (_revealFallback == null) return;
            _revealFallback.Stop();
            _revealFallback = null;
        }

        /// <summary>Positions the (child) window over its monitor in host coordinates.</summary>
        bool ApplyHostRelativeRect()
        {
            try
            {
                if (_ourHwnd == IntPtr.Zero || !Native.IsWindow(_ourHwnd)) return false;
                var r = _monitor.ScreenRect;
                var host = new Native.RECT();
                if (!Native.IsWindow(_injectedHost) || !Native.GetWindowRect(_injectedHost, out host))
                    host = new Native.RECT();

                return Native.SetWindowPos(_ourHwnd, IntPtr.Zero,
                    r.Left - host.Left, r.Top - host.Top,
                    Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top),
                    Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Could not position the wallpaper window", e));
                return false;
            }
        }

        /// <summary>
        /// Reveals the surface inside the wallpaper layer. Only ever called with
        /// a decoded frame ready, or from the repair path — never at creation.
        /// </summary>
        public void ShowSurface()
        {
            if (_closed || _unableToPlay) return;
            if (!_hostReady || !IsParented) return;
            if (_ourHwnd == IntPtr.Zero || !Native.IsWindow(_ourHwnd)) return;

            try
            {
                ApplyHostRelativeRect();
                Native.ShowWindow(_ourHwnd, Native.SW_SHOWNOACTIVATE);
                // Stay at the bottom of our host so Explorer's own children and
                // the icon view keep painting above us.
                Native.SetWindowPos(_ourHwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                Visible = true;
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Reveal inside the wallpaper layer failed", e));
            }
        }

        /// <summary>Called when the video opened; the content is now 'real'.</summary>
        void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_closed) return;
                ApplyScaling();
                UpdatePlayback();
                Logger.Info("Media opened: " + _media.NaturalVideoWidth + "x" + _media.NaturalVideoHeight +
                            " on " + _monitor.DeviceName + ".");
                HasFirstFrame = true;
                StopRevealFallback();

                // Give the render pass one turn to compose the first frame, then
                // reveal. This is one dispatcher tick, not a timed poll loop.
                QueueReveal();

                var h = Opened;
                if (h != null) h();
            }
            catch (Exception ex)
            {
                Logger.Error(new Exception("MediaOpened handling failed for '" + _item.FilePath + "'", ex));
            }
        }

        void QueueReveal()
        {
            if (_revealQueued || _closed) return;
            _revealQueued = true;
            try
            {
                var d = Window != null ? Window.Dispatcher : System.Windows.Threading.Dispatcher.CurrentDispatcher;
                d.BeginInvoke(new Action(() =>
                {
                    _revealQueued = false;
                    Guarded(ShowSurface, "reveal");
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Could not queue the wallpaper reveal", e));
            }
        }

        void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_closed) return;
                // Seamless loop: rewind inside the same decoder session.
                _media.Position = TimeSpan.Zero;
                UpdatePlayback();
            }
            catch (Exception ex)
            {
                Logger.Error(new Exception("Loop rewind failed", ex));
            }
        }

        void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (_unableToPlay) return;
            _unableToPlay = true;
            Logger.Error(new Exception(
                "Playback failed for '" + _item.FilePath + "' on " +
                (_monitor != null && !string.IsNullOrEmpty(_monitor.DeviceName) ? _monitor.DeviceName : "monitor") +
                ": " + (e != null && e.ErrorException != null ? e.ErrorException.ToString() : "unknown")));
            var h = MediaFailed;
            if (h != null) h(this, _item, HumanPlaybackMessage());
        }

        static string HumanPlaybackMessage()
        {
            return "Windows could not decode this video. The file may use a codec that\n" +
                   "is not installed (this is common for some MOV files and for H.265/HEVC\n" +
                   "when the Windows HEVC extensions are missing).\n\n" +
                   "Suggestions:\n" +
                   "  • Use an H.264-encoded MP4 file, or re-encode with a free tool.\n" +
                   "  • Install 'HEVC Video Extensions' from the Microsoft Store.\n" +
                   "  • Verify the file plays in the Movies & TV app.";
        }
        #endregion

        #region Playback control

        bool _fsPaused, _manualPaused, _envPaused;

        public void SetFullscreenPaused(bool paused) { _fsPaused = paused; UpdatePlayback(); }
        public void SetManualPaused(bool paused)   { _manualPaused = paused; UpdatePlayback(); }

        /// <summary>
        /// Pauses for reasons outside the app: locked session, display off,
        /// disconnected session or (optionally) running on battery. The decoder
        /// stops instead of burning CPU/GPU on frames nobody can see.
        /// </summary>
        public void SetEnvironmentPaused(bool paused) { _envPaused = paused; UpdatePlayback(); }

        /// <summary>Silently stop for shutdown or wallpaper switch.</summary>
        public void Shutdown()
        {
            _closed = true;
            StopRevealFallback();
            try { if (_ourHwnd != IntPtr.Zero && Native.IsWindow(_ourHwnd)) Native.ShowWindow(_ourHwnd, Native.SW_HIDE); }
            catch { }
            try { if (_media != null) _media.Stop(); } catch { }
            try { if (_media != null) _media.Close(); } catch { }
            try { if (Window != null) Window.Close(); }
            catch (Exception e) { Logger.Error(new Exception("Closing a wallpaper surface failed", e)); }
            Window = null;
            _ourHwnd = IntPtr.Zero;
            Visible = false;
            IsParented = false;
            _hostReady = false;
        }

        void UpdatePlayback()
        {
            try
            {
                if (_closed || _unableToPlay || _media == null || _media.Source == null) return;

                // A decoder that has not opened yet must not be paused: pausing
                // before the first frame leaves the WPF video surface blank when
                // playback is resumed later (that is what happened when the
                // screen was already unavailable during startup).
                bool opened = HasFirstFrame || _media.NaturalVideoWidth > 0;
                if (_fsPaused || _manualPaused || _envPaused)
                {
                    if (opened) _media.Pause();
                }
                else if (opened)
                {
                    _media.Play();
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("UpdatePlayback failed", e));
            }
        }
        #endregion

        #region Settings / scaling

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null) return;
            try
            {
                if (_media != null) _media.IsMuted = !settings.PlayWallpaperAudio;
            }
            catch (Exception e) { Logger.Error(e); }
            ApplyScaling();
        }

        /// <summary>
        /// Uses the MediaElement's own stretch modes: layout fills the monitor
        /// surface, so no manual transform math depends on timing.
        /// Fill = UniformToFill (crop), Fit = Uniform, Stretch = Fill.
        /// </summary>
        void ApplyScaling()
        {
            if (_closed || _media == null) return;
            _media.RenderTransform = Transform.Identity;

            // A span surface covers the whole combined desktop, so it is fitted
            // as one picture: stretching distorts, preserving keeps the video's
            // aspect ratio and leaves black bars in the unused area.
            if (_isSpan)
            {
                _media.Stretch = _settings.SpanFit == SpanFit.PreserveAspect
                    ? Stretch.Uniform
                    : Stretch.Fill;
                return;
            }

            switch (_settings.Scaling)
            {
                case ScalingMode.Stretch: _media.Stretch = Stretch.Fill; break;
                case ScalingMode.Fit:     _media.Stretch = Stretch.Uniform; break;
                default:                  _media.Stretch = Stretch.UniformToFill; break;
            }
        }
        #endregion

        /// <summary>Closes and releases everything for this surface.</summary>
        public void Dispose()
        {
            Shutdown();
        }
    }

    /// <summary>Lightweight monitor descriptor for engine use only.</summary>
    public class MonitorInfoLite
    {
        public IntPtr Handle;
        public Native.RECT ScreenRect;
        public bool IsPrimary;
        public string DeviceName;

        /// <summary>Stable identity (PnP monitor path, or the GDI name as fallback).</summary>
        public string Key = "";

        /// <summary>"Generic PnP Monitor" style description, may be empty.</summary>
        public string FriendlyName = "";

        public int Width { get { return Math.Abs(ScreenRect.Right - ScreenRect.Left); } }
        public int Height { get { return Math.Abs(ScreenRect.Bottom - ScreenRect.Top); } }

        /// <summary>Short label for lists: "Monitor 1 · 1920×1080 (Primary)".</summary>
        public string Label
        {
            get { return (DeviceName ?? "Monitor") + "  ·  " + Width + "×" + Height + (IsPrimary ? "  (Primary)" : ""); }
        }

        public override string ToString()
        {
            return DeviceName + "  " + Width + "×" + Height + (IsPrimary ? "  (Primary)" : "");
        }
    }
}
