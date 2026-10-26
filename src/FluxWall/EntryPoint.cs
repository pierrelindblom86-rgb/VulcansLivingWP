using System;
using System.Windows;

namespace FluxWall
{
    /// <summary>
    /// Standard entry point (single instance is enforced by Mutex). STA with
    /// Application object created manually so everything stays code-first.
    /// </summary>
    public static class EntryPoint
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // The installer and uninstaller pass --shutdown. It must never create
            // a window, never show "already running", and never touch the library.
            if (HasSwitch(args, "--shutdown"))
            {
                int exit = Common.ShutdownSignal.RequestAndWait(12000);
                // 0 = nothing left running, 3 = it refused to exit. Report both so
                // the installer can fall back to terminating the process.
                Environment.Exit(exit);
            }

            Common.Logger.Init();

            // Automated visualizer verification (see docs/VISUALIZER.md). Runs before
            // the single-instance mutex so it works while the app is installed and
            // running, and touches only its own settings copy.
            string testDir = null;
            bool runSelfTest = false;
            foreach (var raw in args ?? new string[0])
            {
                if (raw == null) continue;
                if (raw.Equals("--visualizer-test", StringComparison.OrdinalIgnoreCase))
                {
                    runSelfTest = true;
                }
                else if (raw.StartsWith("--visualizer-test=", StringComparison.OrdinalIgnoreCase))
                {
                    runSelfTest = true;
                    testDir = raw.Substring("--visualizer-test=".Length).Trim('"');
                }
            }
            if (runSelfTest)
            {
                Environment.Exit(Visualizer.VisualizerSelfTest.Run(testDir));
            }

            // Automated update-checker verification (see docs/UPDATES.md). Also runs
            // before the single-instance mutex and uses its own settings copy.
            string updateTestDir = null;
            bool runUpdateTest = false;
            foreach (var raw in args ?? new string[0])
            {
                if (raw == null) continue;
                if (raw.Equals("--update-test", StringComparison.OrdinalIgnoreCase))
                {
                    runUpdateTest = true;
                }
                else if (raw.StartsWith("--update-test=", StringComparison.OrdinalIgnoreCase))
                {
                    runUpdateTest = true;
                    updateTestDir = raw.Substring("--update-test=".Length).Trim('"');
                }
            }
            if (runUpdateTest)
            {
                Environment.Exit(Updates.UpdateSelfTest.Run(updateTestDir));
            }

            // Multi-monitor layout verification (see docs/TESTING.md). Uses the
            // real desktop layer and its own settings copy.
            string layoutTestDir = null;
            bool runLayoutTest = false;
            foreach (var raw in args ?? new string[0])
            {
                if (raw == null) continue;
                if (raw.Equals("--layout-test", StringComparison.OrdinalIgnoreCase))
                {
                    runLayoutTest = true;
                }
                else if (raw.StartsWith("--layout-test=", StringComparison.OrdinalIgnoreCase))
                {
                    runLayoutTest = true;
                    layoutTestDir = raw.Substring("--layout-test=".Length).Trim('"');
                }
            }
            if (runLayoutTest)
            {
                Environment.Exit(Engine.LayoutSelfTest.Run(layoutTestDir));
            }

            // The installer passes these after copying the files. They only touch
            // the settings file, so no window is created and nothing is played.
            if (Common.InstallerSetup.TryApply(args)) Environment.Exit(0);

            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, Common.ShutdownSignal.MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // A second copy would just fight over the wallpaper window.
                    MessageBox.Show("VulcansLivingWP is already running.\n\nLook for its icon in the system tray (bottom-right corner).",
                        "VulcansLivingWP", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                GC.KeepAlive(mutex);

                Common.Logger.Info("Application starting.");

                var app = new FluxWallApp();
                try
                {
                    app.Run(args);
                }
                catch (Exception e)
                {
                    Common.Logger.Error(e);
                    MessageBox.Show(
                        "VulcansLivingWP could not start:\n" + e.Message +
                        "\n\nTechnical details were written to the log folder.",
                        "VulcansLivingWP", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Common.Logger.Info("Application exiting.");
                }
            }
        }

        static bool HasSwitch(string[] args, string name)
        {
            foreach (var raw in args ?? new string[0])
            {
                if (raw != null && raw.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
