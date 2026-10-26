using System;
using System.Windows;
using System.Windows.Media;
using FluxWall.Settings;

namespace FluxWall.Visualizer
{
    /// <summary>
    /// Draws one visualizer frame. Nothing is put behind the bars: the window is
    /// genuinely transparent, so the desktop (or the video wallpaper) shows through.
    ///
    /// All pens/brushes/geometries are reused; a frame allocates only the geometry
    /// objects WPF needs for the shapes themselves.
    /// </summary>
    public class VisualizerElement : FrameworkElement
    {
        public const int MaxBars = 96;
        public const int WavePoints = 192;

        readonly float[] _bars = new float[MaxBars];
        readonly float[] _peaks = new float[MaxBars];
        readonly float[] _wave = new float[WavePoints];

        VisualizerStyle _style = VisualizerStyle.Bars;
        Color _color = Color.FromRgb(0x7C, 0x5C, 0xFC);
        double _opacity = 0.8;
        bool _showPeaks = true;
        bool _editing;

        Brush _fill, _fillSoft, _peakBrush, _waveBrush;
        Pen _centerPen, _editPen, _peakPen;
        Brush _handleBrush, _handleBorder;
        float _level;

        public VisualizerElement()
        {
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;   // the window decides when the overlay is interactive
            RebuildBrushes();
        }

        public VisualizerStyle Mode
        {
            get { return _style; }
            set { if (_style != value) { _style = value; InvalidateVisual(); } }
        }

        public Color Color
        {
            get { return _color; }
            set
            {
                if (_color == value) return;
                _color = value;
                RebuildBrushes();
                InvalidateVisual();
            }
        }

        /// <summary>10 – 100.</summary>
        public double OpacityPercent
        {
            get { return _opacity * 100; }
            set
            {
                double normalized = Math.Max(0.1, Math.Min(1.0, value / 100.0));
                if (Math.Abs(normalized - _opacity) < 0.0001) return;
                _opacity = normalized;
                RebuildBrushes();
                InvalidateVisual();
            }
        }

        public bool ShowPeaks
        {
            get { return _showPeaks; }
            set { if (_showPeaks != value) { _showPeaks = value; InvalidateVisual(); } }
        }

        /// <summary>Shows the drag/resize border and handles.</summary>
        public bool Editing
        {
            get { return _editing; }
            set
            {
                if (_editing == value) return;
                _editing = value;
                IsHitTestVisible = value;
                InvalidateVisual();
            }
        }

        public int BarCount { get { return MaxBars; } }

        /// <summary>Copies the latest analysis into this element (UI thread).</summary>
        public void SetFrame(float[] bars, float[] peaks, float[] wave, float level)
        {
            if (bars != null) Array.Copy(bars, _bars, Math.Min(bars.Length, _bars.Length));
            if (peaks != null) Array.Copy(peaks, _peaks, Math.Min(peaks.Length, _peaks.Length));
            if (wave != null) Array.Copy(wave, _wave, Math.Min(wave.Length, _wave.Length));
            _level = level;
            InvalidateVisual();
        }

        public void ClearFrame()
        {
            Array.Clear(_bars, 0, _bars.Length);
            Array.Clear(_peaks, 0, _peaks.Length);
            Array.Clear(_wave, 0, _wave.Length);
            _level = 0;
            InvalidateVisual();
        }

        #region Brushes

        void RebuildBrushes()
        {
            byte alpha = (byte)Math.Round(255 * _opacity);
            var solid = Color.FromArgb(alpha, _color.R, _color.G, _color.B);
            var soft = Color.FromArgb((byte)(alpha * 0.55), _color.R, _color.G, _color.B);

            _fill = Frozen(new SolidColorBrush(solid));
            _fillSoft = Frozen(new SolidColorBrush(soft));
            _peakBrush = Frozen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.85), 255, 255, 255)));
            _waveBrush = Frozen(new SolidColorBrush(soft));
            _centerPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.35), _color.R, _color.G, _color.B))), 1));
            _peakPen = Frozen(new Pen(_peakBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

            _editPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(200, 0x7C, 0x5C, 0xFC))), 1)
            {
                DashStyle = new DashStyle(new double[] { 4, 4 }, 0),
            });
            _handleBrush = Frozen(new SolidColorBrush(Color.FromArgb(235, 0x7C, 0x5C, 0xFC)));
            _handleBorder = Frozen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)));
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            if (freezable.CanFreeze) freezable.Freeze();
            return freezable;
        }

        #endregion

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            switch (_style)
            {
                case VisualizerStyle.Waveform: DrawWaveform(dc, w, h); break;
                case VisualizerStyle.Circle: DrawCircle(dc, w, h); break;
                default: DrawBars(dc, w, h); break;
            }

            if (_editing) DrawEditChrome(dc, w, h);
        }

        void DrawBars(DrawingContext dc, double w, double h)
        {
            int n = Math.Max(1, Math.Min(MaxBars, CurrentBars));
            double gap = Math.Max(1.0, Math.Min(4.0, w * 0.006));
            double barWidth = Math.Max(1.0, (w - gap * (n - 1)) / n);
            double radius = Math.Min(barWidth / 2, 3.0);
            double baseLine = h - 1;

            for (int i = 0; i < n; i++)
            {
                double value = _bars[i];
                double height = Math.Max(0, value * (h - 4));
                double x = i * (barWidth + gap);
                if (height > 0.5)
                    dc.DrawRoundedRectangle(_fill, null,
                        new Rect(x, baseLine - height, barWidth, height), radius, radius);

                if (_showPeaks)
                {
                    double peak = Math.Max(value, _peaks[i]);
                    double y = baseLine - Math.Max(height + 3, peak * (h - 4));
                    if (y < baseLine - 2)
                        dc.DrawRectangle(_peakBrush, null, new Rect(x, Math.Max(0, y - 1), barWidth, 2));
                }
            }
        }

        void DrawWaveform(DrawingContext dc, double w, double h)
        {
            double mid = h / 2;
            double amp = (h / 2) - 2;
            int n = WavePoints;
            double step = w / (n - 1);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, mid - _wave[0] * amp), true, true);
                for (int i = 1; i < n; i++)
                    ctx.LineTo(new Point(i * step, mid - _wave[i] * amp), true, false);
                for (int i = n - 1; i >= 0; i--)
                    ctx.LineTo(new Point(i * step, mid + _wave[i] * amp), true, false);
            }
            geometry.Freeze();

            dc.DrawGeometry(_waveBrush, null, geometry);
            dc.DrawLine(_centerPen, new Point(0, mid), new Point(w, mid));
        }

        void DrawCircle(DrawingContext dc, double w, double h)
        {
            double cx = w / 2, cy = h / 2;
            double outer = Math.Max(4, Math.Min(w, h) / 2 - 4);
            double inner = outer * 0.52;
            int n = Math.Max(3, Math.Min(MaxBars, CurrentBars));
            double slice = 2 * Math.PI / n;

            // One geometry for every segment: a single rounded pen stroke.
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = 0; i < n; i++)
                {
                    double angle = -Math.PI / 2 + i * slice;
                    double value = _bars[i];
                    double length = inner + (outer - inner) * value;
                    double cos = Math.Cos(angle), sin = Math.Sin(angle);
                    var from = new Point(cx + Math.Cos(angle - slice * 0.15) * inner,
                                         cy + Math.Sin(angle - slice * 0.15) * inner);
                    var to = new Point(cx + Math.Cos(angle - slice * 0.15) * length,
                                       cy + Math.Sin(angle - slice * 0.15) * length);
                    ctx.BeginFigure(from, false, false);
                    ctx.LineTo(to, true, false);

                    if (_showPeaks && _peaks[i] > 0.02)
                    {
                        double peakLength = inner + (outer - inner) * _peaks[i] + 2;
                        var peakFrom = new Point(cx + Math.Cos(angle + slice * 0.15) * (peakLength - 1),
                                                 cy + Math.Sin(angle + slice * 0.15) * (peakLength - 1));
                        var peakTo = new Point(cx + Math.Cos(angle + slice * 0.15) * peakLength,
                                               cy + Math.Sin(angle + slice * 0.15) * peakLength);
                        ctx.BeginFigure(peakFrom, false, false);
                        ctx.LineTo(peakTo, true, false);
                    }
                }
            }
            geometry.Freeze();

            double thickness = Math.Max(2.0, Math.Min(9.0, (2 * Math.PI * outer / n) * 0.4));
            var pen = new Pen(_fill, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            if (pen.CanFreeze) pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);

            // A quiet inner ring keeps the shape readable when the room is silent.
            dc.DrawEllipse(null, _centerPen, new Point(cx, cy), inner - 2, inner - 2);
        }

        void DrawEditChrome(DrawingContext dc, double w, double h)
        {
            var rect = new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1));
            dc.DrawRectangle(null, _editPen, rect);

            const double size = 8;
            var points = new[]
            {
                new Point(0, 0), new Point(w / 2, 0), new Point(w, 0),
                new Point(0, h / 2), new Point(w, h / 2),
                new Point(0, h), new Point(w / 2, h), new Point(w, h),
            };
            foreach (var p in points)
            {
                dc.DrawRectangle(_handleBrush, new Pen(_handleBorder, 1),
                    new Rect(p.X - size / 2, p.Y - size / 2, size, size));
            }
        }

        /// <summary>Number of bars the current frame was produced with.</summary>
        public int CurrentBars { get; set; } = 48;
    }
}
