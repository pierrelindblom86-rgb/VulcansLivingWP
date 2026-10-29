using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using FluxWall.Common;
using FluxWall.Settings;

namespace FluxWall.Engine
{
    /// <summary>Snapshot helpers for display monitors. Cheap, called seldom.</summary>
    public static class MonitorService
    {
        public static List<MonitorInfoLite> All()
        {
            var list = new List<MonitorInfoLite>();
            try
            {
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                    (IntPtr hmon, IntPtr dc, ref Native.RECT rect, IntPtr data) =>
                {
                    var mi = new Native.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                    if (Native.GetMonitorInfo(hmon, ref mi))
                    {
                        var name = string.IsNullOrEmpty(mi.szDevice) ? "Monitor" : mi.szDevice;
                        var d = DescribeDevice(name);
                        list.Add(new MonitorInfoLite
                        {
                            Handle = hmon,
                            ScreenRect = PhysicalRect(name, mi.rcMonitor),
                            IsPrimary = (mi.dwFlags & 1) != 0,
                            DeviceName = name,
                            Key = d.Key,
                            FriendlyName = d.Name,
                        });
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Monitor enumeration failed", e));
            }

            // Enumeration order is not guaranteed across boots. Sorting by
            // position keeps the UI stable and predictable.
            list.Sort((a, b) =>
            {
                int c = a.ScreenRect.Left.CompareTo(b.ScreenRect.Left);
                return c != 0 ? c : a.ScreenRect.Top.CompareTo(b.ScreenRect.Top);
            });
            return list;
        }

        /// <summary>
        /// Real desktop rectangle of one display, in physical pixels.
        ///
        /// GetMonitorInfo virtualizes rectangles for a system-DPI-aware process:
        /// a 1080x1920 screen scaled to 125% is reported as 864x1536, and the
        /// desktop coordinates of screens at another scale no longer line up
        /// with Explorer's wallpaper layer, which is laid out in physical pixels.
        /// Sizing a wallpaper surface from that rectangle left a fifth of the
        /// screen uncovered (black in portrait, where the video was also cropped
        /// to a narrow column). The current display mode is not virtualized, so
        /// it is the authoritative geometry for wallpaper window placement.
        /// </summary>
        static Native.RECT PhysicalRect(string deviceName, Native.RECT fallback)
        {
            try
            {
                var dm = new Native.DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
                if (Native.EnumDisplaySettingsEx(deviceName, Native.ENUM_CURRENT_SETTINGS, ref dm, 0) &&
                    dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                {
                    return new Native.RECT
                    {
                        Left = dm.dmPositionX,
                        Top = dm.dmPositionY,
                        Right = dm.dmPositionX + dm.dmPelsWidth,
                        Bottom = dm.dmPositionY + dm.dmPelsHeight,
                    };
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Could not read the display mode for " + deviceName + ": " + e.Message);
            }
            return fallback;
        }

        /// <summary>
        /// Stable identity for one monitor: the PnP monitor interface path when
        /// Windows exposes it, otherwise its GDI device name. The path survives
        /// reboots and re-ordering; the GDI name survives only re-ordering.
        /// </summary>
        public static string KeyFor(string deviceName)
        {
            var d = DescribeDevice(deviceName);
            return d.Key;
        }

        struct DeviceInfo { public string Key; public string Name; }

        static DeviceInfo DescribeDevice(string deviceName)
        {
            var info = new DeviceInfo
            {
                Key = "dev:" + (deviceName ?? ""),
                Name = "",
            };
            try
            {
                var dd = new Native.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                if (Native.EnumDisplayDevices(deviceName, 0, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    if (!string.IsNullOrEmpty(dd.DeviceID)) info.Key = "id:" + dd.DeviceID;
                    var friendly = (dd.DeviceString ?? "").Trim();
                    if (friendly.Length > 0) info.Name = friendly;
                }
            }
            catch { }
            return info;
        }

        /// <summary>Resolves the user's monitor-selection setting to concrete monitors.</summary>
        public static List<MonitorInfoLite> Resolve(AppSettings settings, List<MonitorInfoLite> monitors)
        {
            var result = new List<MonitorInfoLite>();
            if (monitors == null || monitors.Count == 0) return result;
            if (monitors.Count == 1)
            {
                result.Add(monitors[0]);
                return result;
            }
            if (settings == null) { result.AddRange(monitors); return result; }

            switch (settings.TargetMonitors)
            {
                case MonitorChoice.All:
                    result.AddRange(monitors);
                    break;

                case MonitorChoice.Selected:
                    foreach (var m in monitors)
                    {
                        if (m.DeviceName == settings.SelectedMonitorDevice ||
                            m.Key == settings.SelectedMonitorDevice)
                        {
                            result.Add(m);
                            break;
                        }
                    }
                    if (result.Count == 0) result.Add(Primary(monitors));
                    break;

                default: // Primary
                    result.Add(Primary(monitors));
                    break;
            }
            return result;
        }

        public static MonitorInfoLite Primary(List<MonitorInfoLite> monitors)
        {
            foreach (var m in monitors)
                if (m != null && m.IsPrimary) return m;
            return monitors[0];
        }

        /// <summary>
        /// Union of the supplied monitors in physical desktop coordinates. Used
        /// for span mode, where the surfaces must not assume a horizontal layout.
        /// </summary>
        public static Native.RECT Union(List<MonitorInfoLite> monitors)
        {
            var r = new Native.RECT();
            bool first = true;
            foreach (var m in monitors)
            {
                if (m == null) continue;
                if (first)
                {
                    r = m.ScreenRect;
                    first = false;
                    continue;
                }
                if (m.ScreenRect.Left < r.Left) r.Left = m.ScreenRect.Left;
                if (m.ScreenRect.Top < r.Top) r.Top = m.ScreenRect.Top;
                if (m.ScreenRect.Right > r.Right) r.Right = m.ScreenRect.Right;
                if (m.ScreenRect.Bottom > r.Bottom) r.Bottom = m.ScreenRect.Bottom;
            }
            return r;
        }

        /// <summary>
        /// Stable description of a display configuration (device + rectangle).
        /// MONITOR handles are re-created on every display change, so they must
        /// never be compared across time; this signature can be.
        /// </summary>
        public static string Signature(List<MonitorInfoLite> monitors)
        {
            if (monitors == null || monitors.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var m in monitors)
            {
                if (m == null) continue;
                sb.Append(m.DeviceName).Append(':')
                  .Append(m.ScreenRect.Left).Append(',')
                  .Append(m.ScreenRect.Top).Append(',')
                  .Append(m.ScreenRect.Right).Append(',')
                  .Append(m.ScreenRect.Bottom).Append('|');
            }
            return sb.ToString();
        }
    }
}
