using System;
using System.Reflection;

namespace FluxWall
{
    /// <summary>
    /// The application version, read from the assembly metadata that the build
    /// fills in from version.props. Nothing else in the code base hardcodes a
    /// version string, so the exe, the About page, the log and the update
    /// checker can never disagree.
    /// </summary>
    public static class AppVersion
    {
        static readonly string _text = Read();

        /// <summary>Display form, for example "1.3.0".</summary>
        public static string Text { get { return _text; } }

        static string Read()
        {
            try
            {
                var asm = typeof(AppVersion).Assembly;

                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var value = info != null ? info.InformationalVersion : null;
                if (!string.IsNullOrEmpty(value))
                {
                    // SourceLink-style suffixes ("1.3.0+abc123") are build noise.
                    int plus = value.IndexOf('+');
                    if (plus > 0) value = value.Substring(0, plus);
                    value = value.Trim();
                    if (value.Length > 0) return value;
                }

                var v = asm.GetName().Version;
                if (v != null) return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch
            {
                // A version is never worth crashing the app over.
            }
            return "0.0.0";
        }
    }
}
