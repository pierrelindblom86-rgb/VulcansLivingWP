using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FluxWall.Common;

namespace FluxWall
{
    /// <summary>
    /// Global pause/resume hotkey, registered through RegisterHotKey on the
    /// main window. Config parsing is forgiving; failures log and disable.
    /// </summary>
    public class HotkeyService : IDisposable
    {
        const int HotkeyId = 0xB001;

        readonly FluxWallApp _app;
        HwndSource _source;
        uint _mods, _vk;

        /// <summary>Why the last Apply() failed, or null when the shortcut is active.</summary>
        public string LastError { get; private set; }

        public HotkeyService(FluxWallApp app)
        {
            _app = app;
            _app.Main.Win.SourceInitialized += (s, e) =>
            {
                _source = PresentationSource.FromVisual(_app.Main.Win) as HwndSource;
                if (_source != null)
                {
                    _source.AddHook(WndProc);
                    Apply(_app.Settings.PauseHotkey);
                }
            };
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                try { _app.Engine.TogglePause(); } catch (Exception e) { Logger.Error(e); }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Applies strings like "Ctrl+Alt+W" or "" (disable).
        /// Returns false (and sets <see cref="LastError"/>) when the text is not a
        /// valid combination or another program already owns it, so the Settings
        /// page can tell the user instead of silently dropping the shortcut.
        /// </summary>
        public bool Apply(string combo)
        {
            LastError = null;
            try
            {
                if (_source == null) return false;   // window not up yet; retried on startup
                if (!Parse(combo ?? "", out _mods, out _vk))
                {
                    try { Native.UnregisterHotKey(_source.Handle, HotkeyId); } catch { }
                    _mods = 0; _vk = 0;
                    if (!string.IsNullOrWhiteSpace(combo))
                    {
                        LastError = "That is not a valid shortcut. Use a form like Ctrl+Alt+W.";
                        Logger.Warn("Hotkey not recognized, disabled: \"" + combo + "\"");
                    }
                    return false;
                }
                try { Native.UnregisterHotKey(_source.Handle, HotkeyId); } catch { }
                if (!Native.RegisterHotKey(_source.Handle, HotkeyId, _mods, _vk))
                {
                    _mods = 0; _vk = 0;
                    LastError = "Another program already uses " + combo + ". Choose a different combination.";
                    Logger.Warn("Hotkey already in use, disabled: " + combo);
                    return false;
                }
                Logger.Info("Hotkey registered: " + combo);
                return true;
            }
            catch (Exception e)
            {
                LastError = "The shortcut could not be registered.";
                Logger.Error(new Exception("Hotkey registration failed for '" + combo + "'", e));
                return false;
            }
        }

        static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(combo)) return false;
            string keyToken = null;
            foreach (var raw in combo.Split('+'))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                switch (token.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "win": case "windows": mods |= Native.MOD_WIN; break;
                    default: keyToken = token; break;
                }
            }
            return keyToken != null && TryParseKey(keyToken, out vk) && mods != 0;
        }

        static bool TryParseKey(string token, out uint vk)
        {
            token = token.ToUpperInvariant();
            vk = 0;
            if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z')
            {
                vk = (uint)token[0];
                return true;
            }
            if (token.Length == 2 && token[0] == 'F' &&
                int.TryParse(token.Substring(1), out var fKey) && fKey >= 1 && fKey <= 12)
            {
                vk = (uint)(0x70 + fKey - 1);  // VK_F1 = 0x70 … VK_F12 = 0x7B
                return true;
            }
            uint spare;
            switch (token)
            {
                case "SPACE": case "SPACEBAR": vk = 0x20; break;
                case "PRIOR": case "PAGEUP": vk = 0x21; break;
                case "PAGEDOWN": vk = 0x22; break;
                case "INSERT": vk = 0x2D; break;
                case "DELETE": vk = 0x2E; break;
                case "HOME": vk = 0x24; break;
                default:
                    if (uint.TryParse(token.Replace("NUM", ""), out spare) && spare >= 0 && spare <= 9)
                    {
                        vk = 0x30 + spare;   // '0'-'9' are the same VK
                        return true;
                    }
                    break;
            }
            return vk != 0;
        }

        public void Dispose()
        {
            try { if (_source != null) Native.UnregisterHotKey(_source.Handle, HotkeyId); } catch { }
        }
    }
}
