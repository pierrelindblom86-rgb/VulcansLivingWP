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
                        list.Add(new MonitorInfoLite
                        {
                            Handle = hmon,
                            ScreenRect = mi.rcMonitor,
                            IsPrimary = (mi.dwFlags & 1) != 0,
                            DeviceName = name,
                        });
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Monitor enumeration failed", e));
            }
            return list;
        }

        /// <summary>Resolves the user's monitor-selection setting to concrete monitors.</summary>
        public static List<MonitorInfoLite> Resolve(AppSettings settings, List<MonitorInfoLite> monitors)
        {
            var result = new List<MonitorInfoLite>();
            if (monitors.Count == 0) return result;
            if (monitors.Count == 1)
            {
                result.Add(monitors[0]);
                return result;
            }

            switch (settings.TargetMonitors)
            {
                case MonitorChoice.All:
                    result.AddRange(monitors);
                    break;

                case MonitorChoice.Selected:
                    foreach (var m in monitors)
                    {
                        if (m.DeviceName == settings.SelectedMonitorDevice)
                        {
                            result.Add(m);
                            break;
                        }
                    }
                    if (result.Count == 0) result.Add(monitors[0]);
                    break;

                default: // Primary
                    foreach (var m in monitors)
                        if (m.IsPrimary) { result.Add(m); break; }
                    if (result.Count == 0) result.Add(monitors[0]);
                    break;
            }
            return result;
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
