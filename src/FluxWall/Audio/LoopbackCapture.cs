using System;
using System.Runtime.InteropServices;
using System.Threading;
using FluxWall.Common;

namespace FluxWall.Audio
{
    /// <summary>
    /// Capture of <b>everything Windows is playing</b> (WASAPI loopback on the
    /// default render device). No microphone is ever opened, nothing is written
    /// to disk and nothing leaves the process.
    ///
    /// Design notes:
    ///  - one background MTA thread owns all COM objects; the UI never touches them;
    ///  - packet delivery is event-driven (AUDCLNT_STREAMFLAGS_EVENTCALLBACK) with a
    ///    short bounded wait, so silence costs a handful of wake-ups per second;
    ///  - the default device is re-checked every 2 seconds, so switching headphones
    ///    or unplugging an HDMI screen recovers automatically;
    ///  - every failure closes the session and retries with a back-off instead of
    ///    throwing into the app.
    /// </summary>
    public sealed class LoopbackCapture : IDisposable
    {
        const int WaitTimeoutMs = 100;
        const int SilenceTopUpMs = 40;       // feed zeros so the display can decay
        const int DeviceCheckMs = 2000;

        /// <summary>Reusable mono buffer; the callback must consume it synchronously.</summary>
        public event Action<float[], int> DataAvailable;

        /// <summary>Informational messages: opened device, device lost, retry…</summary>
        public event Action<string> Status;

        readonly object _gate = new object();
        Thread _thread;
        AutoResetEvent _stop;
        AutoResetEvent _packetEvent;
        IMMDeviceEnumerator _enumerator;
        IMMDevice _device;
        IAudioClient _client;
        IAudioCaptureClient _capture;
        string _deviceId = "";
        MixFormat _format;
        string _deviceName = "";

        bool _running;
        volatile bool _started;
        volatile string _lastError;

        float[] _scratch = new float[0];
        float[] _mono = new float[0];
        long _lastPacketTicks;

        public bool IsRunning { get { lock (_gate) return _running; } }
        public string DeviceName { get { lock (_gate) return _deviceName; } }
        public string LastError { get { lock (_gate) return _lastError; } }
        public MixFormat Format { get { lock (_gate) return _format; } }

        /// <summary>True once the audio client is open and started.</summary>
        public bool IsCapturing { get { lock (_gate) return _client != null && _started; } }

        /// <summary>Blocks until the first device session opens (or the timeout).</summary>
        public bool WaitUntilCapturing(int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (IsCapturing) return true;
                Thread.Sleep(25);
            }
            return IsCapturing;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_running) return;
                _running = true;
                _started = false;
                _lastError = null;
            }

            _stop = new AutoResetEvent(false);
            _packetEvent = new AutoResetEvent(false);

            _thread = new Thread(CaptureThread)
            {
                IsBackground = true,
                Name = "VulcansLivingWP audio loopback",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
            Logger.Info("Audio capture requested (WASAPI loopback, default playback device).");
        }

        public void Stop()
        {
            Thread thread;
            lock (_gate)
            {
                if (!_running && _thread == null) return;
                _running = false;
            }

            try { if (_stop != null) _stop.Set(); } catch { }
            try { if (_packetEvent != null) _packetEvent.Set(); } catch { }

            thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(3000); } catch (Exception e) { Logger.Error(e); }
            }

            CloseSession(complain: false);
            try { if (_stop != null) _stop.Dispose(); } catch { }
            try { if (_packetEvent != null) _packetEvent.Dispose(); } catch { }
            _stop = null;
            _packetEvent = null;

            lock (_gate)
            {
                _started = false;
                _deviceName = "";
            }
            Logger.Info("Audio capture stopped; WASAPI resources released.");
        }

        void CaptureThread()
        {
            try
            {
                CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);
                int failures = 0;
                long lastDeviceCheck = 0;

                while (IsRunning)
                {
                    if (_client == null)
                    {
                        if (!OpenSession())
                        {
                            failures++;
                            if (failures == 1)
                                Logger.Warn("Audio capture could not start: " + (LastError ?? "unknown reason"));
                            // Back off, but keep trying: a device may appear later.
                            if (_stop.WaitOne(Math.Min(4000, 500 * failures))) break;
                            continue;
                        }
                        failures = 0;
                        lastDeviceCheck = Environment.TickCount;
                    }

                    bool signaled = false;
                    try { signaled = _packetEvent.WaitOne(WaitTimeoutMs); }
                    catch (ObjectDisposedException) { break; }

                    if (signaled) DrainPackets();
                    else
                    {
                        // Legacy loopback implementations do not signal the event.
                        // Polling keeps us correct there and costs nothing here.
                        if (!DrainPackets() && IdleMilliseconds() >= SilenceTopUpMs)
                            FeedSilence(SilenceTopUpMs);
                    }

                    long now = Environment.TickCount;
                    if (now - lastDeviceCheck >= DeviceCheckMs)
                    {
                        lastDeviceCheck = now;
                        if (DefaultDeviceChanged()) CloseSession(complain: false);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Audio capture thread failed", e));
            }
            finally
            {
                try { CoUninitialize(); } catch { }
            }
        }

        #region Session

        bool OpenSession()
        {
            try
            {
                if (_enumerator == null) _enumerator = Wasapi.CreateEnumerator();
                _device = Wasapi.DefaultRenderDevice(_enumerator);

                string id;
                try { _device.GetId(out id); } catch { id = ""; }
                _deviceId = id ?? "";

                var client = Wasapi.ActivateAudioClient(_device);
                _client = client;

                IntPtr mixPtr = IntPtr.Zero;
                int hr = client.GetMixFormat(out mixPtr);
                if (hr < 0 || mixPtr == IntPtr.Zero)
                    throw new COMException("GetMixFormat failed", hr < 0 ? hr : unchecked((int)0x80004005));

                MixFormat fmt = MixFormat.Read(mixPtr);
                if (!fmt.IsValid)
                {
                    Marshal.FreeCoTaskMem(mixPtr);
                    throw new NotSupportedException(
                        "Unsupported device mix format (" + fmt.BitsPerSample + "-bit, " +
                        (fmt.IsFloat ? "float" : "PCM") + ").");
                }

                try
                {
                    // 0 duration = let WASAPI pick the engine period; loopback needs no
                    // buffer of our own, we read whatever the engine rendered.
                    hr = client.Initialize(AudioConstants.AUDCLNT_SHAREMODE_SHARED,
                        AudioConstants.AUDCLNT_STREAMFLAGS_LOOPBACK |
                        AudioConstants.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                        0, 0, mixPtr, IntPtr.Zero);
                }
                finally { Marshal.FreeCoTaskMem(mixPtr); }
                if (hr < 0) throw new COMException("IAudioClient.Initialize failed", hr);

                object captureObject;
                var captureIid = Wasapi.IID_IAudioCaptureClient;
                hr = client.GetService(ref captureIid, out captureObject);
                if (hr < 0) throw new COMException("IAudioCaptureClient is unavailable", hr);
                _capture = captureObject as IAudioCaptureClient;
                if (_capture == null) throw new COMException("IAudioCaptureClient is unavailable");

                hr = client.SetEventHandle(_packetEvent.SafeWaitHandle.DangerousGetHandle());
                if (hr < 0)
                    Logger.Warn("Audio event handle rejected (hr=0x" + hr.ToString("X8") +
                                "); falling back to polling.");

                hr = client.Start();
                if (hr < 0) throw new COMException("IAudioClient.Start failed", hr);

                lock (_gate)
                {
                    _format = fmt;
                    _deviceName = Wasapi.FriendlyName(_device);
                    _lastError = null;
                    _started = true;
                }
                _lastPacketTicks = Environment.TickCount;

                Logger.Info(string.Format(
                    "Audio capture started: '{0}' {1} Hz, {2} ch, {3}-bit {4}.",
                    _deviceName, fmt.SampleRate, fmt.Channels, fmt.BitsPerSample,
                    fmt.IsFloat ? "float" : "PCM"));
                RaiseStatus("Listening to " + _deviceName);
                return true;
            }
            catch (Exception e)
            {
                CloseSession(complain: false);
                lock (_gate) _lastError = e.Message;
                RaiseStatus("Audio capture unavailable: " + e.Message);
                return false;
            }
        }

        void CloseSession(bool complain)
        {
            var client = _client;
            var capture = _capture;
            var device = _device;
            _client = null;
            _capture = null;
            _device = null;
            _started = false;

            if (client != null)
            {
                try { client.Stop(); } catch { }
                try { client.Reset(); } catch { }
            }
            Release(capture);
            Release(client);
            Release(device);

            if (complain) Logger.Warn("Audio capture session was closed after an error.");
        }

        static void Release(object com)
        {
            if (com == null) return;
            try { if (Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); } catch { }
        }

        bool DefaultDeviceChanged()
        {
            IMMDevice probe = null;
            try
            {
                probe = Wasapi.DefaultRenderDevice(_enumerator);
                string id;
                probe.GetId(out id);
                if (string.IsNullOrEmpty(id)) return false;
                if (string.Equals(id, _deviceId, StringComparison.OrdinalIgnoreCase)) return false;
                Logger.Info("The default playback device changed; restarting audio capture.");
                RaiseStatus("Switching to the new playback device…");
                return true;
            }
            catch { return false; }
            finally { Release(probe); }
        }

        #endregion

        #region Packet handling

        bool DrainPackets()
        {
            bool gotSomething = false;
            try
            {
                uint packetFrames;
                if (_capture == null) return false;
                if (_capture.GetNextPacketSize(out packetFrames) < 0) return false;

                while (packetFrames > 0)
                {
                    IntPtr data;
                    uint frames, flags;
                    ulong devicePosition, qpcPosition;
                    int hr = _capture.GetBuffer(out data, out frames, out flags, out devicePosition, out qpcPosition);
                    if (hr < 0)
                    {
                        Logger.Warn("Audio packet read failed (hr=0x" + hr.ToString("X8") + "); restarting capture.");
                        CloseSession(complain: false);
                        return gotSomething;
                    }

                    try
                    {
                        bool silent = (flags & AudioConstants.AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == IntPtr.Zero;
                        if (silent) FeedSilenceFromFrames((int)frames);
                        else FeedAudio(data, (int)frames);
                        gotSomething = true;
                        _lastPacketTicks = Environment.TickCount;
                    }
                    finally { try { _capture.ReleaseBuffer(frames); } catch { } }

                    if (_capture == null) break;
                    if (_capture.GetNextPacketSize(out packetFrames) < 0) break;
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception("Audio packet handling failed", e));
                CloseSession(complain: false);
            }
            return gotSomething;
        }

        long IdleMilliseconds()
        {
            return Environment.TickCount - _lastPacketTicks;
        }

        void FeedSilenceFromFrames(int frames)
        {
            var fmt = _format;
            if (frames <= 0 || fmt.SampleRate <= 0) return;
            FeedZeros(frames);
        }

        /// <summary>Feeds zeros for roughly <paramref name="milliseconds"/> of audio.</summary>
        void FeedSilence(int milliseconds)
        {
            var fmt = _format;
            if (fmt.SampleRate <= 0) return;
            int frames = fmt.SampleRate * milliseconds / 1000;
            if (frames > 0) FeedZeros(frames);
        }

        void FeedZeros(int frames)
        {
            EnsureMono(frames);
            Array.Clear(_mono, 0, frames);
            Publish(frames);
        }

        void FeedAudio(IntPtr data, int frames)
        {
            var fmt = _format;
            int channels = fmt.Channels;
            int samples = frames * channels;
            if (samples <= 0) return;

            EnsureScratch(samples);
            EnsureMono(frames);

            if (fmt.IsFloat && fmt.BitsPerSample == 32)
                Marshal.Copy(data, _scratch, 0, samples);
            else
                ReadPcm(data, samples, fmt);

            // Down-mix to mono: the spectrum only needs one channel and this keeps
            // the FFT cost independent of the speaker layout.
            int count = frames;
            float inv = channels > 0 ? 1f / channels : 1f;
            for (int i = 0; i < count; i++)
            {
                int baseIndex = i * channels;
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += _scratch[baseIndex + c];
                _mono[i] = sum * inv;
            }
            Publish(count);
        }

        void ReadPcm(IntPtr data, int samples, MixFormat fmt)
        {
            switch (fmt.BitsPerSample)
            {
                case 8:
                {
                    byte[] raw = new byte[samples];
                    Marshal.Copy(data, raw, 0, samples);
                    for (int i = 0; i < samples; i++) _scratch[i] = (raw[i] - 128) / 128f;
                    break;
                }
                case 16:
                {
                    short[] raw = new short[samples];
                    Marshal.Copy(data, raw, 0, samples);
                    for (int i = 0; i < samples; i++) _scratch[i] = raw[i] / 32768f;
                    break;
                }
                case 24:
                {
                    byte[] raw = new byte[samples * 3];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    for (int i = 0; i < samples; i++)
                    {
                        int b = i * 3;
                        int v = raw[b] | (raw[b + 1] << 8) | (raw[b + 2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        _scratch[i] = v / 8388608f;
                    }
                    break;
                }
                default: // 32-bit integer PCM
                {
                    int[] raw = new int[samples];
                    Marshal.Copy(data, raw, 0, samples);
                    for (int i = 0; i < samples; i++) _scratch[i] = raw[i] / 2147483648f;
                    break;
                }
            }
        }

        void EnsureScratch(int count)
        {
            if (_scratch.Length < count) _scratch = new float[Math.Max(count, 4096)];
        }

        void EnsureMono(int count)
        {
            if (_mono.Length < count) _mono = new float[Math.Max(count, 4096)];
        }

        void Publish(int count)
        {
            var handler = DataAvailable;
            if (handler == null) return;
            try { handler(_mono, count); }
            catch (Exception e) { Logger.Error(new Exception("Audio consumer failed", e)); }
        }

        void RaiseStatus(string message)
        {
            var handler = Status;
            if (handler == null) return;
            try { handler(message); } catch { }
        }

        #endregion

        public void Dispose()
        {
            Stop();
            lock (_gate)
            {
                var enumerator = _enumerator;
                _enumerator = null;
                Release(enumerator);
            }
        }

        [DllImport("ole32.dll")]
        static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        static extern void CoUninitialize();
    }
}
