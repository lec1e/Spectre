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
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(TintOpacity), 0.58);

        public static readonly StyledProperty<double> MaterialOpacityProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(MaterialOpacity), 0.46);

        public static readonly StyledProperty<double> VeilOpacityProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(VeilOpacity), 0.34);

        public static readonly StyledProperty<Color> TintColorProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, Color>(nameof(TintColor), Color.FromRgb(0x5C, 0x10, 0x1C));

        public static readonly StyledProperty<bool> GlassEnabledProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(GlassEnabled), true);

        public static readonly StyledProperty<bool> UseSettingsCornerRadiusProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(UseSettingsCornerRadius), false);

        private ExperimentalAcrylicBorder? _acrylic;
        private Border? _rim;
        private Border? _highlight;
        private Border? _veil;
        private Border? _solid;

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

        public double VeilOpacity
        {
            get => GetValue(VeilOpacityProperty);
            set => SetValue(VeilOpacityProperty, value);
        }

        public Color TintColor
        {
            get => GetValue(TintColorProperty);
            set => SetValue(TintColorProperty, value);
        }

        public bool GlassEnabled
        {
            get => GetValue(GlassEnabledProperty);
            set => SetValue(GlassEnabledProperty, value);
        }

        public bool UseSettingsCornerRadius
        {
            get => GetValue(UseSettingsCornerRadiusProperty);
            set => SetValue(UseSettingsCornerRadiusProperty, value);
        }

        public void SyncTintFromTheme()
        {
            TintColor = global::Froststrap.Utility.ThemeManager.CurrentGlassTint();
            ApplyFromSettings();
        }

        public void ApplyFromSettings()
        {
            var s = App.Settings?.Prop;
            if (s is null)
            {
                ApplyMaterial();
                return;
            }

            GlassEnabled = s.EnableGlass;
            TintColor = global::Froststrap.Utility.ThemeManager.CurrentGlassTint();
            TintOpacity = Math.Clamp(s.GlassTintOpacity, 0.05, 0.85);
            MaterialOpacity = Math.Clamp(s.GlassMaterialOpacity, 0.10, 0.90);
            if (UseSettingsCornerRadius)
                CornerRadius = new CornerRadius(s.GlassCornerRadius);
            else
                CornerRadius = default;
            VeilOpacity = s.EnableGlass ? 0.22 : 0.0;
            if (_highlight is not null)
                _highlight.Opacity = s.EnableGlow ? 0.22 : 0.08;
            ApplyMaterial();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _acrylic = e.NameScope.Find<ExperimentalAcrylicBorder>("PART_Acrylic");
            _rim = e.NameScope.Find<Border>("PART_Rim");
            _highlight = e.NameScope.Find<Border>("PART_Highlight");
            _veil = e.NameScope.Find<Border>("PART_Veil");
            _solid = e.NameScope.Find<Border>("PART_Solid");
            SyncTintFromTheme();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TintColorProperty ||
                change.Property == TintOpacityProperty ||
                change.Property == MaterialOpacityProperty ||
                change.Property == VeilOpacityProperty ||
                change.Property == GlassEnabledProperty ||
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
            if (_veil is not null)
            {
                _veil.CornerRadius = CornerRadius;
                _veil.Opacity = VeilOpacity;
            }
            if (_solid is not null)
                _solid.CornerRadius = CornerRadius;

            bool glass = GlassEnabled;
            _acrylic.IsVisible = glass;
            if (_solid is not null)
                _solid.IsVisible = !glass;

            var tint = TintColor;
            _acrylic.Material = new ExperimentalAcrylicMaterial
            {
                BackgroundSource = AcrylicBackgroundSource.Digger,
                TintColor = tint,
                TintOpacity = TintOpacity,
                MaterialOpacity = MaterialOpacity,
                FallbackColor = Color.FromArgb(0xE0, tint.R, tint.G, tint.B)
            };

            if (_solid is not null)
            {
                _solid.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(0xE0, tint.R, tint.G, tint.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0xF0, 0x14, 0x05, 0x08), 0.55),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0xFF, 0x05, 0x02, 0x03), 1)
                    ]
                };
            }
        }
    }
}
