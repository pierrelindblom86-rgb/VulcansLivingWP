using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FluxWall.Common;
using FluxWall.Settings;

namespace FluxWall.Visualizer
{
    /// <summary>
    /// The desktop overlay: a borderless, per-pixel-transparent window owned by
    /// this process. It is deliberately NOT part of the wallpaper layer, so it
    /// can never interfere with video playback.
    ///
    ///  - WS_EX_TOOLWINDOW / WS_EX_NOACTIVATE: no taskbar entry, never takes focus;
    ///  - WS_EX_TRANSPARENT while locked: clicks fall through to icons and apps;
    ///  - edit mode removes that bit and draws handles for dragging and resizing.
    /// </summary>
    public class VisualizerWindow : IDisposable
    {
        const double MinWidth = 140;
        const double MinHeight = 70;
        const double HandleGrab = 12;

        [Flags]
        enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

        readonly Window _window;
        readonly VisualizerElement _element;

        bool _locked = true;
        bool _dragging;
        bool _resizing;
        Edge _edge;
        Point _dragStart;
        Rect _startRect;

        public VisualizerWindow()
        {
            _element = new VisualizerElement();

            _window = new Window
            {
                Title = "VulcansLivingWP Visualizer",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,          // real per-pixel alpha
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                Width = 520,
                Height = 180,
                Left = 0,
                Top = 0,
                Content = _element,
                UseLayoutRounding = true,
            };

            _window.SourceInitialized += (s, e) => ApplyExtendedStyles();
            _window.Loaded += (s, e) => ApplyExtendedStyles();
            _window.Closed += (s, e) => { };

            _element.MouseLeftButtonDown += OnMouseDown;
            _element.MouseMove += OnMouseMove;
            _element.MouseLeftButtonUp += OnMouseUp;
            _element.LostMouseCapture += (s, e) => { _dragging = _resizing = false; };
        }

        public Window Window { get { return _window; } }
        public VisualizerElement Element { get { return _element; } }
        public bool IsVisible { get { return _window.IsVisible; } }
        public bool IsLocked { get { return _locked; } }
        public bool IsEditing { get { return !_locked && _element.Editing; } }

        /// <summary>Raised after the user drags/resizes: physical screen pixels.</summary>
        public event Action<int, int, int, int> GeometryChanged;

        public IntPtr Handle
        {
            get { try { return new WindowInteropHelper(_window).Handle; } catch { return IntPtr.Zero; } }
        }

        #region Window plumbing

        /// <summary>System DPI factor (the app is system-DPI aware).</summary>
        public double Scale
        {
            get
            {
                try
                {
                    var source = PresentationSource.FromVisual(_element);
                    if (source != null && source.CompositionTarget != null)
                    {
                        double scale = source.CompositionTarget.TransformToDevice.M11;
                        if (scale > 0.1) return scale;
                    }
                }
                catch { }
                return 1.0;
            }
        }

        void ApplyExtendedStyles()
        {
            var hwnd = Handle;
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex |= (long)Native.WS_EX_TOOLWINDOW | (long)Native.WS_EX_NOACTIVATE;
            if (_locked) ex |= (long)Native.WS_EX_TRANSPARENT;
            else ex &= ~(long)Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, (int)ex);
            // Force a hit-test recalculation for the new transparency bit.
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | 0x0020 /* SWP_FRAMECHANGED */);
        }

        /// <summary>Places the window using physical pixels (overall size in DIPs).</summary>
        public void ApplyGeometry(int left, int top, int width, int height)
        {
            try
            {
                double scale = Scale;
                _window.Left = left / scale;
                _window.Top = top / scale;
                _window.Width = Math.Max(MinWidth, width / scale);
                _window.Height = Math.Max(MinHeight, height / scale);
            }
            catch (Exception e) { Logger.Error(e); }
        }

        public void Show()
        {
            if (!_window.IsVisible)
            {
                _window.Show();
                ApplyExtendedStyles();
            }
        }

        public void Hide()
        {
            try { if (_window.IsVisible) _window.Hide(); } catch { }
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            _element.Editing = !locked;
            _element.IsHitTestVisible = !locked;
            ApplyExtendedStyles();
            if (locked) _window.Cursor = Cursors.Arrow;
        }

        public void ApplyStyle(VisualizerStyle style, Color color, int opacityPercent, bool showPeaks, int barCount)
        {
            _element.Mode = style;
            _element.Color = color;
            _element.OpacityPercent = opacityPercent;
            _element.ShowPeaks = showPeaks;
            _element.CurrentBars = barCount;
        }

        #endregion

        #region Drag / resize

        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_locked) return;
            try
            {
                _dragStart = e.GetPosition(_element);
                _startRect = new Rect(_window.Left, _window.Top, _window.Width, _window.Height);
                _edge = HitEdge(_dragStart);
                _resizing = _edge != Edge.None;
                _dragging = !_resizing;
                _element.CaptureMouse();
                e.Handled = true;
            }
            catch (Exception ex) { Logger.Error(ex); }
        }

        Edge HitEdge(Point p)
        {
            double w = _element.ActualWidth, h = _element.ActualHeight;
            Edge edge = Edge.None;
            if (p.X <= HandleGrab) edge |= Edge.Left;
            else if (p.X >= w - HandleGrab) edge |= Edge.Right;
            if (p.Y <= HandleGrab) edge |= Edge.Top;
            else if (p.Y >= h - HandleGrab) edge |= Edge.Bottom;
            return edge;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_locked) return;
            try
            {
                var p = e.GetPosition(_element);
                if (!_dragging && !_resizing)
                {
                    _window.Cursor = CursorFor(HitEdge(p));
                    return;
                }

                double dx = p.X - _dragStart.X;
                double dy = p.Y - _dragStart.Y;
                double left = _startRect.Left, top = _startRect.Top;
                double width = _startRect.Width, height = _startRect.Height;

                if (_resizing)
                {
                    if ((_edge & Edge.Left) != 0)
                    {
                        left = Math.Min(_startRect.Left + dx, _startRect.Right - MinWidth);
                        width = _startRect.Right - left;
                    }
                    if ((_edge & Edge.Right) != 0) width = Math.Max(MinWidth, _startRect.Width + dx);
                    if ((_edge & Edge.Top) != 0)
                    {
                        top = Math.Min(_startRect.Top + dy, _startRect.Bottom - MinHeight);
                        height = _startRect.Bottom - top;
                    }
                    if ((_edge & Edge.Bottom) != 0) height = Math.Max(MinHeight, _startRect.Height + dy);
                }
                else
                {
                    left = _startRect.Left + dx;
                    top = _startRect.Top + dy;
                }

                _window.Left = left;
                _window.Top = top;
                _window.Width = width;
                _window.Height = height;
            }
            catch (Exception ex) { Logger.Error(ex); }
        }

        void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging && !_resizing) return;
            _dragging = _resizing = false;
            try { _element.ReleaseMouseCapture(); } catch { }
            PersistGeometry();
            e.Handled = true;
        }

        static Cursor CursorFor(Edge edge)
        {
            switch (edge)
            {
                case Edge.Left:
                case Edge.Right: return Cursors.SizeWE;
                case Edge.Top:
                case Edge.Bottom: return Cursors.SizeNS;
                case Edge.Left | Edge.Top:
                case Edge.Right | Edge.Bottom: return Cursors.SizeNWSE;
                case Edge.Right | Edge.Top:
                case Edge.Left | Edge.Bottom: return Cursors.SizeNESW;
                default: return Cursors.SizeAll;
            }
        }

        void PersistGeometry()
        {
            _element.UpdateLayout();
            var hwnd = Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.RECT rect;
            if (!Native.GetWindowRect(hwnd, out rect)) return;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;
            var handler = GeometryChanged;
            if (handler != null) handler(rect.Left, rect.Top, width, height);
        }

        #endregion

        public void Dispose()
        {
            try
            {
                _element.MouseLeftButtonDown -= OnMouseDown;
                _element.MouseMove -= OnMouseMove;
                _element.MouseLeftButtonUp -= OnMouseUp;
            }
            catch { }
            try { _window.Close(); } catch { }
        }
    }
}
