using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace FluxWall.Settings
{
    public enum ScalingMode { Fill = 0, Fit = 1, Stretch = 2 }
    public enum MonitorChoice { Primary = 0, All = 1, Selected = 2 }

    /// <summary>
    /// How wallpapers are laid out across monitors.
    /// SameAll (mode C) is the 1.2.x behaviour and the default for upgraded users.
    /// </summary>
    public enum DisplayMode { SameAll = 0, Independent = 1, Span = 2 }

    /// <summary>How the single span video is fitted into the combined desktop.</summary>
    public enum SpanFit { Stretch = 0, PreserveAspect = 1 }

    /// <summary>One saved monitor → wallpaper pairing.</summary>
    public class MonitorAssignment
    {
        /// <summary>Stable monitor key (see MonitorService.KeyFor).</summary>
        public string MonitorKey { get; set; } = "";
        public string WallpaperId { get; set; } = "";
    }

    /// <summary>Visualizer drawing styles.</summary>
    public enum VisualizerStyle { Bars = 0, Waveform = 1, Circle = 2 }

    /// <summary>Where the visualizer sits. Custom means the saved X/Y wins.</summary>
    public enum VisualizerAnchor
    {
        Custom = 0, BottomCenter = 1, BottomLeft = 2, BottomRight = 3,
        TopLeft = 4, TopRight = 5, Center = 6,
    }

    /// <summary>User-visible application settings. Defaults are sane for a brand-new user.</summary>
    [XmlRoot("VulcansLivingWP-Settings")]
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool AutoPauseFullscreen { get; set; } = true;

        /// <summary>
        /// Optional: stop the wallpaper while the computer runs on battery.
        /// Off by default, so a desktop or a plugged-in laptop behaves as before.
        /// </summary>
        public bool PauseOnBattery { get; set; } = false;
        public bool RestoreLastWallpaper { get; set; } = true;
        public bool ShowTrayIcon { get; set; } = true;
        public bool FirstRunDone { get; set; } = false;

        public MonitorChoice TargetMonitors { get; set; } = MonitorChoice.Primary;
        public string SelectedMonitorDevice { get; set; } = "";
        public ScalingMode Scaling { get; set; } = ScalingMode.Fill;

        #region Multi-monitor layout (1.3.0)
        // Upgrading users keep the 1.2.x behaviour: one video on the monitors
        // selected above. Independent and span layouts are opt-in.

        public DisplayMode DisplayMode { get; set; } = DisplayMode.SameAll;

        /// <summary>Per-monitor choices used by Independent mode.</summary>
        public List<MonitorAssignment> MonitorAssignments { get; set; } = new List<MonitorAssignment>();

        /// <summary>Monitor keys taking part in span mode. Empty = every monitor.</summary>
        public List<string> SpanMonitors { get; set; } = new List<string>();

        /// <summary>Video used by span mode, kept separately so mode switching is lossless.</summary>
        public string SpanWallpaperId { get; set; } = "";

        public SpanFit SpanFit { get; set; } = SpanFit.Stretch;

        /// <summary>
        /// Schema marker for settings migrations. 0 = written by 1.2.x or older.
        /// </summary>
        public int SettingsSchema { get; set; } = 0;
        #endregion

        #region Updates (1.3.0)
        public bool CheckForUpdates { get; set; } = true;
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        /// <summary>Last version the user was told about, so startup never nags.</summary>
        public string DismissedUpdateVersion { get; set; } = "";

        // Last successful answer, cached so the About page can show the update
        // state offline without contacting GitHub again.
        public string LatestKnownVersion { get; set; } = "";
        public string LatestReleaseUrl { get; set; } = "";
        public string LatestReleaseNotes { get; set; } = "";
        #endregion

        public string PauseHotkey { get; set; } = "Ctrl+Alt+W";
        public bool PlayWallpaperAudio { get; set; } = false;

        /// <summary>Rotate through the library while the wallpaper plays.</summary>
        public bool AutoSwitchWallpapers { get; set; } = false;

        /// <summary>Minutes between automatic changes. Only the listed choices are valid.</summary>
        public int AutoSwitchMinutes { get; set; } = DefaultAutoSwitchMinutes;

        public const int DefaultAutoSwitchMinutes = 5;

        #region Visualizer settings
        // The visualizer is strictly optional and off for every new user, so an
        // existing install behaves exactly as before until it is switched on.
        // Every value below is validated by Normalize(), which means a hand-edited
        // or corrupt settings file can never break rendering or capture.

        public bool VisualizerEnabled { get; set; } = false;
        public VisualizerStyle VisualizerStyle { get; set; } = VisualizerStyle.Bars;

        /// <summary>Hex colour, "#RRGGBB".</summary>
        public string VisualizerColor { get; set; } = DefaultVisualizerColor;

        /// <summary>10 – 100.</summary>
        public int VisualizerOpacity { get; set; } = 80;

        /// <summary>Width in physical pixels, 160 – 1600.</summary>
        public int VisualizerWidth { get; set; } = 520;

        /// <summary>Height in physical pixels, 90 – 900.</summary>
        public int VisualizerHeight { get; set; } = 180;

        public VisualizerAnchor VisualizerAnchor { get; set; } = VisualizerAnchor.BottomCenter;

        /// <summary>Physical screen position, only used when the anchor is Custom.</summary>
        public int VisualizerX { get; set; } = 0;
        public int VisualizerY { get; set; } = 0;

        /// <summary>Audio gain in percent, 20 – 400.</summary>
        public int VisualizerSensitivity { get; set; } = 100;

        /// <summary>Number of frequency bars / spectrum segments, 8 – 96.</summary>
        public int VisualizerBars { get; set; } = 48;

        /// <summary>Smoothing amount in percent, 0 – 95.</summary>
        public int VisualizerSmoothing { get; set; } = 60;

        public bool VisualizerPeaks { get; set; } = true;

        /// <summary>Locked = click-through and immovable; the safe default.</summary>
        public bool VisualizerLocked { get; set; } = true;

        public const string DefaultVisualizerColor = "#7C5CFC";

        /// <summary>True once the user has an explicit position of their own.</summary>
        public bool VisualizerHasCustomPosition
        {
            get { return VisualizerAnchor == VisualizerAnchor.Custom; }
        }
        #endregion

        /// <summary>Intervals offered by the settings page, in minutes.</summary>
        public static readonly int[] AutoSwitchChoices = { 1, 5, 10, 15, 30, 60 };

        /// <summary>The saved interval, or the default when the file contains something else.</summary>
        public int EffectiveAutoSwitchMinutes
        {
            get { return Array.IndexOf(AutoSwitchChoices, AutoSwitchMinutes) >= 0
                    ? AutoSwitchMinutes
                    : DefaultAutoSwitchMinutes; }
        }

        /// <summary>
        /// Repairs values that older releases (or an edited settings.xml) could
        /// leave behind. Existing keys that are still valid are never touched.
        /// </summary>
        public void Normalize()
        {
            if (AutoSwitchMinutes != EffectiveAutoSwitchMinutes)
                AutoSwitchMinutes = EffectiveAutoSwitchMinutes;
            if (PauseHotkey == null) PauseHotkey = "";
            if (SelectedMonitorDevice == null) SelectedMonitorDevice = "";
            if (ActiveWallpaperId == null) ActiveWallpaperId = "";
            if (SpanWallpaperId == null) SpanWallpaperId = "";
            if (DismissedUpdateVersion == null) DismissedUpdateVersion = "";
            if (LatestKnownVersion == null) LatestKnownVersion = "";
            if (LatestReleaseUrl == null) LatestReleaseUrl = "";
            if (LatestReleaseNotes == null) LatestReleaseNotes = "";

            NormalizeMonitorLayout();

            if (!Enum.IsDefined(typeof(VisualizerStyle), VisualizerStyle)) VisualizerStyle = VisualizerStyle.Bars;
            if (!Enum.IsDefined(typeof(VisualizerAnchor), VisualizerAnchor)) VisualizerAnchor = VisualizerAnchor.BottomCenter;
            VisualizerOpacity = Clamp(VisualizerOpacity, MinVisualizerOpacity, 100, 80);
            VisualizerWidth = Clamp(VisualizerWidth, MinVisualizerWidth, 1600, 520);
            VisualizerHeight = Clamp(VisualizerHeight, MinVisualizerHeight, 900, 180);
            VisualizerSensitivity = Clamp(VisualizerSensitivity, 20, 400, 100);
            VisualizerBars = Clamp(VisualizerBars, 8, 96, 48);
            VisualizerSmoothing = Clamp(VisualizerSmoothing, 0, 95, 60);
            VisualizerX = Clamp(VisualizerX, -20000, 20000, 0);
            VisualizerY = Clamp(VisualizerY, -20000, 20000, 0);
            if (!IsValidVisualizerColor(VisualizerColor)) VisualizerColor = DefaultVisualizerColor;
        }

        /// <summary>
        /// Repairs the 1.3.0 layout fields and migrates 1.2.x settings. Upgraded
        /// users get SameAll with their existing single wallpaper and monitor
        /// choice, which is exactly what 1.2.3 did.
        /// </summary>
        void NormalizeMonitorLayout()
        {
            if (MonitorAssignments == null) MonitorAssignments = new List<MonitorAssignment>();
            if (SpanMonitors == null) SpanMonitors = new List<string>();

            if (!Enum.IsDefined(typeof(DisplayMode), DisplayMode)) DisplayMode = DisplayMode.SameAll;
            if (!Enum.IsDefined(typeof(SpanFit), SpanFit)) SpanFit = SpanFit.Stretch;

            // Drop junk entries and keep the first choice for a monitor key, so a
            // hand-edited file cannot produce two wallpapers for one screen.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clean = new List<MonitorAssignment>();
            foreach (var a in MonitorAssignments)
            {
                if (a == null) continue;
                var key = (a.MonitorKey ?? "").Trim();
                if (key.Length == 0 || !seen.Add(key)) continue;
                clean.Add(new MonitorAssignment { MonitorKey = key, WallpaperId = a.WallpaperId ?? "" });
            }
            MonitorAssignments = clean;

            var spanSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var span = new List<string>();
            foreach (var k in SpanMonitors)
            {
                var key = (k ?? "").Trim();
                if (key.Length > 0 && spanSeen.Add(key)) span.Add(key);
            }
            SpanMonitors = span;

            // 0 → 1: nothing to convert beyond the defaults above (SameAll plus the
            // saved ActiveWallpaperId reproduces 1.2.x behaviour exactly).
            if (SettingsSchema < 1) SettingsSchema = 1;
        }

        /// <summary>Saved wallpaper for one monitor, or an empty string.</summary>
        public string AssignmentFor(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey) || MonitorAssignments == null) return "";
            foreach (var a in MonitorAssignments)
                if (a != null && string.Equals(a.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase))
                    return a.WallpaperId ?? "";
            return "";
        }

        /// <summary>Stores (or clears, with an empty id) one monitor's wallpaper.</summary>
        public void Assign(string monitorKey, string wallpaperId)
        {
            if (string.IsNullOrEmpty(monitorKey)) return;
            if (MonitorAssignments == null) MonitorAssignments = new List<MonitorAssignment>();
            foreach (var a in MonitorAssignments)
            {
                if (a != null && string.Equals(a.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    a.WallpaperId = wallpaperId ?? "";
                    return;
                }
            }
            MonitorAssignments.Add(new MonitorAssignment { MonitorKey = monitorKey, WallpaperId = wallpaperId ?? "" });
        }

        public const int MinVisualizerOpacity = 10;
        public const int MinVisualizerWidth = 160;
        public const int MinVisualizerHeight = 90;

        static int Clamp(int value, int min, int max, int fallback)
        {
            if (value < min || value > max) return fallback;
            return value;
        }

        /// <summary>Accepts "#RRGGBB" and "#AARRGGBB"; anything else is rejected.</summary>
        public static bool IsValidVisualizerColor(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var s = value.Trim();
            if (s.Length != 7 && s.Length != 9) return false;
            if (s[0] != '#') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>Id of the wallpaper that should be restored on next start. Empty = none.</summary>
        [System.ComponentModel.DefaultValue("")]
        public string ActiveWallpaperId { get; set; } = "";

        public event Action Changed;

        public void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }
    }

    /// <summary>Loads/saves settings.xml atomically. Never throws to the caller.</summary>
    public static class SettingsStore
    {
        static AppSettings _current;

        /// <summary>
        /// Test hook: when set, Load/Save use this file instead of the user's real
        /// settings.xml. The automated visualizer tests rely on it so they can prove
        /// persistence and corrupt-file recovery without touching live settings.
        /// </summary>
        public static string PathOverride { get; set; }

        static string FilePath
        {
            get { return string.IsNullOrEmpty(PathOverride) ? Common.Paths.SettingsFile : PathOverride; }
        }

        public static AppSettings Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Load()
        {
            // Start from defaults: a corrupt file must yield a working app, never
            // "whatever was in memory before".
            _current = null;
            try
            {
                var path = FilePath;
                if (File.Exists(path))
                {
                    var ser = new XmlSerializer(typeof(AppSettings));
                    using (var fs = File.OpenRead(path))
                        _current = (AppSettings)ser.Deserialize(fs);
                }
            }
            catch (Exception e)
            {
                Common.Logger.Warn("settings.xml unreadable — using defaults. " + e.Message);
            }
            if (_current == null) _current = new AppSettings();
            _current.Normalize();
        }

        public static void Save()
        {
            try
            {
                if (_current == null) return;
                var path = FilePath;
                var tmp = path + ".tmp";
                var ser = new XmlSerializer(typeof(AppSettings));
                using (var fs = File.Create(tmp))
                    ser.Serialize(fs, _current);
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Common.Logger.Warn("settings.xml save failed. " + e.Message);
            }
        }
    }
}
