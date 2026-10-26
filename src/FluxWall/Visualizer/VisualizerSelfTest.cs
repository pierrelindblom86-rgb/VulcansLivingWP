using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluxWall.Audio;
using FluxWall.Common;
using FluxWall.Settings;

namespace FluxWall.Visualizer
{
    /// <summary>
    /// Automated verification for the optional audio visualiser, run with
    /// <c>VulcansLivingWP.exe --visualizer-test=&lt;folder&gt;</c>.
    ///
    /// It covers the analyser maths, device-format parsing, all three drawing
    /// styles, settings validation/persistence, the real overlay window
    /// (transparency, click-through, geometry) and a genuine WASAPI loopback
    /// capture while a tone is played through the normal Windows audio path.
    /// Nothing here is mocked: if the machine plays no sound, the capture test
    /// fails instead of pretending to pass.
    ///
    /// Exit code 0 = every check passed, 1 = at least one failure.
    /// </summary>
    public static class VisualizerSelfTest
    {
        static readonly List<string> Report = new List<string>();
        static int _passed;
        static int _failed;
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
                Console.WriteLine("SELF-TEST CRASHED: " + e);
                Report.Add("SELF-TEST CRASHED: " + e);
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
                ? Path.Combine(Path.GetTempPath(), "vlwp-visualizer-test")
                : Path.GetFullPath(outputDir);
            Directory.CreateDirectory(_dir);
            _settingsPath = Path.Combine(_dir, "settings-test.xml");

            Heading("VulcansLivingWP — visualizer self-test");
            Note("Time:      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Note("Output:    " + _dir);
            Note("OS:        " + Environment.OSVersion.VersionString);
            Note("Framework: " + Environment.Version);
            Note("Version:   " + AppVersion.Text);

            // The test never touches the live settings file.
            SettingsStore.PathOverride = _settingsPath;
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            SettingsStore.Load();

            _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            Section("Device audio format handling");
            TestFormatParsing();

            Section("FFT, frequency bands and smoothing");
            TestAnalyzer();

            Section("Drawing (all three styles, genuine transparency)");
            TestRendering();

            Section("Settings validation, persistence and recovery");
            TestSettings();

            Section("Overlay window (transparency, click-through, geometry)");
            TestWindow();

            Section("WASAPI loopback capture with real system audio");
            TestCapture();

            Section("Lifecycle, performance and existing behaviour");
            TestLifecycle();

            return Finish();
        }

        #region Format parsing

        static void TestFormatParsing()
        {
            try
            {
                IntPtr p = BuildFormat(48000, 2, 32, true, true);
                var f = MixFormat.Read(p);
                Marshal.FreeHGlobal(p);
                Check("32-bit float stereo mix format", f.IsValid && f.IsFloat &&
                      f.SampleRate == 48000 && f.Channels == 2,
                      string.Format("{0} Hz, {1} ch, {2}-bit float, valid={3}", f.SampleRate, f.Channels, f.BitsPerSample, f.IsValid));
            }
            catch (Exception e) { Fail("32-bit float stereo mix format", e.Message); }

            try
            {
                IntPtr p = BuildFormat(44100, 6, 16, false, true);
                var f = MixFormat.Read(p);
                Marshal.FreeHGlobal(p);
                Check("16-bit PCM 5.1 mix format", f.IsValid && !f.IsFloat &&
                      f.SampleRate == 44100 && f.Channels == 6 && f.BitsPerSample == 16,
                      string.Format("{0} Hz, {1} ch, {2}-bit PCM, valid={3}", f.SampleRate, f.Channels, f.BitsPerSample, f.IsValid));
            }
            catch (Exception e) { Fail("16-bit PCM 5.1 mix format", e.Message); }

            try
            {
                IntPtr p = BuildFormat(96000, 2, 24, false, true);
                var f = MixFormat.Read(p);
                Marshal.FreeHGlobal(p);
                Check("24-bit PCM 96 kHz mix format", f.IsValid && !f.IsFloat && f.BitsPerSample == 24,
                      string.Format("{0} Hz, {1}-bit PCM, valid={2}", f.SampleRate, f.BitsPerSample, f.IsValid));
            }
            catch (Exception e) { Fail("24-bit PCM 96 kHz mix format", e.Message); }
        }

        static IntPtr BuildFormat(int sampleRate, int channels, int bits, bool isFloat, bool extensible)
        {
            int blockAlign = channels * bits / 8;
            int size = extensible ? 40 : 18;
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);

            Marshal.WriteInt16(p, 0, (short)(extensible ? 0xFFFE : (isFloat ? 3 : 1)));
            Marshal.WriteInt16(p, 2, (short)channels);
            Marshal.WriteInt32(p, 4, sampleRate);
            Marshal.WriteInt32(p, 8, sampleRate * blockAlign);
            Marshal.WriteInt16(p, 12, (short)blockAlign);
            Marshal.WriteInt16(p, 14, (short)bits);
            Marshal.WriteInt16(p, 16, (short)(extensible ? 22 : 0));

            if (extensible)
            {
                Marshal.WriteInt16(p, 18, (short)bits);
                var guid = isFloat
                    ? new Guid("00000003-0000-0010-8000-00AA00389B71")
                    : new Guid("00000001-0000-0010-8000-00AA00389B71");
                Marshal.Copy(guid.ToByteArray(), 0, new IntPtr(p.ToInt64() + 24), 16);
            }
            return p;
        }

        #endregion

        #region Analyser

        static void TestAnalyzer()
        {
            try
            {
                var tone = Tone(48000, 1000, 0.5, 0.5f);
                double dominant = SpectrumAnalyzer.DominantFrequency(tone, 48000);
                Check("1 kHz tone recognised by the FFT",
                    Math.Abs(dominant - 1000) < 60, "peak at " + dominant.ToString("F1") + " Hz");
            }
            catch (Exception e) { Fail("1 kHz tone recognised by the FFT", e.Message); }

            var lowAnalyzer = new SpectrumAnalyzer(48000, 48);
            Feed(lowAnalyzer, Tone(48000, 60, 0.4, 0.6f));
            var lowFrame = Snapshot(lowAnalyzer);
            int lowIndex = ArgMax(lowFrame.Bars);
            Check("60 Hz energy lands in the low bars", lowIndex <= 8,
                "strongest bar " + lowIndex + " of 48");

            var highAnalyzer = new SpectrumAnalyzer(48000, 48);
            Feed(highAnalyzer, Tone(48000, 8000, 0.4, 0.6f));
            var highFrame = Snapshot(highAnalyzer);
            int highIndex = ArgMax(highFrame.Bars);
            Check("8 kHz energy lands in the high bars", highIndex >= 34,
                "strongest bar " + highIndex + " of 48");

            // A silent input must not keep bars alive.
            var silentAnalyzer = new SpectrumAnalyzer(48000, 48);
            Feed(silentAnalyzer, Tone(48000, 1000, 0.3, 0.5f));
            Thread.Sleep(1300);
            silentAnalyzer.Write(new float[48000], 48000);
            for (int i = 0; i < 20; i++) silentAnalyzer.AnalyzeNow();
            var silentFrame = Snapshot(silentAnalyzer);
            Check("Silence decays the bars and flags the stream as silent",
                silentFrame.Level < 0.01f && silentFrame.Silent && Max(silentFrame.Bars) < 0.05f,
                string.Format("level={0:F3} silent={1} maxBar={2:F3}",
                    silentFrame.Level, silentFrame.Silent, Max(silentFrame.Bars)));

            // Sensitivity is a gain on the same audio.
            var quiet = new SpectrumAnalyzer(48000, 48);
            quiet.SetResponse(60, 60, true);
            Feed(quiet, Tone(48000, 700, 0.3, 0.08f));
            var quietFrame = Snapshot(quiet);

            var loud = new SpectrumAnalyzer(48000, 48);
            loud.SetResponse(250, 60, true);
            Feed(loud, Tone(48000, 700, 0.3, 0.08f));
            var loudFrame = Snapshot(loud);
            Check("Sensitivity raises the displayed level",
                loudFrame.Level > quietFrame.Level + 0.05f,
                string.Format("60% -> {0:F2}, 250% -> {1:F2}", quietFrame.Level, loudFrame.Level));

            // Smoothing controls how fast bars fall.
            var snappy = new SpectrumAnalyzer(48000, 48);
            snappy.SetResponse(100, 0, true);
            Feed(snappy, Tone(48000, 1000, 0.3, 0.9f));
            snappy.Write(new float[4096], 4096);
            snappy.AnalyzeNow();
            float snappyAfter = Max(Snapshot(snappy).Bars);

            var smooth = new SpectrumAnalyzer(48000, 48);
            smooth.SetResponse(100, 95, true);
            Feed(smooth, Tone(48000, 1000, 0.3, 0.9f));
            smooth.Write(new float[4096], 4096);
            smooth.AnalyzeNow();
            float smoothAfter = Max(Snapshot(smooth).Bars);
            Check("Smoothing keeps bars up longer after the sound stops",
                smoothAfter > snappyAfter + 0.05f,
                string.Format("0% -> {0:F2}, 95% -> {1:F2}", snappyAfter, smoothAfter));

            // Peaks track the bars and fall back down.
            var peakAnalyzer = new SpectrumAnalyzer(48000, 48);
            Feed(peakAnalyzer, Tone(48000, 1000, 0.3, 0.9f));
            float peakHigh = Max(Snapshot(peakAnalyzer).Peaks);
            for (int i = 0; i < 40; i++)
            {
                peakAnalyzer.Write(new float[2048], 2048);
                peakAnalyzer.AnalyzeNow();
            }
            float peakLow = Max(Snapshot(peakAnalyzer).Peaks);
            Check("Peak markers fall back after the sound stops", peakLow < peakHigh - 0.1f,
                string.Format("{0:F2} -> {1:F2}", peakHigh, peakLow));

            // Waveform follows amplitude.
            var waveAnalyzer = new SpectrumAnalyzer(48000, 48);
            Feed(waveAnalyzer, Tone(48000, 440, 0.3, 0.5f));
            var waveFrame = Snapshot(waveAnalyzer);
            Check("Waveform follows the audio amplitude", MaxAbs(waveFrame.Wave) > 0.4f,
                "peak sample " + MaxAbs(waveFrame.Wave).ToString("F2"));

            // A changed bar count is honoured without a restart.
            var resize = new SpectrumAnalyzer(48000, 48);
            resize.Configure(44100, 12);
            var resizedFrame = Snapshot(resize);
            Check("Detail level can change at runtime", resize.Bars == 12 && resize.SampleRate == 44100 &&
                  CountNonZero(resizedFrame.Bars) <= 12,
                "bars=" + resize.Bars + " rate=" + resize.SampleRate);
        }

        static void Feed(SpectrumAnalyzer analyzer, float[] samples)
        {
            const int chunk = 1024;
            for (int offset = 0; offset < samples.Length; offset += chunk)
            {
                int count = Math.Min(chunk, samples.Length - offset);
                var slice = new float[count];
                Array.Copy(samples, offset, slice, 0, count);
                analyzer.Write(slice, count);
                analyzer.AnalyzeNow();
            }
        }

        static float[] Tone(int sampleRate, double frequency, double seconds, float amplitude)
        {
            int count = (int)(sampleRate * seconds);
            var buffer = new float[count];
            for (int i = 0; i < count; i++)
                buffer[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
            return buffer;
        }

        sealed class Frame
        {
            public float[] Bars;
            public float[] Peaks;
            public float[] Wave;
            public float Level;
            public bool Silent;
        }

        static Frame Snapshot(SpectrumAnalyzer analyzer)
        {
            var frame = new Frame
            {
                Bars = new float[VisualizerElement.MaxBars],
                Peaks = new float[VisualizerElement.MaxBars],
                Wave = new float[VisualizerElement.WavePoints],
            };
            int produced;
            analyzer.CopyFrame(frame.Bars, frame.Peaks, frame.Wave, out frame.Level, out frame.Silent, out produced);
            return frame;
        }

        static int ArgMax(float[] values)
        {
            int index = 0;
            float best = float.MinValue;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > best) { best = values[i]; index = i; }
            return index;
        }

        static float Max(float[] values)
        {
            float best = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > best) best = values[i];
            return best;
        }

        static float MaxAbs(float[] values)
        {
            float best = 0;
            for (int i = 0; i < values.Length; i++)
            {
                float abs = Math.Abs(values[i]);
                if (abs > best) best = abs;
            }
            return best;
        }

        static int CountNonZero(float[] values)
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > 0.0001f) count++;
            return count;
        }

        #endregion

        #region Rendering

        static void TestRendering()
        {
            var frame = SyntheticFrame();
            foreach (VisualizerStyle style in new[]
            {
                VisualizerStyle.Bars, VisualizerStyle.Waveform, VisualizerStyle.Circle,
            })
            {
                try
                {
                    var element = BuildElement(style, frame);
                    var bitmap = Render(element, 400, 200);
                    int painted = CountPainted(bitmap, out bool cornerEmpty);
                    Check(style + " style draws on a transparent surface",
                        painted > 150 && cornerEmpty,
                        painted + " painted pixels, background transparent=" + cornerEmpty);
                }
                catch (Exception e) { Fail(style + " style draws on a transparent surface", e.Message); }
            }

            try
            {
                var sample = SyntheticFrame();
                var full = Render(BuildElement(VisualizerStyle.Bars, sample, null, 100), 400, 200);
                var faint = Render(BuildElement(VisualizerStyle.Bars, sample, null, 20), 400, 200);
                int solidPixels = CountPainted(full, 100, out bool _dummy1);
                int faintPixels = CountPainted(faint, 100, out bool _dummy2);
                Check("Opacity setting changes what is drawn",
                    solidPixels > faintPixels * 4 && faintPixels < 500,
                    "solid pixels at 100% = " + solidPixels + ", at 20% = " + faintPixels);
            }
            catch (Exception e) { Fail("Opacity setting changes what is drawn", e.Message); }

            try
            {
                var red = Render(BuildElement(VisualizerStyle.Bars, SyntheticFrame(), Colors.Red, 100), 400, 200);
                Check("Colour setting reaches the pixels", HasRedPixel(red),
                    "found red pixels");
            }
            catch (Exception e) { Fail("Colour setting reaches the pixels", e.Message); }
        }

        static VisualizerElement BuildElement(VisualizerStyle style, Frame frame,
            Color? color = null, double opacity = 100)
        {
            var element = new VisualizerElement
            {
                Width = 400,
                Height = 200,
                Mode = style,
                CurrentBars = 32,
                ShowPeaks = true,
                OpacityPercent = opacity,
            };
            if (color.HasValue) element.Color = color.Value;
            element.Measure(new Size(400, 200));
            element.Arrange(new Rect(0, 0, 400, 200));
            element.SetFrame(frame.Bars, frame.Peaks, frame.Wave, frame.Level);
            element.UpdateLayout();
            return element;
        }

        static Frame SyntheticFrame()
        {
            var frame = new Frame
            {
                Bars = new float[VisualizerElement.MaxBars],
                Peaks = new float[VisualizerElement.MaxBars],
                Wave = new float[VisualizerElement.WavePoints],
                Level = 0.8f,
            };
            for (int i = 0; i < 32; i++)
            {
                frame.Bars[i] = 0.55f + 0.4f * (float)Math.Sin(i * 0.4);
                frame.Peaks[i] = Math.Min(1f, frame.Bars[i] + 0.12f);
            }
            for (int i = 0; i < frame.Wave.Length; i++)
                frame.Wave[i] = 0.6f * (float)Math.Sin(i * 0.25);
            return frame;
        }

        static RenderTargetBitmap Render(VisualizerElement element, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            bitmap.Render(visual);
            bitmap.Render(element);
            return bitmap;
        }

        static int CountPainted(RenderTargetBitmap bitmap, out bool cornerEmpty)
        {
            return CountPainted(bitmap, 10, out cornerEmpty);
        }

        static int CountPainted(RenderTargetBitmap bitmap, int minAlpha, out bool cornerEmpty)
        {
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            int painted = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] >= minAlpha) painted++;

            int alpha = pixels[3];
            cornerEmpty = alpha <= 2;
            return painted;
        }

        static bool HasRedPixel(RenderTargetBitmap bitmap)
        {
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] > 60 && pixels[i + 2] > 150 && pixels[i + 1] < 90 && pixels[i] < 90)
                    return true;
            }
            return false;
        }

        #endregion

        #region Settings

        static void TestSettings()
        {
            try
            {
                SettingsStore.Load();
                var s = SettingsStore.Current;
                s.VisualizerEnabled = true;
                s.VisualizerStyle = VisualizerStyle.Circle;
                s.VisualizerColor = "#35D6ED";
                s.VisualizerOpacity = 55;
                s.VisualizerWidth = 640;
                s.VisualizerHeight = 260;
                s.VisualizerAnchor = VisualizerAnchor.Custom;
                s.VisualizerX = 123;
                s.VisualizerY = 45;
                s.VisualizerSensitivity = 175;
                s.VisualizerBars = 64;
                s.VisualizerSmoothing = 25;
                s.VisualizerPeaks = false;
                s.VisualizerLocked = false;
                SettingsStore.Save();

                SettingsStore.Load();
                var r = SettingsStore.Current;
                bool same = r.VisualizerEnabled && r.VisualizerStyle == VisualizerStyle.Circle &&
                            r.VisualizerColor == "#35D6ED" && r.VisualizerOpacity == 55 &&
                            r.VisualizerWidth == 640 && r.VisualizerHeight == 260 &&
                            r.VisualizerAnchor == VisualizerAnchor.Custom &&
                            r.VisualizerX == 123 && r.VisualizerY == 45 &&
                            r.VisualizerSensitivity == 175 && r.VisualizerBars == 64 &&
                            r.VisualizerSmoothing == 25 && !r.VisualizerPeaks && !r.VisualizerLocked;
                Check("Every visualizer setting survives a save/load round trip", same,
                    same ? "all 14 values restored" : "a value changed");
            }
            catch (Exception e) { Fail("Every visualizer setting survives a save/load round trip", e.Message); }

            try
            {
                // Corrupt XML must never stop the app from starting.
                File.WriteAllText(_settingsPath, "<settings><VisualizerOpacity>not a number");
                SettingsStore.Load();
                var s = SettingsStore.Current;
                Check("Corrupt settings file falls back to working defaults",
                    !s.VisualizerEnabled && s.VisualizerStyle == VisualizerStyle.Bars &&
                    s.VisualizerOpacity == 80 && s.VisualizerBars == 48 &&
                    s.VisualizerColor == AppSettings.DefaultVisualizerColor,
                    "defaults loaded");
            }
            catch (Exception e) { Fail("Corrupt settings file falls back to working defaults", e.Message); }

            try
            {
                // Out-of-range and unknown values are repaired instead of crashing.
                // Written as raw XML because XmlSerializer refuses to *write* an
                // undefined enum value (an editor or an older/newer build can).
                File.WriteAllText(_settingsPath,
                    "<?xml version=\"1.0\"?>" +
                    "<VulcansLivingWP-Settings>" +
                    "<VisualizerEnabled>true</VisualizerEnabled>" +
                    "<VisualizerStyle>42</VisualizerStyle>" +
                    "<VisualizerColor>banana</VisualizerColor>" +
                    "<VisualizerOpacity>9999</VisualizerOpacity>" +
                    "<VisualizerWidth>-10</VisualizerWidth>" +
                    "<VisualizerHeight>40000</VisualizerHeight>" +
                    "<VisualizerAnchor>99</VisualizerAnchor>" +
                    "<VisualizerSensitivity>0</VisualizerSensitivity>" +
                    "<VisualizerBars>1000</VisualizerBars>" +
                    "<VisualizerSmoothing>-5</VisualizerSmoothing>" +
                    "<ActiveWallpaperId>kept-wallpaper</ActiveWallpaperId>" +
                    "<StartWithWindows>true</StartWithWindows>" +
                    "<AutoSwitchMinutes>15</AutoSwitchMinutes>" +
                    "</VulcansLivingWP-Settings>");

                SettingsStore.Load();
                var s = SettingsStore.Current;
                Check("Out-of-range visualizer values are clamped on load",
                    s.VisualizerStyle == VisualizerStyle.Bars &&
                    s.VisualizerAnchor == VisualizerAnchor.BottomCenter &&
                    s.VisualizerColor == AppSettings.DefaultVisualizerColor &&
                    s.VisualizerOpacity == 80 && s.VisualizerWidth == 520 &&
                    s.VisualizerHeight == 180 && s.VisualizerBars == 48 &&
                    s.VisualizerSmoothing == 60 && s.VisualizerSensitivity == 100,
                    string.Format("style={0} opacity={1} width={2} bars={3}",
                        s.VisualizerStyle, s.VisualizerOpacity, s.VisualizerWidth, s.VisualizerBars));

                Check("Repairing visualizer values leaves wallpaper settings alone",
                    s.ActiveWallpaperId == "kept-wallpaper" && s.StartWithWindows && s.AutoSwitchMinutes == 15,
                    "wallpaper id, autostart and auto-switch preserved");
            }
            catch (Exception e) { Fail("Out-of-range visualizer values are clamped on load", e.Message); }

            try
            {
                // A settings file written by 1.2.x has none of the visualizer keys.
                File.WriteAllText(_settingsPath,
                    "<?xml version=\"1.0\"?>" +
                    "<VulcansLivingWP-Settings>" +
                    "<StartWithWindows>false</StartWithWindows>" +
                    "<AutoPauseFullscreen>true</AutoPauseFullscreen>" +
                    "<ShowTrayIcon>true</ShowTrayIcon>" +
                    "<PlayWallpaperAudio>false</PlayWallpaperAudio>" +
                    "<ActiveWallpaperId>old-id</ActiveWallpaperId>" +
                    "</VulcansLivingWP-Settings>");
                SettingsStore.Load();
                var s = SettingsStore.Current;
                Check("Settings from an older version gain visualizer defaults",
                    !s.VisualizerEnabled && s.VisualizerOpacity == 80 &&
                    s.VisualizerColor == AppSettings.DefaultVisualizerColor && s.ActiveWallpaperId == "old-id",
                    "upgrade path safe");
            }
            catch (Exception e) { Fail("Settings from an older version gain visualizer defaults", e.Message); }

            try
            {
                var s = new AppSettings { VisualizerOpacity = 40, VisualizerLocked = true };
                Check("Visualizer colour validation accepts #RRGGBB only",
                    AppSettings.IsValidVisualizerColor("#7C5CFC") &&
                    AppSettings.IsValidVisualizerColor("#AA7C5CFC") &&
                    !AppSettings.IsValidVisualizerColor("red") &&
                    !AppSettings.IsValidVisualizerColor("#GGGGGG") &&
                    !AppSettings.IsValidVisualizerColor(""),
                    "hex parsing strict");
            }
            catch (Exception e) { Fail("Visualizer colour validation accepts #RRGGBB only", e.Message); }

            try
            {
                Check("Visualizer is disabled by default for new users",
                    new AppSettings().VisualizerEnabled == false &&
                    new AppSettings().VisualizerLocked == true,
                    "off + locked");
            }
            catch (Exception e) { Fail("Visualizer is disabled by default for new users", e.Message); }
        }

        #endregion

        #region Window

        static void TestWindow()
        {
            VisualizerWindow window = null;
            try
            {
                SettingsStore.Load();
                window = new VisualizerWindow();
                window.ApplyStyle(VisualizerStyle.Bars, Colors.Cyan, 80, true, 48);
                window.ApplyGeometry(140, 160, 400, 150);
                window.SetLocked(true);
                window.Show();
                Pump(250);

                var hwnd = window.Handle;
                int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                bool toolWindow = (ex & (long)Native.WS_EX_TOOLWINDOW) != 0;
                bool noActivate = (ex & (long)Native.WS_EX_NOACTIVATE) != 0;
                bool transparent = (ex & (long)Native.WS_EX_TRANSPARENT) != 0;
                bool layered = (ex & 0x00080000L) != 0;

                Check("Locked overlay is click-through and never activates",
                    transparent && toolWindow && noActivate,
                    "WS_EX_TRANSPARENT=" + transparent + " TOOLWINDOW=" + toolWindow + " NOACTIVATE=" + noActivate);

                Check("Overlay is a real per-pixel-alpha window",
                    layered && window.Window.AllowsTransparency &&
                    Equals(window.Window.Background, Brushes.Transparent),
                    "WS_EX_LAYERED=" + layered + " AllowsTransparency=" + window.Window.AllowsTransparency);

                Check("Locked overlay ignores the mouse", !window.Element.IsHitTestVisible &&
                      !window.Element.Editing, "element hit-testing disabled");

                window.SetLocked(false);
                Pump(120);
                int ex2 = Native.GetWindowLong(window.Handle, Native.GWL_EXSTYLE);
                Check("Edit mode receives mouse input and shows handles",
                    (ex2 & (long)Native.WS_EX_TRANSPARENT) == 0 &&
                    window.Element.IsHitTestVisible && window.Element.Editing,
                    "WS_EX_TRANSPARENT cleared, handles on");

                window.ApplyGeometry(180, 200, 480, 170);
                Pump(250);
                Native.RECT rect;
                bool got = Native.GetWindowRect(window.Handle, out rect);
                int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
                Check("Width, height and position are applied in real pixels",
                    got && Math.Abs(w - 480) <= 2 && Math.Abs(h - 170) <= 2 &&
                    Math.Abs(rect.Left - 180) <= 2 && Math.Abs(rect.Top - 200) <= 2,
                    string.Format("rect={0},{1} {2}x{3}", rect.Left, rect.Top, w, h));
            }
            catch (Exception e)
            {
                Fail("Overlay window checks", e.ToString());
            }
            finally
            {
                try { window?.Dispose(); } catch { }
                Pump(100);
            }
        }

        #endregion

        #region Capture

        static void TestCapture()
        {
            string tonePath = Path.Combine(_dir, "tone.wav");
            try { WriteToneWav(tonePath); }
            catch (Exception e) { Fail("Test tone can be generated", e.Message); }
            Check("Test tone can be generated", File.Exists(tonePath) && new FileInfo(tonePath).Length > 1000,
                tonePath);

            VisualizerService service = null;
            try
            {
                if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
                SettingsStore.Load();
                var settings = SettingsStore.Current;
                settings.VisualizerEnabled = true;
                settings.VisualizerLocked = true;
                settings.VisualizerWidth = 360;
                settings.VisualizerHeight = 140;
                settings.VisualizerAnchor = VisualizerAnchor.Custom;
                settings.VisualizerX = 160;
                settings.VisualizerY = 160;

                service = new VisualizerService(settings, Dispatcher.CurrentDispatcher);
                service.Apply();
                Pump(200);

                bool started = WaitFor(() => service.IsCapturing, 8000);
                Check("Loopback capture opens the default playback device", started,
                    started
                        ? "'" + service.DeviceName + "' at " +
                          (service.Analyzer != null ? service.Analyzer.SampleRate : 0) + " Hz"
                        : ("last error: " + (service.LastError ?? "none")));

                Check("Visualizer window appears when enabled", service.IsWindowVisible,
                    "visible=" + service.IsWindowVisible);

                // Baseline: whatever is playing right now (normally nothing).
                float baseline = 0;
                for (int i = 0; i < 10; i++) { Pump(100); baseline = Math.Max(baseline, service.CurrentLevel); }

                int framesBefore = service.FramesRendered;
                var player = new System.Media.SoundPlayer(tonePath);
                var playerThread = new Thread(() =>
                {
                    try { player.PlaySync(); } catch (Exception e) { Logger.Warn("Test tone failed: " + e.Message); }
                }) { IsBackground = true, Name = "visualizer-test-tone" };
                playerThread.Start();

                float peakLevel = 0;
                float peakBar = 0;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000)
                {
                    Pump(50);
                    peakLevel = Math.Max(peakLevel, service.CurrentLevel);
                    if (service.Analyzer != null)
                    {
                        var bars = new float[VisualizerElement.MaxBars];
                        var peaks = new float[VisualizerElement.MaxBars];
                        var wave = new float[VisualizerElement.WavePoints];
                        float level; bool silent; int produced;
                        service.Analyzer.CopyFrame(bars, peaks, wave, out level, out silent, out produced);
                        peakBar = Math.Max(peakBar, Max(bars));
                    }
                }
                playerThread.Join(4000);

                Check("Real system audio is captured through WASAPI loopback",
                    peakLevel > Math.Max(0.02f, baseline + 0.02f) && peakBar > 0.05f,
                    string.Format("baseline level {0:F3} -> tone level {1:F3}, peak bar {2:F2}",
                        baseline, peakLevel, peakBar));

                int framesAfter = service.FramesRendered;
                Check("Rendering runs at roughly 30 frames per second while audio plays",
                    framesAfter - framesBefore >= 60 && framesAfter - framesBefore <= 130,
                    (framesAfter - framesBefore) + " frames in 3 s");

                // Let the bars fall back and confirm idle mode stops painting.
                Pump(2200);
                float quietLevel = service.CurrentLevel;
                int quietFrames = service.FramesRendered;
                Pump(1200);
                int quietFramesLater = service.FramesRendered;
                Check("Silence is detected and rendering stops when idle",
                    service.IsSilent && quietLevel < 0.02f && quietFramesLater - quietFrames <= 8,
                    string.Format("level {0:F3}, silent={1}, frames drawn while idle={2}",
                        quietLevel, service.IsSilent, quietFramesLater - quietFrames));

                // Fullscreen behaviour follows the existing pause setting.
                service.SetFullscreenActive(true);
                Pump(300);
                Check("Fullscreen hides the overlay and stops capture",
                    !service.IsWindowVisible && !service.IsCapturing && service.IsSuspended,
                    "hidden=" + !service.IsWindowVisible + " capture=" + service.IsCapturing);

                service.SetFullscreenActive(false);
                Pump(300);
                bool resumed = WaitFor(() => service.IsWindowVisible && service.IsCapturing, 6000);
                Check("Leaving fullscreen restores the overlay and capture", resumed,
                    "visible=" + service.IsWindowVisible + " capture=" + service.IsCapturing);
            }
            catch (Exception e)
            {
                Fail("Loopback capture checks", e.ToString());
            }
            finally
            {
                try { service?.Dispose(); } catch { }
                Pump(200);
            }
        }

        static void WriteToneWav(string path)
        {
            const int sampleRate = 44100;
            const double seconds = 4.0;
            int frames = (int)(sampleRate * seconds);
            int dataBytes = frames * 4;

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)2);          // stereo
                w.Write(sampleRate);
                w.Write(sampleRate * 4);
                w.Write((short)4);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);

                for (int i = 0; i < frames; i++)
                {
                    double t = i / (double)sampleRate;
                    // A fade-in/out avoids clicks and gives several bands something to show.
                    double envelope = Math.Min(1.0, Math.Min(t * 8, (seconds - t) * 8));
                    double sample = envelope * (
                        0.32 * Math.Sin(2 * Math.PI * 180 * t) +
                        0.26 * Math.Sin(2 * Math.PI * 700 * t) +
                        0.18 * Math.Sin(2 * Math.PI * 2500 * t));
                    short value = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sample * 32000));
                    w.Write(value);
                    w.Write(value);
                }
            }
        }

        #endregion

        #region Lifecycle / performance

        static void TestLifecycle()
        {
            VisualizerService service = null;
            try
            {
                if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
                SettingsStore.Load();
                var settings = SettingsStore.Current;
                settings.VisualizerEnabled = true;
                settings.VisualizerLocked = true;
                settings.VisualizerSmoothing = 60;
                settings.VisualizerBars = 48;

                int threadsBefore = ThreadCount();
                service = new VisualizerService(settings, Dispatcher.CurrentDispatcher);
                service.Apply();
                Pump(300);
                WaitFor(() => service.IsCapturing, 6000);

                // CPU with capture running on a silent desktop.
                var process = Process.GetCurrentProcess();
                double idle = MeasureCpu(process, 3000);
                Check("Silent desktop costs little CPU", idle < 3.0,
                    string.Format("{0:F3}% of one core while capturing and idle", idle));

                // CPU while audio plays and 30 fps are drawn: the realistic case.
                string tone = Path.Combine(_dir, "tone-lifecycle.wav");
                if (!File.Exists(tone)) WriteToneWav(tone);
                var player = new System.Media.SoundPlayer(tone);
                var playerThread = new Thread(() =>
                {
                    try { player.PlaySync(); } catch (Exception e) { Logger.Warn("Test tone failed: " + e.Message); }
                }) { IsBackground = true, Name = "visualizer-test-tone-2" };
                playerThread.Start();

                int framesAtStart = service.FramesRendered;
                double active = MeasureCpu(process, 3000);
                int framesDuring = service.FramesRendered - framesAtStart;
                playerThread.Join(5000);

                Check("Playing audio with live drawing stays cheap", active < 8.0,
                    string.Format("{0:F2}% of one core with audio and {1} frames", active, framesDuring));
                Check("Frame rate holds near 30 fps with real audio",
                    framesDuring >= 70 && framesDuring <= 120,
                    framesDuring + " frames in 3 s = " + (framesDuring / 3.0).ToString("F0") + " fps");
                Note("Bars drawn so far: " + service.FramesRendered + ", analyzer frames: " +
                     (service.Analyzer != null ? service.Analyzer.Frames : 0));

                // Disabling must release everything.
                service.SetEnabled(false);
                Pump(500);
                bool stopped = !service.HasCapture && !service.IsCapturing && !service.IsWindowVisible;
                Check("Disabling the visualizer stops capture and closes the window", stopped,
                    "capture=" + service.HasCapture + " window=" + service.IsWindowVisible);

                // Repeated enable/disable cycles must not leak threads or handles.
                for (int i = 0; i < 3; i++)
                {
                    service.SetEnabled(true);
                    Pump(350);
                    WaitFor(() => service.IsCapturing, 5000);
                    service.SetEnabled(false);
                    Pump(250);
                }
                Pump(800);
                int threadsAfter = ThreadCount();
                Check("Enable/disable cycles do not leak threads",
                    threadsAfter <= threadsBefore + 4,
                    "threads " + threadsBefore + " -> " + threadsAfter);

                // Re-enable after disabling, then shut down.
                service.SetEnabled(true);
                Pump(300);
                bool restarted = WaitFor(() => service.IsCapturing, 6000);
                Check("Re-enabling starts capture again", restarted,
                    "capturing=" + service.IsCapturing);

                // Fullscreen pause only applies when the existing setting is on.
                settings.AutoPauseFullscreen = false;
                service.SetFullscreenActive(true);
                Pump(200);
                Check("Fullscreen does not pause the visualizer when auto-pause is off",
                    !service.IsSuspended && service.IsWindowVisible,
                    "suspended=" + service.IsSuspended);
                settings.AutoPauseFullscreen = true;
                service.SetFullscreenActive(false);
                Pump(200);

                // Reset restores documented defaults.
                settings.VisualizerColor = "#123456";
                settings.VisualizerOpacity = 31;
                settings.VisualizerAnchor = VisualizerAnchor.TopLeft;
                settings.VisualizerWidth = 900;
                service.ResetAppearance();
                Pump(200);
                Check("Reset restores the default appearance and position",
                    settings.VisualizerOpacity == 80 && settings.VisualizerWidth == 520 &&
                    settings.VisualizerColor == AppSettings.DefaultVisualizerColor &&
                    settings.VisualizerAnchor == VisualizerAnchor.BottomCenter &&
                    settings.VisualizerLocked,
                    string.Format("opacity={0} width={1} anchor={2}",
                        settings.VisualizerOpacity, settings.VisualizerWidth, settings.VisualizerAnchor));
            }
            catch (Exception e)
            {
                Fail("Lifecycle checks", e.ToString());
            }
            finally
            {
                try { service?.Shutdown(); } catch { }
                try { service?.Dispose(); } catch { }
                Pump(200);
            }
        }

        static double MeasureCpu(Process process, int milliseconds)
        {
            process.Refresh();
            TimeSpan before = process.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            Pump(milliseconds);
            sw.Stop();
            process.Refresh();
            double cpuMs = (process.TotalProcessorTime - before).TotalMilliseconds;
            return cpuMs / Math.Max(1.0, sw.Elapsed.TotalMilliseconds) * 100.0;
        }

        static int ThreadCount()
        {
            try
            {
                using (var p = Process.GetCurrentProcess())
                {
                    p.Refresh();
                    return p.Threads.Count;
                }
            }
            catch { return 0; }
        }

        #endregion

        #region Helpers

        static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Pump(50);
            }
            return condition();
        }

        /// <summary>Pumps the WPF dispatcher so timers and windows make progress.</summary>
        static void Pump(int milliseconds)
        {
            var end = Environment.TickCount + Math.Max(1, milliseconds);
            while (Environment.TickCount < end)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(4);
            }
        }

        static void Heading(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Report.Add("");
            Report.Add(text);
        }

        static void Section(string text)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + text);
            Report.Add("");
            Report.Add("-- " + text);
        }

        static void Note(string text)
        {
            Console.WriteLine("   " + text);
            Report.Add("   " + text);
        }

        static void Check(string name, bool ok, string detail)
        {
            if (ok) _passed++; else _failed++;
            string line = (ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  —  " + detail);
            Console.WriteLine(line);
            Report.Add(line);
            try { Logger.Info("SELFTEST " + line); } catch { }
        }

        static void Fail(string name, string detail)
        {
            Check(name, false, detail);
        }

        static int Finish()
        {
            Report.Add("");
            Report.Add(string.Format("SUMMARY: {0} passed, {1} failed", _passed, _failed));

            string path = Path.Combine(_dir, "visualizer-test-report.txt");
            try { File.WriteAllLines(path, Report, Encoding.UTF8); }
            catch (Exception e) { Console.WriteLine("Could not write report: " + e.Message); }

            Console.WriteLine();
            Console.WriteLine(string.Format("SUMMARY: {0} passed, {1} failed", _passed, _failed));
            Console.WriteLine("Report: " + path);
            return _failed == 0 ? 0 : 1;
        }

        #endregion
    }
}
