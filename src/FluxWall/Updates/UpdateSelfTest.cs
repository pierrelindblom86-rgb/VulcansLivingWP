using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using FluxWall.Settings;

namespace FluxWall.Updates
{
    /// <summary>
    /// Automated verification of the update checker, run with
    /// <c>VulcansLivingWP.exe --update-test=&lt;folder&gt;</c>.
    ///
    /// It checks the version arithmetic, the release parser (drafts,
    /// prereleases, multi-digit components, junk), the settings cache and the
    /// real GitHub endpoint. Offline behaviour is exercised by pointing the
    /// system proxy at a dead port, which is the same code path a real
    /// disconnection takes.
    ///
    /// Exit code 0 = every check passed, 1 = at least one failed.
    /// </summary>
    public static class UpdateSelfTest
    {
        static readonly List<string> Report = new List<string>();
        static int _passed;
        static int _failed;
        static string _dir;
        static string _settingsPath;

        public static int Run(string outputDir)
        {
            try
            {
                return RunCore(outputDir);
            }
            catch (Exception e)
            {
                Console.WriteLine("UPDATE SELF-TEST CRASHED: " + e);
                Report.Add("UPDATE SELF-TEST CRASHED: " + e);
                return 2;
            }
            finally
            {
                SettingsStore.PathOverride = null;
            }
        }

        static int RunCore(string outputDir)
        {
            _dir = string.IsNullOrEmpty(outputDir)
                ? Path.Combine(Path.GetTempPath(), "vlwp-update-test")
                : Path.GetFullPath(outputDir);
            Directory.CreateDirectory(_dir);
            _settingsPath = Path.Combine(_dir, "settings-update-test.xml");
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);

            Heading("VulcansLivingWP — update checker self-test");
            Note("Time:      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Note("Output:    " + _dir);
            Note("Version:   " + AppVersion.Text);
            Note("Endpoint:  " + UpdateService.ApiUrl);

            // Never disturb the real settings file.
            SettingsStore.PathOverride = _settingsPath;
            SettingsStore.Load();

            VersionComparisons();
            UrlSafety();
            ParserFixtures();
            CacheAndSettings();
            LiveCheck();
            OfflineCheck();

            Heading("Result");
            Note("Passed: " + _passed);
            Note("Failed: " + _failed);
            if (_failed == 0) Note("ALL CHECKS PASSED");

            try
            {
                File.WriteAllText(Path.Combine(_dir, "update-test-report.txt"),
                    string.Join(Environment.NewLine, Report.ToArray()) + Environment.NewLine);
            }
            catch { }

            return _failed == 0 ? 0 : 1;
        }

        #region Checks

        static void VersionComparisons()
        {
            Heading("Version comparison");

            var cases = new[]
            {
                new object[] { "1.3.0",   "1.2.3",   true,  "newer patch release" },
                new object[] { "1.2.3",   "1.2.3",   false, "same version" },
                new object[] { "1.2.10",  "1.2.9",   true,  "multi-digit patch" },
                new object[] { "1.10.0",  "1.9.9",   true,  "multi-digit minor, not string-sorted" },
                new object[] { "v1.3.0",  "1.2.3",   true,  "leading v" },
                new object[] { "1.3",     "1.2.9",   true,  "two-component tag" },
                new object[] { "1.3.0-beta.2", "1.3.0", false, "prerelease is not newer than its release" },
                new object[] { "1.3.0",   "1.3.0-beta.2", true, "release beats its prerelease" },
                new object[] { "2.0.0",   "1.99.99", true,  "major bump" },
                new object[] { "1.2.2",   "1.2.3",   false, "older release" },
                new object[] { "garbage", "1.2.3",   false, "unparsable candidate is ignored" },
                new object[] { "1.3.0",   "garbage", true,  "unparsable installed falls back" },
            };

            foreach (var c in cases)
            {
                var actual = UpdateService.IsNewer((string)c[0], (string)c[1]);
                Check("IsNewer(\"" + c[0] + "\", \"" + c[1] + "\") == " + c[2] + "  [" + c[3] + "]",
                      actual == (bool)c[2],
                      "got " + actual);
            }
        }

        static void UrlSafety()
        {
            Heading("URL safety");

            Check("official API URL is https on api.github.com", UpdateService.IsAllowedUrl(UpdateService.ApiUrl));
            Check("plain http is rejected",
                  !UpdateService.IsAllowedUrl("http://api.github.com/repos/x/y/releases"));
            Check("look-alike host is rejected",
                  !UpdateService.IsAllowedUrl("https://api.github.com.evil.example/repos/x/y/releases"));
            Check("file:// is rejected", !UpdateService.IsAllowedUrl("file:///C:/releases.json"));
            Check("releases page is https",
                  UpdateService.ReleasesPageUrl.StartsWith("https://github.com/", StringComparison.Ordinal));
        }

        static void ParserFixtures()
        {
            Heading("Release parser");

            const string json = @"[
              { ""tag_name"": ""v1.4.0"", ""name"": ""1.4.0"", ""draft"": true,  ""prerelease"": false, ""body"": ""draft"", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/v1.4.0"" },
              { ""tag_name"": ""v1.5.0-beta.1"", ""name"": ""beta"", ""draft"": false, ""prerelease"": true, ""body"": ""beta"", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/v1.5.0-beta.1"" },
              { ""tag_name"": ""v1.10.0"", ""name"": ""Ten"", ""draft"": false, ""prerelease"": false, ""body"": ""Big release"", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/v1.10.0"" },
              { ""tag_name"": ""v1.9.0"", ""name"": ""Nine"", ""draft"": false, ""prerelease"": false, ""body"": """", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/v1.9.0"" },
              { ""tag_name"": ""nightly"", ""name"": ""no version"", ""draft"": false, ""prerelease"": false, ""body"": """", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/nightly"" },
              { ""tag_name"": ""v1.11.0"", ""name"": ""bad url"", ""draft"": false, ""prerelease"": false, ""body"": """", ""html_url"": ""http://example.com/insecure"" }
            ]";

            var list = UpdateService.ParseReleases(json);
            Check("draft and prerelease entries are dropped", list.Count == 3, "got " + list.Count);
            Check("unparsable tag skipped", list.Count == 3);
            Check("newest stable is 1.11.0", list.Count > 0 && list[0].Version == "1.11.0",
                  "got " + (list.Count > 0 ? list[0].Version : "(none)"));
            Check("1.10.0 sorts above 1.9.0 (numeric, not string)",
                  list.Count > 2 && list[1].Version == "1.10.0" && list[2].Version == "1.9.0",
                  list.Count > 2 ? list[1].Version + "," + list[2].Version : "too few");
            Check("entry with http html_url falls back to the releases page",
                  list.Count > 0 && list[0].Url == UpdateService.LatestReleasePageUrl,
                  "got " + (list.Count > 0 ? list[0].Url : ""));

            bool threw = false;
            try { UpdateService.ParseReleases("{ not json"); }
            catch (Exception) { threw = true; }
            Check("malformed JSON is reported, not ignored", threw);

            bool emptyOk = UpdateService.ParseReleases("[]").Count == 0;
            Check("empty array -> no releases", emptyOk);

            var single = UpdateService.ParseReleases(
                @"[{ ""tag_name"": ""v1.3.0"", ""name"": ""1.3.0"", ""draft"": false, ""prerelease"": false, ""body"": ""Notes here"", ""html_url"": ""https://github.com/pierrelindblom86-rgb/VulcansLivingWP/releases/tag/v1.3.0"" }]");
            Check("stable entry keeps its notes", single.Count == 1 && single[0].Notes == "Notes here");
        }

        static void CacheAndSettings()
        {
            Heading("Settings cache");

            var settings = SettingsStore.Current;
            settings.LatestKnownVersion = "1.4.0";
            settings.LatestReleaseUrl = UpdateService.LatestReleasePageUrl;
            settings.LatestReleaseNotes = "cached notes";
            settings.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-1);

            var cached = UpdateService.CachedResult(settings);
            Check("cached newer version reports Available", cached.State == UpdateState.Available,
                  "got " + cached.State);
            Check("cached result is marked as cache", cached.FromCache);
            Check("cached result carries the installed version", cached.Installed == AppVersion.Text,
                  "got " + cached.Installed);

            settings.LatestKnownVersion = AppVersion.Text;
            var same = UpdateService.CachedResult(settings);
            Check("cached equal version reports UpToDate", same.State == UpdateState.UpToDate,
                  "got " + same.State);

            UpdateService.Dismiss("9.9.9");
            Check("dismissal is persisted", SettingsStore.Current.DismissedUpdateVersion == "9.9.9");

            settings.CheckForUpdates = false;
            UpdateResult disabled = null;
            UpdateService.StartupCheck(r => disabled = r);
            Check("disabled setting returns Disabled without a request",
                  disabled != null && disabled.State == UpdateState.Disabled,
                  disabled == null ? "no result" : disabled.State.ToString());

            // Re-enabling must round-trip through the real settings file.
            settings.CheckForUpdates = true;
            SettingsStore.Save();
            SettingsStore.Load();
            Check("settings survive a save/load round trip",
                  SettingsStore.Current.CheckForUpdates &&
                  SettingsStore.Current.LatestKnownVersion == AppVersion.Text,
                  "check=" + SettingsStore.Current.CheckForUpdates +
                  " cached=" + SettingsStore.Current.LatestKnownVersion);
        }

        static void LiveCheck()
        {
            Heading("Live GitHub check");
            Note("This is a real HTTPS request to the public releases API.");

            var result = UpdateService.CheckCore(false);
            Note("State:   " + result.State);
            Note("Latest:  " + (string.IsNullOrEmpty(result.Latest) ? "(none)" : result.Latest));
            Note("Message: " + result.Message);

            Check("live check did not fail", result.State == UpdateState.Available || result.State == UpdateState.UpToDate,
                  result.Message);
            Check("live check read the installed version", result.Installed == AppVersion.Text);
            if (result.State == UpdateState.Available)
                Check("available release has a https url",
                      result.Release != null && result.Release.Url.StartsWith("https://github.com/", StringComparison.Ordinal));
        }

        static void OfflineCheck()
        {
            Heading("Offline / unreachable GitHub");

            var original = WebRequest.DefaultWebProxy;
            try
            {
                // A closed loopback port fails immediately, exactly like a broken
                // or blocked connection. The checker must report it, not throw.
                WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:9");
                var result = UpdateService.CheckCore(false);
                Check("offline check reports Failed instead of throwing", result.State == UpdateState.Failed,
                      "got " + result.State);
                Check("offline check explains the problem", !string.IsNullOrEmpty(result.Message));
                Note("Offline message: " + result.Message);
            }
            catch (Exception e)
            {
                Check("offline check never throws", false, e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                WebRequest.DefaultWebProxy = original;
            }
        }

        #endregion

        #region Report helpers

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
