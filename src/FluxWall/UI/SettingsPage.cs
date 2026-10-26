using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.Settings;

namespace FluxWall.UI
{
    /// <summary>Settings page: everyday options are plain toggles; nothing technical.</summary>
    public class SettingsPage
    {
        readonly FluxWallApp _app;
        Grid _page;
        TextBox _hotkeyBox;
        TextBlock _hotkeyStatus;
        VisualizerSection _visualizer;

        public VisualizerSection Visualizer { get { return _visualizer; } }

        public SettingsPage(FluxWallApp app)
        {
            _app = app;
            Build();
        }

        public object Content { get { return _page; } }

        void Build()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(32, 26, 32, 26),
            };

            var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            scroll.Content = root;

            root.Children.Add(Header("Wallpaper"));
            root.Children.Add(SettingsCard(BuildWallpaperSection()));
            root.Children.Add(Space());

            root.Children.Add(Header("Visualizer"));
            _visualizer = new VisualizerSection(_app);
            root.Children.Add(SettingsCard(_visualizer.Content));
            root.Children.Add(Space());

            root.Children.Add(Header("Behaviour"));
            root.Children.Add(SettingsCard(BuildBehaviourSection()));
            root.Children.Add(Space());

            root.Children.Add(Header("Keyboard"));
            root.Children.Add(SettingsCard(BuildHotkeySection()));
            root.Children.Add(Space());

            var sa = Theme.QuietButton("Open log folder", () => _app.OpenLogFolder());
            sa.Margin = new Thickness(0, 0, 0, 10);
            root.Children.Add(sa);

            _page = new Grid { Background = Theme.BaseBgB };
            _page.Children.Add(scroll);
        }

        TextBlock Header(string text) => new TextBlock
        {
            Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextB, Margin = new Thickness(0, 0, 0, 10),
        };

        Border SettingsCard(FrameworkElement content)
        {
            return new Border
            {
                Background = Theme.PanelBgB,
                BorderBrush = Theme.BorderB,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 16, 18, 16),
                Child = content,
            };
        }

        FrameworkElement Space() => new Border { Height = 18 };

        #region Sections
        FrameworkElement BuildWallpaperSection()
        {
            var s = new StackPanel();

            // Monitor selection
            var monitorCombo = new ComboBox
            {
                MinWidth = 220,
                Background = Theme.ChipBgB,
                BorderBrush = Theme.BorderB,
                FontSize = 13,
            };
            var monitors = MonitorService.All();
            foreach (var m in monitors)
            {
                var comboItem = new ComboBoxItem { Content = m.ToString() };
                monitorCombo.Items.Add(comboItem);
            }            monitorCombo.Items.Add(new ComboBoxItem { Content = "All monitors" });
            monitorCombo.Items.Add(new ComboBoxItem { Content = "Primary monitor" });

            // select current
            var settings = _app.Settings;
            int idx;
            if (settings.TargetMonitors == MonitorChoice.All) idx = monitors.Count;
            else if (settings.TargetMonitors == MonitorChoice.Primary ||
                     settings.SelectedMonitorDevice == default(MonitorInfoLite).ToString())
                idx = monitors.Count + 1;
            else
            {
                idx = 0;
                for (int i = 0; i < monitors.Count; i++)
                    if (monitors[i].DeviceName == settings.SelectedMonitorDevice) { idx = i; break; }
            }
            if (idx < monitorCombo.Items.Count) monitorCombo.SelectedIndex = idx;

            monitorCombo.SelectionChanged += (s, e) =>
            {
                try
                {
                    var cx = monitorCombo.SelectedIndex;
                    if (cx < 0) return;
                    if (cx >= monitors.Count + 1) { settings.TargetMonitors = MonitorChoice.All; }
                    else if (cx == monitors.Count) { settings.TargetMonitors = MonitorChoice.Primary; }
                    else
                    {
                        settings.TargetMonitors = MonitorChoice.Selected;
                        settings.SelectedMonitorDevice = monitors[cx].DeviceName;
                    }
                    SettingsStore.Save();
                    if (_app.Engine.Current != null) _app.Engine.Reapply();
                }
                catch (Exception ex) { Logger.Error(ex); }
            };

            s.Children.Add(Theme.SettingsRow("Show wallpaper on",
                "Pick a monitor, or spread it across every display. For a different video on each " +
                "screen, or one video spanning several screens, use the Displays page.", monitorCombo));

            // Scaling
            var scalingCombo = new ComboBox { MinWidth = 220, FontSize = 13 };
            var scaleModes = new[] { "Fill (cover, crops edges)", "Fit (full video, black bars)", "Stretch (distorts to fit)" };
            foreach (var mode in scaleModes) scalingCombo.Items.Add(new ComboBoxItem { Content = mode });
            scalingCombo.SelectedIndex = (int)_app.Settings.Scaling;
            scalingCombo.SelectionChanged += (s, e) =>
            {
                try
                {
                    _app.Settings.Scaling = (ScalingMode)scalingCombo.SelectedIndex;
                    SettingsStore.Save();
                    _app.Engine.ApplySettingsChange();
                }
                catch (Exception ex) { Logger.Error(ex); }
            };
            s.Children.Add(Theme.SettingsRow("Video scaling", "How the video fills each screen.", scalingCombo));

            // Audio
            var audioToggle = new ToggleSwitch { IsOn = _app.Settings.PlayWallpaperAudio };
            audioToggle.Toggled += () =>
            {
                _app.Settings.PlayWallpaperAudio = audioToggle.IsOn;
                SettingsStore.Save();
                _app.Engine.ApplySettingsChange();
            };
            s.Children.Add(Theme.SettingsRow("Play wallpaper audio",
                "Off by default. Wallpapers stay silent unless you ask for it.", audioToggle));

            // Automatic wallpaper switching
            var autoToggle = new ToggleSwitch { IsOn = _app.Settings.AutoSwitchWallpapers };

            var intervalCombo = new ComboBox { MinWidth = 220, FontSize = 13 };
            foreach (var minutes in AppSettings.AutoSwitchChoices)
            {
                intervalCombo.Items.Add(new ComboBoxItem
                {
                    Content = minutes == 1 ? "Every minute" : "Every " + minutes + " minutes",
                });
            }
            int intervalIndex = Array.IndexOf(AppSettings.AutoSwitchChoices, _app.Settings.EffectiveAutoSwitchMinutes);
            intervalCombo.SelectedIndex = intervalIndex < 0 ? 1 : intervalIndex;
            intervalCombo.SelectionChanged += (s, e) =>
            {
                try
                {
                    var i = intervalCombo.SelectedIndex;
                    if (i < 0 || i >= AppSettings.AutoSwitchChoices.Length) return;
                    _app.Settings.AutoSwitchMinutes = AppSettings.AutoSwitchChoices[i];
                    SettingsStore.Save();
                    _app.Engine.ArmAutoSwitch();
                }
                catch (Exception ex) { Logger.Error(ex); }
            };

            var intervalRow = Theme.SettingsRow("Change every",
                "How long each wallpaper stays on screen.", intervalCombo);

            Action<bool> applyIntervalState = on =>
            {
                intervalCombo.IsEnabled = on;
                intervalRow.Opacity = on ? 1.0 : 0.45;
            };
            applyIntervalState(autoToggle.IsOn);

            autoToggle.Toggled += () =>
            {
                _app.Settings.AutoSwitchWallpapers = autoToggle.IsOn;
                SettingsStore.Save();
                applyIntervalState(autoToggle.IsOn);
                _app.Engine.ArmAutoSwitch();
            };
            s.Children.Add(Theme.SettingsRow("Change wallpaper automatically",
                "Rotates through the videos in your library while the wallpaper plays. Fullscreen pauses win.",
                autoToggle));
            s.Children.Add(intervalRow);

            return s;
        }

        FrameworkElement BuildBehaviourSection()
        {
            var s = new StackPanel();

            var fsToggle = new ToggleSwitch { IsOn = _app.Settings.AutoPauseFullscreen };
            fsToggle.Toggled += () =>
            {
                _app.Settings.AutoPauseFullscreen = fsToggle.IsOn;
                SettingsStore.Save();
                _app.WatcherUpdate();
            };
            s.Children.Add(Theme.SettingsRow("Pause during fullscreen apps",
                "The wallpaper pauses when a game or app occupies the whole screen, and resumes afterwards.", fsToggle));

            // Playback already stops whenever the screen is locked or switched off;
            // this is the extra, optional rule for laptop battery life.
            var batteryToggle = new ToggleSwitch { IsOn = _app.Settings.PauseOnBattery };
            batteryToggle.Toggled += () =>
            {
                _app.Settings.PauseOnBattery = batteryToggle.IsOn;
                SettingsStore.Save();
                _app.Engine.RefreshEnvironmentPause();
            };
            s.Children.Add(Theme.SettingsRow("Pause when on battery",
                "Stops the video while the laptop runs on battery, and resumes when you plug it in. " +
                "Playback always stops when the screen is locked or off.", batteryToggle));

            var startupToggle = new ToggleSwitch { IsOn = _app.Settings.StartWithWindows };
            startupToggle.Toggled += () =>
            {
                _app.Settings.StartWithWindows = startupToggle.IsOn;
                SettingsStore.Save();
                _app.ApplyStartWithWindows();
            };
            s.Children.Add(Theme.SettingsRow("Start with Windows",
                "Launch VulcansLivingWP automatically when you sign in.", startupToggle));

            var minimizedToggle = new ToggleSwitch { IsOn = _app.Settings.StartMinimized };
            minimizedToggle.Toggled += () =>
            {
                _app.Settings.StartMinimized = minimizedToggle.IsOn;
                SettingsStore.Save();
            };
            s.Children.Add(Theme.SettingsRow("Start minimized to the tray",
                "Keep the main window hidden until you open it from the tray.", minimizedToggle));

            var restoreToggle = new ToggleSwitch { IsOn = _app.Settings.RestoreLastWallpaper };
            restoreToggle.Toggled += () =>
            {
                _app.Settings.RestoreLastWallpaper = restoreToggle.IsOn;
                SettingsStore.Save();
            };
            s.Children.Add(Theme.SettingsRow("Restore wallpaper on startup",
                "Replay your last applied wallpaper when VulcansLivingWP launches.", restoreToggle));

            var updateToggle = new ToggleSwitch { IsOn = _app.Settings.CheckForUpdates };
            updateToggle.Toggled += () =>
            {
                _app.Settings.CheckForUpdates = updateToggle.IsOn;
                SettingsStore.Save();
                // Turning it back on should not require restarting the app.
                if (updateToggle.IsOn && Updates.UpdateService.Last == null)
                    Updates.UpdateService.StartupCheck(null);
            };
            s.Children.Add(Theme.SettingsRow("Check for updates",
                "Asks GitHub for the latest stable release in the background, at most once every " +
                "12 hours. Nothing is downloaded or installed automatically; the About page shows " +
                "the result and a link to the release.", updateToggle));

            var trayToggle = new ToggleSwitch { IsOn = _app.Settings.ShowTrayIcon };
            trayToggle.Toggled += () =>
            {
                _app.Settings.ShowTrayIcon = trayToggle.IsOn;
                SettingsStore.Save();
                _app.Tray.CommitVisible(trayToggle.IsOn);
            };
            s.Children.Add(Theme.SettingsRow("Show system tray icon",
                "Quick pause, resume and exit live in the tray.", trayToggle));

            return s;
        }

        FrameworkElement BuildHotkeySection()
        {
            var s = new StackPanel();

            _hotkeyBox = new TextBox
            {
                Text = _app.Settings.PauseHotkey ?? "",
                MinWidth = 220,
                FontSize = 13,
                Background = Theme.Bru(Theme.From(0x23232D)),
                Foreground = Theme.TextB,
                BorderBrush = Theme.BorderB,
                Padding = new Thickness(8, 6, 8, 6),
            };
            _hotkeyBox.LostKeyboardFocus += (s, e) =>
            {
                _app.Settings.PauseHotkey = _hotkeyBox.Text?.Trim() ?? "";
                SettingsStore.Save();
                _app.ApplyHotkey();
                RefreshHotkeyStatus();
            };

            s.Children.Add(Theme.SettingsRow("Pause / resume hotkey",
                "Example: Ctrl+Alt+W — plus-separated keys, press Enter to apply. Leave empty to disable.",
                _hotkeyBox));

            // Tells the user why a combination did not take effect instead of
            // silently ignoring it.
            _hotkeyStatus = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 0, 0),
                Foreground = Theme.TextMutedB,
            };
            s.Children.Add(_hotkeyStatus);
            RefreshHotkeyStatus();

            return s;
        }
        void RefreshHotkeyStatus()
        {
            if (_hotkeyStatus == null) return;
            var error = _app.Hotkeys != null ? _app.Hotkeys.LastError : null;
            if (!string.IsNullOrEmpty(error))
            {
                _hotkeyStatus.Text = error;
                _hotkeyStatus.Foreground = Theme.BadB;
            }
            else if (string.IsNullOrWhiteSpace(_app.Settings.PauseHotkey))
            {
                _hotkeyStatus.Text = "Shortcut disabled.";
                _hotkeyStatus.Foreground = Theme.TextMutedB;
            }
            else
            {
                _hotkeyStatus.Text = "Active: " + _app.Settings.PauseHotkey;
                _hotkeyStatus.Foreground = Theme.GoodB;
            }
        }
        #endregion

        public void Root()
        {
            // no op
        }
    }
}
