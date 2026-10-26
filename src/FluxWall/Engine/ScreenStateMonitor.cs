using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using FluxWall.Common;
using Microsoft.Win32;

namespace FluxWall.Engine
{
    /// <summary>
    /// Tells the engine when the desktop cannot be seen, so playback can stop
    /// instead of decoding video behind a lock screen, a switched-off display,
    /// a disconnected Remote Desktop session or a sleeping machine.
    ///
    /// Everything is event driven: Windows sends a session notification on
    /// lock/unlock, and a power-setting notification for the console display
    /// state. Notifications are registered on a private message-only window, so
    /// no polling loop is needed. If registration fails the app simply keeps
    /// playing as before — this component must never prevent startup.
    /// </summary>
    public class ScreenStateMonitor : IDisposable
    {
        #region Interop
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_POWERSETTINGCHANGE = 0x8017;
        const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

        /// <summary>{6FE69556-704A-47A0-8F24-C28D936FDA47} — 0 = off, 1 = on, 2 = dimmed.</summary>
        static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [StructLayout(LayoutKind.Sequential)]
        struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid guid, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        #endregion

        HwndSource _source;
        IntPtr _registration = IntPtr.Zero;

        bool _locked;
        bool _displayOff;
        bool _disconnected;
        bool _suspended;
        bool _forced;
        bool _started;
        bool _lastUnavailable;

        /// <summary>Raised whenever <see cref="Unavailable"/> may have changed. Any thread.</summary>
        public event Action Changed;

        /// <summary>True while the wallpaper cannot be seen by the user.</summary>
        public bool Unavailable
        {
            get { return _forced || _suspended || _locked || _disconnected || _displayOff; }
        }

        /// <summary>Short reason, for the log and the diagnostics UI.</summary>
        public string Describe()
        {
            if (_forced) return "test override";
            if (_suspended) return "system suspended";
            if (_locked) return "session locked";
            if (_disconnected) return "session disconnected";
            if (_displayOff) return "display off";
            return "screen visible";
        }

        public void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                var p = new HwndSourceParameters("VulcansLivingWP screen state")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0,
                    ParentWindow = HWND_MESSAGE,   // message-only: never appears anywhere
                };
                _source = new HwndSource(p);
                _source.AddHook(WndProc);
                var displayGuid = GUID_CONSOLE_DISPLAY_STATE;   // ref needs a writable local
                _registration = RegisterPowerSettingNotification(
                    _source.Handle, ref displayGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
                if (_registration == IntPtr.Zero)
                    Logger.Warn("Display-state notifications are unavailable; playback will not pause when the screen turns off.");
            }
            catch (Exception e)
            {
                Logger.Warn("Screen-state window could not be created: " + e.Message);
            }

            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception e)
            {
                Logger.Warn("Session notifications are unavailable: " + e.Message);
            }

            Recompute();
        }

        /// <summary>
        /// Test hook (--testsuspend): pretends the screen is unavailable, so the
        /// pause/resume path can be verified without locking a real session.
        /// </summary>
        public void ForceUnavailable(bool unavailable)
        {
            _forced = unavailable;
            Recompute();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _locked = true;
                    break;
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    _disconnected = true;
                    break;
                case SessionSwitchReason.SessionUnlock:
                    _locked = false;
                    // Windows reports the display as on once the session is usable
                    // again; a stale "off" flag would pause playback forever.
                    _displayOff = false;
                    break;
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                    _disconnected = false;
                    _displayOff = false;
                    break;
                default:
                    return;
            }
            Recompute();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend: _suspended = true; break;
                case PowerModes.Resume: _suspended = false; break;
                default: return;
            }
            Recompute();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
                {
                    var setting = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(
                        lParam, typeof(POWERBROADCAST_SETTING));
                    if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE && setting.DataLength >= 4)
                    {
                        _displayOff = setting.Data == 0;
                        Recompute();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Handling a display-state notification failed", e));
            }
            return IntPtr.Zero;
        }

        void Recompute()
        {
            bool now = Unavailable;
            if (now == _lastUnavailable) return;
            _lastUnavailable = now;

            Logger.Info(now
                ? "Screen unavailable: " + Describe() + "."
                : "Screen available again (" + Describe() + ").");

            var h = Changed;
            if (h != null)
            {
                try { h(); } catch (Exception e) { Logger.Error(e); }
            }
        }

        public void Dispose()
        {
            _started = false;
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch { }

            if (_registration != IntPtr.Zero)
            {
                try { UnregisterPowerSettingNotification(_registration); } catch { }
                _registration = IntPtr.Zero;
            }
            if (_source != null)
            {
                try { _source.RemoveHook(WndProc); } catch { }
                try { _source.Dispose(); } catch { }
                _source = null;
            }
        }
    }
}
