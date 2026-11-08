using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Froststrap.Models;
using FontFamily = Avalonia.Media.FontFamily;
using GradientStop = Avalonia.Media.GradientStop;

namespace Froststrap.Utility
{
    /// <summary>
    /// Flat "workbench" theming. Every surface, line and text colour in the UI is a <c>Wb*</c>
    /// resource written here; the Fluent keys stock controls read are remapped to the same palette.
    /// </summary>
    public static class ThemeManager
    {
        public const string DefaultFontName = "Sora";

        public static event Action? ThemeChanged;

        /// <summary>Raised when only the animated backdrop (dim, motion) needs refreshing.</summary>
        public static event Action? BackdropChanged;

        public static void NotifyBackdropChanged() => BackdropChanged?.Invoke();

        public static IReadOnlyList<WorkbenchPalette> Palettes { get; } =
        [
            Dark("Graphite", "Neutral monochrome",
                deep: "#0A0A0A", canvas: "#0F0F0F", surface: "#111111", raised: "#1A1A1A", raisedHover: "#1F1F1F", overlay: "#141414",
                line: "#1C1C1C", lineStrong: "#282828",
                fg: "#CCCCCC", muted: "#A3A3A3", faint: "#808080", bright: "#FFFFFF",
                accent: "#FFFFFF", accentHover: "#E5E5E5", onAccent: "#000000"),
            Dark("Spectre", "Crimson accent",
                deep: "#0B0909", canvas: "#100D0E", surface: "#131011", raised: "#1C1718", raisedHover: "#221C1D", overlay: "#161213",
                line: "#211A1B", lineStrong: "#302627",
                fg: "#D4CBCC", muted: "#A89C9E", faint: "#85797B", bright: "#FFFFFF",
                accent: "#E11D48", accentHover: "#F43F5E", onAccent: "#FFFFFF"),
            Dark("OLED", "Pure black",
                deep: "#000000", canvas: "#000000", surface: "#050505", raised: "#101010", raisedHover: "#171717", overlay: "#0A0A0A",
                line: "#161616", lineStrong: "#232323",
                fg: "#CCCCCC", muted: "#A3A3A3", faint: "#7A7A7A", bright: "#FFFFFF",
                accent: "#FFFFFF", accentHover: "#E5E5E5", onAccent: "#000000"),
            Dark("Midnight", "Deep blue",
                deep: "#080B10", canvas: "#0C1016", surface: "#0F141B", raised: "#161D27", raisedHover: "#1B2330", overlay: "#121822",
                line: "#1A2230", lineStrong: "#263244",
                fg: "#C9D2DE", muted: "#98A4B5", faint: "#75818F", bright: "#FFFFFF",
                accent: "#60A5FA", accentHover: "#93C5FD", onAccent: "#06101C"),
            Dark("Nord", "Arctic slate",
                deep: "#1F232B", canvas: "#242933", surface: "#272C36", raised: "#2E3440", raisedHover: "#343B48", overlay: "#2A2F3A",
                line: "#323845", lineStrong: "#3B4252",
                fg: "#D8DEE9", muted: "#AEB6C4", faint: "#8790A0", bright: "#ECEFF4",
                accent: "#88C0D0", accentHover: "#8FBCBB", onAccent: "#1F232B"),
            Light("Paper", "Light neutral",
                deep: "#EDEDED", canvas: "#F5F5F5", surface: "#FFFFFF", raised: "#F0F0F0", raisedHover: "#E8E8E8", overlay: "#FFFFFF",
                line: "#E3E3E3", lineStrong: "#D2D2D2",
                fg: "#262626", muted: "#5C5C5C", faint: "#858585", bright: "#0A0A0A",
                accent: "#111111", accentHover: "#333333", onAccent: "#FFFFFF"),
            Light("Daylight", "Light with blue accent",
                deep: "#EBEEF2", canvas: "#F4F6F9", surface: "#FFFFFF", raised: "#EEF1F5", raisedHover: "#E5E9EF", overlay: "#FFFFFF",
                line: "#E0E4EA", lineStrong: "#CDD3DC",
                fg: "#1F2733", muted: "#566273", faint: "#7F8A99", bright: "#0B1220",
                accent: "#2563EB", accentHover: "#1D4ED8", onAccent: "#FFFFFF"),
        ];

        public static IReadOnlyList<string> FontNames { get; } = ["Sora", "Segoe UI", "Inter", "System"];

        public static WorkbenchPalette Current { get; private set; } = Palettes[0];

        /// <summary>UI palette for a background: plain scenes (and Spectre) use their tuned palette, the rest are derived from the scene's tint and accent.</summary>
        public static WorkbenchPalette FromScene(SceneSpec spec)
        {
            if (!spec.IsAnimated || spec.Id == SceneCatalog.DefaultId)
            {
                var named = Palettes.FirstOrDefault(p => string.Equals(p.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
                if (named is not null)
                    return named;
            }

            Color tint = Color.Parse(spec.Tint);
            Color accent = Color.Parse(spec.Accent);

            if (spec.IsLight)
            {
                return new WorkbenchPalette
                {
                    Name = spec.Name,
                    Description = spec.Description,
                    IsLight = true,
                    Deep = Lerp(tint, Colors.Black, 0.04),
                    Canvas = tint,
                    Surface = Lerp(tint, Colors.White, 0.7),
                    Raised = Lerp(tint, Colors.Black, 0.035),
                    RaisedHover = Lerp(tint, Colors.Black, 0.07),
                    Overlay = Lerp(tint, Colors.White, 0.8),
                    Line = Lerp(tint, Colors.Black, 0.09),
                    LineStrong = Lerp(tint, Colors.Black, 0.17),
                    Fg = Lerp(Color.Parse("#26262B"), accent, 0.08),
                    FgMuted = Lerp(Color.Parse("#5C5C66"), accent, 0.08),
                    FgFaint = Lerp(Color.Parse("#85858F"), accent, 0.06),
                    FgBright = Color.Parse("#0A0A0C"),
                    Accent = accent,
                    AccentHover = Lerp(accent, Colors.Black, 0.15),
                    OnAccent = Luminance(accent) > 0.6 ? Color.FromRgb(0x0A, 0x0A, 0x0A) : Colors.White
                };
            }

            return new WorkbenchPalette
            {
                Name = spec.Name,
                Description = spec.Description,
                Deep = Lerp(tint, Colors.Black, 0.3),
                Canvas = tint,
                Surface = Lerp(tint, Colors.White, 0.025),
                Raised = Lerp(tint, Colors.White, 0.065),
                RaisedHover = Lerp(tint, Colors.White, 0.095),
                Overlay = Lerp(tint, Colors.White, 0.045),
                Line = Lerp(tint, Colors.White, 0.075),
                LineStrong = Lerp(tint, Colors.White, 0.13),
                Fg = Lerp(Color.Parse("#D4D4D8"), accent, 0.07),
                FgMuted = Lerp(Color.Parse("#A1A1AA"), accent, 0.07),
                FgFaint = Lerp(Color.Parse("#7E7E87"), accent, 0.06),
                FgBright = Colors.White,
                Accent = accent,
                AccentHover = Lerp(accent, Colors.White, 0.18),
                OnAccent = Luminance(accent) > 0.55 ? Color.FromRgb(0x0A, 0x0A, 0x0A) : Colors.White
            };
        }

        /// <summary>Brush painted behind the shell when the user picked a custom gradient/image background.</summary>
        public static IBrush? CustomBackground { get; set; }

        /// <summary>True when panels are drawn translucent so the animated scene or custom background shows through.</summary>
        public static bool GlassActive { get; private set; }

        public static SceneSpec Scene { get; private set; } = SceneCatalog.Get(SceneCatalog.DefaultId);

        public static void ApplyFromSettings()
        {
            var settings = App.Settings?.Prop;
            if (settings is null)
                return;

            if (settings.BootstrapperTitle is "Eclipse" or "Eclipse-QA" or "Froststrap" or "Bloxstrap" or "FROSTBITE")
                settings.BootstrapperTitle = App.BrandName;

            if (settings.AppearanceRevision < 2)
            {
                settings.BackgroundId = SceneCatalog.DefaultId;
                settings.AppearanceRevision = 2;
            }

            Scene = SceneCatalog.Get(settings.BackgroundId);
            GlassActive = settings.GlassPanels && (CustomBackground is not null || Scene.IsAnimated);

            Apply(FromScene(Scene), settings.WorkbenchAccent, settings.WorkbenchFont);
        }

        public static void Apply(WorkbenchPalette palette, string? accentOverride, string? fontName)
        {
            if (Application.Current is null)
                return;

            Current = palette;
            var res = Application.Current.Resources;

            Color accent = palette.Accent;
            Color accentHover = palette.AccentHover;
            Color onAccent = palette.OnAccent;
            if (!string.IsNullOrWhiteSpace(accentOverride) && Color.TryParse(accentOverride.Trim(), out var custom))
            {
                accent = WithAlpha(custom, 0xFF);
                accentHover = Lerp(accent, palette.IsLight ? Colors.Black : Colors.White, 0.18);
                onAccent = Luminance(accent) > 0.55 ? Color.FromRgb(0x0A, 0x0A, 0x0A) : Colors.White;
            }

            Color ink = palette.IsLight ? Colors.Black : Colors.White;
            bool light = palette.IsLight;

            Color deep = palette.Deep, canvas = palette.Canvas, surface = palette.Surface;
            Color raised = palette.Raised, raisedHover = palette.RaisedHover;
            Color line = palette.Line, lineStrong = palette.LineStrong;
            if (GlassActive)
            {
                deep = WithAlpha(palette.Deep, light ? (byte)0xC4 : (byte)0x99);
                canvas = WithAlpha(palette.Canvas, light ? (byte)0x8C : (byte)0x40);
                surface = WithAlpha(palette.Surface, light ? (byte)0xB0 : (byte)0x7A);
                raised = WithAlpha(palette.Raised, light ? (byte)0xD0 : (byte)0xA8);
                raisedHover = WithAlpha(palette.RaisedHover, light ? (byte)0xE4 : (byte)0xC4);
                line = WithAlpha(ink, light ? (byte)0x18 : (byte)0x14);
                lineStrong = WithAlpha(ink, light ? (byte)0x26 : (byte)0x22);
            }

            Color fill = WithAlpha(ink, 0x08);
            Color hover = WithAlpha(ink, palette.IsLight ? (byte)0x0F : (byte)0x12);
            Color active = WithAlpha(ink, palette.IsLight ? (byte)0x14 : (byte)0x0B);
            Color lineSubtle = WithAlpha(ink, palette.IsLight ? (byte)0x12 : (byte)0x0F);
            Color lineFocus = WithAlpha(ink, palette.IsLight ? (byte)0x4D : (byte)0x38);
            Color focus = WithAlpha(ink, palette.IsLight ? (byte)0x80 : (byte)0x66);
            Color scrollbar = WithAlpha(palette.FgMuted, 0x38);
            Color accentSoft = WithAlpha(accent, palette.IsLight ? (byte)0x1A : (byte)0x24);

            Color success = palette.IsLight ? Color.Parse("#2F8A4A") : Color.Parse("#66A978");
            Color warning = palette.IsLight ? Color.Parse("#A86B12") : Color.Parse("#D2A15A");
            Color danger = palette.IsLight ? Color.Parse("#B23A31") : Color.Parse("#C95C54");
            Color online = palette.IsLight ? Color.Parse("#3B78A0") : Color.Parse("#7D9FB5");
            Color inGame = palette.IsLight ? Color.Parse("#2F8A4A") : Color.Parse("#70A981");
            Color studio = palette.IsLight ? Color.Parse("#9A7231") : Color.Parse("#B99A69");

            void Put(string key, Color color)
            {
                res[key + "Color"] = color;
                res[key + "Brush"] = new SolidColorBrush(color);
            }

            Put("WbDeep", deep);
            Put("WbCanvas", canvas);
            Put("WbSurface", surface);
            Put("WbRaised", raised);
            Put("WbRaisedHover", raisedHover);
            Put("WbOverlay", palette.Overlay);
            Put("WbFill", fill);
            Put("WbHover", hover);
            Put("WbActive", active);
            Put("WbFg", palette.Fg);
            Put("WbFgMuted", palette.FgMuted);
            Put("WbFgFaint", palette.FgFaint);
            Put("WbFgBright", palette.FgBright);
            Put("WbAccent", accent);
            Put("WbAccentHover", accentHover);
            Put("WbOnAccent", onAccent);
            Put("WbAccentSoft", accentSoft);
            Put("WbLineSubtle", lineSubtle);
            Put("WbLine", line);
            Put("WbLineStrong", lineStrong);
            Put("WbLineFocus", lineFocus);
            Put("WbFocus", focus);
            Put("WbScrollbar", scrollbar);
            Put("WbSuccess", success);
            Put("WbWarning", warning);
            Put("WbDanger", danger);
            Put("WbDangerSoft", WithAlpha(danger, 0x26));
            Put("WbOnline", online);
            Put("WbInGame", inGame);
            Put("WbStudio", studio);

            Put("WbSidebar", GlassActive ? WithAlpha(palette.Deep, light ? (byte)0xB8 : (byte)0x8C) : palette.Deep);
            Put("WbDock", GlassActive ? WithAlpha(palette.Overlay, light ? (byte)0xD8 : (byte)0xB8) : palette.Raised);
            Put("WbNavSelected", WithAlpha(ink, light ? (byte)0x12 : (byte)0x14));
            Put("WbTileIcon", WithAlpha(accent, light ? (byte)0x1F : (byte)0x2E));
            res["WbHeroBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(WithAlpha(accent, light ? (byte)0x2A : (byte)0x40), 0),
                    new GradientStop(WithAlpha(accent, light ? (byte)0x0A : (byte)0x10), 0.55),
                    new GradientStop(WithAlpha(accent, 0), 1),
                }
            };

            string[] logo = SceneCatalog.LogoColors(Scene);
            res["WbLogoBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = Scene.IsLight
                    ? [new GradientStop(Color.Parse(logo[0]), 0), new GradientStop(Color.Parse(logo[1]), 0.6), new GradientStop(Color.Parse(logo[2]), 1)]
                    : [new GradientStop(Color.Parse(logo[1]), 0), new GradientStop(Color.Parse(logo[2]), 0.6), new GradientStop(Color.Parse(logo[3]), 1)]
            };

            res["WbFontFamily"] = ResolveFont(fontName);

            IBrush window = CustomBackground ?? new SolidColorBrush(palette.Canvas);
            res["WbWindowBackgroundBrush"] = window;
            res["ApplicationBackgroundColor"] = window;
            res["PrimaryBackgroundColor"] = new SolidColorBrush(palette.Surface);
            res["NormalDarkAndLightBackground"] = new SolidColorBrush(palette.Surface);
            res["NotificationBackgroundColor"] = new SolidColorBrush(palette.Overlay);
            res["NewTextEditorBackground"] = new SolidColorBrush(palette.Deep);
            res["NewTextEditorForeground"] = new SolidColorBrush(palette.Fg);
            res["NewTextEditorLink"] = new SolidColorBrush(accent);
            res["CompletionListBackgroundBrush"] = new SolidColorBrush(palette.Overlay);

            // Legacy brand keys still read by bootstrapper dialogs and custom themes.
            res["BrandAccentColor"] = accent;
            res["BrandAccentBrush"] = new SolidColorBrush(accent);
            res["BrandInkBrush"] = new SolidColorBrush(palette.Deep);
            res["BrandSurfaceBrush"] = new SolidColorBrush(palette.Surface);
            res["BrandHairlineBrush"] = new SolidColorBrush(palette.LineStrong);
            res["BrandPurpleBrush"] = new SolidColorBrush(accentHover);
            res["BrandGradientBrush"] = new SolidColorBrush(accent);
            res["GlassHeaderBrush"] = new SolidColorBrush(palette.Deep);
            res["GlassRailBrush"] = new SolidColorBrush(palette.Surface);
            res["GlassStageBrush"] = new SolidColorBrush(palette.Canvas);
            res["GlassFillBrush"] = new SolidColorBrush(palette.Surface);
            res["GlassCardBrush"] = new SolidColorBrush(palette.Surface);
            res["GlassBorderBrush"] = new SolidColorBrush(palette.LineStrong);

            // Fluent keys used by stock control templates.
            Put("TextFillColorPrimary", palette.Fg);
            Put("TextFillColorSecondary", palette.FgMuted);
            Put("TextFillColorTertiary", palette.FgFaint);
            Put("TextFillColorDisabled", WithAlpha(palette.FgFaint, 0xA0));
            Put("TextOnAccentFillColorPrimary", onAccent);
            Put("TextOnAccentFillColorSecondary", WithAlpha(onAccent, 0xB3));
            Put("AccentFillColorDefault", accent);
            Put("AccentFillColorSecondary", accentHover);
            Put("AccentFillColorTertiary", WithAlpha(accent, 0xCC));
            Put("AccentTextFillColorPrimary", accent);
            Put("ControlFillColorDefault", raised);
            Put("ControlFillColorSecondary", raisedHover);
            Put("ControlFillColorTertiary", palette.Overlay);
            Put("ControlFillColorDisabled", WithAlpha(palette.Raised, 0x80));
            Put("ControlFillColorInputActive", deep);
            Put("ControlStrokeColorDefault", lineStrong);
            Put("ControlStrokeColorSecondary", lineStrong);
            Put("ControlStrongStrokeColorDefault", palette.FgFaint);
            Put("CardBackgroundFillColorDefault", surface);
            Put("CardBackgroundFillColorSecondary", raised);
            Put("CardStrokeColorDefault", line);
            Put("SolidBackgroundFillColorBase", palette.Canvas);
            Put("SolidBackgroundFillColorSecondary", palette.Surface);
            Put("SolidBackgroundFillColorTertiary", palette.Raised);
            Put("SolidBackgroundFillColorQuarternary", palette.RaisedHover);
            Put("LayerFillColorDefault", palette.Surface);
            Put("SubtleFillColorSecondary", hover);
            Put("SubtleFillColorTertiary", active);
            Put("DividerStrokeColorDefault", line);
            Put("SurfaceStrokeColorFlyout", palette.LineStrong);
            Put("SystemFillColorSuccess", success);
            Put("SystemFillColorCaution", warning);
            Put("SystemFillColorCritical", danger);
            res["ControlElevationBorderBrush"] = new SolidColorBrush(lineStrong);
            res["TextControlElevationBorderBrush"] = new SolidColorBrush(lineStrong);
            res["TextControlElevationBorderFocusedBrush"] = new SolidColorBrush(lineFocus);
            res["CircleElevationBorderBrush"] = new SolidColorBrush(palette.LineStrong);
            res["FlyoutPresenterBackground"] = new SolidColorBrush(palette.Overlay);
            void Brush(string key, Color color) => res[key] = new SolidColorBrush(color);

            // NavigationView aliases are StaticResources in the FA theme, so they must be overridden directly.
            Brush("NavigationViewDefaultPaneBackground", palette.Deep);
            Brush("NavigationViewExpandedPaneBackground", palette.Deep);
            Brush("NavigationViewTopPaneBackground", palette.Deep);
            Brush("NavigationViewContentBackground", palette.Canvas);
            Brush("NavigationViewContentGridBorderBrush", palette.Line);
            Brush("NavigationViewItemSeparatorForeground", palette.Line);
            Brush("NavigationViewItemHeaderForeground", palette.FgFaint);
            Brush("NavigationViewSelectionIndicatorForeground", accent);
            foreach (string state in new[] { "", "Checked" })
            {
                Brush($"NavigationViewItemBackground{state}", Colors.Transparent);
                Brush($"NavigationViewItemBackground{state}PointerOver", hover);
                Brush($"NavigationViewItemBackground{state}Pressed", active);
                Brush($"NavigationViewItemBorderBrush{state}", Colors.Transparent);
                Brush($"NavigationViewItemBorderBrush{state}PointerOver", hover);
                Brush($"NavigationViewItemBorderBrush{state}Pressed", active);
            }
            Brush("NavigationViewItemBackgroundSelected", active);
            Brush("NavigationViewItemBackgroundSelectedPointerOver", active);
            Brush("NavigationViewItemBackgroundSelectedPressed", hover);
            Brush("NavigationViewItemBorderBrushSelected", active);
            Brush("NavigationViewItemBorderBrushSelectedPointerOver", active);
            Brush("NavigationViewItemBorderBrushSelectedPressed", hover);
            foreach (string state in new[] { "", "Checked", "Selected" })
            {
                Brush($"NavigationViewItemForeground{state}", palette.FgMuted);
                Brush($"NavigationViewItemForeground{state}PointerOver", palette.Fg);
                Brush($"NavigationViewItemForeground{state}Pressed", palette.FgMuted);
                Brush($"NavigationViewItemForeground{state}Disabled", WithAlpha(palette.FgFaint, 0xA0));
            }
            Brush("NavigationViewItemForegroundSelected", palette.FgBright);
            Brush("NavigationViewItemForegroundSelectedPointerOver", palette.FgBright);
            Brush("NavigationViewButtonBackgroundPointerOver", hover);
            Brush("NavigationViewButtonBackgroundPressed", active);
            Brush("NavigationViewButtonForegroundPointerOver", palette.Fg);
            Brush("NavigationViewButtonForegroundPressed", palette.FgMuted);

            res["SystemAccentColor"] = accent;
            res["SystemAccentColorLight1"] = accentHover;
            res["SystemAccentColorDark1"] = Lerp(accent, Colors.Black, 0.15);

            Application.Current.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

            var faTheme = Application.Current.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            if (faTheme is not null)
            {
                faTheme.PreferSystemTheme = false;
                faTheme.PreferUserAccentColor = false;
                faTheme.CustomAccentColor = accent;

                // FA's accent brushes resolve these from its own dictionary first, and its generated ramp lightens the fill.
                var faRes = faTheme.Resources;
                faRes["SystemAccentColor"] = accent;
                faRes["SystemAccentColorLight1"] = accentHover;
                faRes["SystemAccentColorLight2"] = accent;
                faRes["SystemAccentColorLight3"] = Lerp(accent, Colors.White, 0.35);
                faRes["SystemAccentColorDark1"] = accent;
                faRes["SystemAccentColorDark2"] = Lerp(accent, Colors.Black, 0.18);
                faRes["SystemAccentColorDark3"] = Lerp(accent, Colors.Black, 0.32);
            }

            ThemeChanged?.Invoke();
        }

        private static FontFamily ResolveFont(string? name) => name switch
        {
            "Segoe UI" => new FontFamily("Segoe UI Variable Text, Segoe UI, Inter, sans-serif"),
            "Inter" => new FontFamily("Inter, Segoe UI, sans-serif"),
            "System" => FontFamily.Default,
            _ => new FontFamily("avares://Spectre/Resources/Fonts/Sora-VariableFont_wght.ttf#Sora")
        };

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
            0xFF,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static WorkbenchPalette Dark(string name, string description,
            string deep, string canvas, string surface, string raised, string raisedHover, string overlay,
            string line, string lineStrong, string fg, string muted, string faint, string bright,
            string accent, string accentHover, string onAccent)
            => Make(name, description, false, deep, canvas, surface, raised, raisedHover, overlay,
                line, lineStrong, fg, muted, faint, bright, accent, accentHover, onAccent);

        private static WorkbenchPalette Light(string name, string description,
            string deep, string canvas, string surface, string raised, string raisedHover, string overlay,
            string line, string lineStrong, string fg, string muted, string faint, string bright,
            string accent, string accentHover, string onAccent)
            => Make(name, description, true, deep, canvas, surface, raised, raisedHover, overlay,
                line, lineStrong, fg, muted, faint, bright, accent, accentHover, onAccent);

        private static WorkbenchPalette Make(string name, string description, bool isLight,
            string deep, string canvas, string surface, string raised, string raisedHover, string overlay,
            string line, string lineStrong, string fg, string muted, string faint, string bright,
            string accent, string accentHover, string onAccent) => new()
        {
            Name = name,
            Description = description,
            IsLight = isLight,
            Deep = Color.Parse(deep),
            Canvas = Color.Parse(canvas),
            Surface = Color.Parse(surface),
            Raised = Color.Parse(raised),
            RaisedHover = Color.Parse(raisedHover),
            Overlay = Color.Parse(overlay),
            Line = Color.Parse(line),
            LineStrong = Color.Parse(lineStrong),
            Fg = Color.Parse(fg),
            FgMuted = Color.Parse(muted),
            FgFaint = Color.Parse(faint),
            FgBright = Color.Parse(bright),
            Accent = Color.Parse(accent),
            AccentHover = Color.Parse(accentHover),
            OnAccent = Color.Parse(onAccent)
        };
    }
}
