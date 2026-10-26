using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using FluxWall.Common;

namespace FluxWall.Library
{
    #region Shell thumbnail interop (IShellItemImageFactory)
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8dc59b30d163")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(int cx, int cy, uint flags, out IntPtr phbitmap);
    }

    static class ShellThumbNative
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        public const uint SIIGBF_RESIZETOFIT = 0;
        public const uint SIIGBF_THUMBNAILONLY = 8;
    }
    #endregion

    /// <summary>
    /// Generates one small poster frame per wallpaper item.
    ///
    /// Primary path: <see cref="FrameGrabber"/> pulls a frame straight from the
    /// Windows media decoder. That is fully offscreen — earlier versions showed a
    /// temporary window and screen-captured it, which flashed a second video
    /// window while importing. Secondary path: the Windows shell thumbnail
    /// provider, for files the shell can preview but the decoder cannot open.
    /// Single background worker plus a small PNG disk cache, so relaunches cost
    /// zero decode work.
    /// </summary>
    public class ThumbnailService
    {
        const int ThumbW = 480, ThumbH = 270;

        readonly LibraryStore _library;
        readonly BlockingCollection<WallpaperItem> _queue =
            new BlockingCollection<WallpaperItem>(new ConcurrentQueue<WallpaperItem>());
        readonly Thread _worker;

        // A card that has no poster asks for one every time it is rebuilt, and
        // a video the decoder cannot open fails every single time. Remembering
        // what was already tried (and the file stamp it was tried at) turns that
        // into one attempt per file instead of a decode loop on every refresh.
        readonly object _gate = new object();
        readonly Dictionary<string, long> _attempted = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly HashSet<string> _queued = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Raised (on the worker thread) after a thumbnail becomes available.</summary>
        public event Action<WallpaperItem> ThumbnailReady;

        public string ThumbPath(WallpaperItem item)
        {
            if (item == null) return null;
            return Path.Combine(Paths.ThumbsDir, item.Id + ".png");
        }

        public ThumbnailService(LibraryStore library)
        {
            _library = library;
            _worker = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = "VulcansLivingWP-Thumbs",
                Priority = ThreadPriority.BelowNormal,
            };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        public void RequestAll()
        {
            foreach (var item in _library.Items)
                Request(item);
        }

        public void Request(WallpaperItem item)
        {
            if (item == null || item.IsMissing) return;
            try
            {
                if (File.Exists(ThumbPath(item))) { item.HasThumb = true; return; }

                var stamp = FileStamp(item);
                lock (_gate)
                {
                    if (_queued.Contains(item.Id)) return;
                    long tried;
                    if (_attempted.TryGetValue(item.Id, out tried) && tried == stamp) return;
                    _queued.Add(item.Id);
                }

                if (_queue.IsAddingCompleted)
                {
                    lock (_gate) _queued.Remove(item.Id);
                    return;
                }
                _queue.Add(item);
            }
            catch
            {
                lock (_gate) _queued.Remove(item.Id);
            }
        }

        /// <summary>Last write time, so a re-encoded file at the same path is retried.</summary>
        static long FileStamp(WallpaperItem item)
        {
            try { return File.GetLastWriteTimeUtc(item.FilePath).Ticks; }
            catch { return 0; }
        }

        void WorkLoop()
        {
            PruneOrphans();
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { Process(item); }
                catch (Exception e) { Logger.Error(e); }
                finally
                {
                    lock (_gate)
                    {
                        _queued.Remove(item.Id);
                        _attempted[item.Id] = FileStamp(item);
                    }
                }
            }
        }

        /// <summary>
        /// Drops cached posters whose video is no longer in the library. They are
        /// never shown again and used to accumulate silently (~200 KB each) with
        /// no way for the user to notice.
        /// </summary>
        void PruneOrphans()
        {
            try
            {
                if (!Directory.Exists(Paths.ThumbsDir)) return;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in _library.Items)
                    if (item != null && !string.IsNullOrEmpty(item.Id)) keep.Add(item.Id + ".png");

                int removed = 0;
                foreach (var path in Directory.GetFiles(Paths.ThumbsDir, "*.png"))
                {
                    if (keep.Contains(Path.GetFileName(path))) continue;
                    try { File.Delete(path); removed++; } catch { }
                }
                if (removed > 0)
                    Logger.Info("Removed " + removed + " cached thumbnail(s) for videos no longer in the library.");
            }
            catch (Exception e)
            {
                Logger.Warn("Cached thumbnail cleanup failed: " + e.Message);
            }
        }

        void Process(WallpaperItem item)
        {
            if (item.IsMissing) return;
            var png = ThumbPath(item);
            Paths.Ensure();

            if (TryDecode(item, png) || TryShell(item, png))
            {
                item.HasThumb = true;
                var h = ThumbnailReady;
                if (h != null) h(item);
            }
            else
            {
                Logger.Info("No thumbnail could be produced for '" + item.FilePath + "'.");
            }
        }

        #region Primary: decode a frame with Media Foundation (no window)
        static bool TryDecode(WallpaperItem item, string png)
        {
            try
            {
                using (var frame = FrameGrabber.ExtractPoster(item.FilePath, ThumbW, ThumbH))
                {
                    if (frame == null) return false;
                    var target = Path.GetDirectoryName(png);
                    if (!string.IsNullOrEmpty(target) && !Directory.Exists(target))
                        Directory.CreateDirectory(target);
                    frame.Save(png, ImageFormat.Png);
                }
                return IsUsable(png);
            }
            catch (Exception e)
            {
                Logger.Info("Frame decode failed for '" + item.FilePath + "': " + e.Message);
                return false;
            }
        }
        #endregion

        #region Fallback: Windows shell thumbnail provider
        bool TryShell(WallpaperItem item, string png)
        {
            IntPtr frame = IntPtr.Zero;
            try
            {
                var guid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                ShellThumbNative.SHCreateItemFromParsingName(item.FilePath, IntPtr.Zero, ref guid, out factory);
                try
                {
                    frame = AskFrame(factory);
                    if (frame != IntPtr.Zero)
                    {
                        SaveFrame(frame, png);
                        return IsUsable(png);
                    }
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch (Exception e)
            {
                Logger.Info("Shell thumbnail failed for '" + item.FilePath + "': " + e.Message);
                if (frame != IntPtr.Zero) { try { ShellThumbNative.DeleteObject(frame); } catch { } }
                try { if (File.Exists(png)) File.Delete(png); } catch { }
            }
            return false;
        }

        static IntPtr AskFrame(IShellItemImageFactory factory)
        {
            var hbmp = IntPtr.Zero;
            if (factory.GetImage(ThumbW, ThumbH, ShellThumbNative.SIIGBF_THUMBNAILONLY, out hbmp) == 0 && hbmp != IntPtr.Zero)
                return hbmp;
            if (factory.GetImage(ThumbW, ThumbH, ShellThumbNative.SIIGBF_RESIZETOFIT, out hbmp) == 0 && hbmp != IntPtr.Zero)
                return hbmp;
            return IntPtr.Zero;
        }

        static void SaveFrame(IntPtr hBitmap, string png)
        {
            // System.Drawing's Image.FromHbitmap rejects the DIBs-with-alpha the
            // shell returns for video files, so decode through WPF interop which
            // handles both DDB and DIB sections.
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(source));
                using (var fs = File.Create(png))
                    enc.Save(fs);
            }
            finally
            {
                ShellThumbNative.DeleteObject(hBitmap);
            }
        }
        #endregion

        /// <summary>
        /// A poster that is essentially uniform black usually means the decoder
        /// produced nothing usable; those are worth another attempt later.
        /// </summary>
        static bool IsUsable(string png)
        {
            try
            {
                if (!File.Exists(png)) return false;
                return new FileInfo(png).Length > 1600;
            }
            catch { return false; }
        }
    }
}
