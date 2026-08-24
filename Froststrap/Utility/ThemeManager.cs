using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Froststrap.Models;

namespace Froststrap.Utility
{
    public static class ThemeManager
    {
        public static event Action? ThemeChanged;

        public static readonly Dictionary<string, ThemePalette> Presets = new()
        {
            ["Eclipse"] = P("#C084FC", "#A855F7", "#22D3EE", "#E879F9", "#A855F7", "#07060C", "#12101A", "#2E2640"),
            ["Purple Haze"] = P("#A855F7", "#A855F7", "#22D3EE", "#EC4899", "#A855F7", "#0A0614", "#16101F", "#3B2A55"),
            ["Midnight"] = P("#818CF8", "#6366F1", "#22D3EE", "#A78BFA", "#6366F1", "#05060F", "#0F1220", "#2A2F4A"),
            ["Obsidian"] = P("#A1A1AA", "#71717A", "#D4D4D8", "#A78BFA", "#71717A", "#09090B", "#18181B", "#3F3F46"),
            ["Blood Red"] = P("#FF4D4D", "#FF4D4D", "#FF9838", "#FB7185", "#FF4D4D", "#0F0A0A", "#1C1212", "#4A2020"),
            ["Crimson"] = P("#EF4444", "#DC2626", "#F97316", "#FB7185", "#EF4444", "#120808", "#1F1010", "#5B2121"),
            ["Rose"] = P("#FB7185", "#F43F5E", "#F472B6", "#FDA4AF", "#F43F5E", "#12080C", "#1F1218", "#5B2840"),
            ["Ocean"] = P("#38BDF8", "#38BDF8", "#818CF8", "#818CF8", "#38BDF8", "#0A0F14", "#121821", "#1E3A4A"),
            ["Abyss"] = P("#22D3EE", "#06B6D4", "#3B82F6", "#67E8F9", "#06B6D4", "#040B10", "#0B1520", "#164E63"),
            ["Arctic"] = P("#7DD3FC", "#38BDF8", "#E0F2FE", "#A5F3FC", "#38BDF8", "#071018", "#0F1C28", "#1E4055"),
            ["Emerald"] = P("#34D399", "#34D399", "#A3E635", "#22D3EE", "#34D399", "#0A0F0C", "#121C16", "#1F3D2E"),
            ["Forest"] = P("#4ADE80", "#22C55E", "#86EFAC", "#A3E635", "#22C55E", "#07140C", "#102016", "#245C3B"),
            ["Mint"] = P("#2DD4BF", "#14B8A6", "#5EEAD4", "#99F6E4", "#14B8A6", "#071412", "#10201C", "#115E59"),
            ["Sunset"] = P("#FB923C", "#FB923C", "#F472B6", "#F472B6", "#FB923C", "#0F0A0C", "#1C1418", "#5B3A28"),
            ["Amber"] = P("#FBBF24", "#F59E0B", "#F97316", "#FDE68A", "#F59E0B", "#120E06", "#1F1A0C", "#5B4A18"),
            ["Neon"] = P("#D946EF", "#A855F7", "#22D3EE", "#F0ABFC", "#D946EF", "#0A0510", "#160A22", "#4A1D6A"),
            ["Cyber"] = P("#22D3EE", "#06B6D4", "#A855F7", "#67E8F9", "#22D3EE", "#050B10", "#0A1520", "#155E75"),
            ["Vapor"] = P("#E879F9", "#C026D3", "#22D3EE", "#F0ABFC", "#E879F9", "#0C0612", "#1A0C24", "#6B2180"),
            ["Lavender"] = P("#C4B5FD", "#A78BFA", "#DDD6FE", "#E9D5FF", "#A78BFA", "#0C0A14", "#181422", "#3B2F55"),
            ["Indigo"] = P("#818CF8", "#4F46E5", "#6366F1", "#A5B4FC", "#4F46E5", "#080814", "#12122A", "#312E81"),
            ["Sky"] = P("#38BDF8", "#0EA5E9", "#7DD3FC", "#BAE6FD", "#0EA5E9", "#061018", "#0E1C28", "#075985"),
            ["Mono"] = P("#E5E7EB", "#E5E7EB", "#9CA3AF", "#9CA3AF", "#E5E7EB", "#0B0B0B", "#161616", "#3F3F46"),
            ["Steel"] = P("#94A3B8", "#64748B", "#CBD5E1", "#E2E8F0", "#64748B", "#0A0C10", "#151922", "#334155"),
            ["Gold"] = P("#FBBF24", "#EAB308", "#FDE68A", "#FCD34D", "#EAB308", "#100E06", "#1C180C", "#713F12"),
            ["Copper"] = P("#F59E0B", "#D97706", "#FB923C", "#FDBA74", "#D97706", "#120C08", "#1F1610", "#7C2D12"),
            ["Cherry"] = P("#F472B6", "#DB2777", "#FB7185", "#F9A8D4", "#DB2777", "#12080E", "#1F1018", "#9D174D"),
            ["Grape"] = P("#A78BFA", "#7C3AED", "#C4B5FD", "#DDD6FE", "#7C3AED", "#0A0614", "#160F24", "#5B21B6"),
            ["Teal"] = P("#2DD4BF", "#0D9488", "#5EEAD4", "#99F6E4", "#0D9488", "#061210", "#0E1F1C", "#134E4A"),
            ["Lime"] = P("#A3E635", "#84CC16", "#BEF264", "#D9F99D", "#84CC16", "#0A1006", "#141C0C", "#3F6212"),
            ["Ice"] = P("#E0F2FE", "#7DD3FC", "#F0F9FF", "#BAE6FD", "#7DD3FC", "#080C10", "#121820", "#1E3A5F"),
        };

        private static ThemePalette P(
            string accent, string gStart, string gEnd, string purple, string glow,
            string bg, string surface, string hairline) => new()
        {
            Accent = accent,
            GradientStart = gStart,
            GradientEnd = gEnd,
            Purple = purple,
            Glow = glow,
            Background = bg,
            Surface = surface,
            Hairline = hairline
        };

        private static string _lastSignature = "";

        public static IReadOnlyList<string> PresetNames { get; } = Presets.Keys.OrderBy(k => k == "Eclipse" ? "" : k).ToList();

        public static Color Parse(string? hex, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var color))
                return color;
            return fallback;
        }

        public static void ApplyFromSettings()
        {
            var settings = App.Settings?.Prop;
            if (settings is null)
                return;

            ThemePalette palette = settings.Palette ?? new ThemePalette();

            if (!string.IsNullOrEmpty(settings.SelectedThemePreset)
                && Presets.TryGetValue(settings.SelectedThemePreset, out var preset)
                && settings.SelectedThemePreset != "Custom")
            {
                palette = preset.Clone();
                settings.Palette = palette;
            }

            string signature = Signature(palette);
            if (signature == _lastSignature)
                return;

            Apply(palette);
            _lastSignature = signature;
        }

        public static void ApplyPreset(string name)
        {
            if (!Presets.TryGetValue(name, out var preset))
                return;

            App.Settings.Prop.SelectedThemePreset = name;
            App.Settings.Prop.Palette = preset.Clone();
            Apply(App.Settings.Prop.Palette);
            _lastSignature = Signature(App.Settings.Prop.Palette);
        }

        public static void Apply(ThemePalette p)
        {
            if (Application.Current is null)
                return;

            var res = Application.Current.Resources;

            Color accent = Parse(p.Accent, Color.FromRgb(0xA8, 0x55, 0xF7));
            Color gStart = Parse(p.GradientStart, accent);
            Color gEnd = Parse(p.GradientEnd, Color.FromRgb(0x7C, 0x3A, 0xED));
            Color purple = Parse(p.Purple, Color.FromRgb(0xC0, 0x84, 0xFC));
            Color ink = Parse(p.Background, Color.FromRgb(0x0A, 0x0A, 0x0F));
            Color surface = Parse(p.Surface, Color.FromRgb(0x14, 0x12, 0x1C));
            Color hairline = Parse(p.Hairline, Color.FromRgb(0x2A, 0x24, 0x38));
            Color glow = Parse(p.Glow, accent);

            bool glass = App.Settings?.Prop?.EnableGlass ?? true;

            res["BrandAccentColor"] = accent;
            res["BrandPurpleColor"] = purple;
            res["BrandInkColor"] = ink;
            res["BrandSurfaceColor"] = surface;
            res["BrandHairlineColor"] = hairline;
            res["BrandGlowColor"] = glow;
            res["BrandGradientEnd"] = gEnd;

            res["BrandAccentBrush"] = new SolidColorBrush(accent);
            res["BrandPurpleBrush"] = new SolidColorBrush(purple);
            res["BrandInkBrush"] = new SolidColorBrush(ink);
            res["BrandSurfaceBrush"] = new SolidColorBrush(surface);
            res["BrandHairlineBrush"] = new SolidColorBrush(hairline);

            // Glass fills follow the active palette ink (so presets actually change the shell)
            res["GlassFillBrush"] = glass
                ? new SolidColorBrush(Color.FromArgb(0xE8, ink.R, ink.G, ink.B))
                : new SolidColorBrush(surface);
            res["GlassRailBrush"] = glass
                ? new SolidColorBrush(Color.FromArgb(0xF0, ink.R, ink.G, ink.B))
                : new SolidColorBrush(ink);
            res["GlassHeaderBrush"] = glass
                ? new SolidColorBrush(Color.FromArgb(0xF2, ink.R, ink.G, ink.B))
                : new SolidColorBrush(Color.FromArgb(0xEE, ink.R, ink.G, ink.B));
            res["GlassStageBrush"] = glass
                ? new SolidColorBrush(Color.FromArgb(0xE0, ink.R, ink.G, ink.B))
                : new SolidColorBrush(surface);
            res["GlassCardBrush"] = glass
                ? new SolidColorBrush(Color.FromArgb(0xCC, surface.R, surface.G, surface.B))
                : new SolidColorBrush(surface);
            res["GlassBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x88, purple.R, purple.G, purple.B));

            res["BrandGradientBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new Avalonia.Media.GradientStop(gStart, 0),
                    new Avalonia.Media.GradientStop(purple, 0.45),
                    new Avalonia.Media.GradientStop(gEnd, 1)
                ]
            };

            var faTheme = Application.Current.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            if (faTheme is not null)
            {
                faTheme.CustomAccentColor = accent;
                faTheme.PreferUserAccentColor = false;
            }

            if (App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark)
            {
                res["ApplicationBackgroundColor"] = new SolidColorBrush(ink);
                res["PrimaryBackgroundColor"] = new SolidColorBrush(Color.FromArgb(0xCC, ink.R, ink.G, ink.B));
            }

            ThemeChanged?.Invoke();
        }

        private static string Signature(ThemePalette p)
        {
            var s = App.Settings?.Prop;
            return string.Join("|", p.Accent, p.GradientStart, p.GradientEnd, p.Purple, p.Background,
                p.Surface, p.Hairline, p.Glow, s?.EnableAurora, s?.EnableGlass, s?.EnableGlow,
                s?.EnableLiquidCursor, s?.SelectedThemePreset);
        }
    }
}
