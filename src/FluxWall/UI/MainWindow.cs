using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.Library;

namespace FluxWall.UI
{
    /// <summary>
    /// Main application window. Borderless custom chrome (safe with Aero),
    /// dark sidebar navigation, hosts the three pages. Closing the window
    /// hides to tray instead of exiting — Exit lives in the tray menu.
    /// </summary>
    public class MainWindow
    {
        readonly FluxWallApp _app;

        Window _win;
        Grid _root;
        Grid _titleBar;
        ContentControl _pageHost;
        TextBlock _stateChip;
        Border _stateDot;
        LibraryPage _library;
        SettingsPage _settingsPage;
        AboutPage _about;
        Button _navLibrary, _navSettings, _navAbout;

        public MainWindow(FluxWallApp app)
        {
            _app = app;
            Build();
        }

        public Window Win { get { return _win; } }
        public LibraryPage LibraryPage { get { return _library; } }

        void Build()
        {
            _win = new Window
            {
                Title = "VulcansLivingWP",
                Width = 1080,
                Height = 700,
                MinWidth = 820,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.BaseBgB,
                Foreground = Theme.TextB,
                FontFamily = Theme.Font,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ShowInTaskbar = true,
            };
            try { _win.Icon = WpfIconFlag(); } catch { }

            _win.Closing += OnClosing;
            _win.Loaded += OnLoaded;
            _win.StateChanged += (s, e) => UpdateMaximizeButton();
            _win.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && _win.WindowState != WindowState.Maximized) _win.Close();
            };

            _root = new Grid { Background = Theme.BaseBgB };
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });  // title bar
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _titleBar = BuildTitleBar();
            Grid.SetRow(_titleBar, 0);
            _root.Children.Add(_titleBar);

            var body = BuildBody();
            Grid.SetRow(body, 1);
            _root.Children.Add(body);

            _win.Content = _root;
        }

        ImageSource WpfIconFlag()
        {
            try
            {
                using (var s = new System.IO.MemoryStream(IconData.IcoBytes))
                {
                    var dec = System.Windows.Media.Imaging.BitmapFrame.Create(
                        s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    dec.Freeze();
                    return dec;
                }
            }
            catch { return null; }
        }

        static uint _taskbarCreatedMsg;

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyChrome();
            // "TaskbarCreated" is broadcast by Windows whenever Explorer (re)starts —
            // our wallpaper host layer comes back around the same moment.
            try
            {
                _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
                var src = HwndSource.FromVisual(_win) as HwndSource;
                src?.AddHook(WndHook);
            }
            catch (Exception ex) { Logger.Error(ex); }
        }

        IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
            {
                handled = false;
                var engine = _app?.Engine;
                if (engine != null && engine.Current != null)
                {
                    // The WorkerW layer is gone with the old Explorer instance,
                    // so the engine has to acquire a fresh host before applying.
                    engine.OnDisplayConfigurationChanged();
                    Logger.Info("Explorer restart detected (TaskbarCreated); wallpaper will be re-applied.");
                }
            }
            else if (msg == Native.WM_DISPLAYCHANGE)
            {
                // A cable, resolution, DPI or monitor-count change rebuilds the
                // desktop layer. Re-applying from here is event-driven; the 5s
                // health tick stays as the backstop for missed notifications.
                handled = false;
                Logger.Info(string.Format("WM_DISPLAYCHANGE received (msg=0x{0:X4}, w=0x{1:X}, l=0x{2:X}).",
                    msg, wParam.ToInt64(), lParam.ToInt64()));
                try { _app?.Engine?.OnDisplayConfigurationChanged(); }
                catch (Exception ex) { Logger.Error(ex); }
            }
            return IntPtr.Zero;
        }

        void ApplyChrome()
        {
            // WindowChrome via code keeps the title bar draggable with system
            // snapping + maximize behavior, while drawing our own 44px bar.
            var chrome = new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 44,
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(_win, chrome);
        }

        Grid BuildTitleBar()
        {
            var g = new Grid { Background = Theme.PanelBgB };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(206) }); // sidebar strip
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // state chip
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // buttons

            // Dragging is handled by WindowChrome (the bar is the caption area).
            // A manual DragMove here swallowed every click on the buttons.
            g.Cursor = Cursors.Arrow;

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
            };
            var logo = new Ellipse { Width = 18, Height = 18, Fill = Theme.AccentB };
            var brandLbl = Theme.Label("VulcansLivingWP");
            brandLbl.FontSize = 14;
            brandLbl.FontWeight = FontWeights.SemiBold;
            brandLbl.Margin = new Thickness(10, 0, 0, 0);
            brand.Children.Add(logo);
            brand.Children.Add(brandLbl);
            Grid.SetColumn(brand, 0);
            g.Children.Add(brand);

            var spacer = new Border { }; // click-through none
            Grid.SetColumn(spacer, 1);
            g.Children.Add(spacer);

            // Playback state chip
            _stateDot = new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Theme.GoodB,
            };
            _stateChip = new TextBlock
            {
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Text = "Idle",
            };
            var chip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0),
                IsHitTestVisible = false,
            };
            chip.Children.Add(_stateDot);
            chip.Children.Add(_stateChip);
            Grid.SetColumn(chip, 2);
            g.Children.Add(chip);

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var minBtn = ShellButton("–", () => _win.WindowState = WindowState.Minimized);
            minBtn.ToolTip = "Minimize";
            btns.Children.Add(minBtn);

            _maxBtn = ShellButton("☐", () =>
                _win.WindowState = _win.WindowState == WindowState.Maximized
                    ? WindowState.Normal : WindowState.Maximized);
            btns.Children.Add(_maxBtn);
            UpdateMaximizeButton();

            var closeBtn = CloseButton();
            closeBtn.ToolTip = "Close";
            btns.Children.Add(closeBtn);
            Grid.SetColumn(btns, 3);
            g.Children.Add(btns);

            return g;
        }

        Button _maxBtn;

        /// <summary>Shows Restore (❐) while maximized, Maximize (☐) otherwise.</summary>
        void UpdateMaximizeButton()
        {
            if (_maxBtn == null) return;
            bool max = _win != null && _win.WindowState == WindowState.Maximized;
            ((TextBlock)_maxBtn.Content).Text = max ? "❐" : "☐";
            _maxBtn.ToolTip = max ? "Restore" : "Maximize";
        }

        Button ShellButton(string glyph, Action click)
        {
            var b = new Button
            {
                Content = new TextBlock
                {
                    Text = glyph, FontSize = 12, Foreground = Theme.TextMutedB,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                Width = 42, Height = 44,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            // Without this, WindowChrome treats the caption area as a drag handle
            // and the click never reaches the button.
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (s, e) => { try { click(); } catch (Exception ex) { Logger.Error(ex); } };
            b.MouseEnter += (s, e) => b.Background = Theme.Bru(Theme.From(0x23232D));
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }

        Button CloseButton()
        {
            var b = ShellButton("✕", () => TryHide());
            b.Width = 46;
            return b;
        }

        Grid BuildBody()
        {
            var g = new Grid { Background = Theme.BaseBgB };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            // The page column must exist: without it the page renders under the
            // sidebar and its left side is clipped.
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            g.Children.Add(sidebar);

            _pageHost = new ContentControl
            {
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(_pageHost, 1);
            g.Children.Add(_pageHost);
            return g;
        }

        Grid BuildSidebar()
        {
            var g = new Grid { Background = Theme.PanelBgB };
            var panel = new StackPanel { Margin = new Thickness(14, 18, 14, 14) };

            _navLibrary = NavButton("Library", true);
            _navSettings = NavButton("Settings", false);
            _navAbout = NavButton("About", false);
            panel.Children.Add(_navLibrary);
            panel.Children.Add(_navSettings);
            panel.Children.Add(_navAbout);

            // Bottom-side info
            var bottom = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 0.9,
            };
            bottom.Children.Add(Theme.Label("Free forever"));
            bottom.Children.Add(Theme.Label("v" + AppVersion.Text));
            ((TextBlock)bottom.Children[0]).Foreground = Theme.TextMutedB;
            ((TextBlock)bottom.Children[0]).FontSize = 11;
            ((TextBlock)bottom.Children[1]).Foreground = Theme.TextMutedB;
            ((TextBlock)bottom.Children[1]).FontSize = 11;

            g.Children.Add(panel);
            g.Children.Add(bottom);
            return g;
        }

        Button NavButton(string text, bool active)
        {
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = text,
                FontSize = 14,
                Foreground = active ? Theme.TextB : Theme.TextMutedB,
                Background = active ? Theme.Bru(Theme.From(0x23232D)) : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 10, 14, 10),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 6),
            };
            b.Template = Theme.ButtonTemplate;
            b.Click += (s, e) =>
            {
                if (text == "Library") Navigate(Page.Library);
                else if (text == "Settings") Navigate(Page.Settings);
                else Navigate(Page.About);
            };
            return b;
        }

        #region Pages
        public enum Page { Library, Settings, About }

        public void Navigate(Page page)
        {
            switch (page)
            {
                case Page.Library:
                    _pageHost.Content = _library.Content;
                    break;
                case Page.Settings:
                    _pageHost.Content = _settingsPage.Content;
                    break;
                case Page.About:
                    _pageHost.Content = _about.Content;
                    break;
            }
            Highlight(page);
        }

        void Highlight(Page page)
        {
            SetNav(_navLibrary, page == Page.Library);
            SetNav(_navSettings, page == Page.Settings);
            SetNav(_navAbout, page == Page.About);
        }

        void SetNav(Button b, bool on)
        {
            b.Background = on ? Theme.Bru(Theme.From(0x23232D)) : Brushes.Transparent;
            b.Foreground = on ? Theme.TextB : Theme.TextMutedB;
        }
        #endregion

        #region Public surface

        public void Init(FluxWallApp app)
        {
            _library = new LibraryPage(app, _win);
            _settingsPage = new SettingsPage(app);
            _about = new AboutPage(app);
            UpdateStateChip();
            Navigate(Page.Library);

            _app.Engine.ManualPauseChanged += UpdateStateChip;
            _app.Engine.WallpaperApplied += OnApplied;
            _app.Library.ItemsChanged += OnLibraryChanged;
        }

        public void UpdateStateChip()
        {
            if (_stateChip == null) return;
            var engine = _app.Engine;
            if (engine == null || engine.Current == null)
            {
                _stateDot.Background = Theme.Bru(Theme.From(0x4C4C58));
                _stateChip.Text = "No wallpaper";
            }
            else if (engine.ManualPaused)
            {
                _stateDot.Background = Theme.WarnB;
                _stateChip.Text = "Paused (manual)";
            }
            else if (engine.FullscreenActive && _app.Settings.AutoPauseFullscreen)
            {
                _stateDot.Background = Theme.WarnB;
                _stateChip.Text = "Paused (fullscreen)";
            }
            else
            {
                _stateDot.Background = Theme.GoodB;
                _stateChip.Text = "Active: " + System.IO.Path.GetFileName(engine.Current.FilePath);
            }
        }

        void OnApplied(WallpaperItem item)
        {
            try
            {
                _win.Dispatcher.Invoke(() =>
                {
                    UpdateStateChip();
                    _library?.RefreshCards();
                });
            }
            catch { }
        }

        void OnLibraryChanged()
        {
            try
            {
                _win.Dispatcher.Invoke(() =>
                {
                    _library?.RefreshCards();
                    UpdateStateChip();
                });
            }
            catch { }
        }

        public void Show()
        {
            _win.Show();
            _win.Activate();
        }

        public void TryHide()
        {
            if (_app.Settings.ShowTrayIcon && _app.Tray != null)
            {
                _win.Hide();
            }
            else
            {
                _app.ExitClean();
            }
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_app.Settings.ShowTrayIcon)
            {
                e.Cancel = true;      // hiding to tray, app keeps wallpaper
                _win.Hide();
            }
        }
        #endregion
    }
}
