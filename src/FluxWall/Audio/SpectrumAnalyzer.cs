using System;
using System.Diagnostics;

namespace FluxWall.Audio
{
    /// <summary>
    /// Turns mono PCM into display values: log-spaced frequency bars, their
    /// peaks, a down-sampled waveform and an overall level.
    ///
    /// Everything is pre-allocated: a frame costs one 2048-point FFT (a few
    /// hundred microseconds) and no garbage. Analysis is throttled to roughly
    /// 30 frames per second no matter how fast packets arrive.
    /// </summary>
    public sealed class SpectrumAnalyzer
    {
        public const int FftSize = 2048;
        public const int WavePoints = 192;
        const int HopMs = 33;              // ~30 analyses per second
        const float DbFloor = -72f;
        const float DbCeil = -8f;

        readonly object _gate = new object();

        int _sampleRate = 48000;
        int _bars = 48;
        int _sensitivity = 100;
        int _smoothing = 60;
        bool _peaks = true;

        float[] _window;                   // Hann
        int[] _reversal;
        float[] _cos;
        float[] _sin;

        float[] _ring = new float[FftSize];
        int _ringPos;
        int _ringCount;

        float[] _re = new float[FftSize];
        float[] _im = new float[FftSize];

        int[] _binLow = new int[0];
        int[] _binHigh = new int[0];

        float[] _smooth = new float[0];
        float[] _peak = new float[0];
        float[] _wave = new float[WavePoints];
        float _wavePeak;
        float _level;
        bool _silent = true;

        long _lastStepMs = long.MinValue;
        long _lastLoudMs;
        int _frames;

        public SpectrumAnalyzer(int sampleRate, int bars)
        {
            Configure(sampleRate, bars);
        }

        public int Bars { get { lock (_gate) return _bars; } }
        public int SampleRate { get { lock (_gate) return _sampleRate; } }
        public int Frames { get { lock (_gate) return _frames; } }
        public float Level { get { lock (_gate) return _level; } }
        public bool Silent { get { lock (_gate) return _silent; } }

        /// <summary>(Re)builds tables for a device format or bar count change.</summary>
        public void Configure(int sampleRate, int bars)
        {
            if (sampleRate < 8000) sampleRate = 48000;
            if (bars < 2) bars = 2;
            if (bars > 256) bars = 256;

            lock (_gate)
            {
                _sampleRate = sampleRate;
                _bars = bars;

                if (_window == null || _window.Length != FftSize) _window = BuildHann();
                if (_reversal == null) _reversal = BuildReversal();
                if (_cos == null || _sin == null) BuildTwiddles();

                _binLow = new int[bars];
                _binHigh = new int[bars];
                double nyquist = sampleRate / 2.0;
                double lowHz = 40.0;
                double highHz = Math.Min(16000.0, nyquist * 0.95);
                if (highHz <= lowHz * 2) highHz = Math.Max(lowHz * 2, nyquist);

                for (int i = 0; i < bars; i++)
                {
                    double f0 = lowHz * Math.Pow(highHz / lowHz, (double)i / bars);
                    double f1 = lowHz * Math.Pow(highHz / lowHz, (double)(i + 1) / bars);
                    int b0 = (int)Math.Floor(f0 / sampleRate * FftSize);
                    int b1 = (int)Math.Ceiling(f1 / sampleRate * FftSize);
                    if (b1 <= b0) b1 = b0 + 1;
                    _binLow[i] = Math.Max(1, Math.Min(FftSize / 2 - 1, b0));
                    _binHigh[i] = Math.Max(_binLow[i], Math.Min(FftSize / 2 - 1, b1));
                }

                _smooth = new float[bars];
                _peak = new float[bars];
            }
        }

        public void SetResponse(int sensitivityPercent, int smoothingPercent, bool showPeaks)
        {
            lock (_gate)
            {
                _sensitivity = sensitivityPercent < 20 ? 20 : (sensitivityPercent > 400 ? 400 : sensitivityPercent);
                _smoothing = smoothingPercent < 0 ? 0 : (smoothingPercent > 95 ? 95 : smoothingPercent);
                _peaks = showPeaks;
            }
        }

        /// <summary>Push mono samples. Called on the capture thread.</summary>
        public void Write(float[] mono, int count)
        {
            if (mono == null || count <= 0) return;
            lock (_gate)
            {
                for (int i = 0; i < count; i++)
                {
                    _ring[_ringPos] = mono[i];
                    _ringPos = (_ringPos + 1) % FftSize;
                    if (_ringCount < FftSize) _ringCount++;
                }
            }
        }

        /// <summary>
        /// Produces a new analysis frame when 33 ms have passed. Returns true when
        /// the display should be refreshed. Called on the capture thread.
        /// </summary>
        public bool Step()
        {
            long now = NowMs();
            lock (_gate)
            {
                if (_lastStepMs != long.MinValue && now - _lastStepMs < HopMs) return false;
                _lastStepMs = now;
                return StepCore(now);
            }
        }

        /// <summary>Test hook: analyse immediately, ignoring the 33 ms throttle.</summary>
        public bool AnalyzeNow()
        {
            lock (_gate) return StepCore(NowMs());
        }

        /// <summary>
        /// Millisecond clock with sub-millisecond resolution. Environment.TickCount
        /// only advances in ~15.6 ms steps, which silently halved the frame rate.
        /// </summary>
        static long NowMs()
        {
            return (long)(Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency);
        }

        bool StepCore(long now)
        {
                if (_ringCount <= 0)
                {
                    // Nothing captured yet: still decay so a stopped stream cannot
                    // leave frozen bars on screen.
                    DecayOnly();
                    return true;
                }

                // Oldest → newest copy of the ring.
                int oldest = _ringCount == FftSize ? _ringPos : 0;
                float rawPeak = 0f;
                double sumSquares = 0;
                for (int i = 0; i < FftSize; i++)
                {
                    float sample = _ring[(oldest + i) % FftSize];
                    _re[i] = sample * _window[i];
                    _im[i] = 0f;
                    float abs = Math.Abs(sample);
                    if (abs > rawPeak) rawPeak = abs;
                    sumSquares += sample * (double)sample;
                }

                float gain = _sensitivity / 100f;
                float rms = (float)Math.Sqrt(sumSquares / FftSize);
                _level = Clamp01(rms * gain * 2.2f);

                bool loud = rawPeak * gain > 0.002f;
                if (loud) _lastLoudMs = now;
                _silent = !loud && now - _lastLoudMs > 1200;

                Fft(_re, _im);

                // Magnitudes for the positive half of the spectrum.
                float norm = 4f / FftSize;             // full-scale sine ≈ 1.0
                float gainDb = 20f * (float)Math.Log10(gain);

                for (int b = 0; b < _bars; b++)
                {
                    float peakMag = 0f;
                    int lo = _binLow[b], hi = _binHigh[b];
                    for (int k = lo; k <= hi; k++)
                    {
                        float re = _re[k], im = _im[k];
                        float mag = (float)Math.Sqrt(re * re + im * im) * norm;
                        if (mag > peakMag) peakMag = mag;
                    }

                    float db = 20f * (float)Math.Log10(peakMag + 1e-9f) + gainDb;
                    float target = Clamp01((db - DbFloor) / (DbCeil - DbFloor));

                    float retain = 0.2f + 0.75f * (_smoothing / 100f);
                    float current = _smooth[b];
                    _smooth[b] = target > current
                        ? current + (target - current) * 0.7f      // fast attack
                        : current * retain + target * (1f - retain); // smooth release

                    float peak = _peak[b] - 0.006f;
                    if (_smooth[b] > peak) peak = _smooth[b];
                    if (peak < 0f) peak = 0f;
                    _peak[b] = peak;
                }

                BuildWaveform(gain);
                _frames++;
            return true;
        }

        void BuildWaveform(float gain)
        {
            int oldest = _ringCount == FftSize ? _ringPos : 0;
            int stride = Math.Max(1, FftSize / WavePoints);
            float peak = 0f;

            for (int i = 0; i < WavePoints; i++)
            {
                int index = (oldest + i * stride) % FftSize;
                float value = Clamp1(_ring[index] * gain * 3.2f);
                // Light smoothing keeps the line from shimmering at 30 fps.
                float smoothed = _wave[i] * 0.45f + value * 0.55f;
                _wave[i] = smoothed;
                float abs = Math.Abs(smoothed);
                if (abs > peak) peak = abs;
            }
            _wavePeak = peak;
        }

        void DecayOnly()
        {
            float retain = 0.2f + 0.75f * (_smoothing / 100f);
            for (int b = 0; b < _bars; b++)
            {
                _smooth[b] *= retain;
                _peak[b] = Math.Max(0f, _peak[b] - 0.006f);
                if (_smooth[b] > _peak[b]) _peak[b] = _smooth[b];
            }
            for (int i = 0; i < WavePoints; i++) _wave[i] *= 0.5f;
            _level *= 0.5f;
            _wavePeak *= 0.5f;
            _silent = true;
        }

        /// <summary>
        /// Copies the latest frame into caller-owned buffers. Safe to call from the
        /// UI thread; no allocation, no shared state.
        /// </summary>
        public void CopyFrame(float[] bars, float[] peaks, float[] wave,
            out float level, out bool silent, out int produced)
        {
            lock (_gate)
            {
                int n = Math.Min(bars.Length, _bars);
                Array.Copy(_smooth, bars, n);
                if (n < bars.Length) Array.Clear(bars, n, bars.Length - n);
                if (peaks != null)
                {
                    int p = Math.Min(peaks.Length, _bars);
                    Array.Copy(_peak, peaks, p);
                    if (p < peaks.Length) Array.Clear(peaks, p, peaks.Length - p);
                }
                if (wave != null)
                {
                    int w = Math.Min(wave.Length, WavePoints);
                    Array.Copy(_wave, wave, w);
                }
                level = _level;
                silent = _silent;
                produced = _frames;
            }
        }

        #region FFT

        static float[] BuildHann()
        {
            var w = new float[FftSize];
            for (int i = 0; i < FftSize; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
            return w;
        }

        static int[] BuildReversal()
        {
            var table = new int[FftSize];
            int bits = 0;
            while ((1 << bits) < FftSize) bits++;
            for (int i = 0; i < FftSize; i++)
            {
                int value = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) value |= 1 << (bits - 1 - b);
                table[i] = value;
            }
            return table;
        }

        void BuildTwiddles()
        {
            _cos = new float[FftSize / 2];
            _sin = new float[FftSize / 2];
            for (int i = 0; i < FftSize / 2; i++)
            {
                double angle = -2.0 * Math.PI * i / FftSize;
                _cos[i] = (float)Math.Cos(angle);
                _sin[i] = (float)Math.Sin(angle);
            }
        }

        /// <summary>In-place iterative radix-2 FFT (no allocations).</summary>
        void Fft(float[] re, float[] im)
        {
            int n = FftSize;
            for (int i = 0; i < n; i++)
            {
                int j = _reversal[i];
                if (j > i)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                int step = n / size;
                for (int start = 0; start < n; start += size)
                {
                    int twiddle = 0;
                    for (int k = start; k < start + half; k++)
                    {
                        int j = k + half;
                        float wr = _cos[twiddle], wi = _sin[twiddle];
                        float tr = re[j] * wr - im[j] * wi;
                        float ti = re[j] * wi + im[j] * wr;
                        re[j] = re[k] - tr;
                        im[j] = im[k] - ti;
                        re[k] += tr;
                        im[k] += ti;
                        twiddle += step;
                    }
                }
            }
        }

        /// <summary>Test hook: dominant frequency of a mono block, in Hz.</summary>
        public static double DominantFrequency(float[] mono, int sampleRate)
        {
            var re = new float[FftSize];
            var im = new float[FftSize];
            var window = BuildHann();
            int count = Math.Min(mono.Length, FftSize);
            for (int i = 0; i < count; i++)
            {
                re[i] = mono[i] * window[i];
                im[i] = 0f;
            }

            var table = BuildReversal();
            for (int i = 0; i < FftSize; i++)
            {
                int j = table[i];
                if (j > i) { var t = re[i]; re[i] = re[j]; re[j] = t; }
            }

            for (int size = 2; size <= FftSize; size <<= 1)
            {
                int half = size >> 1;
                int step = FftSize / size;
                for (int start = 0; start < FftSize; start += size)
                {
                    int twiddle = 0;
                    for (int k = start; k < start + half; k++)
                    {
                        int j = k + half;
                        double angle = -2.0 * Math.PI * twiddle / FftSize;
                        float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
                        float tr = re[j] * wr - im[j] * wi;
                        float ti = re[j] * wi + im[j] * wr;
                        re[j] = re[k] - tr;
                        im[j] = im[k] - ti;
                        re[k] += tr;
                        im[k] += ti;
                        twiddle += step;
                    }
                }
            }

            int best = 1;
            double bestMag = 0;
            for (int k = 1; k < FftSize / 2; k++)
            {
                double mag = re[k] * (double)re[k] + im[k] * (double)im[k];
                if (mag > bestMag) { bestMag = mag; best = k; }
            }
            return best * (double)sampleRate / FftSize;
        }

        #endregion

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Clamp1(float v) { return v < -1f ? -1f : (v > 1f ? 1f : v); }
    }
}
