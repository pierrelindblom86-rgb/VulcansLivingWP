using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using FluxWall.Common;
using FluxWall.Settings;

namespace FluxWall.Updates
{
    /// <summary>
    /// A numeric version as used by releases ("1.3.0"). Comparison is numeric,
    /// component by component, so 1.10.0 is correctly newer than 1.9.9. A
    /// prerelease suffix is parsed but never treated as newer than the release
    /// it belongs to, and prereleases are ignored by the checker entirely.
    /// </summary>
    public struct SemVersion : IComparable<SemVersion>
    {
        public int Major, Minor, Patch;
        public bool Valid;
        /// <summary>True for "1.3.0-beta.2": numerically equal, but older than 1.3.0.</summary>
        public bool IsPrerelease;

        public static SemVersion Parse(string text)
        {
            var v = new SemVersion();
            if (string.IsNullOrEmpty(text)) return v;
            var s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);

            // Drop a prerelease ("1.3.0-beta.2") or build ("1.3.0+abc") suffix,
            // remembering whether the version was a prerelease.
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                v.IsPrerelease = s[cut] == '-';
                s = s.Substring(0, cut);
            }

            var parts = s.Split('.');
            if (parts.Length == 0 || parts.Length > 3) return v;

            int[] values = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n))
                    return v;
                values[i] = n;
            }
            v.Major = values[0];
            v.Minor = values[1];
            v.Patch = values[2];
            v.Valid = true;
            return v;
        }

        public int CompareTo(SemVersion other)
        {
            if (Major != other.Major) return Major < other.Major ? -1 : 1;
            if (Minor != other.Minor) return Minor < other.Minor ? -1 : 1;
            if (Patch != other.Patch) return Patch < other.Patch ? -1 : 1;
            // 1.3.0 is newer than 1.3.0-beta.2.
            if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;
            return 0;
        }

        public override string ToString()
        {
            return Major + "." + Minor + "." + Patch;
        }
    }

    /// <summary>One public, stable release from the GitHub API.</summary>
    public sealed class UpdateRelease
    {
        public string Version = "";
        public string Tag = "";
        public string Name = "";
        public string Notes = "";
        public string Url = "";
    }

    public enum UpdateState
    {
        /// <summary>Nothing checked yet (or the result was never saved).</summary>
        Unknown = 0,
        Disabled,
        UpToDate,
        Available,
        Failed,
    }

    /// <summary>Outcome of one check, with everything the UI needs.</summary>
    public sealed class UpdateResult
    {
        public UpdateState State = UpdateState.Unknown;
        public string Installed = "";
        public string Latest = "";
        public string Message = "";
        public UpdateRelease Release;
        public DateTime CheckedUtc = DateTime.MinValue;
        /// <summary>True when the values come from the last saved check.</summary>
        public bool FromCache;
    }

    /// <summary>
    /// Checks the project's public GitHub releases for a newer stable version.
    ///
    /// Safety rules baked in here:
    ///   • HTTPS only, official api.github.com endpoint, no credentials.
    ///   • Drafts and prereleases are ignored.
    ///   • Nothing is downloaded or executed; the user can only open the
    ///     release page in a browser.
    ///   • Any failure (offline, timeout, rate limit, bad JSON) is reported as
    ///     a message and never throws into the UI or the wallpaper engine.
    ///   • Automatic checks run at most once every 12 hours and never block
    ///     startup.
    /// </summary>
    public static class UpdateService
    {
        public const string Owner = "pierrelindblom86-rgb";
        public const string Repo = "VulcansLivingWP";

        public const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=20";
        public const string ReleasesPageUrl = "https://github.com/" + Owner + "/" + Repo + "/releases";
        public const string LatestReleasePageUrl = ReleasesPageUrl + "/latest";

        /// <summary>Automatic checks never run more often than this.</summary>
        public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(12);

        static readonly object _gate = new object();
        static UpdateResult _last;
        static bool _inFlight;

        /// <summary>The most recent result, or null before the first check.</summary>
        public static UpdateResult Last
        {
            get { lock (_gate) { return _last; } }
        }

        public static bool IsChecking
        {
            get { lock (_gate) { return _inFlight; } }
        }

        /// <summary>Raised on the calling thread's context (see CheckAsync).</summary>
        public static event Action<UpdateResult> Completed;

        #region Public API

        /// <summary>
        /// Runs a check on a background thread and calls <paramref name="done"/>
        /// on the thread pool. Never throws.
        /// </summary>
        public static void CheckAsync(Action<UpdateResult> done)
        {
            lock (_gate)
            {
                if (_inFlight) return;
                _inFlight = true;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                UpdateResult result;
                try { result = CheckCore(true); }
                finally { lock (_gate) { _inFlight = false; } }

                lock (_gate) { _last = result; }
                try { Logger.Info("Update check: " + result.State + " — " + result.Message); }
                catch { }

                try { if (done != null) done(result); } catch (Exception e) { Logger.Error(e); }
                RaiseCompleted(result);
            });
        }

        /// <summary>
        /// Tells the UI that a result is available. The About page is built at
        /// startup, before the delayed automatic check runs, so it starts in the
        /// "checking" state; every path that resolves the check — including the
        /// cached and disabled shortcuts — has to report its result or the card
        /// would stay on "Checking GitHub" with a disabled button for the session.
        /// </summary>
        static void RaiseCompleted(UpdateResult result)
        {
            var handlers = Completed;
            if (handlers == null) return;
            try { handlers(result); } catch (Exception e) { Logger.Error(e); }
        }

        /// <summary>
        /// Startup path. Honors the user's setting and the 12-hour interval, so
        /// relaunching the app never hammers GitHub. Uses the saved result when
        /// it is still fresh.
        /// </summary>
        public static void StartupCheck(Action<UpdateResult> done)
        {
            try
            {
                var settings = SettingsStore.Current;
                if (settings == null || !settings.CheckForUpdates)
                {
                    var disabled = new UpdateResult
                    {
                        State = UpdateState.Disabled,
                        Installed = AppVersion.Text,
                        Message = "Update checks are turned off in Settings.",
                    };
                    lock (_gate) { _last = disabled; }
                    if (done != null) done(disabled);
                    RaiseCompleted(disabled);
                    return;
                }

                var fresh = CachedResult(settings);
                if (fresh != null && DateTime.UtcNow - settings.LastUpdateCheckUtc < AutomaticInterval)
                {
                    lock (_gate) { _last = fresh; }
                    Logger.Info("Update check skipped: last check was " +
                                settings.LastUpdateCheckUtc.ToString("u") + " (within 12 hours).");
                    if (done != null) done(fresh);
                    RaiseCompleted(fresh);
                    return;
                }

                CheckAsync(done);
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Startup update check failed", e));
            }
        }

        /// <summary>What the About page shows before/without a network call.</summary>
        public static UpdateResult CachedResult(AppSettings settings)
        {
            var r = new UpdateResult { Installed = AppVersion.Text };
            if (settings == null) return r;

            r.CheckedUtc = settings.LastUpdateCheckUtc;
            r.Latest = settings.LatestKnownVersion ?? "";
            if (r.Latest.Length == 0) return r;

            r.FromCache = true;
            r.Release = new UpdateRelease
            {
                Version = r.Latest,
                Notes = settings.LatestReleaseNotes ?? "",
                Url = string.IsNullOrEmpty(settings.LatestReleaseUrl)
                    ? LatestReleasePageUrl
                    : settings.LatestReleaseUrl,
            };

            if (IsNewer(r.Latest, r.Installed))
            {
                r.State = UpdateState.Available;
                r.Message = "Version " + r.Latest + " is available.";
            }
            else
            {
                r.State = UpdateState.UpToDate;
                r.Message = "You are running the latest stable version.";
            }
            return r;
        }

        /// <summary>Numeric comparison with a safe fallback for unparsable text.</summary>
        public static bool IsNewer(string candidate, string installed)
        {
            var a = SemVersion.Parse(candidate);
            var b = SemVersion.Parse(installed);
            if (!a.Valid) return false;
            if (!b.Valid) return true;
            return a.CompareTo(b) > 0;
        }

        #endregion

        #region Network

        /// <summary>Synchronous check. Exposed for the automated self-test.</summary>
        public static UpdateResult CheckCore(bool save)
        {
            var result = new UpdateResult { Installed = AppVersion.Text, CheckedUtc = DateTime.UtcNow };

            if (!IsAllowedUrl(ApiUrl))
            {
                result.State = UpdateState.Failed;
                result.Message = "Update checks must use HTTPS.";
                return result;
            }

            string json;
            try
            {
                EnableTls();
                json = HttpGet(ApiUrl, 8000);
            }
            catch (WebException we)
            {
                result.State = UpdateState.Failed;
                result.Message = DescribeWebException(we);
                Logger.Warn("Update check failed: " + result.Message);
                return result;
            }
            catch (Exception e)
            {
                result.State = UpdateState.Failed;
                result.Message = "Could not reach GitHub. " + e.Message;
                Logger.Warn("Update check failed: " + result.Message);
                return result;
            }

            List<UpdateRelease> releases;
            try
            {
                releases = ParseReleases(json);
            }
            catch (Exception e)
            {
                result.State = UpdateState.Failed;
                result.Message = "GitHub's response could not be read: " + e.Message;
                Logger.Warn("Update check failed: " + result.Message);
                return result;
            }

            if (releases.Count == 0)
            {
                result.State = UpdateState.Failed;
                result.Message = "No published stable release was found on GitHub yet.";
                return result;
            }

            var latest = releases[0];
            result.Latest = latest.Version;
            result.Release = latest;

            if (IsNewer(latest.Version, AppVersion.Text))
            {
                result.State = UpdateState.Available;
                result.Message = "Version " + latest.Version + " is available. You have " + AppVersion.Text + ".";
            }
            else
            {
                result.State = UpdateState.UpToDate;
                result.Message = "You are running the latest stable version (" + AppVersion.Text + ").";
            }

            if (save) SaveToSettings(result);
            return result;
        }

        static void EnableTls()
        {
            try
            {
                // GitHub has required TLS 1.2+ for years; older .NET defaults
                // to SSL3/TLS1.0 on some Windows 7/8 installs.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = "VulcansLivingWP/" + AppVersion.Text + " (+" + ReleasesPageUrl + ")";
            req.Accept = "application/vnd.github+json";
            req.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            req.Proxy = WebRequest.DefaultWebProxy;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        static string DescribeWebException(WebException we)
        {
            var resp = we.Response as HttpWebResponse;
            if (resp != null)
            {
                int code = (int)resp.StatusCode;
                if (code == 403 || code == 429)
                    return "GitHub is rate limiting update checks right now (HTTP " + code +
                           "). Try again in a little while.";
                if (code == 404)
                    return "The release list could not be found on GitHub (HTTP 404).";
                return "GitHub returned HTTP " + code + ".";
            }

            switch (we.Status)
            {
                case WebExceptionStatus.Timeout:
                    return "GitHub did not answer in time. Check your connection and try again.";
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "You appear to be offline, or GitHub is unreachable.";
                default:
                    return "Could not reach GitHub (" + we.Status + ").";
            }
        }

        /// <summary>Only https URLs may be requested.</summary>
        public static bool IsAllowedUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses the releases array and returns stable releases, newest first.
        /// Drafts, prereleases and entries without a usable version are skipped.
        /// </summary>
        public static List<UpdateRelease> ParseReleases(string json)
        {
            var list = new List<UpdateRelease>();
            if (string.IsNullOrEmpty(json)) return list;

            var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var parsed = ser.Deserialize<object>(json);
            var array = parsed as object[];
            if (array == null) throw new FormatException("expected a list of releases");

            foreach (var entry in array)
            {
                var map = entry as Dictionary<string, object>;
                if (map == null) continue;

                if (Bool(map, "draft") || Bool(map, "prerelease")) continue;

                var tag = Str(map, "tag_name");
                var version = SemVersion.Parse(tag);
                if (!version.Valid) continue;

                var release = new UpdateRelease
                {
                    Tag = tag,
                    Version = version.ToString(),
                    Name = Str(map, "name"),
                    Notes = Truncate(Str(map, "body"), 4000),
                    Url = SafeReleaseUrl(Str(map, "html_url")),
                };
                list.Add(release);
            }

            list.Sort((a, b) =>
            {
                int c = SemVersion.Parse(b.Version).CompareTo(SemVersion.Parse(a.Version));
                return c;
            });
            return list;
        }

        static bool Bool(Dictionary<string, object> map, string key)
        {
            object v;
            return map.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        static string Str(Dictionary<string, object> map, string key)
        {
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r\n", "\n").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "\n…";
        }

        /// <summary>Release links are only ever opened in a browser, but keep them https.</summary>
        static string SafeReleaseUrl(string url)
        {
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return uri.AbsoluteUri;
            return LatestReleasePageUrl;
        }

        #endregion

        #region Persistence

        static void SaveToSettings(UpdateResult result)
        {
            try
            {
                var settings = SettingsStore.Current;
                if (settings == null || result == null) return;

                settings.LastUpdateCheckUtc = result.CheckedUtc;
                if (result.State == UpdateState.Available || result.State == UpdateState.UpToDate)
                {
                    settings.LatestKnownVersion = result.Latest ?? "";
                    settings.LatestReleaseUrl = result.Release != null ? result.Release.Url : "";
                    settings.LatestReleaseNotes = result.Release != null ? result.Release.Notes : "";
                }
                SettingsStore.Save();
            }
            catch (Exception e)
            {
                Logger.Warn("Could not save the update check result: " + e.Message);
            }
        }

        /// <summary>Records that the user chose "remind me later" for a version.</summary>
        public static void Dismiss(string version)
        {
            try
            {
                var settings = SettingsStore.Current;
                if (settings == null) return;
                settings.DismissedUpdateVersion = version ?? "";
                SettingsStore.Save();
            }
            catch (Exception e) { Logger.Error(e); }
        }

        #endregion
    }
}
