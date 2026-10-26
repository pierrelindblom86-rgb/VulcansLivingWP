using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FluxWall.Common;

namespace FluxWall.UI
{
    /// <summary>A rounded switch, styled for the dark theme. Drop-in for settings rows.</summary>
    public class ToggleSwitch : Border
    {
        readonly Ellipse _knob;

        public ToggleSwitch()
        {
            Width = 44; Height = 24;
            CornerRadius = new CornerRadius(12);
            Background = Theme.Bru(Theme.ChipBg);
            BorderBrush = Theme.BorderB;
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;

            _knob = new Ellipse
            {
                Width = 16, Height = 16,
                Fill = Theme.TextMutedB,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            Child = new Grid { Children = { _knob } };

            MouseLeftButtonUp += OnClicked;
            RenderIsOn();
        }

        void OnClicked(object sender, MouseButtonEventArgs e)
        {
            IsOn = !IsOn;
            var t = Toggled; if (t != null) t();
        }

        public bool IsOn
        {
            get { return _isOn; }
            set { _isOn = value; RenderIsOn(); }
        }

        bool _isOn;

        public event Action Toggled;

        void RenderIsOn()
        {
            Background = _isOn ? Theme.AccentB : Theme.Bru(Theme.ChipBg);
            _knob.Fill = _isOn ? Theme.Bru(Colors.White) : Theme.TextMutedB;
            var m = new Thickness(_isOn ? 24 : 4, 0, 0, 0);
            _knob.BeginAnimation(MarginProperty, new ThicknessAnimation
            {
                To = m,
                Duration = new Duration(TimeSpan.FromMilliseconds(120)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            });
        }
    }
}
