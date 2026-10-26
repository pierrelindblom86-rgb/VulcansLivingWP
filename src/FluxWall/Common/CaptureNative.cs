using System;
using System.Runtime.InteropServices;

namespace FluxWall.Common
{
    /// <summary>PrintWindow with DWM-composited content support.</summary>
    public static class CaptureNative
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        public static bool PrintWindowWithContent(IntPtr hwnd, IntPtr hdc, uint flags)
        {
            try { return PrintWindow(hwnd, hdc, flags); }
            catch { return false; }
        }
    }
}
