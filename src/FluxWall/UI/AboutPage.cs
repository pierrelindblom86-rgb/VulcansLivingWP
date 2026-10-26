using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluxWall.Common;

namespace FluxWall.UI
{
    /// <summary>About page + friendly play-info row.</summary>
    public class AboutPage
    {
        private readonly FluxWallApp _app;
        Grid _page;

        public AboutPage(FluxWallApp app)
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
                       "• No accounts, no tracking, no network: it keeps working fully offline.",
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

            var troubleshooting = Theme.AccentButton("Open troubleshooting log folder", () => _app.OpenLogFolder(), 13);
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
