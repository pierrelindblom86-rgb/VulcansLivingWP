using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FluxWall.Common;
using FluxWall.Library;
using FluxWall.Settings;

namespace FluxWall.Engine
{
    /// <summary>
    /// Verification for the 1.3.0 display modes, run with
    /// <c>VulcansLivingWP.exe --layout-test=&lt;folder&gt;</c>.
    ///
    /// Two halves:
    ///   • plan tests — pure geometry with synthetic monitors (negative
    ///     coordinates, vertical stacks, mixed sizes, subset spans), plus the
    ///     1.2.3 settings migration.
    ///   • live tests — the real engine, the real desktop layer and real videos
    ///     on the monitors that are actually connected. They prove that each
    ///     monitor can play its own decoder, that span mode creates exactly one
    ///     surface covering the union of the monitors, and that switching modes
    ///     never drops the previous wallpaper first.
    ///
    /// Exit code 0 = every executable check passed, 1 = at least one failed,
    /// 2 = crashed. Multi-monitor checks that need two screens are skipped and
    /// reported as such when only one is attached.
    /// </summary>
    public static class LayoutSelfTest
    {
        static readonly List<string> Report = new List<string>();
        static int _passed;
        static int _failed;
        static int _skipped;
        static string _dir;
        static string _settingsPath;
        static Application _application;

        public static int Run(string outputDir)
        {
            try
            {
                return RunCore(outputDir);
            }
            catch (Exception e)
            {
                Console.WriteLine("LAYOUT SELF-TEST CRASHED: " + e);
                Report.Add("LAYOUT SELF-TEST CRASHED: " + e);
                return 2;
            }
            finally
            {
                SettingsStore.PathOverride = null;
                try { _application?.Shutdown(); } catch { }
            }
        }

        static int RunCore(string outputDir)
        {
            _dir = string.IsNullOrEmpty(outputDir)
                ? Path.Combine(Path.GetTempPath(), "vlwp-layout-test")
                : Path.GetFullPath(outputDir);
            Directory.CreateDirectory(_dir);
            _settingsPath = Path.Combine(_dir, "settings-layout-test.xml");
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);

            Heading("VulcansLivingWP — multi-monitor layout self-test");
            Note("Time:     " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Note("Output:   " + _dir);
            Note("Version:  " + AppVersion.Text);

            var monitors = MonitorService.All();
            Note("Monitors: " + monitors.Count);
            foreach (var m in monitors)
                Note("  " + m.Label + "  rect=" + Rect(m.ScreenRect) + "  key=" + m.Key);

            // The test never touches the live settings file.
            SettingsStore.PathOverride = _settingsPath;
            SettingsStore.Load();

            _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            GeometryTests(monitors);
            PlanTests(monitors);
            MigrationTests();
            LiveEngineTests(monitors);

            Heading("Result");
            Note("Passed:  " + _passed);
            Note("Failed:  " + _failed);
            Note("Skipped: " + _skipped);
            if (_failed == 0) Note("ALL EXECUTABLE CHECKS PASSED");
            if (_skipped > 0) Note("Skipped checks are listed above with the reason; they are NOT passes.");

            try
            {
                File.WriteAllText(Path.Combine(_dir, "layout-test-report.txt"),
                    string.Join(Environment.NewLine, Report.ToArray()) + Environment.NewLine);
            }
            catch { }

            return _failed == 0 ? 0 : 1;
        }

        #region Display geometry

        /// <summary>
        /// The wallpaper layer is a physical-pixel surface, so MonitorService must
        /// report the display mode's rectangle, not the DPI-virtualized one that
        /// GetMonitorInfo returns to a system-DPI-aware process. A 125%-scaled
        /// screen used to be reported 20% small here, which left the outer fifth
        /// of the screen uncovered and, in portrait, cropped the video to a column.
        /// </summary>
        static void GeometryTests(List<MonitorInfoLite> monitors)
        {
            Heading("Display geometry (physical pixels)");
            Check("At least one monitor is enumerated", monitors.Count > 0, "got " + monitors.Count);

            int scaled = 0;
            foreach (var m in monitors)
            {
                var dm = new Native.DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
                bool known = Native.EnumDisplaySettingsEx(m.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref dm, 0);
                var mode = known
                    ? new Native.RECT
                    {
                        Left = dm.dmPositionX,
                        Top = dm.dmPositionY,
                        Right = dm.dmPositionX + dm.dmPelsWidth,
                        Bottom = dm.dmPositionY + dm.dmPelsHeight,
                    }
                    : m.ScreenRect;

                Check("Monitor rectangle matches the physical display mode: " + m.DeviceName,
                      known && RectExt.RectsEqual(m.ScreenRect, mode),
                      "reported " + Rect(m.ScreenRect) + " vs mode " + Rect(mode));

                // A screen that is taller than it is wide must produce a surface
                // that is taller than it is wide; orientation is carried by the
                // rectangle, not removed by a stale width/height pair.
                if (m.Height > m.Width) scaled++;
            }

            if (scaled > 0)
                Note(scaled + " portrait monitor(s) present; their rectangles are checked above.");
        }

        #endregion

        #region Plan tests

        static void PlanTests(List<MonitorInfoLite> real)
        {
            Heading("Plan geometry (synthetic displays)");

            var lib = new LibraryStore();
            var a = Item(lib, "C:\\videos\\a.mp4");
            var b = Item(lib, "C:\\videos\\b.mp4");

            var settings = new AppSettings
            {
                ActiveWallpaperId = a.Id,
                TargetMonitors = MonitorChoice.All,
            };

            // Three screens: middle is primary, one is left of the origin and
            // wider than it is tall, one sits lower with a different height.
            var left = Mon(-1280, 200, 0, 1224, "\\\\.\\DISPLAY3", "id:left");
            var main = Mon(0, 0, 1920, 1080, "\\\\.\\DISPLAY1", "id:main", primary: true);
            var right = Mon(1920, 0, 3840, 1080, "\\\\.\\DISPLAY2", "id:right");
            var all = new List<MonitorInfoLite> { left, main, right };

            // --- Same wallpaper everywhere (1.2.x behaviour) ---
            settings.DisplayMode = DisplayMode.SameAll;
            var sameAll = WallpaperPlan.Build(settings, lib, all);
            Check("SameAll creates one surface per selected monitor", sameAll.Surfaces.Count == 3,
                  "got " + sameAll.Surfaces.Count);
            Check("SameAll surfaces all play the same video",
                  sameAll.Surfaces.All(s => s.Item != null && s.Item.Id == a.Id));
            Check("SameAll surfaces use the real monitor rectangles",
                  sameAll.Surfaces.All(s => all.Any(m => RectExt.RectsEqual(m.ScreenRect, s.Rect))));

            // --- Independent ---
            settings.DisplayMode = DisplayMode.Independent;
            settings.Assign("id:left", b.Id);
            settings.Assign("id:main", a.Id);
            // right stays unassigned on purpose

            var ind = WallpaperPlan.Build(settings, lib, all);
            Check("Independent creates one surface per monitor", ind.Surfaces.Count == 3,
                  "got " + ind.Surfaces.Count);
            Check("Left monitor plays its own video", ItemAt(ind, "id:left") == b.Id);
            Check("Main monitor plays a different video", ItemAt(ind, "id:main") == a.Id);
            Check("Unassigned monitor falls back to the shared wallpaper", ItemAt(ind, "id:right") == a.Id);

            var shuffled = new List<MonitorInfoLite> { right, left, main };
            var indShuffled = WallpaperPlan.Build(settings, lib, shuffled);
            Check("Enumeration order does not move assignments between monitors",
                  ItemAt(indShuffled, "id:left") == b.Id && ItemAt(indShuffled, "id:main") == a.Id &&
                  ItemAt(indShuffled, "id:right") == a.Id);

            // A saved assignment for a monitor that is not connected must be
            // kept for later, and must not land on a different screen.
            settings.Assign("id:ghost", b.Id);
            var withGhost = WallpaperPlan.Build(settings, lib, all);
            Check("Disconnected monitor's assignment does not create a surface",
                  withGhost.Surfaces.Count == 3, "got " + withGhost.Surfaces.Count);
            Check("Disconnected monitor's assignment is preserved in settings",
                  settings.AssignmentFor("id:ghost") == b.Id);

            // --- Span ---
            settings.DisplayMode = DisplayMode.Span;
            settings.SpanWallpaperId = b.Id;
            settings.SpanMonitors = new List<string>();   // all monitors

            var span = WallpaperPlan.Build(settings, lib, all);
            Check("Span creates exactly one surface", span.Surfaces.Count == 1,
                  "got " + span.Surfaces.Count);
            Check("Span surface is marked as a span", span.Surfaces.Count == 1 && span.Surfaces[0].IsSpan);
            var expected = new Native.RECT { Left = -1280, Top = 0, Right = 3840, Bottom = 1224 };
            Check("Span rectangle is the union of the virtual desktop, including negative coordinates",
                  span.Surfaces.Count == 1 && RectExt.RectsEqual(span.Surfaces[0].Rect, expected),
                  span.Surfaces.Count == 1 ? Rect(span.Surfaces[0].Rect) : "no surface");
            Check("Span surface plays the span video", span.Surfaces.Count == 1 && span.Surfaces[0].Item.Id == b.Id);

            settings.SpanMonitors = new List<string> { "id:main", "id:right" };
            var subset = WallpaperPlan.Build(settings, lib, all);
            var expectedSubset = new Native.RECT { Left = 0, Top = 0, Right = 3840, Bottom = 1080 };
            Check("Span of a subset covers only those monitors",
                  subset.Surfaces.Count == 1 && RectExt.RectsEqual(subset.Surfaces[0].Rect, expectedSubset),
                  subset.Surfaces.Count == 1 ? Rect(subset.Surfaces[0].Rect) : "no surface");

            // Vertical arrangement: one screen above the other.
            var top = Mon(0, -1080, 1920, 0, "\\\\.\\DISPLAY4", "id:top");
            var bottom = Mon(0, 0, 1920, 1080, "\\\\.\\DISPLAY1", "id:main", primary: true);
            settings.SpanMonitors = new List<string>();
            var vertical = WallpaperPlan.Build(settings, lib, new List<MonitorInfoLite> { top, bottom });
            var expectedVertical = new Native.RECT { Left = 0, Top = -1080, Right = 1920, Bottom = 1080 };
            Check("Span handles monitors stacked vertically",
                  vertical.Surfaces.Count == 1 && RectExt.RectsEqual(vertical.Surfaces[0].Rect, expectedVertical),
                  vertical.Surfaces.Count == 1 ? Rect(vertical.Surfaces[0].Rect) : "no surface");

            // --- Mode switching keeps assignments ---
            settings.DisplayMode = DisplayMode.SameAll;
            var back = WallpaperPlan.Build(settings, lib, all);
            settings.DisplayMode = DisplayMode.Independent;
            var again = WallpaperPlan.Build(settings, lib, all);
            Check("Span round trip keeps the independent assignments",
                  ItemAt(again, "id:left") == b.Id && ItemAt(again, "id:main") == a.Id);

            // --- Signature stability ---
            var sig1 = WallpaperPlan.Build(settings, lib, all).Signature;
            var sig2 = WallpaperPlan.Build(settings, lib, all).Signature;
            settings.Assign("id:right", b.Id);
            var sig3 = WallpaperPlan.Build(settings, lib, all).Signature;
            Check("The same layout has a stable signature", sig1 == sig2);
            Check("A changed assignment changes the signature", sig1 != sig3);

            // --- Failure handling ---
            var missing = new AppSettings { DisplayMode = DisplayMode.SameAll, ActiveWallpaperId = "no-such-id" };
            var empty = WallpaperPlan.Build(missing, lib, all);
            Check("A missing wallpaper produces an empty plan instead of throwing", empty.IsEmpty);

            var noMonitors = WallpaperPlan.Build(settings, lib, new List<MonitorInfoLite>());
            Check("Zero monitors produces an empty plan", noMonitors.IsEmpty);

            if (real.Count >= 2)
            {
                Heading("Plan geometry (this machine's displays)");
                var live = new AppSettings
                {
                    DisplayMode = DisplayMode.Span,
                    SpanWallpaperId = b.Id,
                    SpanMonitors = new List<string>(),
                };
                var livePlan = WallpaperPlan.Build(live, lib, real);
                var union = MonitorService.Union(real);
                Check("Span covers the real desktop union",
                      livePlan.Surfaces.Count == 1 && RectExt.RectsEqual(livePlan.Surfaces[0].Rect, union),
                      livePlan.Surfaces.Count == 1 ? Rect(livePlan.Surfaces[0].Rect) : "no surface");
                Note("Real union: " + Rect(union));
            }
        }

        #endregion

        #region Migration

        static void MigrationTests()
        {
            Heading("Upgrade from 1.2.3 settings");

            var legacyPath = Path.Combine(_dir, "legacy-1.2.3-settings.xml");
            const string legacy = @"<?xml version=""1.0"" encoding=""utf-8""?>
<VulcansLivingWP-Settings xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <StartWithWindows>true</StartWithWindows>
  <StartMinimized>true</StartMinimized>
  <AutoPauseFullscreen>true</AutoPauseFullscreen>
  <PauseOnBattery>false</PauseOnBattery>
  <RestoreLastWallpaper>true</RestoreLastWallpaper>
  <ShowTrayIcon>true</ShowTrayIcon>
  <FirstRunDone>true</FirstRunDone>
  <TargetMonitors>All</TargetMonitors>
  <SelectedMonitorDevice />
  <Scaling>Fill</Scaling>
  <PauseHotkey>Ctrl+Alt+W</PauseHotkey>
  <PlayWallpaperAudio>false</PlayWallpaperAudio>
  <ActiveWallpaperId>legacy-wallpaper-id</ActiveWallpaperId>
  <AutoSwitchWallpapers>false</AutoSwitchWallpapers>
  <AutoSwitchMinutes>10</AutoSwitchMinutes>
  <VisualizerEnabled>false</VisualizerEnabled>
</VulcansLivingWP-Settings>";
            File.WriteAllText(legacyPath, legacy);

            var previous = SettingsStore.PathOverride;
            SettingsStore.PathOverride = legacyPath;
            try
            {
                SettingsStore.Load();
                var s = SettingsStore.Current;

                Check("Legacy settings load without an exception", s != null);
                Check("Upgraded users stay in SameAll mode (1.2.x behaviour)",
                      s.DisplayMode == DisplayMode.SameAll, "got " + s.DisplayMode);
                Check("Schema marker is upgraded to 1", s.SettingsSchema == 1, "got " + s.SettingsSchema);
                Check("No fake per-monitor assignments are invented",
                      s.MonitorAssignments.Count == 0, "got " + s.MonitorAssignments.Count);
                Check("Span state starts empty", s.SpanMonitors.Count == 0 && s.SpanWallpaperId == "");
                Check("The existing wallpaper is preserved",
                      s.ActiveWallpaperId == "legacy-wallpaper-id", "got " + s.ActiveWallpaperId);
                Check("Other user choices are preserved",
                      s.StartWithWindows && s.StartMinimized && s.AutoSwitchMinutes == 10);
                Check("New update fields have safe defaults",
                      s.CheckForUpdates && s.LatestKnownVersion == "" && s.DismissedUpdateVersion == "");
            }
            finally
            {
                SettingsStore.PathOverride = previous;
                SettingsStore.Load();
            }
        }

        #endregion

        #region Live engine

        static void LiveEngineTests(List<MonitorInfoLite> monitors)
        {
            Heading("Live engine");

            if (monitors.Count < 2)
            {
                Skip("Independent mode on two monitors", "only " + monitors.Count + " monitor connected");
                Skip("Span across monitors", "only " + monitors.Count + " monitor connected");
                return;
            }

            var videos = PickVideos(monitors.Count);
            if (videos.Count < 2)
            {
                Skip("Independent mode on two monitors", "fewer than two playable videos available");
                Skip("Span across monitors", "fewer than two playable videos available");
                return;
            }
            foreach (var v in videos) Note("Video: " + v.FilePath);

            var lib = new LibraryStore();
            var items = new List<WallpaperItem>();
            foreach (var v in videos) items.Add(Item(lib, v.FilePath));

            var settings = SettingsStore.Current;
            settings.DisplayMode = DisplayMode.Independent;
            settings.TargetMonitors = MonitorChoice.All;
            settings.ActiveWallpaperId = items[0].Id;

            var left = monitors[0];
            var right = monitors[1];
            settings.Assign(Key(left), items[0].Id);
            settings.Assign(Key(right), items[1].Id);

            var engine = new WallpaperEngine(lib, settings);
            engine.Start();
            try
            {
                engine.ApplyCurrentLayout();

                bool ready = WaitFor(
                    () => engine.WindowsSnapshot.Count == 2 && engine.WindowsSnapshot.All(w => w.HasFirstFrame),
                    TimeSpan.FromSeconds(20));
                Check("Independent: two surfaces appear and produce frames", ready,
                      Describe(engine));

                Pump(TimeSpan.FromSeconds(1.2));

                var wins = engine.WindowsSnapshot;
                Check("Independent: exactly two decoders, one per monitor", wins.Count == 2, "got " + wins.Count);
                Check("Independent: no span surface exists", wins.All(w => !w.IsSpan));

                var byRect = new Dictionary<string, WallpaperWindow>();
                foreach (var w in wins) byRect[Rect(w.MonitorRect)] = w;

                Check("Independent: a surface sits exactly on each monitor",
                      byRect.ContainsKey(Rect(left.ScreenRect)) && byRect.ContainsKey(Rect(right.ScreenRect)),
                      string.Join(" | ", wins.Select(w => Rect(w.MonitorRect)).ToArray()));

                WallpaperWindow wl = null, wr = null;
                byRect.TryGetValue(Rect(left.ScreenRect), out wl);
                byRect.TryGetValue(Rect(right.ScreenRect), out wr);

                Check("Independent: each monitor plays the video assigned to it",
                      wl != null && wr != null &&
                      wl.Item.FilePath == items[0].FilePath && wr.Item.FilePath == items[1].FilePath,
                      (wl != null ? wl.Item.FilePath : "none") + " / " + (wr != null ? wr.Item.FilePath : "none"));

                CheckAll("Independent surfaces", wins);

                var pos1 = wins.Select(w => w.Position).ToArray();
                Pump(TimeSpan.FromSeconds(1.0));
                var pos2 = engine.WindowsSnapshot.Select(w => w.Position).ToArray();
                bool advanced = pos1.Length == pos2.Length && pos1.Length > 0 &&
                                pos1.Zip(pos2, (p, q) => q > p).All(x => x);
                Check("Independent: both videos are really playing (position advances)", advanced);

                // --- Switch to span, watching for a black gap ---
                settings.SpanWallpaperId = items[1].Id;
                settings.SpanMonitors = new List<string>();
                bool sawEmpty = false;
                engine.SetDisplayMode(DisplayMode.Span);

                var sw = Stopwatch.StartNew();
                bool spanReady = false;
                while (sw.Elapsed < TimeSpan.FromSeconds(20))
                {
                    var snap = engine.WindowsSnapshot;
                    if (snap.Count == 0) sawEmpty = true;
                    if (snap.Count == 1 && snap[0].IsSpan && snap[0].HasFirstFrame) { spanReady = true; break; }
                    Pump(TimeSpan.FromMilliseconds(100));
                }

                Check("Span: one surface is created for all monitors", spanReady, Describe(engine));
                Check("Span: the previous wallpaper is never dropped first (no black gap)", !sawEmpty);

                var spanWin = engine.WindowsSnapshot.FirstOrDefault();
                var union = MonitorService.Union(monitors);
                Check("Span: the surface covers the union of the monitors exactly",
                      spanWin != null && RectExt.RectsEqual(spanWin.MonitorRect, union),
                      spanWin != null ? Rect(spanWin.MonitorRect) + " vs " + Rect(union) : "no surface");
                Check("Span: the single surface plays the span video",
                      spanWin != null && spanWin.Item.Id == items[1].Id);
                Check("Span: only one decoder exists (no per-monitor duplicate)",
                      engine.WindowsSnapshot.Count == 1, "got " + engine.WindowsSnapshot.Count);

                if (spanWin != null)
                {
                    var sp1 = spanWin.Position;
                    Pump(TimeSpan.FromSeconds(1.0));
                    var spanNow = engine.WindowsSnapshot.FirstOrDefault();
                    Check("Span: playback advances", spanNow != null && spanNow.Position > sp1);
                }

                // --- Back to independent: assignments restored ---
                engine.SetDisplayMode(DisplayMode.Independent);
                bool restored = WaitFor(
                    () => engine.WindowsSnapshot.Count == 2 && engine.WindowsSnapshot.All(w => w.HasFirstFrame),
                    TimeSpan.FromSeconds(20));
                Check("Switching back restores the per-monitor wallpapers", restored, Describe(engine));

                var backWins = engine.WindowsSnapshot;
                WallpaperWindow bl = null, br = null;
                foreach (var w in backWins)
                {
                    if (RectExt.RectsEqual(w.MonitorRect, left.ScreenRect)) bl = w;
                    if (RectExt.RectsEqual(w.MonitorRect, right.ScreenRect)) br = w;
                }
                Check("Restored assignments point at the original videos",
                      bl != null && br != null &&
                      bl.Item.FilePath == items[0].FilePath && br.Item.FilePath == items[1].FilePath,
                      (bl != null ? bl.Item.FilePath : "none") + " / " + (br != null ? br.Item.FilePath : "none"));

                // --- Same wallpaper on all monitors ---
                engine.SetDisplayMode(DisplayMode.SameAll);
                var expectedSurfaces = MonitorService.Resolve(settings, MonitorService.All()).Count;
                // The previous layout had the same number of surfaces, so wait for
                // the shared video itself, not just "two windows with frames".
                var sharedId = settings.ActiveWallpaperId;
                bool sameReady = WaitFor(
                    () => engine.WindowsSnapshot.Count == expectedSurfaces &&
                          engine.WindowsSnapshot.All(w => w.HasFirstFrame && w.Item.Id == sharedId),
                    TimeSpan.FromSeconds(20));
                Check("SameAll: every monitor shows the shared wallpaper again", sameReady, Describe(engine));
                Check("SameAll: all surfaces play the active wallpaper",
                      engine.WindowsSnapshot.Count > 0 &&
                      engine.WindowsSnapshot.All(w => w.Item.Id == sharedId),
                      "active=" + sharedId + " windows=" +
                      string.Join(",", engine.WindowsSnapshot.Select(w => w.Item.Id).ToArray()));

                // --- Persistence across a restart of the engine ---
                SettingsStore.Save();
                SettingsStore.Load();
                var reloaded = SettingsStore.Current;
                Check("Assignments survive a settings reload",
                      reloaded.AssignmentFor(Key(left)) == items[0].Id &&
                      reloaded.AssignmentFor(Key(right)) == items[1].Id);
                Check("The selected display mode survives a settings reload",
                      reloaded.DisplayMode == DisplayMode.SameAll, "got " + reloaded.DisplayMode);
            }
            finally
            {
                try { engine.Stop(); } catch (Exception e) { Logger.Error(e); }
                try { engine.Dispose(); } catch { }
                Pump(TimeSpan.FromSeconds(0.5));
                Check("Engine stop removes every surface", engine.WindowsSnapshot.Count == 0,
                      "got " + engine.WindowsSnapshot.Count);
            }
        }

        static void CheckAll(string label, IReadOnlyList<WallpaperWindow> wins)
        {
            bool ok = wins.Count > 0 && wins.All(w =>
                w.IsParented && w.Visible && !w.Failed && w.Visible);
            Check(label + " are attached to the desktop layer and visible", ok,
                  string.Join(",", wins.Select(w =>
                      (w.IsParented ? "parented" : "floating") + "/" + (w.Visible ? "visible" : "hidden") +
                      (w.Failed ? "/failed" : "")).ToArray()));
        }

        static List<WallpaperItem> PickVideos(int wanted)
        {
            var result = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var real = new LibraryStore();
                real.Load();
                foreach (var item in real.Items)
                {
                    if (item.IsMissing || !File.Exists(item.FilePath)) continue;
                    if (!seen.Add(item.FilePath)) continue;
                    result.Add(item);
                    if (result.Count >= Math.Min(wanted, 2)) break;
                }
            }
            catch (Exception e) { Logger.Warn("Could not read the library for test videos: " + e.Message); }

            // Always keep a fallback so the live test can run on a clean machine.
            var sample = Path.Combine(Path.GetDirectoryName(_dir) ?? _dir, "..", "test-videos", "sample.mp4");
            try { sample = Path.GetFullPath(sample); } catch { }
            if (File.Exists(sample))
            {
                int copy = 0;
                while (result.Count < 2 && copy < 2)
                {
                    var dest = Path.Combine(_dir, "layout-video-" + copy + ".mp4");
                    if (!File.Exists(dest)) File.Copy(sample, dest, true);
                    result.Add(new WallpaperItem { FilePath = dest });
                    copy++;
                }
            }
            return result;
        }

        #endregion

        #region Helpers

        static WallpaperItem Item(LibraryStore lib, string path)
        {
            var item = new WallpaperItem { FilePath = path };
            lib.Items.Add(item);
            return item;
        }

        static MonitorInfoLite Mon(int l, int t, int r, int b, string device, string key, bool primary = false)
        {
            return new MonitorInfoLite
            {
                Handle = IntPtr.Zero,
                ScreenRect = new Native.RECT { Left = l, Top = t, Right = r, Bottom = b },
                IsPrimary = primary,
                DeviceName = device,
                Key = key,
                FriendlyName = device,
            };
        }

        static string Key(MonitorInfoLite m)
        {
            return m.Key.Length > 0 ? m.Key : m.DeviceName;
        }

        static string ItemAt(WallpaperPlan plan, string key)
        {
            foreach (var s in plan.Surfaces)
                if (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
                    return s.Item != null ? s.Item.Id : "";
            return "";
        }

        static string Rect(Native.RECT r)
        {
            return r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
        }

        static string Describe(WallpaperEngine engine)
        {
            try
            {
                var wins = engine.WindowsSnapshot;
                if (wins.Count == 0) return "no surfaces";
                return string.Join(" | ", wins.Select(w =>
                    w.Item.FilePath + " @ " + Rect(w.MonitorRect) +
                    (w.HasFirstFrame ? " ready" : " no-frame") +
                    (w.IsSpan ? " span" : "")).ToArray());
            }
            catch { return "(unavailable)"; }
        }

        static bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try { if (condition()) return true; } catch { }
                Pump(TimeSpan.FromMilliseconds(100));
            }
            try { return condition(); } catch { return false; }
        }

        /// <summary>Runs the WPF dispatcher for the given time.</summary>
        static void Pump(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        static void Heading(string text)
        {
            Console.WriteLine();
            Console.WriteLine("== " + text + " ==");
            Report.Add("");
            Report.Add("== " + text + " ==");
        }

        static void Note(string text)
        {
            Console.WriteLine("   " + text);
            Report.Add("   " + text);
        }

        static void Skip(string what, string reason)
        {
            _skipped++;
            var line = "SKIP  " + what + "  (" + reason + ")";
            Console.WriteLine("   " + line);
            Report.Add("   " + line);
        }

        static void Check(string what, bool ok, string detail = null)
        {
            if (ok) _passed++; else _failed++;
            var line = (ok ? "PASS  " : "FAIL  ") + what +
                       (!ok && !string.IsNullOrEmpty(detail) ? "  (" + detail + ")" : "");
            Console.WriteLine("   " + line);
            Report.Add("   " + line);
        }

        #endregion
    }
}
