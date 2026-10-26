using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluxWall.Common;
using FluxWall.Updates;

namespace FluxWall.UI
{
    /// <summary>About page, installed version and the GitHub update check.</summary>
    public class AboutPage
    {
        private readonly FluxWallApp _app;
        Grid _page;

        TextBlock _updateHeadline;
        TextBlock _updateStatus;
        TextBlock _updateNotes;
        Button _checkButton;
        Button _openButton;
        ScrollViewer _notesBox;

        public AboutPage(FluxWallApp app)
        {
            _app = app;
            Build();
            UpdateService.Completed += OnUpdateChecked;
            RefreshUpdate(UpdateService.Last);
        }

        public object Content { get { return _page; } }

        void Build()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(32, 30, 32, 32),
            };

            var root = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            scroll.Content = root;

            var title = Theme.Label("VulcansLivingWP — Free Live Wallpaper");
            title.FontSize = 22; title.FontWeight = FontWeights.SemiBold;
            root.Children.Add(title);

            var ver = new TextBlock
            {
                Text = "Version " + AppVersion.Text + "  —  Built for Windows 10 & 11",
                Foreground = Theme.TextMutedB, FontSize = 13, Margin = new Thickness(0, 4, 0, 16),
            };
            root.Children.Add(ver);

            root.Children.Add(new TextBlock
            {
                Text = "What is this?",
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB, Margin = new Thickness(0, 0, 0, 8),
            });
            root.Children.Add(new TextBlock
            {
                Text = "VulcansLivingWP takes your own MP4 or MOV video files and plays them as an animated\n" +
                       "wallpaper on your desktop, behind your icons — for free. It uses the Windows\n" +
                       "Media Foundation projector + the desktop WorkerW layer for a natural, native feel.",
                Foreground = Theme.TextMutedB, FontSize = 13, LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 14),
            });

            root.Children.Add(new TextBlock
            {
                Text = "How it stays light",
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB, Margin = new Thickness(0, 0, 0, 8),
            });
            root.Children.Add(new TextBlock
            {
                Text = "• Hardware-accelerated decoding (Intel/NVIDIA/AMD) with Windows Media Foundation.\n" +
                       "• Zero layout while paused and almost-never-render when the desktop is covered.\n" +
                       "• No files are copied or re-encoded — your library is a simple list of paths.\n" +
                       "• No accounts and no tracking. The only network request is the optional GitHub\n" +
                       "  update check, and everything keeps working offline.",
                Foreground = Theme.TextMutedB, FontSize = 13, LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 14),
            });

            root.Children.Add(new TextBlock
            {
                Text = "Codecs",
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB, Margin = new Thickness(0, 0, 0, 8),
            });
            root.Children.Add(new TextBlock
            {
                Text = "H.264- or H.265-encoded MP4 and MOV files work the moment the matching\n" +
                       "Windows codecs are present. (Windows ships H.264 everywhere; H.265/HEVC is\n" +
                       "free from the Microsoft Store as 'HEVC Video Extensions'.) Older Apple ProRes\n" +
                       "or animation-encoded MOV files are not decodable by Windows, so a re-encode\n" +
                       "with any free tool to H.264 MP4 fixes playback.",
                Foreground = Theme.TextMutedB, FontSize = 13, LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 16),
            });

            root.Children.Add(BuildUpdateCard());

            var troubleshooting = Theme.AccentButton("Open troubleshooting log folder", () => _app.OpenLogFolder(), 13);
            troubleshooting.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(troubleshooting);

            root.Children.Add(new TextBlock
            {
                Text = "This application is original free software released under the MIT license.\n" +
                       "It contains no advertising, subscriptions, accounts or online activation.",
                Foreground = Theme.TextMutedB, FontSize = 12,
                Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap,
            });

            _page = new Grid { Background = Theme.BaseBgB };
            _page.Children.Add(scroll);
        }

        #region Updates

        FrameworkElement BuildUpdateCard()
        {
            var card = new Border
            {
                Background = Theme.PanelBgB,
                BorderBrush = Theme.BorderB,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 18),
            };
            var s = new StackPanel();
            card.Child = s;

            s.Children.Add(new TextBlock
            {
                Text = "Updates",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextB,
                Margin = new Thickness(0, 0, 0, 6),
            });

            _updateHeadline = new TextBlock
            {
                Text = "Installed version " + AppVersion.Text,
                FontSize = 13,
                Foreground = Theme.TextB,
            };
            s.Children.Add(_updateHeadline);

            _updateStatus = new TextBlock
            {
                Text = "",
                FontSize = 12.5,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10),
            };
            s.Children.Add(_updateStatus);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _checkButton = Theme.AccentButton("Check for updates", () =>
            {
                if (UpdateService.IsChecking) return;
                RefreshUpdate(null);
                UpdateService.CheckAsync(null);
            }, 13);
            buttons.Children.Add(_checkButton);

            _openButton = Theme.QuietButton("Open release page", OpenReleasePage, Theme.TextMutedB);
            _openButton.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(_openButton);
            s.Children.Add(buttons);

            _notesBox = new ScrollViewer
            {
                MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 0),
            };
            _updateNotes = new TextBlock
            {
                FontSize = 12,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
            };
            _notesBox.Content = _updateNotes;
            s.Children.Add(_notesBox);

            s.Children.Add(new TextBlock
            {
                Text = "Checks read the public release list on GitHub. Nothing is downloaded or " +
                       "installed automatically, and no personal information is sent. " +
                       "Turn the check on or off in Settings.",
                FontSize = 11.5,
                Foreground = Theme.TextMutedB,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });

            return card;
        }

        void OnUpdateChecked(UpdateResult result)
        {
            try
            {
                _app.Dispatcher.BeginInvoke(new Action(() => RefreshUpdate(result)));
            }
            catch (Exception e) { Logger.Error(e); }
        }

        /// <summary>Redraws the update card. Null means "a check is running".</summary>
        public void RefreshUpdate(UpdateResult result)
        {
            if (_updateStatus == null) return;

            if (result == null)
            {
                _updateHeadline.Text = "Installed version " + AppVersion.Text;
                _updateStatus.Text = "Checking GitHub for the latest stable release…";
                _updateStatus.Foreground = Theme.TextMutedB;
                _checkButton.IsEnabled = false;
                return;
            }

            _checkButton.IsEnabled = true;
            _updateHeadline.Text = "Installed version " + AppVersion.Text +
                                   (string.IsNullOrEmpty(result.Latest) ? "" : "   ·   Latest stable " + result.Latest);

            switch (result.State)
            {
                case UpdateState.Available:
                    _updateStatus.Foreground = Theme.GoodB;
                    break;
                case UpdateState.Failed:
                    _updateStatus.Foreground = Theme.WarnB;
                    break;
                default:
                    _updateStatus.Foreground = Theme.TextMutedB;
                    break;
            }

            var text = result.Message ?? "";
            if (result.CheckedUtc > DateTime.MinValue && result.FromCache)
                text += "  (last checked " + result.CheckedUtc.ToLocalTime().ToString("g") + ")";
            _updateStatus.Text = text;

            bool hasNotes = result.Release != null && !string.IsNullOrEmpty(result.Release.Notes);
            _updateNotes.Text = hasNotes ? result.Release.Notes : "";
            _notesBox.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;

            bool available = result.State == UpdateState.Available;
            _openButton.Content = available ? "Open release page" : "Open all releases";
        }

        void OpenReleasePage()
        {
            try
            {
                var last = UpdateService.Last;
                var url = last != null && last.State == UpdateState.Available && last.Release != null
                    ? last.Release.Url
                    : UpdateService.ReleasesPageUrl;

                // Only ever an https GitHub link; the user's browser does the rest.
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    url = UpdateService.ReleasesPageUrl;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e) { Logger.Error(e); }
        }

        #endregion

        void OpenLogFolderSafe()
        {
            try
            {
                if (!Directory.Exists(Logger.LogFolder))
                    Directory.CreateDirectory(Logger.LogFolder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Logger.LogFolder + "\"");
            }
            catch (Exception e) { Logger.Error(e); }
        }
    }
}
