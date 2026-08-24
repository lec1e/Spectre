using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Deep-ocean abyss backdrop: hadal ink, drifting bioluminescent gleams,
    /// and twinkling plankton lights (inspired by abyssal / bioluminescent UI motifs).
    /// Rendered inside the glass shell so it is actually visible.
    /// </summary>
    public class AbyssGlowBackground : Control
    {
        private const int PlanktonCount = 55;
        private const int GleamCount = 7;

        private readonly Gleam[] _gleams = new Gleam[GleamCount];
        private readonly Plankton[] _plankton = new Plankton[PlanktonCount];
        private readonly Random _rng = new(42);

        private DispatcherTimer? _timer;
        private float _time;
        private bool _enabled = true;

        // Hadal deep-ocean palette
        private Color _void = Color.FromRgb(0x02, 0x05, 0x10);
        private Color _deep = Color.FromRgb(0x06, 0x0E, 0x1C);
        private Color _cyan = Color.FromRgb(0x46, 0xF0, 0xD9);
        private Color _blue = Color.FromRgb(0x6A, 0xB8, 0xFF);
        private Color _violet = Color.FromRgb(0x88, 0x55, 0xFF);
        private Color _eclipse = Color.FromRgb(0xC0, 0x84, 0xFC);

        public AbyssGlowBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            InitParticles();

            Loaded += (_, _) =>
            {
                SyncFromTheme();
                SyncFromSettings();
                Start();
            };
            Unloaded += (_, _) => Stop();
        }

        private void InitParticles()
        {
            for (int i = 0; i < GleamCount; i++)
            {
                _gleams[i] = new Gleam
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Radius = 0.18f + (float)_rng.NextDouble() * 0.22f,
                    SpeedX = 0.04f + (float)_rng.NextDouble() * 0.08f,
                    SpeedY = 0.03f + (float)_rng.NextDouble() * 0.06f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Pulse = 0.6f + (float)_rng.NextDouble() * 0.8f,
                    Kind = i % 4
                };
            }

            for (int i = 0; i < PlanktonCount; i++)
            {
                _plankton[i] = new Plankton
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Size = 0.8f + (float)_rng.NextDouble() * 2.2f,
                    Twinkle = 0.8f + (float)_rng.NextDouble() * 2.5f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Drift = 0.01f + (float)_rng.NextDouble() * 0.025f,
                    Kind = i % 3
                };
            }
        }

        public void SyncFromSettings()
        {
            _enabled = App.Settings?.Prop?.EnableAurora ?? true;
            if (_enabled) Start();
            else { Stop(); InvalidateVisual(); }
        }

        public void SyncFromTheme()
        {
            if (Application.Current?.Resources is not { } res)
                return;

            if (res.TryGetValue("BrandInkColor", out var ink) && ink is Color i)
            {
                // Pull void toward theme ink but keep it oceanic-dark
                _void = Color.FromRgb(
                    (byte)Math.Min(i.R, (byte)0x08),
                    (byte)Math.Min(i.G, (byte)0x0A),
                    (byte)Math.Max(i.B, (byte)0x10));
                _deep = Color.FromRgb(
                    (byte)Math.Min(i.R + 4, 20),
                    (byte)Math.Min(i.G + 8, 24),
                    (byte)Math.Min(i.B + 18, 40));
            }

            if (res.TryGetValue("BrandAccentColor", out var a) && a is Color accent)
                _eclipse = accent;
            if (res.TryGetValue("BrandGlowColor", out var g) && g is Color glow)
                _violet = glow;
            if (res.TryGetValue("BrandGradientEnd", out var e) && e is Color end)
                _cyan = end;

            InvalidateVisual();
        }

        private void Start()
        {
            Stop();
            if (!_enabled)
                return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) =>
            {
                _time += 0.033f;

                for (int i = 0; i < PlanktonCount; i++)
                {
                    ref var p = ref _plankton[i];
                    p.Y -= p.Drift * 0.015f; // rise slowly like marine snow / bubbles
                    p.X += MathF.Sin(_time * 0.3f + p.Phase) * 0.00035f;
                    if (p.Y < -0.02f)
                    {
                        p.Y = 1.02f;
                        p.X = (float)_rng.NextDouble();
                    }
                }

                InvalidateVisual();
            };
            _timer.Start();
        }

        private void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width;
            double h = Bounds.Height;
            if (w < 2 || h < 2)
                return;

            // Depth gradient — surface-ish dark blue to hadal black
            var depth = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new Avalonia.Media.GradientStop(_deep, 0),
                    new Avalonia.Media.GradientStop(_void, 0.55),
                    new Avalonia.Media.GradientStop(Color.FromRgb(0x01, 0x03, 0x08), 1)
                ]
            };
            context.FillRectangle(depth, new Rect(0, 0, w, h));

            if (!_enabled)
                return;

            // Large bioluminescent gleams (soft caustic-like blobs)
            for (int i = 0; i < GleamCount; i++)
            {
                var g = _gleams[i];
                double cx = (g.X + Math.Sin(_time * g.SpeedX + g.Phase) * 0.12) * w;
                double cy = (g.Y + Math.Cos(_time * g.SpeedY + g.Phase * 1.4f) * 0.10) * h;
                double pulse = 0.72 + 0.28 * Math.Sin(_time * g.Pulse + g.Phase);
                double radius = Math.Min(w, h) * g.Radius * pulse;

                Color c = g.Kind switch
                {
                    0 => _cyan,
                    1 => _blue,
                    2 => _violet,
                    _ => _eclipse
                };

                byte aCore = (byte)(95 * pulse);
                var brush = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(aCore, c.R, c.G, c.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(aCore * 0.4), c.R, c.G, c.B), 0.4),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                    ]
                };
                context.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
            }

            // Twinkling plankton / bioluminescent motes
            for (int i = 0; i < PlanktonCount; i++)
            {
                var p = _plankton[i];
                double tw = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(_time * p.Twinkle + p.Phase));
                if (tw < 0.2)
                    continue;

                Color c = p.Kind switch
                {
                    0 => _cyan,
                    1 => _blue,
                    _ => _eclipse
                };

                byte a = (byte)(tw * 200);
                double px = p.X * w;
                double py = p.Y * h;
                double r = p.Size * (0.7 + tw * 0.6);

                // Tiny glow
                var glow = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, c.R, c.G, c.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.35), c.R, c.G, c.B), 0.45),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                    ]
                };
                context.DrawEllipse(glow, null, new Point(px, py), r * 3.5, r * 3.5);
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null, new Point(px, py), r * 0.45, r * 0.45);
            }

            // Slow horizontal caustic bands (subtle light ripples)
            for (int b = 0; b < 3; b++)
            {
                double y = ((0.2 + b * 0.28) + Math.Sin(_time * 0.25 + b) * 0.04) * h;
                double bandH = h * 0.08;
                byte ba = (byte)(18 + b * 4);
                var band = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, _cyan.R, _cyan.G, _cyan.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb(ba, _cyan.R, _cyan.G, _cyan.B), 0.35 + 0.1 * Math.Sin(_time + b)),
                        new Avalonia.Media.GradientStop(Color.FromArgb(ba, _blue.R, _blue.G, _blue.B), 0.65),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, _blue.R, _blue.G, _blue.B), 1)
                    ]
                };
                context.FillRectangle(band, new Rect(0, y - bandH * 0.5, w, bandH));
            }
        }

        private struct Gleam
        {
            public float X, Y, Radius, SpeedX, SpeedY, Phase, Pulse;
            public int Kind;
        }

        private struct Plankton
        {
            public float X, Y, Size, Twinkle, Phase, Drift;
            public int Kind;
        }
    }
}
