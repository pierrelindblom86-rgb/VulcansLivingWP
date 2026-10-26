using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace FluxWall.Library
{
    /// <summary>An imported video. We store a reference to the original file only.</summary>
    public class WallpaperItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; set; } = "";
        public DateTime AddedAt { get; set; } = DateTime.Now;

        [XmlIgnore, NonSerialized] public bool IsMissing;
        [XmlIgnore, NonSerialized] public bool HasThumb;
    }

    /// <summary>In-memory library + library.xml persistence. References files only.</summary>
    public class LibraryStore
    {
        static readonly XmlSerializer Ser = new XmlSerializer(typeof(List<WallpaperItem>));

        public ObservableCollection<WallpaperItem> Items { get; } = new ObservableCollection<WallpaperItem>();

        public event Action ItemsChanged;

        public void Load()
        {
            Items.Clear();
            try
            {
                var path = Common.Paths.LibraryFile;
                if (File.Exists(path))
                {
                    List<WallpaperItem> list;
                    using (var fs = File.OpenRead(path))
                        list = (List<WallpaperItem>)Ser.Deserialize(fs);

                    foreach (var item in list)
                        Items.Add(item);
                }
            }
            catch (Exception e)
            {
                Common.Logger.Warn("library.xml unreadable — starting with empty library. " + e.Message);
            }
            RefreshMissing();
        }

        public void Save()
        {
            try
            {
                var path = Common.Paths.LibraryFile;
                var tmp = path + ".tmp";
                using (var fs = File.Create(tmp))
                    Ser.Serialize(fs, Items.ToList());
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Common.Logger.Warn("library.xml save failed. " + e.Message);
            }
        }

        /// <summary>Adds files that are not already in the library. Returns newly added items.</summary>
        public List<WallpaperItem> AddFiles(IEnumerable<string> paths)
        {
            var added = new List<WallpaperItem>();
            var existing = new HashSet<string>(
                Items.Select(i => i.FilePath.GetFullPathSafe()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    var full = Path.GetFullPath(p);
                    if (!File.Exists(full))
                    {
                        Common.Logger.Warn("Import skipped, file not found: " + full);
                        continue;
                    }
                    if (existing.Contains(full)) continue;

                    var ext = System.IO.Path.GetExtension(full) ?? "";
                    if (!IsSupportedExtension(ext))
                    {
                        Common.Logger.Info("Import rejected, unsupported extension '" + ext + "': " + full);
                        continue;
                    }

                    existing.Add(full);
                    var item = new WallpaperItem { FilePath = full };
                    Items.Add(item);
                    added.Add(item);
                    Common.Logger.Info("Imported: " + full);
                }
                catch (Exception e)
                {
                    Common.Logger.Error(new Exception("Import failed for '" + p + "'", e));
                }
            }

            if (added.Count > 0)
            {
                Save();
                RaiseItemsChanged();
            }
            return added;
        }

        public static bool IsSupportedExtension(string ext)
        {
            ext = (ext ?? "").TrimStart('.').ToLowerInvariant();
            return ext == "mp4" || ext == "mov";
        }

        /// <summary>Removes the reference only — never touches the original video file.</summary>
        public bool Remove(WallpaperItem item)
        {
            if (item == null) return false;
            Items.Remove(item);
            Save();
            RaiseItemsChanged();
            Common.Logger.Info("Removed library entry (original file untouched): " + item.FilePath);
            return true;
        }

        public WallpaperItem Find(string id)
        {
            return Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        }

        public void RefreshMissing()
        {
            foreach (var item in Items)
            {
                bool missing;
                try { missing = !File.Exists(item.FilePath); }
                catch { missing = true; }
                item.IsMissing = missing;
            }
        }

        void RaiseItemsChanged()
        {
            var h = ItemsChanged;
            if (h != null) h();
        }
    }

    internal static class PathExt
    {
        public static string GetFullPathSafe(this string s)
        {
            try { return System.IO.Path.GetFullPath(s); }
            catch { return s ?? ""; }
        }
    }
}
