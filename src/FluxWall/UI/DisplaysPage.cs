using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.Library;
using FluxWall.Settings;

namespace FluxWall.UI
{
    /// <summary>
    /// Multi-monitor layout: the three display modes, one wallpaper per monitor,
    /// and the span configuration. The page is rebuilt from the live monitor list
    /// every time it is opened (or asked to refresh), so plugging a screen in or
    /// changing a resolution never leaves stale rows behind.
    /// </summary>
    public class DisplaysPage
    {
        readonly FluxWallApp _app;
        Grid _page;
        StackPanel _root;

        public DisplaysPage(FluxWallApp app)
        {
            _app = app;
            Build();
            Refresh();
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

            _root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            scroll.Content = _root;

            _page = new Grid { Background = Theme.BaseBgB };
            _page.Children.Add(scroll);
        }

        /// <summary>Rebuilds the page from the current monitors and settings.</summary>
        public void Refresh()
        {
            try
            {
                _root.Children.Clear();
                var settings = _app.Settings;
                var monitors = MonitorService.All();

                _root.Children.Add(Header("Display layout"));
                _root.Children.Add(Card(ModeChooser()));
                _root.Children.Add(Space());

                if (settings.DisplayMode == DisplayMode.Independent)
                {
                    _root.Children.Add(Header("Wallpaper per monitor"));
                    _root.Children.Add(Card(BuildIndependent(monitors)));
                }
                else if (settings.DisplayMode == DisplayMode.Span)
                {
                    _root.Children.Add(Header("Span across monitors"));
                    _root.Children.Add(Card(BuildSpan(monitors)));
                }
                else
                {
                    _root.Children.Add(Header("Same wallpaper everywhere"));
                    _root.Children.Add(Card(BuildSameAll(monitors)));
                }

                _root.Children.Add(Space());
                _root.Children.Add(Hint(
                    "Assignments are saved per monitor, so they survive restarts and a monitor being " +
                    "unplugged for a while. Switching mode never deletes them."));
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Building the Displays page failed", e));
            }
        }

        #region Mode chooser
        FrameworkElement ModeChooser()
        {
            var s = new StackPanel();
            s.Children.Add(ModeCard(DisplayMode.Independent, "Separate wallpaper per monitor",
                "Every monitor plays its own video. Changing one does not affect the others."));
            s.Children.Add(ModeCard(DisplayMode.Span, "One video spanning all monitors",
                "A single continuous picture across the combined desktop — one decoder, no seams."));
            s.Children.Add(ModeCard(DisplayMode.SameAll, "Same wallpaper on every monitor",
                "The classic layout: one video repeated on each selected monitor."));
            return s;
        }

        FrameworkElement ModeCard(DisplayMode mode, string title, string description)
        {
            bool active = _app.Settings.DisplayMode == mode;
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB,
            });
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 12, 0),
            });
            content.Children.Add(text);

            var dot = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = active ? Theme.AccentB : Theme.BorderB,
                Background = active ? Theme.AccentB : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dot, 1);
            content.Children.Add(dot);

            var card = new Border
            {
                Background = active ? Theme.CardBgHoverB : Theme.CardBgB,
                BorderBrush = active ? Theme.AccentB : Theme.BorderB,
                BorderThickness = new Thickness(active ? 2 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = content,
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    if (_app.Settings.DisplayMode == mode) return;
                    _app.Engine.SetDisplayMode(mode);
                    Refresh();
                }
                catch (Exception ex) { Logger.Error(ex); }
            };
            return card;
        }
        #endregion

        #region Independent mode
        FrameworkElement BuildIndependent(List<MonitorInfoLite> monitors)
        {
            var s = new StackPanel();
            s.Children.Add(Hint("Pick a video for each screen. Unassigned screens keep showing the last " +
                                "wallpaper you applied, so nothing goes blank."));
            s.Children.Add(new Border { Height = 10 });

            foreach (var m in monitors)
                s.Children.Add(MonitorRow(m));

            s.Children.Add(new Border { Height = 6 });
            s.Children.Add(AllMonitorsRow(monitors));
            return s;
        }

        FrameworkElement MonitorRow(MonitorInfoLite m)
        {
            var key = m.Key.Length > 0 ? m.Key : m.DeviceName;
            var item = _app.Library.Find(_app.Settings.AssignmentFor(key));

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Thumbnail of this monitor's video
            var thumb = new Border
            {
                Width = 96,
                Height = 54,
                CornerRadius = new CornerRadius(6),
                Background = Theme.Bru(Theme.From(0x141419)),
                ClipToBounds = true,
            };
            if (item != null)
            {
                try
                {
                    var png = _app.Thumbs.ThumbPath(item);
                    if (File.Exists(png))
                        thumb.Child = new Image { Source = LibraryPage.LoadPng(png), Stretch = Stretch.UniformToFill };
                }
                catch { }
            }
            row.Children.Add(thumb);

            var info = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = m.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB,
            });
            info.Children.Add(new TextBlock
            {
                Text = item != null ? Path.GetFileName(item.FilePath) : "Not assigned — using the shared wallpaper",
                FontSize = 12,
                Foreground = item != null ? Theme.TextMutedB : Theme.WarnB,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(ItemChooser(item, picked =>
            {
                _app.Engine.AssignToMonitor(picked, key);
                Refresh();
            }));

            var clear = Theme.QuietButton("Clear", () =>
            {
                _app.Engine.ClearMonitor(key);
                Refresh();
            }, Theme.TextMutedB);
            clear.Margin = new Thickness(8, 0, 0, 0);
            clear.IsEnabled = item != null;
            actions.Children.Add(clear);

            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            return row;
        }

        FrameworkElement AllMonitorsRow(List<MonitorInfoLite> monitors)
        {
            var s = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var label = new TextBlock
            {
                Text = "Use the same video on every monitor:",
                Foreground = Theme.TextMutedB,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            row.Children.Add(label);

            var chooser = ItemChooser(null, picked =>
            {
                if (picked == null) return;
                foreach (var m in monitors)
                {
                    var key = m.Key.Length > 0 ? m.Key : m.DeviceName;
                    _app.Settings.Assign(key, picked.Id);
                }
                _app.Settings.ActiveWallpaperId = picked.Id;
                SettingsStore.Save();
                _app.Engine.ApplyCurrentLayout();
                Refresh();
            }, "Set for all…");
            row.Children.Add(chooser);

            s.Children.Add(row);
            return s;
        }
        #endregion

        #region Span mode
        FrameworkElement BuildSpan(List<MonitorInfoLite> monitors)
        {
            var s = new StackPanel();
            var spanItem = _app.Library.Find(_app.Settings.SpanWallpaperId);
            if (spanItem == null) spanItem = _app.Library.Find(_app.Settings.ActiveWallpaperId);

            var participants = WallpaperPlan.SelectSpanMonitors(_app.Settings, monitors);
            var union = MonitorService.Union(participants);
            s.Children.Add(Hint("One video is stretched across " + participants.Count + " monitor(s): " +
                                (union.Right - union.Left) + "×" + (union.Bottom - union.Top) +
                                " pixels. Panels are laid out with their real Windows positions, including " +
                                "monitors above or to the left of the primary one."));
            s.Children.Add(new Border { Height = 10 });

            // Participants
            s.Children.Add(new TextBlock
            {
                Text = "Monitors in the span",
                Foreground = Theme.TextB,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            });
            var boxes = new WrapPanel();
            foreach (var m in monitors)
            {
                var key = m.Key.Length > 0 ? m.Key : m.DeviceName;
                bool included = _app.Settings.SpanMonitors.Count == 0 ||
                                _app.Settings.SpanMonitors.Contains(key);
                var cb = new CheckBox
                {
                    Content = m.Label,
                    IsChecked = included,
                    Foreground = Theme.TextB,
                    Margin = new Thickness(0, 0, 18, 6),
                };
                cb.Click += (sender, e) =>
                {
                    try
                    {
                        var keys = new List<string>();
                        foreach (var other in monitors)
                        {
                            var otherKey = other.Key.Length > 0 ? other.Key : other.DeviceName;
                            if (other == m ? cb.IsChecked == true : AppSettingsContains(otherKey))
                                keys.Add(otherKey);
                        }
                        _app.Engine.SetSpanMonitors(keys);
                        Refresh();
                    }
                    catch (Exception ex) { Logger.Error(ex); }
                };
                boxes.Children.Add(cb);
            }
            s.Children.Add(boxes);
            s.Children.Add(new Border { Height = 8 });

            // Fit
            s.Children.Add(new TextBlock
            {
                Text = "Video fit",
                Foreground = Theme.TextB,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            });
            var fitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            fitRow.Children.Add(FitButton(SpanFit.Stretch, "Stretch to fill",
                "Fills the whole area; the picture can look stretched."));
            fitRow.Children.Add(FitButton(SpanFit.PreserveAspect, "Preserve aspect ratio",
                "Keeps the video's proportions; unused space stays black."));
            s.Children.Add(fitRow);

            // Video
            var videoRow = new StackPanel { Orientation = Orientation.Horizontal };
            videoRow.Children.Add(new TextBlock
            {
                Text = "Video:",
                Foreground = Theme.TextMutedB,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            videoRow.Children.Add(ItemChooser(spanItem, picked =>
            {
                if (picked == null) return;
                _app.Settings.SpanWallpaperId = picked.Id;
                _app.Settings.ActiveWallpaperId = picked.Id;
                SettingsStore.Save();
                _app.Engine.ApplyCurrentLayout();
                Refresh();
            }));
            s.Children.Add(videoRow);
            return s;
        }

        bool AppSettingsContains(string key)
        {
            foreach (var k in _app.Settings.SpanMonitors)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        FrameworkElement FitButton(SpanFit fit, string title, string hint)
        {
            bool active = _app.Settings.SpanFit == fit;
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = active ? Theme.TextB : Theme.TextMutedB,
            });
            content.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11.5,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
            });
            var b = new Border
            {
                Background = active ? Theme.CardBgHoverB : Theme.CardBgB,
                BorderBrush = active ? Theme.AccentB : Theme.BorderB,
                BorderThickness = new Thickness(active ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = content,
            };
            b.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    _app.Settings.SpanFit = fit;
                    SettingsStore.Save();
                    _app.Engine.ApplySettingsChange();
                    _app.Engine.ApplyCurrentLayout();
                    Refresh();
                }
                catch (Exception ex) { Logger.Error(ex); }
            };
            return b;
        }
        #endregion

        #region Same-all
        FrameworkElement BuildSameAll(List<MonitorInfoLite> monitors)
        {
            var s = new StackPanel();
            var item = _app.Library.Find(_app.Settings.ActiveWallpaperId);
            s.Children.Add(Hint("The same video is shown on each selected monitor (" +
                                _app.Settings.TargetMonitors + "). Choose which monitors in Settings → Wallpaper."));
            s.Children.Add(new Border { Height = 10 });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = "Video:",
                Foreground = Theme.TextMutedB,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            row.Children.Add(ItemChooser(item, picked =>
            {
                if (picked == null) return;
                _app.Settings.ActiveWallpaperId = picked.Id;
                SettingsStore.Save();
                _app.Engine.ApplyCurrentLayout();
                Refresh();
            }));
            s.Children.Add(row);

            s.Children.Add(new Border { Height = 8 });
            s.Children.Add(new TextBlock
            {
                Text = "Scaling: " + _app.Settings.Scaling + "  (Settings → Wallpaper)",
                Foreground = Theme.TextMutedB,
                FontSize = 12,
            });
            return s;
        }
        #endregion

        #region Small helpers
        /// <summary>ComboBox of library videos; fires when the user picks one.</summary>
        ComboBox ItemChooser(WallpaperItem current, Action<WallpaperItem> picked, string placeholder = null)
        {
            var combo = new ComboBox
            {
                MinWidth = 260,
                FontSize = 12.5,
                Background = Theme.ChipBgB,
                BorderBrush = Theme.BorderB,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Choose a video for this monitor",
            };

            combo.Items.Add(new ComboBoxItem { Content = placeholder ?? "Choose a video…", IsEnabled = false });
            int selected = 0;
            int index = 1;
            foreach (var item in _app.Library.Items)
            {
                var ci = new ComboBoxItem
                {
                    Content = Path.GetFileName(item.FilePath),
                    Tag = item,
                    ToolTip = item.FilePath,
                };
                combo.Items.Add(ci);
                if (current != null && current.Id == item.Id) selected = index;
                index++;
            }
            combo.SelectedIndex = selected;

            bool suppress = true;
            combo.SelectionChanged += (s, e) =>
            {
                if (suppress) { suppress = false; return; }
                var ci = combo.SelectedItem as ComboBoxItem;
                var item = ci != null ? ci.Tag as WallpaperItem : null;
                if (item == null) return;
                try { picked(item); } catch (Exception ex) { Logger.Error(ex); }
            };
            return combo;
        }

        TextBlock Header(string text) => new TextBlock
        {
            Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextB, Margin = new Thickness(0, 0, 0, 10),
        };

        TextBlock Hint(string text) => new TextBlock
        {
            Text = text, FontSize = 12.5, Foreground = Theme.TextMutedB,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        };

        Border Card(FrameworkElement content)
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
        #endregion
    }
}
