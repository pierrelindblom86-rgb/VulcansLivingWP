using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using FluxWall.Common;

namespace FluxWall.UI
{
    /// <summary>Central dark theme palette + small control factory helpers.</summary>
    public static class Theme
    {
        public static readonly Color BaseBg      = From(0x111116);
        public static readonly Color PanelBg     = From(0x17171D);
        public static readonly Color CardBg      = From(0x1E1E26);
        public static readonly Color CardBgHover = From(0x262632);
        public static readonly Color Border      = From(0x2A2A34);
        public static readonly Color TextMain    = From(0xEDEBF2);
        public static readonly Color TextMuted   = From(0x9C99AB);
        public static readonly Color Accent      = From(0x7C5CFC);
        public static readonly Color AccentHover = From(0x8F74FF);
        public static readonly Color Good        = From(0x3FBF7F);
        public static readonly Color Warn        = From(0xE0A64B);
        public static readonly Color Bad         = From(0xE05768);
        public static readonly Color ChipBg      = From(0x2C2C38);

        public static Color From(int rgb) => Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

        public static SolidColorBrush Bru(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static Brush BaseBgB      { get { return Bru(BaseBg); } }
        public static Brush PanelBgB     { get { return Bru(PanelBg); } }
        public static Brush CardBgB      { get { return Bru(CardBg); } }
        public static Brush CardBgHoverB { get { return Bru(CardBgHover); } }
        public static Brush BorderB      { get { return Bru(Border); } }
        public static Brush TextB        { get { return Bru(TextMain); } }
        public static Brush TextMutedB   { get { return Bru(TextMuted); } }
        public static Brush AccentB      { get { return Bru(Accent); } }
        public static Brush AccentHoverB { get { return Bru(AccentHover); } }
        public static Brush GoodB        { get { return Bru(Good); } }
        public static Brush WarnB        { get { return Bru(Warn); } }
        public static Brush BadB         { get { return Bru(Bad); } }
        public static Brush ChipBgB      { get { return Bru(ChipBg); } }

        public static FontFamily Font => SystemFonts.MessageFontFamily;

        static ControlTemplate _buttonTemplate;

        /// <summary>
        /// Rounded button template with hover, pressed and disabled feedback.
        /// Replaces the flat default button look.
        /// </summary>
        public static ControlTemplate ButtonTemplate
        {
            get
            {
                if (_buttonTemplate == null)
                {
                    const string xaml =
                        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">" +
                        "<Border x:Name=\"bd\" CornerRadius=\"8\" SnapsToDevicePixels=\"True\" " +
                        "Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" " +
                        "BorderThickness=\"{TemplateBinding BorderThickness}\">" +
                        "<ContentPresenter Margin=\"{TemplateBinding Padding}\" " +
                        "HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
                        "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\" RecognizesAccessKey=\"True\"/>" +
                        "</Border>" +
                        "<ControlTemplate.Triggers>" +
                        "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"bd\" Property=\"Opacity\" Value=\"0.85\"/></Trigger>" +
                        "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"bd\" Property=\"Opacity\" Value=\"0.7\"/></Trigger>" +
                        "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter TargetName=\"bd\" Property=\"Opacity\" Value=\"0.45\"/></Trigger>" +
                        "</ControlTemplate.Triggers></ControlTemplate>";
                    _buttonTemplate = (ControlTemplate)XamlReader.Parse(xaml);
                }
                return _buttonTemplate;
            }
        }

        static ControlTemplate _sliderTemplate;

        /// <summary>
        /// Dark, slim slider. Built from a template because the stock one is a
        /// light-themed Windows control that would stand out badly here.
        /// </summary>
        public static ControlTemplate SliderTemplate
        {
            get
            {
                if (_sliderTemplate == null)
                {
                    const string xaml =
                        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Slider\">" +
                        "<Grid Height=\"22\" VerticalAlignment=\"Center\" Background=\"Transparent\">" +
                        "<Border Height=\"4\" CornerRadius=\"2\" Background=\"#2C2C38\" VerticalAlignment=\"Center\"/>" +
                        "<Track x:Name=\"PART_Track\" VerticalAlignment=\"Center\">" +
                        "<Track.DecreaseRepeatButton>" +
                        "<RepeatButton Command=\"{x:Static Slider.DecreaseLarge}\" Focusable=\"False\">" +
                        "<RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\">" +
                        "<Border Height=\"4\" CornerRadius=\"2\" Background=\"#7C5CFC\"/>" +
                        "</ControlTemplate></RepeatButton.Template></RepeatButton>" +
                        "</Track.DecreaseRepeatButton>" +
                        "<Track.IncreaseRepeatButton>" +
                        "<RepeatButton Command=\"{x:Static Slider.IncreaseLarge}\" Focusable=\"False\">" +
                        "<RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\">" +
                        "<Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template></RepeatButton>" +
                        "</Track.IncreaseRepeatButton>" +
                        "<Track.Thumb>" +
                        "<Thumb Width=\"14\" Height=\"14\">" +
                        "<Thumb.Template><ControlTemplate TargetType=\"Thumb\">" +
                        "<Grid><Ellipse Fill=\"#FFFFFF\" Width=\"13\" Height=\"13\"/>" +
                        "<Ellipse Fill=\"Transparent\" Stroke=\"#7C5CFC\" StrokeThickness=\"2\" Width=\"14\" Height=\"14\"/></Grid>" +
                        "</ControlTemplate></Thumb.Template></Thumb>" +
                        "</Track.Thumb></Track></Grid></ControlTemplate>";
                    _sliderTemplate = (ControlTemplate)XamlReader.Parse(xaml);
                }
                return _sliderTemplate;
            }
        }

        public static Slider DarkSlider(double min, double max, double value, Action<double> onChanged)
        {
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                SmallChange = 1,
                LargeChange = Math.Max(1, Math.Round((max - min) / 10)),
                IsMoveToPointEnabled = true,
                Height = 22,
                Background = Brushes.Transparent,
            };
            slider.Template = SliderTemplate;
            if (onChanged != null)
                slider.ValueChanged += (s, e) => onChanged(slider.Value);
            return slider;
        }

        /// <summary>Muted secondary button used for quiet actions.</summary>
        public static Button QuietButton(string text, Action onClick, Brush fg = null)
        {
            var b = new Button
            {
                Content = text,
                Foreground = fg ?? TextB,
                Background = Bru(From(0x23232D)),
                BorderBrush = BorderB,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            b.Template = ButtonTemplate;
            b.Click += (s, e) => { try { onClick?.Invoke(); } catch (Exception ex) { Logger.Error(ex); } };
            return b;
        }

        /// <summary>Primary violet action button.</summary>
        public static Button AccentButton(string text, Action onClick, double fontSize = 14, Thickness? padding = null)
        {
            var b = new Button
            {
                Content = text,
                Foreground = Brushes.White,
                Background = AccentB,
                BorderThickness = new Thickness(0),
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Padding = padding ?? new Thickness(18, 10, 18, 10),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            b.Template = ButtonTemplate;
            b.Click += (s, e) => { try { onClick?.Invoke(); } catch (Exception ex) { Logger.Error(ex); } };
            return b;
        }

        public static Button DangerButton(string text, Action onClick)
        {
            var b = QuietButton(text, onClick, Bru(Warn));
            return b;
        }

        public static TextBlock Label(string text) => new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = TextB,
            FontFamily = Font,
        };

        /// <summary>A row of a settings panel: label on the left, control on the right.</summary>
        public static Grid SettingsRow(string label, string hint, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var lab = Label(label);
            lab.FontSize = 14;
            lab.FontWeight = FontWeights.Medium;
            stack.Children.Add(lab);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Label(hint);
                h.Foreground = TextMutedB;
                h.FontSize = 12;
                h.Margin = new Thickness(0, 2, 8, 0);
                h.TextWrapping = TextWrapping.Wrap;
                stack.Children.Add(h);
            }
            g.Children.Add(stack);
            Grid.SetColumn(stack, 0);

            control.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(control);
            Grid.SetColumn(control, 1);
            return g;
        }
    }
}
