using System;
using FluxWall.Settings;
using Microsoft.Win32;

namespace FluxWall.Common
{
    /// <summary>
    /// Handles the settings the installer offers as checkboxes. Running the app
    /// normally to apply them would have shown a window and started the wallpaper,
    /// so these switches are handled before any UI exists.
    ///
    ///   --setstartup=on|off
    ///   --settray=on|off
    ///   --setrestore=on|off
    /// </summary>
    public static class InstallerSetup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "VulcansLivingWP";

        /// <summary>Applies any installer switches in <paramref name="args"/>.</summary>
        /// <returns>True when at least one switch was handled (caller should exit).</returns>
        public static bool TryApply(string[] args)
        {
            if (args == null) return false;

            bool handled = false;
            var settings = SettingsStore.Current;

            foreach (var raw in args)
            {
                if (raw == null) continue;
                string a = raw.Trim();
                if (TryFlag(a, "--setstartup=", v => { settings.StartWithWindows = v; ApplyRunKey(v); })) { handled = true; continue; }
                if (TryFlag(a, "--settray=", v => settings.ShowTrayIcon = v)) { handled = true; continue; }
                if (TryFlag(a, "--setrestore=", v => settings.RestoreLastWallpaper = v)) { handled = true; continue; }
            }

            if (!handled) return false;

            // The installer just placed a fresh copy on disk; do not trigger the
            // first-run balloon in front of the setup wizard.
            settings.FirstRunDone = true;
            SettingsStore.Save();
            Logger.Info("Installer options applied.");
            return true;
        }

        static bool TryFlag(string arg, string prefix, Action<bool> apply)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string value = arg.Substring(prefix.Length).Trim();
            bool on = value.Equals("on", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            try { apply(on); }
            catch (Exception e) { Logger.Warn("Installer option '" + arg + "' failed: " + e.Message); }
            return true;
        }

        static void ApplyRunKey(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunValue, "\"" + exe + "\" --minimized");
                    }
                    else if (key.GetValue(RunValue) != null)
                    {
                        key.DeleteValue(RunValue, throwOnMissingValue: false);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Start-with-Windows registry update failed: " + e.Message);
            }
        }
    }
}
