using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FluxWall.Audio;
using FluxWall.Common;
using FluxWall.Settings;

namespace FluxWall.Visualizer
{
    /// <summary>
    /// Owns the whole optional feature: WASAPI loopback capture, the analyser,
    /// the transparent overlay and the 30 fps render loop.
    ///
    /// Lifecycle rules:
    ///  - disabled → no capture, no window, no timers (exactly the old app);
    ///  - enabled + silent → capture keeps running (that is how sound is noticed)
    ///    but rendering slows to 4 Hz and paints nothing;
    ///  - fullscreen (and auto-pause on) → window hidden and capture stopped, so
    ///    a game is never covered and no audio work happens in the background.
    /// </summary>
    public sealed class VisualizerService : IDisposable
    {
        static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(33);  // ~30 fps
        static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(250);   // silent screen

        readonly AppSettings _settings;
        readonly Dispatcher _dispatcher;
        readonly float[] _bars = new float[VisualizerElement.MaxBars];
        readonly float[] _peaks = new float[VisualizerElement.MaxBars];
        readonly float[] _wave = new float[VisualizerElement.WavePoints];

        VisualizerWindow _window;
        LoopbackCapture _capture;
        SpectrumAnalyzer _analyzer;
        DispatcherTimer _timer;
        bool _disposed;
        bool _suspended;
        int _lastProduced = -1;
        int _framesRendered;
        string _status = "";
        string _error = "";
        int _barCapacity;

        public VisualizerService(FluxWallApp app)
            : this(app.Settings, app.Dispatcher)
        {
        }

        /// <summary>Test-friendly constructor: no dependency on the rest of the app.</summary>
        public VisualizerService(AppSettings settings, Dispatcher dispatcher)
        {
            _settings = settings;
            _dispatcher = dispatcher;
        }

        AppSettings Settings { get { return _settings; } }

        public bool IsEnabled { get { return Settings.VisualizerEnabled; } }
        public bool IsCapturing { get { return _capture != null && _capture.IsCapturing; } }
        public bool HasCapture { get { return _capture != null; } }

        /// <summary>Live analyser (used by the self-test and for diagnostics).</summary>
        public SpectrumAnalyzer Analyzer { get { return _analyzer; } }

        /// <summary>Latest overall loudness 0–1 (refreshed by the render loop).</summary>
        public float CurrentLevel { get; private set; }

        /// <summary>True while the captured stream is effectively silent.</summary>
        public bool IsSilent { get; private set; }
        public bool IsSuspended { get { return _suspended; } }
        public bool IsWindowVisible { get { return _window != null && _window.IsVisible; } }
        public int FramesRendered { get { return _framesRendered; } }
        public string Status { get { return _status; } }
        public string LastError { get { return _error; } }
        public string DeviceName { get { return _capture != null ? _capture.DeviceName : ""; } }
        public VisualizerWindow Window { get { return _window; } }

        /// <summary>Raised when the capture status text changes (UI hint).</summary>
        public event Action StatusChanged;

        #region Public commands

        /// <summary>Brings capture, window and rendering in line with the settings.</summary>
        public void Apply()
        {
            if (_disposed) return;
            try
            {
                var s = Settings;
                if (!s.VisualizerEnabled)
                {
                    StopEverything();
                    return;
                }

                EnsureWindow();
                var color = ParseColor(s.VisualizerColor);
                _window.Element.CurrentBars = s.VisualizerBars;
                _window.ApplyStyle(s.VisualizerStyle, color, s.VisualizerOpacity,
                    s.VisualizerPeaks, s.VisualizerBars);
                _window.ApplyGeometry(ResolveX(s), ResolveY(s), s.VisualizerWidth, s.VisualizerHeight);
                _window.SetLocked(s.VisualizerLocked);

                if (_suspended)
                {
                    _window.Hide();
                    StopCapture();
                }
                else
                {
                    _window.Show();
                    EnsureAnalyzer(s.VisualizerBars, _analyzer != null ? _analyzer.SampleRate : 48000);
                    EnsureTimer();
                    StartCapture();
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Visualizer could not be applied", e));
                _error = e.Message;
            }
        }

        public void SetEnabled(bool enabled)
        {
            var s = Settings;
            s.VisualizerEnabled = enabled;
            if (enabled && !s.VisualizerHasCustomPosition)
            {
                // First run: start in edit mode so the user can place it right away.
                s.VisualizerLocked = false;
            }
            SettingsStore.Save();
            Apply();
            if (enabled && !s.VisualizerLocked)
                RaiseStatus("Editing: drag the visualizer, then lock it.");
        }

        public void SetLocked(bool locked)
        {
            Settings.VisualizerLocked = locked;
            SettingsStore.Save();
            if (_window != null) _window.SetLocked(locked);
            RaiseStatus(locked ? "Locked: clicks pass through." : "Editing: drag the visualizer, then lock it.");
        }

        /// <summary>Tray/settings helper: unlock, show, and report how to finish.</summary>
        public void BeginEdit()
        {
            if (!Settings.VisualizerEnabled)
            {
                SetEnabled(true);
                return;
            }
            if (_suspended)
            {
                RaiseStatus("Editing is unavailable while a fullscreen app is in front.");
                return;
            }
            if (_window == null) EnsureWindow();
            _window.Show();
            SetLocked(false);
            _window.Element.Editing = true;
        }

        /// <summary>Restores default size, position, colour, opacity and response.</summary>
        public void ResetAppearance()
        {
            var s = Settings;
            s.VisualizerStyle = VisualizerStyle.Bars;
            s.VisualizerColor = AppSettings.DefaultVisualizerColor;
            s.VisualizerOpacity = 80;
            s.VisualizerWidth = 520;
            s.VisualizerHeight = 180;
            s.VisualizerAnchor = VisualizerAnchor.BottomCenter;
            s.VisualizerX = 0;
            s.VisualizerY = 0;
            s.VisualizerSensitivity = 100;
            s.VisualizerBars = 48;
            s.VisualizerSmoothing = 60;
            s.VisualizerPeaks = true;
            s.VisualizerLocked = true;
            SettingsStore.Save();
            Apply();
            RaiseStatus("Visualizer appearance reset to defaults.");
        }

        /// <summary>Called by the app when fullscreen activity starts or ends.</summary>
        public void SetFullscreenActive(bool active)
        {
            bool shouldSuspend = active && Settings.AutoPauseFullscreen;
            if (shouldSuspend == _suspended) return;
            _suspended = shouldSuspend;
            Logger.Info(shouldSuspend
                ? "Visualizer paused for fullscreen activity."
                : "Visualizer resumed.");
            Apply();
        }

        public void Shutdown()
        {
            StopEverything();
        }

        #endregion

        #region Window / geometry

        void EnsureWindow()
        {
            if (_window != null) return;

            _window = new VisualizerWindow();
            _window.GeometryChanged += OnGeometryChanged;
            _window.Element.MouseLeftButtonUp += (s, e) => PersistLockHint();
            _window.Window.Closed += (s, e) =>
            {
                _window = null;
                StopTimer();
            };
            Logger.Info("Visualizer window created.");
        }

        void PersistLockHint()
        {
            if (Settings.VisualizerLocked) return;
            RaiseStatus("Position saved. Use Lock when you are happy with it.");
        }

        void OnGeometryChanged(int x, int y, int width, int height)
        {
            try
            {
                var s = Settings;
                s.VisualizerX = x;
                s.VisualizerY = y;
                s.VisualizerWidth = Math.Max(AppSettings.MinVisualizerWidth, width);
                s.VisualizerHeight = Math.Max(AppSettings.MinVisualizerHeight, height);
                s.VisualizerAnchor = VisualizerAnchor.Custom;
                SettingsStore.Save();
                Logger.Info(string.Format(
                    "Visualizer geometry saved: {0}x{1} at {2},{3}.", width, height, x, y));
            }
            catch (Exception e) { Logger.Error(e); }
        }

        int ResolveX(AppSettings s)
        {
            var wa = SystemParameters.WorkArea;
            if (s.VisualizerAnchor == VisualizerAnchor.Custom && IsOnScreen(s.VisualizerX, s.VisualizerY))
                return s.VisualizerX;

            double scale = _window != null ? _window.Scale : 1.0;
            double left = wa.Left * scale;
            double right = (wa.Left + wa.Width) * scale;
            switch (s.VisualizerAnchor)
            {
                case VisualizerAnchor.BottomLeft:
                case VisualizerAnchor.TopLeft:
                    return (int)(left + 24 * scale);
                case VisualizerAnchor.BottomRight:
                case VisualizerAnchor.TopRight:
                    return (int)(right - s.VisualizerWidth - 24 * scale);
                default:
                    return (int)(left + (wa.Width * scale - s.VisualizerWidth) / 2);
            }
        }

        int ResolveY(AppSettings s)
        {
            var wa = SystemParameters.WorkArea;
            if (s.VisualizerAnchor == VisualizerAnchor.Custom && IsOnScreen(s.VisualizerX, s.VisualizerY))
                return s.VisualizerY;

            double scale = _window != null ? _window.Scale : 1.0;
            double top = wa.Top * scale;
            double bottom = (wa.Top + wa.Height) * scale;
            switch (s.VisualizerAnchor)
            {
                case VisualizerAnchor.TopLeft:
                case VisualizerAnchor.TopRight:
                    return (int)(top + 24 * scale);
                case VisualizerAnchor.Center:
                    return (int)(top + (wa.Height * scale - s.VisualizerHeight) / 2);
                default:
                    return (int)(bottom - s.VisualizerHeight - 24 * scale);
            }
        }

        /// <summary>A saved position that is no longer on any screen is ignored.</summary>
        bool IsOnScreen(int x, int y)
        {
            double scale = _window != null ? _window.Scale : 1.0;
            double vx = SystemParameters.VirtualScreenLeft * scale;
            double vy = SystemParameters.VirtualScreenTop * scale;
            double vw = SystemParameters.VirtualScreenWidth * scale;
            double vh = SystemParameters.VirtualScreenHeight * scale;
            // A single visible pixel is enough to keep the saved position.
            return x + 40 >= vx && x <= vx + vw - 40 && y + 20 >= vy && y <= vy + vh - 20;
        }

        public static Color ParseColor(string value)
        {
            try
            {
                if (AppSettings.IsValidVisualizerColor(value))
                {
                    var s = value.Trim().TrimStart('#');
                    if (s.Length == 8)
                    {
                        return Color.FromArgb(
                            Convert.ToByte(s.Substring(0, 2), 16),
                            Convert.ToByte(s.Substring(2, 2), 16),
                            Convert.ToByte(s.Substring(4, 2), 16),
                            Convert.ToByte(s.Substring(6, 2), 16));
                    }
                    if (s.Length == 6)
                    {
                        return Color.FromRgb(
                            Convert.ToByte(s.Substring(0, 2), 16),
                            Convert.ToByte(s.Substring(2, 2), 16),
                            Convert.ToByte(s.Substring(4, 2), 16));
                    }
                }
            }
            catch { }
            return (Color)ColorConverter.ConvertFromString(AppSettings.DefaultVisualizerColor);
        }

        #endregion

        #region Capture / analysis / render

        void EnsureAnalyzer(int bars, int sampleRate)
        {
            int cap = Math.Max(8, Math.Min(VisualizerElement.MaxBars, bars));
            if (_analyzer == null)
            {
                _analyzer = new SpectrumAnalyzer(sampleRate, cap);
                _barCapacity = cap;
            }
            else if (cap != _barCapacity)
            {
                _analyzer.Configure(_analyzer.SampleRate, cap);
                _barCapacity = cap;
                _lastProduced = -1;
            }
            var s = Settings;
            _analyzer.SetResponse(s.VisualizerSensitivity, s.VisualizerSmoothing, s.VisualizerPeaks);
        }

        void StartCapture()
        {
            if (_capture != null) return;
            _capture = new LoopbackCapture();
            _capture.Status += OnCaptureStatus;
            _capture.DataAvailable += OnAudio;
            _capture.Start();
        }

        void StopCapture()
        {
            var capture = _capture;
            _capture = null;
            if (capture == null) return;
            capture.DataAvailable -= OnAudio;
            capture.Status -= OnCaptureStatus;
            try { capture.Dispose(); } catch (Exception e) { Logger.Error(e); }
            _lastProduced = -1;
        }

        void OnCaptureStatus(string message)
        {
            _status = message;
            if (message.StartsWith("Audio capture unavailable", StringComparison.OrdinalIgnoreCase))
                _error = message;
            else
                _error = "";
            RaiseStatus(message);
        }

        void OnAudio(float[] buffer, int count)
        {
            var analyzer = _analyzer;
            if (analyzer == null) return;
            try
            {
                var capture = _capture;
                if (capture != null)
                {
                    var fmt = capture.Format;
                    if (fmt.SampleRate > 0 && fmt.SampleRate != analyzer.SampleRate)
                    {
                        analyzer.Configure(fmt.SampleRate, analyzer.Bars);
                        Logger.Info("Audio analysis resampled for " + fmt.SampleRate + " Hz device output.");
                    }
                }
                analyzer.Write(buffer, count);
                analyzer.Step();
            }
            catch (Exception e) { Logger.Error(new Exception("Audio analysis failed", e)); }
        }

        void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
                {
                    Interval = ActiveInterval,
                };
                _timer.Tick += (s, e) => RenderTick();
            }
            if (!_timer.IsEnabled) _timer.Start();
        }

        void StopTimer()
        {
            if (_timer == null) return;
            try { _timer.Stop(); } catch { }
            _timer = null;
        }

        void RenderTick()
        {
            if (_window == null || _analyzer == null) return;
            try
            {
                var s = Settings;
                _analyzer.SetResponse(s.VisualizerSensitivity, s.VisualizerSmoothing, s.VisualizerPeaks);

                float level;
                bool silent;
                int produced;
                _analyzer.CopyFrame(_bars, _peaks, _wave, out level, out silent, out produced);
                CurrentLevel = level;
                IsSilent = silent;

                bool fresh = produced != _lastProduced;
                if (fresh)
                {
                    _lastProduced = produced;
                    _window.Element.SetFrame(_bars, _peaks, _wave, level);
                    _framesRendered++;
                }

                bool active = level > 0.001f;
                if (!active)
                {
                    for (int i = 0; i < _bars.Length; i++)
                        if (_bars[i] > 0.004f) { active = true; break; }
                }
                if (!active)
                {
                    for (int i = 0; i < _wave.Length; i++)
                        if (Math.Abs(_wave[i]) > 0.004f) { active = true; break; }
                }

                // Nothing on screen and nothing to show: probe only 4×/s. Any sound
                // brings the 30 fps loop straight back.
                var wanted = active ? ActiveInterval : IdleInterval;
                if (_timer != null && _timer.Interval != wanted) _timer.Interval = wanted;
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Visualizer render tick failed", e));
            }
        }

        #endregion

        void StopEverything()
        {
            StopTimer();
            StopCapture();
            _window?.Hide();
            _lastProduced = -1;
        }

        void RaiseStatus(string message)
        {
            try
            {
                var handler = StatusChanged;
                if (handler != null) handler();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopEverything();
            try { _capture?.Dispose(); } catch { }
            _capture = null;
            try { _window?.Dispose(); } catch { }
            _window = null;
            _analyzer = null;
        }
    }
}
