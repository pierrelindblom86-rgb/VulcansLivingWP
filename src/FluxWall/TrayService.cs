using System;
using System.Drawing;
using System.Windows.Forms;
using FluxWall.Common;
using FluxWall.Engine;
using FluxWall.UI;

namespace FluxWall
{
    /// <summary>
    /// Tray icon + lightweight context menu. WinForms' NotifyIcon is used for
    /// ICO rendering only; everything else stays in WPF. Painting-free design:
    /// we reuse the app's own icon (no extra 100KB asset precision work).
    /// </summary>
    public class TrayService : IDisposable
    {
        readonly FluxWallApp _app;
        NotifyIcon _icon;
        ContextMenuStrip _menu;
        ToolStripMenuItem _pauseItem;
        ToolStripMenuItem _visualizerItem;
        ToolStripMenuItem _visualizerEditItem;
        ToolStripMenuItem _visualizerLockItem;

        public TrayService(FluxWallApp app)
        {
            _app = app;
            Build();
        }

        void Build()
        {
            _menu = new ContextMenuStrip();
            _pauseItem = new ToolStripMenuItem("Pause wallpaper") { Checked = false };
            _pauseItem.Click += (s, e) => _app.Engine.TogglePause();
            _menu.Items.Add(_pauseItem);

            var open = new ToolStripMenuItem("Open VulcansLivingWP…");
            open.Click += (s, e) => _app.ShowMainWindow();
            _menu.Items.Add(open);

            _menu.Items.Add(new ToolStripSeparator());

            // Audio visualiser: enable/pause, unlock for editing, lock again.
            // The lock lives here as well as in Settings because a locked
            // visualiser is click-through, so it cannot unlock itself.
            _visualizerItem = new ToolStripMenuItem("Audio visualiser") { CheckOnClick = true };
            _visualizerItem.Click += (s, e) => _app.Visualizer.SetEnabled(_visualizerItem.Checked);
            _menu.Items.Add(_visualizerItem);

            _visualizerEditItem = new ToolStripMenuItem("Edit visualiser position…");
            _visualizerEditItem.Click += (s, e) => _app.Visualizer.BeginEdit();
            _menu.Items.Add(_visualizerEditItem);

            _visualizerLockItem = new ToolStripMenuItem("Lock visualiser") { CheckOnClick = true };
            _visualizerLockItem.Click += (s, e) =>
            {
                if (!_app.Settings.VisualizerEnabled)
                {
                    _visualizerLockItem.Checked = false;
                    return;
                }
                _app.Visualizer.SetLocked(_visualizerLockItem.Checked);
            };
            _menu.Items.Add(_visualizerLockItem);

            var visualizerSettings = new ToolStripMenuItem("Visualiser settings…");
            visualizerSettings.Click += (s, e) =>
            {
                _app.ShowMainWindow();
                _app.Main?.Navigate(MainWindow.Page.Settings);
            };
            _menu.Items.Add(visualizerSettings);

            _menu.Items.Add(new ToolStripSeparator());
            var restore = new ToolStripMenuItem("Restore last wallpaper");
            restore.Click += (s, e) => _app.RestoreLast();
            _menu.Items.Add(restore);

            var stop = new ToolStripMenuItem("Stop wallpaper");
            stop.Click += (s, e) => _app.Engine.StopAll();
            _menu.Items.Add(stop);

            _menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => _app.ExitClean();
            _menu.Items.Add(exit);

            _icon = new NotifyIcon
            {
                Text = "VulcansLivingWP — animated wallpaper",
                Visible = _app.Settings.ShowTrayIcon,
            };
            try
            {
                using (var ms = new System.IO.MemoryStream(IconData.IcoBytes))
                    _icon.Icon = new Icon(ms);
            }
            catch (Exception e) { Logger.Error(e); }

            _icon.ContextMenuStrip = _menu;
            _icon.DoubleClick += (s, e) => _app.ShowMainWindow();
            _icon.Click += (s, e) => UpdateMenuState();

            _app.Engine.ManualPauseChanged += UpdateMenuState;
            _app.Engine.WallpaperApplied += _ => UpdateMenuState();
            _app.Visualizer.StatusChanged += () =>
            {
                try { _app.Dispatcher.BeginInvoke(new Action(UpdateMenuState)); }
                catch { }
            };
            UpdateMenuState();
        }

        /// <summary>Applies the ShowTrayIcon setting live (from the settings page).</summary>
        public void CommitVisible(bool visible)
        {
            if (_icon != null) _icon.Visible = visible;
        }

        void UpdateMenuState()
        {
            try
            {
                _pauseItem.Checked = _app.Engine.ManualPaused;
                _pauseItem.Text = _app.Engine.ManualPaused ? "Resume wallpaper" : "Pause wallpaper";

                bool visualizerOn = _app.Settings.VisualizerEnabled;
                _visualizerItem.Checked = visualizerOn;
                _visualizerEditItem.Enabled = visualizerOn;
                _visualizerLockItem.Enabled = visualizerOn;
                _visualizerLockItem.Checked = visualizerOn && _app.Settings.VisualizerLocked;
                _icon.Text = _app.Engine.ManualPaused
                    ? "VulcansLivingWP — wallpaper paused"
                    : "VulcansLivingWP — animated wallpaper";
            }
            catch { }
        }

        public void ShowFirstRunBalloon()
        {
            try
            {
                _icon.BalloonTipTitle = "VulcansLivingWP is running";
                _icon.BalloonTipText = "VulcansLivingWP lives in your system tray. Double-click to open it.";
                _icon.ShowBalloonTip(2500);
            }
            catch { }
        }

        public void Dispose()
        {
            try { if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; } } catch { }
            try { if (_menu != null) { _menu.Dispose(); _menu = null; } } catch { }
        }
    }
}
