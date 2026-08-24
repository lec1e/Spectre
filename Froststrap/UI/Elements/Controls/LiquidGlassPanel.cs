using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Dark frosted glass shell only — no animated background blobs.
    /// Cursor liquid effect lives in <see cref="LiquidCursorOverlay"/>.
    /// </summary>
    public class LiquidGlassPanel : ContentControl
    {
        public static readonly StyledProperty<double> TintOpacityProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(TintOpacity), 0.95);

        public static readonly StyledProperty<double> MaterialOpacityProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(MaterialOpacity), 0.97);

        public static readonly StyledProperty<Color> TintColorProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, Color>(nameof(TintColor), Color.FromRgb(0x0A, 0x06, 0x12));

        private ExperimentalAcrylicBorder? _acrylic;
        private Border? _rim;
        private Border? _highlight;

        public double TintOpacity
        {
            get => GetValue(TintOpacityProperty);
            set => SetValue(TintOpacityProperty, value);
        }

        public double MaterialOpacity
        {
            get => GetValue(MaterialOpacityProperty);
            set => SetValue(MaterialOpacityProperty, value);
        }

        public Color TintColor
        {
            get => GetValue(TintColorProperty);
            set => SetValue(TintColorProperty, value);
        }

        public void SyncTintFromTheme()
        {
            if (Application.Current?.Resources.TryGetValue("BrandInkColor", out var v) == true && v is Color ink)
                TintColor = ink;
            ApplyMaterial();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _acrylic = e.NameScope.Find<ExperimentalAcrylicBorder>("PART_Acrylic");
            _rim = e.NameScope.Find<Border>("PART_Rim");
            _highlight = e.NameScope.Find<Border>("PART_Highlight");
            SyncTintFromTheme();
            ApplyMaterial();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TintColorProperty ||
                change.Property == TintOpacityProperty ||
                change.Property == MaterialOpacityProperty ||
                change.Property == CornerRadiusProperty)
            {
                ApplyMaterial();
            }
        }

        private void ApplyMaterial()
        {
            if (_acrylic is null)
                return;

            _acrylic.CornerRadius = CornerRadius;
            if (_rim is not null) _rim.CornerRadius = CornerRadius;
            if (_highlight is not null) _highlight.CornerRadius = CornerRadius;

            var tint = TintColor;
            _acrylic.Material = new ExperimentalAcrylicMaterial
            {
                BackgroundSource = AcrylicBackgroundSource.Digger,
                TintColor = tint,
                TintOpacity = TintOpacity,
                MaterialOpacity = MaterialOpacity,
                FallbackColor = Color.FromArgb(0xF4, tint.R, tint.G, tint.B)
            };
        }
    }
}
