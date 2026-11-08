using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Base
{
    public abstract class AvaloniaWindow : Window
    {
        public AvaloniaWindow()
        {
            if (!OperatingSystem.IsMacOS())
            {
                this.WindowDecorations = WindowDecorations.None;
                this.ExtendClientAreaToDecorationsHint = true;
            }
            else
            {
                this.WindowDecorations = WindowDecorations.Full;
            }

            TransparencyLevelHint = SpectreChrome.Opaque;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
            ApplyTheme();
        }

        public static void ApplyTheme()
        {
            if (Application.Current == null) return;

            global::Froststrap.Utility.ThemeManager.CustomBackground =
                App.Settings.Prop.Theme == Enums.Theme.Custom ? BuildCustomBackground() : null;
            global::Froststrap.Utility.ThemeManager.ApplyFromSettings();
        }

        private static IBrush? BuildCustomBackground()
        {
            var settings = App.Settings.Prop;

            if (settings.BackgroundType == BackgroundMode.Gradient)
            {
                var stops = new Avalonia.Media.GradientStops();
                foreach (var s in settings.CustomGradientStops)
                {
                    if (Color.TryParse(s.Color, out var color))
                        stops.Add(new Avalonia.Media.GradientStop(color, s.Offset));
                }

                double angleRad = (Math.PI / 180.0) * (settings.GradientAngle - 90);
                return new LinearGradientBrush
                {
                    GradientStops = stops,
                    StartPoint = new RelativePoint(0.5 - Math.Cos(angleRad) * 0.5, 0.5 - Math.Sin(angleRad) * 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5 + Math.Cos(angleRad) * 0.5, 0.5 + Math.Sin(angleRad) * 0.5, RelativeUnit.Relative)
                };
            }

            if (settings.BackgroundType == BackgroundMode.Image)
            {
                string path = settings.BackgroundImagePath ?? string.Empty;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                try
                {
                    return new ImageBrush(new Bitmap(path))
                    {
                        Stretch = (Stretch)settings.BackgroundStretch,
                        Opacity = settings.BackgroundOpacity
                    };
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("AvaloniaWindow", $"Image load error: {ex.Message}");
                }
            }

            return null;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
#if QA_BUILD            
            this.BorderBrush = Brushes.Red;
            this.BorderThickness = new Thickness(4);
#endif

            Locale.ApplyLocaleToWindow(this);
            SpectreChrome.RoundCorners(this);
        }
    }
}
