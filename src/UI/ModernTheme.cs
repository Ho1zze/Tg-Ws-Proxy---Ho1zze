using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TgProxyHoizze.UI {
    public static class ModernTheme {
        // Black - Gray Aesthetic Colors (iOS / Bento Widget style)
        public static readonly Color BgDarkColor = Color.FromRgb(10, 10, 12);
        public static readonly Color CardBgColor = Color.FromRgb(21, 21, 25);
        public static readonly Color CardBgSecondaryColor = Color.FromRgb(28, 28, 34);
        public static readonly Color CardBgHoverColor = Color.FromRgb(34, 34, 42);
        public static readonly Color CardBorderColor = Color.FromRgb(38, 38, 46);
        public static readonly Color CardBorderSubtleColor = Color.FromRgb(30, 30, 36);
        public static readonly Color CardBorderHighlightColor = Color.FromRgb(58, 58, 70);

        public static readonly Color DockBgColor = Color.FromRgb(18, 18, 22);
        public static readonly Color DockBorderColor = Color.FromRgb(36, 36, 44);

        public static readonly Color TextPrimaryColor = Color.FromRgb(255, 255, 255);
        public static readonly Color TextSecondaryColor = Color.FromRgb(161, 161, 170);
        public static readonly Color TextMutedColor = Color.FromRgb(113, 113, 122);

        public static readonly Color GreenColor = Color.FromRgb(16, 185, 129);
        public static readonly Color GreenBgColor = Color.FromRgb(18, 45, 34);
        public static readonly Color GreenBorderColor = Color.FromRgb(28, 80, 58);

        public static readonly Color RedColor = Color.FromRgb(239, 68, 68);
        public static readonly Color RedBgColor = Color.FromRgb(48, 20, 24);
        public static readonly Color RedBorderColor = Color.FromRgb(85, 30, 38);

        public static readonly Color AccentGrayColor = Color.FromRgb(40, 40, 48);
        public static readonly Color AccentGrayHoverColor = Color.FromRgb(52, 52, 64);

        // Brushes
        public static readonly SolidColorBrush BgDark = FreezeBrush(new SolidColorBrush(BgDarkColor));
        public static readonly SolidColorBrush CardBg = FreezeBrush(new SolidColorBrush(CardBgColor));
        public static readonly SolidColorBrush CardBgSecondary = FreezeBrush(new SolidColorBrush(CardBgSecondaryColor));
        public static readonly SolidColorBrush CardBgHover = FreezeBrush(new SolidColorBrush(CardBgHoverColor));
        public static readonly SolidColorBrush CardBorder = FreezeBrush(new SolidColorBrush(CardBorderColor));
        public static readonly SolidColorBrush CardBorderSubtle = FreezeBrush(new SolidColorBrush(CardBorderSubtleColor));
        public static readonly SolidColorBrush CardBorderHighlight = FreezeBrush(new SolidColorBrush(CardBorderHighlightColor));

        public static readonly SolidColorBrush DockBg = FreezeBrush(new SolidColorBrush(DockBgColor));
        public static readonly SolidColorBrush DockBorder = FreezeBrush(new SolidColorBrush(DockBorderColor));

        public static readonly SolidColorBrush TextPrimary = FreezeBrush(new SolidColorBrush(TextPrimaryColor));
        public static readonly SolidColorBrush TextSecondary = FreezeBrush(new SolidColorBrush(TextSecondaryColor));
        public static readonly SolidColorBrush TextMuted = FreezeBrush(new SolidColorBrush(TextMutedColor));

        public static readonly SolidColorBrush Green = FreezeBrush(new SolidColorBrush(GreenColor));
        public static readonly SolidColorBrush GreenBg = FreezeBrush(new SolidColorBrush(GreenBgColor));
        public static readonly SolidColorBrush GreenBorder = FreezeBrush(new SolidColorBrush(GreenBorderColor));

        public static readonly SolidColorBrush Red = FreezeBrush(new SolidColorBrush(RedColor));
        public static readonly SolidColorBrush RedBg = FreezeBrush(new SolidColorBrush(RedBgColor));
        public static readonly SolidColorBrush RedBorder = FreezeBrush(new SolidColorBrush(RedBorderColor));

        public static readonly SolidColorBrush AccentGray = FreezeBrush(new SolidColorBrush(AccentGrayColor));
        public static readonly SolidColorBrush AccentGrayHover = FreezeBrush(new SolidColorBrush(AccentGrayHoverColor));

        private static SolidColorBrush FreezeBrush(SolidColorBrush brush) {
            brush.Freeze();
            return brush;
        }

        private static ImageSource _cachedLogo;

        public static ImageSource GetLogoSource() {
            if (_cachedLogo != null) return _cachedLogo;

            try {
                if (File.Exists("logo.png")) {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(Path.GetFullPath("logo.png"));
                    bi.EndInit();
                    bi.Freeze();
                    _cachedLogo = bi;
                    return _cachedLogo;
                }
            } catch { }

            try {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("logo.png")) {
                    if (stream != null) {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = stream;
                        bi.EndInit();
                        bi.Freeze();
                        _cachedLogo = bi;
                        return _cachedLogo;
                    }
                }
            } catch { }

            return null;
        }

        public static Border CreateCard(UIElement child, double padding = 16, double cornerRadius = 20, SolidColorBrush bg = null, SolidColorBrush border = null) {
            return new Border {
                Background = bg ?? CardBg,
                BorderBrush = border ?? CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(padding),
                Child = child,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        public static Button CreateButton(string text, SolidColorBrush bg, SolidColorBrush fg, double height = 40, double fontSize = 13, bool isBold = true, double cornerRadius = 14, SolidColorBrush border = null) {
            var btn = new Button {
                Content = text,
                Background = bg,
                Foreground = fg,
                Height = height,
                FontSize = fontSize,
                FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(16, 0, 16, 0),
                BorderThickness = new Thickness(border != null ? 1 : 0),
                BorderBrush = border
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            borderFactory.SetValue(Border.BackgroundProperty, bg);
            if (border != null) {
                borderFactory.SetValue(Border.BorderBrushProperty, border);
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            }

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;

            var triggerHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            triggerHover.Setters.Add(new Setter(Border.OpacityProperty, 0.86));
            template.Triggers.Add(triggerHover);

            var triggerPress = new Trigger { Property = Button.IsPressedProperty, Value = true };
            triggerPress.Setters.Add(new Setter(Border.OpacityProperty, 0.68));
            template.Triggers.Add(triggerPress);

            btn.Template = template;
            return btn;
        }

        public static Button CreateIconButton(string text, double size = 32, double cornerRadius = 16, SolidColorBrush bg = null, SolidColorBrush fg = null) {
            var btn = new Button {
                Content = text,
                Width = size,
                Height = size,
                Background = bg ?? CardBgSecondary,
                Foreground = fg ?? TextSecondary,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            borderFactory.SetValue(Border.BackgroundProperty, bg ?? CardBgSecondary);
            borderFactory.SetValue(Border.BorderBrushProperty, CardBorder);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;

            var triggerHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            triggerHover.Setters.Add(new Setter(Border.BackgroundProperty, CardBgHover));
            triggerHover.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            template.Triggers.Add(triggerHover);

            btn.Template = template;
            return btn;
        }

        public static TextBox CreateTextBox(string text, double height = 38, double cornerRadius = 12) {
            var tb = new TextBox {
                Text = text,
                Height = height,
                Background = CardBgSecondary,
                Foreground = TextPrimary,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                CaretBrush = TextPrimary
            };

            var template = new ControlTemplate(typeof(TextBox));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            borderFactory.SetValue(Border.BackgroundProperty, CardBgSecondary);
            borderFactory.SetValue(Border.BorderBrushProperty, CardBorder);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollFactory.Name = "PART_ContentHost";
            scrollFactory.SetValue(ScrollViewer.MarginProperty, new Thickness(0));
            scrollFactory.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(scrollFactory);

            template.VisualTree = borderFactory;

            var triggerFocus = new Trigger { Property = TextBox.IsFocusedProperty, Value = true };
            triggerFocus.Setters.Add(new Setter(Border.BorderBrushProperty, CardBorderHighlight, "border"));
            template.Triggers.Add(triggerFocus);

            tb.Template = template;
            return tb;
        }

        public static ScrollViewer WrapInScrollViewer(UIElement content) {
            var sv = new ScrollViewer {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };
            return sv;
        }
    }
}
