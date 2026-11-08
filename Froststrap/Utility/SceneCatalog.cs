using System.Globalization;

namespace Froststrap.Utility
{
    public enum SceneKind
    {
        Plain,
        Fluid,
        Silk,
        Nebula,
        Aurora,
        Lava,
        Caustics,
        Mesh
    }

    public enum SceneParticles
    {
        None,
        Stars,
        Embers,
        Drift,
        Bubbles
    }

    /// <summary>One selectable background. Colours are hex strings so the catalogue has no UI dependencies.</summary>
    public sealed record SceneSpec
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public SceneKind Kind { get; init; }

        /// <summary>Shader ramp: shadow, body, highlight, sheen.</summary>
        public string[] Colors { get; init; } = ["#000000", "#000000", "#000000", "#000000"];

        public float Scale { get; init; } = 1.0f;
        public float Speed { get; init; } = 1.0f;
        public float Detail { get; init; } = 1.0f;
        public float Contrast { get; init; } = 1.0f;

        public SceneParticles Particles { get; init; }
        public int ParticleCount { get; init; }
        public string ParticleColor { get; init; } = "#FFFFFF";
        public string ParticleAltColor { get; init; } = "#FFFFFF";

        public bool IsLight { get; init; }

        /// <summary>UI accent derived from the scene.</summary>
        public required string Accent { get; init; }

        /// <summary>Base colour the UI surfaces are tinted from.</summary>
        public required string Tint { get; init; }

        public bool IsAnimated => Kind != SceneKind.Plain;
    }

    public static class SceneCatalog
    {
        public const string DefaultId = "spectre";

        public static IReadOnlyList<SceneSpec> All { get; } =
        [
            new()
            {
                Id = "spectre", Name = "Spectre", Description = "Molten crimson silk",
                Kind = SceneKind.Fluid,
                Colors = ["#030001", "#2E0510", "#A8143B", "#FF9AB2"],
                Scale = 0.9f, Speed = 1.0f, Detail = 1.0f, Contrast = 0.9f,
                Particles = SceneParticles.Embers, ParticleCount = 36,
                ParticleColor = "#FDA4AF", ParticleAltColor = "#FB923C",
                Accent = "#E11D48", Tint = "#120709",
            },
            new()
            {
                Id = "obsidian", Name = "Obsidian", Description = "Liquid black chrome",
                Kind = SceneKind.Fluid,
                Colors = ["#020202", "#141416", "#5E5E66", "#FFFFFF"],
                Scale = 0.85f, Speed = 0.9f, Detail = 1.1f, Contrast = 1.1f,
                Accent = "#F2F2F2", Tint = "#0C0C0D",
            },
            new()
            {
                Id = "pearl", Name = "Pearl", Description = "Soft white marble",
                Kind = SceneKind.Fluid, IsLight = true,
                Colors = ["#A9A9B2", "#DCDCE2", "#F4F4F7", "#FFFFFF"],
                Scale = 0.85f, Speed = 0.8f, Detail = 1.0f, Contrast = 0.6f,
                Accent = "#18181B", Tint = "#EEEEF1",
            },
            new()
            {
                Id = "aurora", Name = "Aurora", Description = "Teal and violet currents",
                Kind = SceneKind.Fluid,
                Colors = ["#020B10", "#2A1F78", "#0E8C7C", "#9AF5E0"],
                Scale = 0.8f, Speed = 1.0f, Detail = 0.9f, Contrast = 0.8f,
                Accent = "#34D399", Tint = "#06171B",
            },
            new()
            {
                Id = "ember", Name = "Ember", Description = "Flowing fire and smoke",
                Kind = SceneKind.Fluid,
                Colors = ["#050100", "#3A1002", "#B8450B", "#FFD2A0"],
                Scale = 0.9f, Speed = 1.1f, Detail = 1.0f, Contrast = 0.9f,
                Particles = SceneParticles.Embers, ParticleCount = 30,
                ParticleColor = "#FDBA74", ParticleAltColor = "#FDE68A",
                Accent = "#F97316", Tint = "#140904",
            },
            new()
            {
                Id = "silk", Name = "Silk", Description = "Lavender satin folds",
                Kind = SceneKind.Silk,
                Colors = ["#0E0820", "#3A2667", "#B9A4F4", "#F6F2FF"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#C4B5FD", Tint = "#120C22",
            },
            new()
            {
                Id = "nebula", Name = "Nebula", Description = "Pink gas clouds and stars",
                Kind = SceneKind.Nebula,
                Colors = ["#05031A", "#3A1670", "#D6338A", "#FFD0EC"],
                Scale = 1.6f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Particles = SceneParticles.Stars, ParticleCount = 160,
                Accent = "#F472B6", Tint = "#0D0818",
            },
            new()
            {
                Id = "lavalamp", Name = "Lava Lamp", Description = "Slow glowing wax",
                Kind = SceneKind.Lava,
                Colors = ["#14061F", "#2D0B3E", "#F97316", "#FF4F8B"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#FB923C", Tint = "#150A1C",
            },
            new()
            {
                Id = "lagoon", Name = "Lagoon", Description = "Sunlight through shallow water",
                Kind = SceneKind.Caustics,
                Colors = ["#031A1F", "#0A4550", "#5EEAD4", "#E6FFFB"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#2DD4BF", Tint = "#06191C",
            },
            new()
            {
                Id = "borealis", Name = "Borealis", Description = "Northern lights over the night",
                Kind = SceneKind.Aurora,
                Colors = ["#030612", "#0A1430", "#1FE07A", "#9D4DFF"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Particles = SceneParticles.Stars, ParticleCount = 130,
                Accent = "#34D399", Tint = "#080C1C",
            },
            new()
            {
                Id = "ocean", Name = "Ocean", Description = "Deep blue caustics",
                Kind = SceneKind.Caustics,
                Colors = ["#010818", "#05224A", "#38BDF8", "#DDF4FF"],
                Scale = 0.8f, Speed = 0.8f, Detail = 1.0f, Contrast = 0.85f,
                Particles = SceneParticles.Bubbles, ParticleCount = 26, ParticleColor = "#BAE6FD",
                Accent = "#38BDF8", Tint = "#061020",
            },
            new()
            {
                Id = "cosmos", Name = "Cosmos", Description = "Blue galactic dust",
                Kind = SceneKind.Nebula,
                Colors = ["#01030C", "#0B2257", "#2F7BF5", "#BDF4FF"],
                Scale = 1.4f, Speed = 0.9f, Detail = 1.0f, Contrast = 0.9f,
                Particles = SceneParticles.Stars, ParticleCount = 180,
                Accent = "#60A5FA", Tint = "#070B18",
            },
            new()
            {
                Id = "sunset", Name = "Sunset", Description = "Warm drifting gradient",
                Kind = SceneKind.Mesh,
                Colors = ["#160A2C", "#7A1C4C", "#C2410C", "#B7791F"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#FB923C", Tint = "#170C1E",
            },
            new()
            {
                Id = "venom", Name = "Venom", Description = "Toxic green liquid",
                Kind = SceneKind.Fluid,
                Colors = ["#010401", "#082611", "#139443", "#C8FFD9"],
                Scale = 0.9f, Speed = 1.0f, Detail = 1.0f, Contrast = 0.9f,
                Accent = "#22C55E", Tint = "#07120A",
            },
            new()
            {
                Id = "gold", Name = "Gold", Description = "Molten gold leaf",
                Kind = SceneKind.Fluid,
                Colors = ["#040301", "#2A1B07", "#A87818", "#FFEDB8"],
                Scale = 0.85f, Speed = 0.85f, Detail = 1.1f, Contrast = 1.0f,
                Accent = "#F59E0B", Tint = "#120E07",
            },
            new()
            {
                Id = "rose", Name = "Rose", Description = "Pink satin",
                Kind = SceneKind.Silk,
                Colors = ["#1A0711", "#6E1B3D", "#FF9FB6", "#FFF1F4"],
                Scale = 1.0f, Speed = 0.9f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#FB7185", Tint = "#1A0B12",
            },
            new()
            {
                Id = "dusk", Name = "Dusk", Description = "Indigo and magenta haze",
                Kind = SceneKind.Mesh,
                Colors = ["#070A22", "#2C2A8C", "#9D1D6B", "#1F4FA8"],
                Scale = 1.0f, Speed = 0.9f, Detail = 1.0f, Contrast = 1.0f,
                Particles = SceneParticles.Stars, ParticleCount = 70,
                Accent = "#818CF8", Tint = "#0C0E22",
            },
            new()
            {
                Id = "smoke", Name = "Smoke", Description = "Slow grey vapour",
                Kind = SceneKind.Nebula,
                Colors = ["#060607", "#1E1E22", "#5A5A63", "#D6D6DC"],
                Scale = 1.3f, Speed = 0.7f, Detail = 1.0f, Contrast = 0.7f,
                Accent = "#D4D4D8", Tint = "#0D0D0F",
            },
            new()
            {
                Id = "mint", Name = "Mint", Description = "Light pastel gradient",
                Kind = SceneKind.Mesh, IsLight = true,
                Colors = ["#D9FBEF", "#C3D4FF", "#FFD6EC", "#FFF4C7"],
                Scale = 1.0f, Speed = 0.9f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#0F9F78", Tint = "#F1F6F5",
            },
            new()
            {
                Id = "starfield", Name = "Starfield", Description = "Deep space with parallax",
                Kind = SceneKind.Nebula,
                Colors = ["#000000", "#060A1A", "#1C2A5C", "#7C8CD6"],
                Scale = 1.2f, Speed = 0.6f, Detail = 1.0f, Contrast = 0.45f,
                Particles = SceneParticles.Drift, ParticleCount = 240, ParticleColor = "#E0E7FF",
                Accent = "#A5B4FC", Tint = "#06080F",
            },
            new()
            {
                Id = "glacier", Name = "Glacier", Description = "Icy blue meltwater",
                Kind = SceneKind.Fluid,
                Colors = ["#01050A", "#08192A", "#2A76A4", "#D6F3FF"],
                Scale = 0.85f, Speed = 0.8f, Detail = 1.0f, Contrast = 0.9f,
                Accent = "#7DD3FC", Tint = "#071018",
            },
            new()
            {
                Id = "amethyst", Name = "Amethyst", Description = "Liquid violet crystal",
                Kind = SceneKind.Fluid,
                Colors = ["#020106", "#150729", "#5B22B8", "#E2CCFF"],
                Scale = 0.85f, Speed = 0.9f, Detail = 1.0f, Contrast = 0.9f,
                Accent = "#A78BFA", Tint = "#0E0818",
            },
            new()
            {
                Id = "sapphire", Name = "Sapphire", Description = "Midnight blue satin",
                Kind = SceneKind.Silk,
                Colors = ["#040A1C", "#102A66", "#7AA2FF", "#EEF3FF"],
                Scale = 1.0f, Speed = 0.9f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#60A5FA", Tint = "#08101F",
            },
            new()
            {
                Id = "polar", Name = "Polar", Description = "Cyan and magenta aurora",
                Kind = SceneKind.Aurora,
                Colors = ["#05040F", "#140C2A", "#38E1F0", "#FF5CC8"],
                Scale = 1.0f, Speed = 0.9f, Detail = 1.0f, Contrast = 1.0f,
                Particles = SceneParticles.Stars, ParticleCount = 120,
                Accent = "#22D3EE", Tint = "#0A0818",
            },
            new()
            {
                Id = "inferno", Name = "Inferno", Description = "Burning gas and sparks",
                Kind = SceneKind.Nebula,
                Colors = ["#060100", "#3A0A04", "#E2541A", "#FFD08A"],
                Scale = 1.4f, Speed = 1.1f, Detail = 1.0f, Contrast = 1.0f,
                Particles = SceneParticles.Embers, ParticleCount = 34,
                ParticleColor = "#FDBA74", ParticleAltColor = "#FDE68A",
                Accent = "#F97316", Tint = "#140805",
            },
            new()
            {
                Id = "neonlamp", Name = "Neon Lamp", Description = "Cyan and magenta wax",
                Kind = SceneKind.Lava,
                Colors = ["#050816", "#0D1236", "#22D3EE", "#E879F9"],
                Scale = 1.0f, Speed = 1.0f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#22D3EE", Tint = "#0A0C1C",
            },
            new()
            {
                Id = "abyss", Name = "Abyss", Description = "Violet light in deep water",
                Kind = SceneKind.Caustics,
                Colors = ["#05020F", "#1A0B3A", "#8B5CF6", "#EDE9FE"],
                Scale = 0.9f, Speed = 0.8f, Detail = 1.0f, Contrast = 0.9f,
                Particles = SceneParticles.Bubbles, ParticleCount = 20, ParticleColor = "#DDD6FE",
                Accent = "#A78BFA", Tint = "#0B0716",
            },
            new()
            {
                Id = "sakura", Name = "Sakura", Description = "Light blush satin",
                Kind = SceneKind.Silk, IsLight = true,
                Colors = ["#D6A3B6", "#EFCFDB", "#FFB3C8", "#FFFFFF"],
                Scale = 1.0f, Speed = 0.8f, Detail = 1.0f, Contrast = 1.0f,
                Accent = "#DB2777", Tint = "#FBF1F4",
            },

            new() { Id = "graphite", Name = "Graphite", Description = "Plain neutral dark", Accent = "#FFFFFF", Tint = "#0F0F0F", Colors = ["#0F0F0F", "#141414", "#FFFFFF", "#FFFFFF"] },
            new() { Id = "oled", Name = "OLED", Description = "Pure black", Accent = "#FFFFFF", Tint = "#000000", Colors = ["#000000", "#0A0A0A", "#FFFFFF", "#FFFFFF"] },
            new() { Id = "midnight", Name = "Midnight", Description = "Plain deep blue", Accent = "#5B8DEF", Tint = "#0C1016", Colors = ["#0C1016", "#161D27", "#5B8DEF", "#E6EDF7"] },
            new() { Id = "nord", Name = "Nord", Description = "Plain arctic slate", Accent = "#88C0D0", Tint = "#2E3440", Colors = ["#2E3440", "#3B4252", "#88C0D0", "#ECEFF4"] },
            new() { Id = "paper", Name = "Paper", Description = "Plain light", IsLight = true, Accent = "#111111", Tint = "#FAFAFA", Colors = ["#FAFAFA", "#EDEDED", "#111111", "#111111"] },
            new() { Id = "daylight", Name = "Daylight", Description = "Light with blue accent", IsLight = true, Accent = "#2563EB", Tint = "#F7F8FA", Colors = ["#F7F8FA", "#E8EBF0", "#2563EB", "#2563EB"] },
        ];

        public static SceneSpec Get(string? id) =>
            All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? All.First(s => s.Id == DefaultId);

        /// <summary>Logo gradient for a scene: three colours the ribbon cycles through, then the sheen colour.</summary>
        public static string[] LogoColors(SceneSpec spec)
        {
            var accent = Parse(spec.Accent);

            if (spec.IsLight)
                return [Hex(Mix(accent, (0, 0, 0), 0.35)), spec.Accent, Hex(Mix(accent, (1, 1, 1), 0.3)), Hex(Mix(accent, (1, 1, 1), 0.65))];

            return [Hex(Lift(Parse(spec.Colors[1]))), Hex(Lift(Parse(spec.Colors[2]))), spec.Accent, spec.Colors[3]];
        }

        private static (double R, double G, double B) Parse(string hex)
        {
            int v = int.Parse(hex.TrimStart('#')[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
        }

        private static string Hex((double R, double G, double B) c) =>
            $"#{(int)Math.Round(Math.Clamp(c.R, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(c.G, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(c.B, 0, 1) * 255):X2}";

        private static (double R, double G, double B) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
            (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        /// <summary>Brightens very dark ramp colours so the logo never disappears into the background.</summary>
        private static (double R, double G, double B) Lift((double R, double G, double B) c)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            if (max >= 0.7)
                return c;
            if (max < 0.02)
                return (0.7, 0.7, 0.74);

            double k = Math.Min(0.7 / max, 8);
            return (c.R * k, c.G * k, c.B * k);
        }
    }
}
