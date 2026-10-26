using System;
using System.IO;
using System.Reflection;

namespace FluxWall
{
    /// <summary>Reads the compiled app icon (embedded as a resource or on disk next to the exe).</summary>
    public static class IconData
    {
        static volatile byte[] _cache;

        public static byte[] IcoBytes
        {
            get
            {
                if (_cache != null) return _cache;
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetName().Name;
                    var res = name + ".app.ico";
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        if (s != null)
                        {
                            var buf = new byte[(int)s.Length];
                            s.Read(buf, 0, buf.Length);
                            _cache = buf;
                            return _cache;
                        }
                    }
                    // Fallback: exe itself has the icon compiled in
                    var exe = asm.Location;
                    if (File.Exists(exe))
                    {
                        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                        using (var ms = new MemoryStream())
                        {
                            icon.Save(ms);
                            _cache = ms.ToArray();
                        }
                    }
                }
                catch { }
                if (_cache == null) _cache = new byte[0];
                return _cache;
            }
        }
    }
}
