using System;
using System.IO;
using System.Xml.Serialization;

namespace FluxWall.Settings
{
    public enum ScalingMode { Fill = 0, Fit = 1, Stretch = 2 }
    public enum MonitorChoice { Primary = 0, All = 1, Selected = 2 }

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
