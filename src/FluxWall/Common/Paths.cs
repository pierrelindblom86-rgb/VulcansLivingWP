using System;
using System.IO;

namespace FluxWall.Common
{
    /// <summary>Central, well-known folder locations. Created on demand, never throws.</summary>
    public static class Paths
    {
        public static string Root
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "VulcansLivingWP");
                }
                catch { return "."; }
            }
        }

        public static string LogsDir { get { return Path.Combine(Root, "logs"); } }
        public static string ThumbsDir { get { return Path.Combine(Root, "thumbs"); } }
        public static string SettingsFile { get { return Path.Combine(Root, "VulcansLivingWP-settings.xml"); } }
        public static string LibraryFile { get { return Path.Combine(Root, "VulcansLivingWP-library.xml"); } }

        /// <summary>Folder used by 1.0.x, which still shipped under the old name.</summary>
        public static string LegacyRoot
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FluxWall");
                }
                catch { return null; }
            }
        }

        public static void Ensure()
        {
            foreach (var dir in new[] { Root, LogsDir, ThumbsDir })
            {
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                catch { /* never fatal */ }
            }
        }

        /// <summary>
        /// Copies settings, library and cached thumbnails from the 1.0.x folder
        /// (<c>%LOCALAPPDATA%\FluxWall</c>) into the current one, once.
        /// The old folder is left untouched: an upgrade must never destroy data,
        /// and a returning 1.0 user keeps a working install.
        /// </summary>
        public static void MigrateLegacyData()
        {
            try
            {
                var legacy = LegacyRoot;
                if (string.IsNullOrEmpty(legacy) || !Directory.Exists(legacy)) return;
                if (string.Equals(legacy, Root, StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(SettingsFile)) return; // already migrated / already ours

                Ensure();

                CopyIfExists(Path.Combine(legacy, "settings.xml"), SettingsFile);
                CopyIfExists(Path.Combine(legacy, "library.xml"), LibraryFile);

                var oldThumbs = Path.Combine(legacy, "thumbs");
                if (Directory.Exists(oldThumbs))
                {
                    foreach (var png in Directory.GetFiles(oldThumbs, "*.png"))
                        CopyIfExists(png, Path.Combine(ThumbsDir, Path.GetFileName(png)));
                }

                Logger.Info("Migrated settings from the older data folder: " + legacy);
            }
            catch (Exception e)
            {
                Logger.Warn("Could not migrate the older data folder: " + e.Message);
            }
        }

        static void CopyIfExists(string from, string to)
        {
            try
            {
                if (!File.Exists(from) || File.Exists(to)) return;
                File.Copy(from, to);
            }
            catch (Exception e)
            {
                Logger.Warn("Could not copy " + from + ": " + e.Message);
            }
        }
    }
}
