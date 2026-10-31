using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.Library;

namespace FluxWall.UI
{
    /// <summary>
    /// The Library page: header with Add Wallpaper, import/empty state, and the
    /// video card grid. Preview now plays the video inside the card itself
    /// instead of opening a separate window.
    /// </summary>
    public class LibraryPage
    {
        readonly FluxWallApp _app;
        readonly Window _owner;

        Grid _page;
        WrapPanel _gridPanel;
        StackPanel _empty;
        Border _notice;

        // The card whose preview is currently playing. Full grid rebuilds (thumbnail
        // ready, wallpaper applied) reuse this card so the live MediaElement survives.
        CardPreview _livePreview;
        UIElement _liveCard;

        public LibraryPage(FluxWallApp app, Window owner)
        {
            _app = app;
            _owner = owner;
            Build();

            _app.Library.ItemsChanged += OnItemsChanged;
            _app.Engine.WallpaperApplied += _ => SafeRefresh();
            _app.Thumbs.ThumbnailReady += item => _owner.Dispatcher.Invoke(
                () => SafeRefresh(), System.Windows.Threading.DispatcherPriority.Background);
        }

        void SafeRefresh()
        {
            try { RefreshCards(); } catch (Exception e) { Logger.Error(e); }
        }

        void OnItemsChanged()
        {
            _owner.Dispatcher.Invoke(() =>
            {
                SafeRefresh();
            });
        }

        public object Content { get { return _page; } }

        void Build()
        {
            _page = new Grid { Background = Theme.BaseBgB };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(28, 20, 28, 24),
            };
            _page.Children.Add(scroll);

            var root = new StackPanel();
            scroll.Content = root;

            // ── Header row ──
            var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var h1 = Theme.Label("Video Library");
            h1.FontSize = 22; h1.FontWeight = FontWeights.SemiBold;
            var h2 = new TextBlock
            {
                Text = "Import MP4 or MOV videos, then apply one as your animated desktop wallpaper.",
                Foreground = Theme.TextMutedB, FontSize = 12, Margin = new Thickness(0, 3, 0, 0),
            };
            titleStack.Children.Add(h1);
            titleStack.Children.Add(h2);
            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            var addBtn = Theme.AccentButton("＋  Add Wallpaper", () => AddWallpapersClicked(), 14);
            Grid.SetColumn(addBtn, 1);
            header.Children.Add(addBtn);
            root.Children.Add(header);

            // ── Notice banner (missing files / playback errors) ──
            _notice = new Border
            {
                Background = Theme.Bru(Color.FromArgb(200, 0xE0, 0xA6, 0x4B)),
                BorderBrush = Theme.WarnB,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Visibility = Visibility.Collapsed,
            };
            root.Children.Add(_notice);

            // ── Empty state ──
            _empty = BuildEmpty();
            root.Children.Add(_empty);

            // ── Card grid ──
            _gridPanel = new WrapPanel();
            root.Children.Add(_gridPanel);

            // Drag & drop anywhere on the page
            _page.AllowDrop = true;
            _page.DragOver += (s, e) =>
            {
                e.Effects = DragFromExplorer(e) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            _page.Drop += OnDrop;
        }

        static bool DragFromExplorer(DragEventArgs e)
        {
            return e.Data.GetDataPresent("FileDrop") ||
                   e.Data.GetDataPresent(DataFormats.FileDrop);
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (!DragFromExplorer(e)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                ImportAndStop(files);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        public void AddWallpapersClicked()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose video files for your wallpaper",
                    Filter = "Video files (*.mp4;*.mov)|*.mp4;*.mov|All supported files|*.mp4;*.mov",
                    Multiselect = true,
                    CheckFileExists = true,
                };
                if (dialog.ShowDialog(_owner) == true)
                    ImportAndStop(dialog.FileNames);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(_owner, "Could not open the file picker.\n\n" + ex.Message,
                    "VulcansLivingWP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void ImportAndStop(string[] files)
        {
            var added = _app.Library.AddFiles(files);
            if (added.Count > 0)
            {
                foreach (var it in added) _app.Thumbs.Request(it);
            }
            else
            {
                ShowNotice("No new videos were added — the selected files were already in your library or are not MP4/MOV videos.");
            }
        }

        StackPanel BuildEmpty()
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 40),
            };
            var title = Theme.Label("Bring your desktop to life");
            title.FontSize = 18; title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            var sub = new TextBlock
            {
                Text = "Add an MP4 or MOV video to use it as an animated wallpaper.\n" +
                       "Your files stay where they are — VulcansLivingWP only remembers their location.",
                Foreground = Theme.TextMutedB, FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8, 0, 22),
            };
            panel.Children.Add(sub);
            var btn = Theme.AccentButton("＋  Add your first wallpaper", () => AddWallpapersClicked(), 14);
            btn.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(btn);
            var dropHint = new TextBlock
            {
                Text = "You can also drag and drop videos anywhere in this window.",
                Foreground = Theme.TextMutedB, FontSize = 12,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
            };
            panel.Children.Add(dropHint);
            return panel;
        }

        /// <summary>Full card-grid re-render. Card count stays small, so this is fine.</summary>
        public void RefreshCards()
        {
            _app.Library.RefreshMissing();
            if (_livePreview != null && !_livePreview.IsActive) { _livePreview = null; _liveCard = null; }
            _gridPanel.Children.Clear();

            var showEmpty = _app.Library.Items.Count == 0;
            _empty.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;

            foreach (var item in _app.Library.Items)
                _gridPanel.Children.Add(BuildCard(item));

            UpdateNotice();
        }

        /// <summary>Shows or clears the yellow banner area.</summary>
        public void ShowNotice(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = Theme.TextB,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
            };
            _notice.Child = tb;
            _notice.Visibility = Visibility.Visible;
        }

        void UpdateNotice()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var i in _app.Library.Items)
                if (i.IsMissing) missing.Add(i.FilePath);

            if (missing.Count > 0)
            {
                var names = new System.Text.StringBuilder();
                foreach (var path in missing)
                    names.Append("• ").AppendLine(Path.GetFileName(path));
                ShowNotice(
                    "These videos are missing (moved or deleted on disk?). VulcansLivingWP kept their entries.\n" +
                    "Re-add the files from their new location, or remove them from the library:\n" +
                    names);
            }
            else
            {
                _notice.Visibility = Visibility.Collapsed;
            }
        }

        UIElement BuildCard(WallpaperItem item)
        {
            const double cardW = 216, thumbH = 122, cardH = 190;

            if (_livePreview != null && _liveCard != null && _livePreview.IsActive && _livePreview.Item == item)
                return _liveCard;

            var card = new Border
            {
                Width = cardW, Height = cardH,
                Background = Theme.CardBgB,
                BorderBrush = _app.Engine.Current == item ? Theme.AccentB : Theme.BorderB,
                BorderThickness = new Thickness(_app.Engine.Current == item ? 2 : 1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 14, 14),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
            };
            var g = new Grid();
            card.Child = g;
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(thumbH) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Preview host lives inside the card thumbnail area.
            var thumbHost = new Border { Background = Theme.PanelBgB, ClipToBounds = true };
            Grid.SetRow(thumbHost, 0);
            g.Children.Add(thumbHost);

            var previewCtrl = new CardPreview(thumbHost, _app, item);
            previewCtrl.Refresh(item);

            // Missing overlay
            if (item.IsMissing)
            {
                var missingOverlay = new Border
                {
                    Background = Theme.Bru(Color.FromArgb(200, 34, 30, 28)),
                };
                var mo = new TextBlock
                {
                    Text = "⚠  File missing",
                    Foreground = Theme.WarnB,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                };
                missingOverlay.Child = mo;
                thumbHost.Child = missingOverlay;
            }

            // Bottom section: name + buttons
            var bottom = new StackPanel { Margin = new Thickness(10, 6, 10, 0) };
            Grid.SetRow(bottom, 1);
            var name = new TextBlock
            {
                Text = Path.GetFileName(item.FilePath),
                Foreground = Theme.TextB,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 5),
            };
            bottom.Children.Add(name);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var apply = MiniButton(
                _app.Engine.Current == item ? "✓ Active" : "Apply",
                _app.Engine.Current == item ? Theme.GoodB : Theme.AccentB,
                () => Apply(item));
            apply.IsEnabled = !item.IsMissing;
            if (!apply.IsEnabled) apply.Opacity = 0.5;
            row.Children.Add(apply);

            var preview = MiniButton("Preview", Theme.TextMutedB,
                () => TogglePreview(previewCtrl, card));
            preview.IsEnabled = !item.IsMissing;
            if (!preview.IsEnabled) preview.Opacity = 0.5;
            preview.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(preview);

            var remove = MiniButton("✕", Theme.TextMutedB,
                () => Remove(item));
            remove.ToolTip = "Remove from library (the original video file is not deleted)";
            remove.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(remove);

            bottom.Children.Add(row);
            g.Children.Add(bottom);

            card.MouseEnter += (s, e) => card.Background = Theme.CardBgHoverB;
            card.MouseLeave += (s, e) => card.Background = Theme.CardBgB;

            // Double-click the card body to apply the wallpaper
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && !item.IsMissing) Apply(item);
            };
            return card;
        }

        public static BitmapImage LoadPng(string path)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = 432;   // 2x card thumb for crisp DPI scaling
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        /// Kept, but no longer used for library cards. CardImage now falls back
        /// to an animated placeholder so a video's moving preview never depends
        /// on a separate thumbnail capture window.
        static bool HasShellFrame(string path)
        {
            try
            {
                var info = new System.IO.FileInfo(path);
                return info.Exists && info.Length > 1600;
            }
            catch { return false; }
        }

        Button MiniButton(string text, Brush fg, Action click)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 11.5,
                Foreground = fg,
                Background = Theme.ChipBgB,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(9, 4, 9, 4),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            b.Template = Theme.ButtonTemplate;
            b.Click += (s, e) => { try { click(); } catch (Exception ex) { Logger.Error(ex); } };
            return b;
        }

        void TogglePreview(CardPreview ctrl, UIElement card)
        {
            if (_livePreview != null && _livePreview != ctrl) _livePreview.Stop();
            ctrl.Toggle();
            if (ctrl.IsActive) { _livePreview = ctrl; _liveCard = card; }
        }

        void Apply(WallpaperItem item)
        {
            if (item.IsMissing)
            {
                ShowNotice("This video's file is missing on disk — VulcansLivingWP cannot play it.\n" +
                           "Remove it from the library, then re-add it from the new location.");
                return;
            }
            _app.Engine.Apply(item);
        }

        void Remove(WallpaperItem item)
        {
            if (item == null) return;
            var r = MessageBox.Show(
                _owner,
                "Remove \"" + Path.GetFileName(item.FilePath) + "\" from the library?\n\n" +
                "The original video file will NOT be deleted.",
                "Remove wallpaper",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            var wasActive = _app.Engine.Current == item;
            _app.Library.Remove(item);
            try { File.Delete(_app.Thumbs.ThumbPath(item)); } catch { }
            if (wasActive) _app.Engine.StopAll();
        }
    }

    /// <summary>
    /// Plays a video inside the existing card thumbnail area. One instance per card.
    /// </summary>
    public class CardPreview
    {
        readonly Border _host;
        readonly FluxWallApp _app;
        readonly WallpaperItem _item;

        MediaElement _media;
        TextBlock _status;
        System.Windows.Threading.DispatcherTimer _watchdog;
        bool _opened;
        bool _playing;

        public CardPreview(Border host, FluxWallApp app, WallpaperItem item)
        {
            _host = host;
            _app = app;
            _item = item;
        }

        public void Refresh(WallpaperItem item)
        {
            _item.Id = item.Id;
            StopMedia();
            SetPlaceholder(item);
        }

        public WallpaperItem Item { get { return _item; } }
        public bool IsActive { get { return _media != null; } }

        public void Stop()
        {
            StopMedia();
        }

        public void Toggle()
        {
            Logger.Info("Card preview toggle: " + (_item == null ? "null" : Path.GetFileName(_item.FilePath)));
            if (_item == null || _item.IsMissing) return;

            if (_media == null)
            {
                StartMedia();
                return;
            }

            if (!_opened)
            {
                // Still loading or stuck: restart it.
                StopMedia();
                StartMedia();
                return;
            }

            try
            {
                _playing = !_playing;
                if (_playing) _media.Play();
                else _media.Pause();
            }
            catch (Exception e) { Logger.Error(e); }
        }

        void StartMedia()
        {
            if (_media != null) return;
            if (_item == null || _item.IsMissing) return;
            Logger.Info("Card preview start: " + _item.FilePath);

            _media = new MediaElement
            {
                UnloadedBehavior = MediaState.Close,
                LoadedBehavior = MediaState.Manual,
                ScrubbingEnabled = false,
                IsMuted = true,
            };
            _media.MediaOpened += (s, e) =>
            {
                Logger.Info("Card preview opened: " + Path.GetFileName(_item.FilePath));
                _opened = true;
                StopWatchdog();
                UpdateStatus("");
                PositionUnderThumb();
                try { _media.Play(); _playing = true; }
                catch (Exception ex) { Logger.Error(ex); }
            };
            _media.MediaFailed += (s, e) =>
            {
                Logger.Error(e.ErrorException ?? new Exception("Card preview media failed: " + _item.FilePath));
                var msg = "Windows could not decode this file." + HumanPlaybackMessage();
                UpdateStatus(msg, Theme.BadB);
            };
            _media.MediaEnded += (s, e) =>
            {
                try
                {
                    _media.Position = TimeSpan.Zero;
                    if (_playing) _media.Play();
                }
                catch { }
            };

            _status = new TextBlock
            {
                Text = "Loading preview…",
                Foreground = Theme.TextMutedB, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 0, 6, 4),
            };
            var layers = new Grid();
            layers.Children.Add(_media);
            layers.Children.Add(_status);
            _host.Child = layers;
            _opened = false;
            StartWatchdog();
            var media = _media;
            var uri = new Uri(_item.FilePath);
            media.Source = uri;
            try { media.Play(); _playing = true; }
            catch (Exception ex) { Logger.Error(ex); }
            PositionUnderThumb();
        }


        void StartWatchdog()
        {
            StopWatchdog();
            _watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _watchdog.Tick += (s, e) =>
            {
                StopWatchdog();
                if (_opened || _media == null) return;
                Logger.Warn("Card preview did not open within 8s: " + _item.FilePath);
                UpdateStatus("Preview didn't start. Click Preview to try again.", Theme.BadB);
            };
            _watchdog.Start();
        }

        void StopWatchdog()
        {
            if (_watchdog == null) return;
            _watchdog.Stop();
            _watchdog = null;
        }

        void StopMedia()
        {
            StopWatchdog();
            if (_media == null) return;
            try
            {
                _playing = false;
                _media.Stop();
                _media.Source = null;
                _media.Close();
            }
            catch { }
            _media = null;
            _host.Child = null;
            SetPlaceholder(_item);
        }

        void SetPlaceholder(WallpaperItem item)
        {
            if (item == null) return;
            if (item.IsMissing)
            {
                var missingOverlay = new Border
                {
                    Background = Theme.Bru(Color.FromArgb(200, 34, 30, 28)),
                };
                var mo = new TextBlock
                {
                    Text = "⚠  File missing",
                    Foreground = Theme.WarnB,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                };
                missingOverlay.Child = mo;
                _host.Child = missingOverlay;
                return;
            }

            var thumbPath = _app.Thumbs.ThumbPath(item);
            if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
            {
                try
                {
                    var img = new Image
                    {
                        Stretch = Stretch.UniformToFill,
                        Source = LibraryPage.LoadPng(thumbPath),
                    };
                    _host.Child = img;
                }
                catch { }
            }
            else
            {
                var placeholder = new TextBlock
                {
                    Text = "Generating…",
                    Foreground = Theme.TextMutedB,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                };
                _host.Child = placeholder;
                if (!item.IsMissing) _app.Thumbs.Request(item);
            }
        }

        void PositionUnderThumb()
        {
            if (_media == null) return;
            _media.Stretch = Stretch.UniformToFill;
            _media.HorizontalAlignment = HorizontalAlignment.Stretch;
            _media.VerticalAlignment = VerticalAlignment.Stretch;
        }

        void UpdateStatus(string text, Brush fg = null)
        {
            if (_status == null) return;
            _status.Text = text;
            _status.Foreground = fg ?? Theme.TextMutedB;
        }

        static string HumanPlaybackMessage()
        {
            return " This preview cannot open the video:\n" +
                   "the codec may be missing (e.g. HEVC/ProRes). Use an H.264 MP4 or install the\n" +
                   "'HEVC Video Extensions' from the Microsoft Store.";
        }
    }
}
