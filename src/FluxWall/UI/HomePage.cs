using System;
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
    /// Landing page (first tab): what is playing now, playback state and quick actions.
    /// Reads engine and library state only; it never changes engine behaviour.
    /// </summary>
    public class HomePage
    {
        readonly FluxWallApp _app;
        readonly Action<MainWindow.Page> _navigate;
        readonly Grid _root;
        Image _preview;
        TextBlock _placeholder, _title, _subtitle, _status;
        TextBlock _libraryValue, _modeValue, _stateValue;
        Button _pauseButton;

        public HomePage(FluxWallApp app, Action<MainWindow.Page> navigate)
        {
            _app = app;
            _navigate = navigate;
            _root = Build();

            _app.Engine.ManualPauseChanged += Refresh;
            _app.Engine.WallpaperApplied += item => Refresh();
            _app.Library.ItemsChanged += Refresh;
            Refresh();
        }

        public object Content { get { return _root; } }

        Grid Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(Text("Home", 26, Theme.TextB, true));
            header.Children.Add(Text("What's playing now, and what to do next.", 13, Theme.TextMutedB));
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // Now playing card
            var previewHost = new Grid { Height = 240, Background = Theme.PanelBgB };
            _preview = new Image { Stretch = Stretch.Uniform };
            _placeholder = Text("No preview yet", 14, Theme.TextMutedB);
            _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            _placeholder.VerticalAlignment = VerticalAlignment.Center;
            previewHost.Children.Add(_preview);
            previewHost.Children.Add(_placeholder);

            _title = Text("", 20, Theme.TextB, true);
            _title.Margin = new Thickness(0, 14, 0, 2);
            _subtitle = Text("", 13, Theme.TextMutedB);
            _status = Text("", 13, Theme.CyanB);
            _status.Margin = new Thickness(0, 8, 0, 0);

            _pauseButton = Theme.QuietButton("Pause", TogglePause);
            _pauseButton.Width = 120;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            actions.Children.Add(_pauseButton);

            var nowStack = new StackPanel();
            nowStack.Children.Add(previewHost);
            nowStack.Children.Add(_title);
            nowStack.Children.Add(_subtitle);
            nowStack.Children.Add(_status);
            nowStack.Children.Add(actions);

            var nowCard = Card(nowStack, 18, new Thickness(0, 0, 18, 0));
            Grid.SetColumn(nowCard, 0);
            body.Children.Add(nowCard);

            // Side column: stats and quick actions
            var side = new StackPanel();
            _libraryValue = AddStat(side, "Wallpapers in library");
            _modeValue = AddStat(side, "Display mode");
            _stateValue = AddStat(side, "Playback");

            var choose = Theme.AccentButton("Choose wallpaper", () => _navigate(MainWindow.Page.Library), 14, new Thickness(14, 10, 14, 10));
            choose.Margin = new Thickness(0, 6, 0, 8);
            side.Children.Add(choose);

            var displays = Theme.QuietButton("Open displays", () => _navigate(MainWindow.Page.Displays));
            displays.Margin = new Thickness(0, 0, 0, 8);
            side.Children.Add(displays);

            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            return root;
        }

        /// <summary>Re-reads engine and library state. Safe to call from any thread.</summary>
        public void Refresh()
        {
            if (!_root.Dispatcher.CheckAccess())
            {
                _root.Dispatcher.BeginInvoke(new Action(Refresh));
                return;
            }

            var engine = _app.Engine;
            var item = engine.Current;

            if (item == null)
            {
                _title.Text = "No wallpaper applied";
                _subtitle.Text = "Choose a video from your library to get started.";
            }
            else
            {
                _title.Text = Path.GetFileNameWithoutExtension(item.FilePath);
                _subtitle.Text = "Now showing";
            }
            SetPreview(item);

            string state =
                item == null ? "No wallpaper" :
                engine.ManualPaused ? "Paused" :
                engine.FullscreenActive ? "Paused for a fullscreen app" :
                engine.EnvironmentPaused ? "Paused by a system rule" :
                engine.IsPlaying ? "Playing" : "Idle";
            _status.Text = state;
            _stateValue.Text = state;

            _libraryValue.Text = _app.Library.Items.Count.ToString();
            _modeValue.Text = ModeText(_app.Settings.DisplayMode);

            _pauseButton.Content = engine.ManualPaused ? "Resume" : "Pause";
            _pauseButton.IsEnabled = item != null;
        }

        void TogglePause()
        {
            _app.Engine.TogglePause();
        }

        void SetPreview(WallpaperItem item)
        {
            _preview.Source = null;
            _placeholder.Visibility = Visibility.Visible;
            if (item == null || !item.HasThumb) return;

            var path = _app.Thumbs.ThumbPath(item);
            if (!File.Exists(path)) return;
            try
            {
                _preview.Source = LibraryPage.LoadPng(path);
                _placeholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        static string ModeText(DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.Independent: return "Independent per display";
                case DisplayMode.Span: return "Span across displays";
                default: return "Same on all displays";
            }
        }

        static TextBlock AddStat(Panel host, string label)
        {
            var value = Text("", 18, Theme.TextB, true);
            var stack = new StackPanel();
            stack.Children.Add(Text(label, 12, Theme.TextMutedB));
            stack.Children.Add(value);
            host.Children.Add(Card(stack, 14, new Thickness(0, 0, 0, 10)));
            return value;
        }

        static Border Card(UIElement content, double padding, Thickness margin)
        {
            return Theme.Card(content, padding, margin);
        }

        static TextBlock Text(string text, double size, Brush foreground, bool bold = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = foreground,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
            };
        }
    }
}
