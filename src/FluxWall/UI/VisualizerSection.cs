using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluxWall.Common;
using FluxWall.Settings;
using FluxWall.Visualizer;

namespace FluxWall.UI
{
    /// <summary>
    /// The "Visualizer" block on the Settings page. Plain-language labels, no
    /// numbers to type except through sliders, and every change is saved and
    /// applied immediately — the overlay updates while the user drags.
    /// </summary>
    public class VisualizerSection
    {
        static readonly string[] ColorPresets =
        {
            "#00FF88", "#00D4FF", "#35D6ED", "#3FBF7F",
            "#E0A64B", "#E05768", "#E057A8", "#FFFFFF",
        };

        readonly FluxWallApp _app;
        readonly StackPanel _root = new StackPanel();
        readonly StackPanel _detail = new StackPanel();
        readonly StackPanel _swatches = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock _status;
        readonly ComboBox _position;
        readonly ToggleSwitch _lock;
        readonly ToggleSwitch _enable;
        readonly System.Collections.Generic.List<SliderBinding> _sliders =
            new System.Collections.Generic.List<SliderBinding>();

        sealed class SliderBinding
        {
            public Slider Slider;
            public TextBlock Readout;
            public Func<double> Read;
            public string Suffix;
        }

        public VisualizerSection(FluxWallApp app)
        {
            _app = app;
            _enable = new ToggleSwitch { IsOn = app.Settings.VisualizerEnabled };
            _lock = new ToggleSwitch { IsOn = app.Settings.VisualizerLocked };
            _position = new ComboBox { MinWidth = 200, FontSize = 13 };
            _status = new TextBlock
            {
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Build();
        }

        public FrameworkElement Content { get { return _root; } }

        Settings.AppSettings Settings { get { return _app.Settings; } }

        void Build()
        {
            var intro = new TextBlock
            {
                Text = "A music visualiser that reacts to everything your PC plays — games, " +
                       "browsers, players, movies. It listens to the sound Windows is already " +
                       "playing, never to a microphone, and nothing is recorded or sent anywhere.",
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            };
            _root.Children.Add(intro);

            _enable.Toggled += () =>
            {
                _app.Visualizer.SetEnabled(_enable.IsOn);
                RefreshEnabledState();
                RefreshStatus();
            };
            _root.Children.Add(Theme.SettingsRow("Show audio visualiser",
                "Off by default. Turning it on adds the overlay without touching your wallpaper.",
                _enable));

            BuildDetail();
            _detail.Margin = new Thickness(0, 6, 0, 0);
            _root.Children.Add(_detail);
            RefreshEnabledState();
        }

        void BuildDetail()
        {
            var settings = Settings;

            // Style
            var styleCombo = new ComboBox { MinWidth = 200, FontSize = 13 };
            styleCombo.Items.Add(new ComboBoxItem { Content = "Classic bars" });
            styleCombo.Items.Add(new ComboBoxItem { Content = "Waveform" });
            styleCombo.Items.Add(new ComboBoxItem { Content = "Circular spectrum" });
            styleCombo.SelectedIndex = (int)settings.VisualizerStyle;
            styleCombo.SelectionChanged += (s, e) =>
            {
                if (styleCombo.SelectedIndex < 0) return;
                settings.VisualizerStyle = (VisualizerStyle)styleCombo.SelectedIndex;
                SaveAndApply();
            };
            _detail.Children.Add(Theme.SettingsRow("Style",
                "Bars are the classic equaliser, waveform shows loudness over time, circular wraps the spectrum into a ring.",
                styleCombo));

            // Colour
            BuildSwatches();
            _detail.Children.Add(Theme.SettingsRow("Colour",
                "Pick a preset, or open the colour picker for anything you like.",
                _swatches));

            // Opacity
            _detail.Children.Add(SliderRow("Opacity", "How solid the visualiser looks over your wallpaper.",
                AppSettings.MinVisualizerOpacity, 100, settings.VisualizerOpacity, "%",
                () => Settings.VisualizerOpacity,
                v => { Settings.VisualizerOpacity = (int)Math.Round(v); SaveAndApply(); }));

            // Sensitivity
            _detail.Children.Add(SliderRow("Sensitivity", "Raise it for quiet music, lower it if the bars sit at the top.",
                20, 400, settings.VisualizerSensitivity, "%",
                () => Settings.VisualizerSensitivity,
                v => { Settings.VisualizerSensitivity = (int)Math.Round(v); SaveAndApply(); }));

            // Detail
            _detail.Children.Add(SliderRow("Detail", "How many frequency bars (or ring segments) are drawn.",
                8, 96, settings.VisualizerBars, " bars",
                () => Settings.VisualizerBars,
                v =>
                {
                    Settings.VisualizerBars = (int)Math.Round(v);
                    SaveAndApply();
                }));

            // Smoothing
            _detail.Children.Add(SliderRow("Smoothing", "Higher values glide; lower values snap to every beat.",
                0, 95, settings.VisualizerSmoothing, "%",
                () => Settings.VisualizerSmoothing,
                v => { Settings.VisualizerSmoothing = (int)Math.Round(v); SaveAndApply(); }));

            // Peaks
            var peaks = new ToggleSwitch { IsOn = settings.VisualizerPeaks };
            peaks.Toggled += () =>
            {
                settings.VisualizerPeaks = peaks.IsOn;
                SaveAndApply();
            };
            _detail.Children.Add(Theme.SettingsRow("Peak markers",
                "A thin marker that floats above each bar and falls back down. Costs almost nothing.",
                peaks));

            // Size
            _detail.Children.Add(SliderRow("Width", "In pixels on screen.",
                AppSettings.MinVisualizerWidth, 1600, settings.VisualizerWidth, " px",
                () => Settings.VisualizerWidth,
                v =>
                {
                    Settings.VisualizerWidth = (int)Math.Round(v);
                    SaveAndApply();
                }));

            _detail.Children.Add(SliderRow("Height", "In pixels on screen.",
                AppSettings.MinVisualizerHeight, 900, settings.VisualizerHeight, " px",
                () => Settings.VisualizerHeight,
                v =>
                {
                    Settings.VisualizerHeight = (int)Math.Round(v);
                    SaveAndApply();
                }));

            // Position
            _position.Items.Add(new ComboBoxItem { Content = "Custom (where I dragged it)" });
            _position.Items.Add(new ComboBoxItem { Content = "Bottom centre" });
            _position.Items.Add(new ComboBoxItem { Content = "Bottom left" });
            _position.Items.Add(new ComboBoxItem { Content = "Bottom right" });
            _position.Items.Add(new ComboBoxItem { Content = "Top left" });
            _position.Items.Add(new ComboBoxItem { Content = "Top right" });
            _position.Items.Add(new ComboBoxItem { Content = "Centre" });
            _position.SelectedIndex = (int)settings.VisualizerAnchor;
            _position.SelectionChanged += (s, e) =>
            {
                int index = _position.SelectedIndex;
                if (index < 0) return;
                if ((VisualizerAnchor)index == VisualizerAnchor.Custom &&
                    settings.VisualizerAnchor != VisualizerAnchor.Custom)
                {
                    // "Custom" is informational until something is dragged.
                    _position.SelectedIndex = (int)settings.VisualizerAnchor;
                    return;
                }
                settings.VisualizerAnchor = (VisualizerAnchor)index;
                SaveAndApply();
            };
            _detail.Children.Add(Theme.SettingsRow("Position",
                "Presets sit above the taskbar on your main screen. Drag it anywhere for a custom spot.",
                _position));

            // Lock + edit
            var editRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edit = Theme.QuietButton("Edit on desktop", () =>
            {
                if (!settings.VisualizerEnabled)
                {
                    _enable.IsOn = true;
                    _app.Visualizer.SetEnabled(true);
                }
                _app.Visualizer.BeginEdit();
                _lock.IsOn = false;
                RefreshEnabledState();
                RefreshStatus();
            });
            editRow.Children.Add(edit);
            _detail.Children.Add(Theme.SettingsRow("Placement",
                "Shows a dashed border with handles: drag the middle to move, drag the corners to resize.",
                editRow));

            _lock.Toggled += () =>
            {
                _app.Visualizer.SetLocked(_lock.IsOn);
                RefreshStatus();
            };
            _detail.Children.Add(Theme.SettingsRow("Lock position",
                "While locked the visualiser ignores the mouse completely, so it never blocks icons, games or clicks.",
                _lock));

            _detail.Children.Add(_status);

            var reset = Theme.QuietButton("Reset appearance", () =>
            {
                _app.Visualizer.ResetAppearance();
                RefreshFromSettings();
            });
            var resetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            resetRow.Children.Add(reset);
            _detail.Children.Add(resetRow);

            _app.Visualizer.StatusChanged += () => _app.Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshStatus();
                if (_position.SelectedIndex != (int)Settings.VisualizerAnchor)
                    _position.SelectedIndex = (int)Settings.VisualizerAnchor;
                if (_lock.IsOn != Settings.VisualizerLocked) _lock.IsOn = Settings.VisualizerLocked;
            }));

            RefreshStatus();
        }

        void BuildSwatches()
        {
            _swatches.Children.Clear();
            foreach (var hex in ColorPresets)
            {
                _swatches.Children.Add(Swatch(hex, 26));
            }

            var custom = new Button
            {
                Content = "Custom…",
                Foreground = Theme.TextB,
                Background = Theme.ChipBgB,
                BorderBrush = Theme.BorderB,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
            };
            custom.Template = Theme.ButtonTemplate;
            custom.Click += (s, e) => PickCustomColor();
            _swatches.Children.Add(custom);
            RefreshSwatches();
        }

        FrameworkElement Swatch(string hex, double size)
        {
            var color = VisualizerService.ParseColor(hex);
            var border = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(6),
                Background = Theme.Bru(color),
                BorderBrush = Theme.BorderB,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = hex,
            };
            border.MouseLeftButtonUp += (s, e) =>
            {
                Settings.VisualizerColor = hex;
                SaveAndApply();
                RefreshSwatches();
            };
            return border;
        }

        void RefreshSwatches()
        {
            var current = (Settings.VisualizerColor ?? "").ToUpperInvariant();
            foreach (var child in _swatches.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                bool selected = string.Equals((string)border.Tag, current, StringComparison.OrdinalIgnoreCase);
                border.BorderBrush = selected ? Theme.Bru(Colors.White) : Theme.BorderB;
                border.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        void PickCustomColor()
        {
            try
            {
                var current = VisualizerService.ParseColor(Settings.VisualizerColor);
                using (var dialog = new System.Windows.Forms.ColorDialog())
                {
                    dialog.FullOpen = true;
                    dialog.AnyColor = true;
                    dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    Settings.VisualizerColor = string.Format("#{0:X2}{1:X2}{2:X2}",
                        dialog.Color.R, dialog.Color.G, dialog.Color.B);
                    SaveAndApply();
                    RefreshSwatches();
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Colour picker failed", e));
            }
        }

        /// <summary>Label + slider + live value, stacked so it stays readable.</summary>
        FrameworkElement SliderRow(string label, string hint, double min, double max,
            double value, string suffix, Func<double> read, Action<double> onChange)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Theme.Label(label);
            name.FontSize = 14;
            name.FontWeight = FontWeights.Medium;
            header.Children.Add(name);

            var readout = new TextBlock
            {
                Text = Format(value, suffix),
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(readout, 1);
            header.Children.Add(readout);
            panel.Children.Add(header);

            var slider = Theme.DarkSlider(min, max, value, v =>
            {
                readout.Text = Format(v, suffix);
                onChange(v);
            });
            slider.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(slider);
            _sliders.Add(new SliderBinding
            {
                Slider = slider,
                Readout = readout,
                Read = read,
                Suffix = suffix,
            });

            var hintText = new TextBlock
            {
                Text = hint,
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            panel.Children.Add(hintText);
            return panel;
        }

        static string Format(double value, string suffix)
        {
            if (suffix == "%") return ((int)Math.Round(value)) + "%";
            if (suffix == " bars") return ((int)Math.Round(value)) + " bars";
            if (suffix == " px") return ((int)Math.Round(value)) + " px";
            return ((int)Math.Round(value)) + suffix;
        }

        void SaveAndApply()
        {
            try
            {
                SettingsStore.Save();
                _app.Visualizer.Apply();
            }
            catch (Exception e) { Logger.Error(e); }
        }

        void RefreshEnabledState()
        {
            bool on = Settings.VisualizerEnabled;
            _detail.IsEnabled = on;
            _detail.Opacity = on ? 1.0 : 0.45;
            _enable.IsOn = on;
            if (!on)
            {
                _status.Text = "Visualiser is off. Nothing is captured and no window is shown.";
                return;
            }
        }

        void RefreshStatus()
        {
            var v = _app.Visualizer;
            if (!Settings.VisualizerEnabled)
            {
                _status.Text = "Visualiser is off. Nothing is captured and no window is shown.";
                _status.Foreground = Theme.TextMutedB;
                return;
            }

            string text;
            Brush brush = Theme.TextMutedB;
            if (!string.IsNullOrEmpty(v.LastError))
            {
                text = v.LastError;
                brush = Theme.BadB;
            }
            else if (v.IsSuspended)
            {
                text = "Paused while a fullscreen app is in front.";
            }
            else if (!Settings.VisualizerLocked)
            {
                text = "Editing: drag the visualiser into place, then turn Lock position back on.";
                brush = Theme.WarnB;
            }
            else if (v.IsCapturing)
            {
                text = "Locked and listening." +
                       (string.IsNullOrEmpty(v.DeviceName) ? "" : " Audio device: " + v.DeviceName);
                brush = Theme.GoodB;
            }
            else
            {
                text = string.IsNullOrEmpty(v.Status)
                    ? "Waiting for audio…"
                    : v.Status;
            }
            _status.Text = text;
            _status.Foreground = brush;
        }

        /// <summary>Re-reads every control from the settings object (after Reset).</summary>
        public void RefreshFromSettings()
        {
            var s = Settings;
            _enable.IsOn = s.VisualizerEnabled;
            _lock.IsOn = s.VisualizerLocked;
            _position.SelectedIndex = (int)s.VisualizerAnchor;
            foreach (var binding in _sliders)
            {
                double value = binding.Read();
                if (Math.Abs(binding.Slider.Value - value) > 0.001)
                {
                    binding.Slider.Value = value;
                    binding.Readout.Text = Format(value, binding.Suffix);
                }
            }
            RefreshSwatches();
            RefreshEnabledState();
            RefreshStatus();
        }
    }
}
