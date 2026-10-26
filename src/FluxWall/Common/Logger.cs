using System;
using System.IO;
using System.Threading;

namespace FluxWall.Common
{
    /// <summary>
    /// Very small rolling file logger. Writes to %LOCALAPPDATA%\VulcansLivingWP\logs.
    /// Never throws: logging failures must not take the app down.
    /// </summary>
    public static class Logger
    {
        static readonly object _lock = new object();
        static string _file;

        public static void Init()
        {
            Paths.Ensure();
            _file = Path.Combine(Paths.LogsDir, "VulcansLivingWP-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            PurgeOld();
            Info("Logger started. Version=" + AppVersion.Text);
            // Upgrade path: 1.0.x kept its data under %LOCALAPPDATA%\FluxWall.
            Paths.MigrateLegacyData();
        }

        public static string LogFile { get { return _file; } }
        public static string LogFolder { get { return Paths.LogsDir; } }

        public static void Info(string message)     { Write("INFO ", message); }
        public static void Warn(string message)     { Write("WARN ", message); }
        public static void Error(Exception ex)      { WriteNested("ERROR", ex); }

        static void WriteNested(string level, Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.Append(level).Append(' ').Append(e.GetType().Name)
                  .Append(": ").AppendLine(e.Message);
                if (e.StackTrace != null) sb.AppendLine(e.StackTrace);
            }
            Write(level, sb.ToString());
        }

        static void Write(string level, string message)
        {
            try
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_file)) return;
                    File.AppendAllText(_file, line + Environment.NewLine, System.Text.Encoding.UTF8);
                }
            }
            catch { /* never fatal */ }
        }

        static void PurgeOld()
        {
            try
            {
                var dir = new DirectoryInfo(Paths.LogsDir);
                if (!dir.Exists) return;
                foreach (var f in dir.GetFiles())
                {
                    if (!f.Name.StartsWith("VulcansLivingWP-") || f.Name.EndsWith(".log") == false)
                        continue;
                    if (f.Name == Path.GetFileName(_file))
                        continue;
                    if ((DateTime.Now - f.LastWriteTime).TotalDays > 90)
                        try { f.Delete(); } catch { }
                }
            }
            catch { /* never fatal */ }
        }
    }
}
