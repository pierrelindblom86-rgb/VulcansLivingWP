using System;
using System.Collections.Generic;
using System.Text;
using FluxWall.Common;
using FluxWall.Library;
using FluxWall.Settings;

namespace FluxWall.Engine
{
    /// <summary>One video surface the engine has to create.</summary>
    public sealed class SurfaceSpec
    {
        public WallpaperItem Item;
        public MonitorInfoLite Monitor;   // synthetic descriptor for a span surface
        public bool IsSpan;
        /// <summary>Screen-space rectangle in physical pixels (virtual desktop coords).</summary>
        public Native.RECT Rect;
        public string Key = "";

        public string Describe()
        {
            return Key + " (" + (Rect.Right - Rect.Left) + "x" + (Rect.Bottom - Rect.Top) +
                   " at " + Rect.Left + "," + Rect.Top + ")";
        }
    }

    /// <summary>
    /// The set of surfaces that should be on screen for the current settings.
    ///
    /// DisplayMode.SameAll     → one surface per selected monitor, same video (1.2.x)
    /// DisplayMode.Independent → one surface per monitor, its own video
    /// DisplayMode.Span        → ONE surface covering the union of the selected
    ///                           monitors, so the video is continuous across seams
    ///
    /// Building a plan is pure: it reads settings, the library and the monitor
    /// list, and returns what should be rendered. That makes display changes and
    /// mode switches a matter of rebuilding and comparing, instead of special
    /// cases scattered through the engine.
    /// </summary>
    public sealed class WallpaperPlan
    {
        public List<SurfaceSpec> Surfaces = new List<SurfaceSpec>();
        public DisplayMode Mode = DisplayMode.SameAll;
        public WallpaperItem PrimaryItem;
        public string Signature = "";
        /// <summary>Human-readable note when something had to be approximated.</summary>
        public string Note = "";

        public bool IsEmpty { get { return Surfaces.Count == 0; } }

        public static WallpaperPlan Build(AppSettings settings, LibraryStore library,
                                          List<MonitorInfoLite> monitors)
        {
            var plan = new WallpaperPlan();
            if (settings == null || library == null || monitors == null || monitors.Count == 0)
                return plan;

            plan.Mode = settings.DisplayMode;
            switch (settings.DisplayMode)
            {
                case DisplayMode.Independent: BuildIndependent(plan, settings, library, monitors); break;
                case DisplayMode.Span: BuildSpan(plan, settings, library, monitors); break;
                default: BuildSameAll(plan, settings, library, monitors); break;
            }

            plan.Signature = ComputeSignature(plan);
            plan.PrimaryItem = PickPrimary(plan, monitors);
            return plan;
        }

        static void BuildSameAll(WallpaperPlan plan, AppSettings settings, LibraryStore library,
                                 List<MonitorInfoLite> monitors)
        {
            var item = library.Find(settings.ActiveWallpaperId);
            if (item == null || item.IsMissing)
            {
                if (!string.IsNullOrEmpty(settings.ActiveWallpaperId))
                    plan.Note = "The wallpaper saved for this display is missing from the library.";
                return;
            }

            foreach (var m in MonitorService.Resolve(settings, monitors))
            {
                plan.Surfaces.Add(new SurfaceSpec
                {
                    Item = item,
                    Monitor = m,
                    IsSpan = false,
                    Rect = m.ScreenRect,
                    Key = m.Key.Length > 0 ? m.Key : m.DeviceName,
                });
            }
        }

        static void BuildIndependent(WallpaperPlan plan, AppSettings settings, LibraryStore library,
                                     List<MonitorInfoLite> monitors)
        {
            // Every connected monitor participates. A monitor with no assignment
            // falls back to the last wallpaper that was applied globally, which is
            // predictable: it shows the same video the user last picked.
            var fallback = library.Find(settings.ActiveWallpaperId);
            if (fallback != null && fallback.IsMissing) fallback = null;

            foreach (var m in monitors)
            {
                var key = m.Key.Length > 0 ? m.Key : m.DeviceName;
                var item = library.Find(settings.AssignmentFor(key));
                if (item == null || item.IsMissing)
                {
                    item = fallback;
                    if (item != null) plan.Note = "Unassigned monitors use the last wallpaper that was applied.";
                }
                if (item == null) continue;

                plan.Surfaces.Add(new SurfaceSpec
                {
                    Item = item,
                    Monitor = m,
                    IsSpan = false,
                    Rect = m.ScreenRect,
                    Key = key,
                });
            }
        }

        static void BuildSpan(WallpaperPlan plan, AppSettings settings, LibraryStore library,
                              List<MonitorInfoLite> monitors)
        {
            var participants = SelectSpanMonitors(settings, monitors);
            if (participants.Count == 0) return;

            var item = library.Find(settings.SpanWallpaperId);
            if (item == null || item.IsMissing) item = library.Find(settings.ActiveWallpaperId);
            if (item == null || item.IsMissing)
            {
                plan.Note = "No video is assigned to span mode yet.";
                return;
            }

            var union = MonitorService.Union(participants);
            var spanMonitor = new MonitorInfoLite
            {
                Handle = IntPtr.Zero,
                ScreenRect = union,
                IsPrimary = true,
                DeviceName = "Span",
                Key = "span",
                FriendlyName = "Span surface",
            };

            plan.Surfaces.Add(new SurfaceSpec
            {
                Item = item,
                Monitor = spanMonitor,
                IsSpan = true,
                Rect = union,
                Key = "span",
            });
            plan.Note = "Spanning " + participants.Count + " monitor(s): " +
                        (union.Right - union.Left) + "x" + (union.Bottom - union.Top) + ".";
        }

        /// <summary>
        /// Monitors taking part in span mode: the saved keys that are still
        /// present, or every connected monitor when nothing specific was saved.
        /// </summary>
        public static List<MonitorInfoLite> SelectSpanMonitors(AppSettings settings, List<MonitorInfoLite> monitors)
        {
            var result = new List<MonitorInfoLite>();
            if (monitors == null || monitors.Count == 0) return result;
            if (settings == null || settings.SpanMonitors == null || settings.SpanMonitors.Count == 0)
            {
                result.AddRange(monitors);
                return result;
            }

            // A saved monitor that is currently unplugged must not break the
            // others: keep the ones that are here and fall back to all monitors
            // only when none of the saved keys match (for example after a driver
            // reinstall changed the device path).
            foreach (var m in monitors)
            {
                var key = m.Key.Length > 0 ? m.Key : m.DeviceName;
                foreach (var saved in settings.SpanMonitors)
                    if (string.Equals(saved, key, StringComparison.OrdinalIgnoreCase)) { result.Add(m); break; }
            }
            if (result.Count == 0)
            {
                result.AddRange(monitors);
                if (settings != null)
                {
                    // Remember the fallback so the UI shows what is really used.
                    Logger.Info("Span: none of the saved monitors are connected; spanning every monitor instead.");
                }
            }
            return result;
        }

        static string ComputeSignature(WallpaperPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append(plan.Mode).Append('#');
            foreach (var s in plan.Surfaces)
            {
                sb.Append(s.Key).Append('=')
                  .Append(s.Item != null ? s.Item.Id : "").Append('@')
                  .Append(s.Rect.Left).Append(',').Append(s.Rect.Top).Append(',')
                  .Append(s.Rect.Right).Append(',').Append(s.Rect.Bottom)
                  .Append('|');
            }
            return sb.ToString();
        }

        static WallpaperItem PickPrimary(WallpaperPlan plan, List<MonitorInfoLite> monitors)
        {
            if (plan.Surfaces.Count == 0) return null;
            if (plan.Mode == DisplayMode.Independent)
            {
                var primary = MonitorService.Primary(monitors);
                foreach (var s in plan.Surfaces)
                    if (s.Monitor != null && s.Monitor.IsPrimary) return s.Item;
                if (primary != null)
                    foreach (var s in plan.Surfaces)
                        if (ReferenceEquals(s.Monitor, primary)) return s.Item;
            }
            return plan.Surfaces[0].Item;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Mode).Append(": ");
            foreach (var s in Surfaces)
                sb.Append('[').Append(s.Describe()).Append(" -> ")
                  .Append(s.Item != null ? System.IO.Path.GetFileName(s.Item.FilePath) : "none")
                  .Append("] ");
            return sb.ToString().TrimEnd();
        }
    }
}
