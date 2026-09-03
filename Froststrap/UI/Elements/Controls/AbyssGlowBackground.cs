using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Dark crimson-to-black wash. No ribbons, rays, or baked-in lines —
    /// the cursor beam lives in <see cref="LiquidCursorOverlay"/>.
    /// </summary>
    public class AbyssGlowBackground : Control
    {
        private const int EmberCount = 16;
        private const int BlobCount = 5;

        private readonly Blob[] _blobs = new Blob[BlobCount];
        private readonly Ember[] _embers = new Ember[EmberCount];
        private readonly Random _rng = new(42);

        private DispatcherTimer? _timer;
        private float _time;
        private bool _aurora = true;
        private bool _glow = true;
        private bool _pointerInside;

        private float _tx = 0.5f, _ty = 0.5f;
        private float _px = 0.5f, _py = 0.5f;

        private Color _void = Color.FromRgb(0x05, 0x02, 0x03);
        private Color _ember = Color.FromRgb(0xE1, 0x1D, 0x48);
        private Color _wine = Color.FromRgb(0x7F, 0x1D, 0x1D);
        private Color _rose = Color.FromRgb(0xFB, 0x71, 0x85);

        public AbyssGlowBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            InitField();

            Loaded += (_, _) =>
            {
                SyncFromTheme();
                SyncFromSettings();
                Start();
            };
            Unloaded += (_, _) => Stop();
        }

        public void SetPointer(Point local, bool inside)
        {
            _pointerInside = inside;
            if (!inside || Bounds.Width < 2 || Bounds.Height < 2)
                return;

            _tx = (float)Math.Clamp(local.X / Bounds.Width, 0, 1);
            _ty = (float)Math.Clamp(local.Y / Bounds.Height, 0, 1);
        }

        private void InitField()
        {
            for (int i = 0; i < BlobCount; i++)
            {
                _blobs[i] = new Blob
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Radius = 0.30f + (float)_rng.NextDouble() * 0.28f,
                    Speed = 0.08f + (float)_rng.NextDouble() * 0.12f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Kind = i % 3,
                    Parallax = 0.04f + (float)_rng.NextDouble() * 0.05f
                };
            }

            for (int i = 0; i < EmberCount; i++)
            {
                _embers[i] = new Ember
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Size = 0.8f + (float)_rng.NextDouble() * 1.6f,
                    Twinkle = 0.40f + (float)_rng.NextDouble() * 1.2f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Drift = 0.004f + (float)_rng.NextDouble() * 0.010f
                };
            }
        }

        public void SyncFromSettings()
        {
            var s = App.Settings?.Prop;
            _aurora = s?.EnableAurora ?? true;
            _glow = s?.EnableGlow ?? true;
            Start();
            InvalidateVisual();
        }

        public void SyncFromTheme()
        {
            if (Application.Current?.Resources is not { } res)
                return;

            if (res.TryGetValue("BrandInkColor", out var ink) && ink is Color i)
            {
                _void = Color.FromRgb(
                    (byte)Math.Min(i.R, (byte)0x0C),
                    (byte)Math.Min(i.G, (byte)0x08),
                    (byte)Math.Min(i.B, (byte)0x0A));
            }

            if (res.TryGetValue("BrandAccentColor", out var a) && a is Color accent)
                _ember = accent;
            if (res.TryGetValue("BrandGlowColor", out var g) && g is Color glow)
                _rose = glow;
            if (res.TryGetValue("BrandGradientStart", out var gs) && gs is Color start)
                _wine = start;
            else if (res.TryGetValue("BrandHairlineColor", out var h) && h is Color hair)
                _wine = hair;

            InvalidateVisual();
        }

        private void Start()
        {
            Stop();
            if (!_aurora && !_glow)
                return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) =>
            {
                _time += 0.033f;

                float follow = _pointerInside ? 0.10f : 0.04f;
                _px += (_tx - _px) * follow;
                _py += (_ty - _py) * follow;

                for (int i = 0; i < EmberCount; i++)
                {
                    ref var p = ref _embers[i];
                    p.Y -= p.Drift * 0.014f;
                    p.X += MathF.Sin(_time * 0.22f + p.Phase) * 0.00028f;
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

            var wash = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new Avalonia.Media.GradientStop(Color.FromArgb(0xAA, _wine.R, _wine.G, _wine.B), 0),
                    new Avalonia.Media.GradientStop(Color.FromArgb(0x88, 0x1A, 0x06, 0x0A), 0.45),
                    new Avalonia.Media.GradientStop(Color.FromArgb(0xBB, _void.R, _void.G, _void.B), 1)
                ]
            };
            context.FillRectangle(wash, new Rect(0, 0, w, h));

            if (!_aurora && !_glow)
                return;

            double ox = (_px - 0.5) * w * 0.04;
            double oy = (_py - 0.5) * h * 0.03;
            double idleX = Math.Sin(_time * 0.12) * w * 0.006;
            double idleY = Math.Cos(_time * 0.09) * h * 0.005;

            if (_glow)
            {
                var cursorBloom = new RadialGradientBrush
                {
                    Center = new RelativePoint(_px, _py, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(_px, _py, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(0.22, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.18, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x1A, _ember.R, _ember.G, _ember.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x0A, _wine.R, _wine.G, _wine.B), 0.55),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x00, _void.R, _void.G, _void.B), 1)
                    ]
                };
                context.FillRectangle(cursorBloom, new Rect(0, 0, w, h));
            }

            for (int i = 0; i < BlobCount; i++)
            {
                var b = _blobs[i];
                double bx = (b.X + Math.Sin(_time * b.Speed + b.Phase) * 0.04) * w + ox * (0.5 + b.Parallax * 4) + idleX;
                double by = (b.Y + Math.Cos(_time * b.Speed * 0.8f + b.Phase) * 0.03) * h + oy * (0.5 + b.Parallax * 4) + idleY;
                double radius = Math.Min(w, h) * b.Radius;
                Color c = b.Kind switch
                {
                    0 => _ember,
                    1 => _wine,
                    _ => _rose
                };
                byte a = (byte)((_glow ? 36 : 22) + b.Kind * 6);
                var brush = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, c.R, c.G, c.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.45), c.R, c.G, c.B), 0.40),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                    ]
                };
                context.DrawEllipse(brush, null, new Point(bx, by), radius, radius * 0.78);
            }

            if (!_glow)
                return;

            for (int i = 0; i < EmberCount; i++)
            {
                var p = _embers[i];
                double tw = 0.22 + 0.78 * (0.5 + 0.5 * Math.Sin(_time * p.Twinkle + p.Phase));
                if (tw < 0.28)
                    continue;

                byte a = (byte)(tw * 70);
                double px = p.X * w + ox * 0.3;
                double py = p.Y * h + oy * 0.3;
                double rad = p.Size * (0.7 + tw * 0.5);
                var spark = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, 0xFE, 0xE2, 0xE2), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.5), _ember.R, _ember.G, _ember.B), 0.32),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, _ember.R, _ember.G, _ember.B), 1)
                    ]
                };
                context.DrawEllipse(spark, null, new Point(px, py), rad * 3.2, rad * 3.2);
            }
        }

        private struct Blob
        {
            public float X, Y, Radius, Speed, Phase, Parallax;
            public int Kind;
        }

        private struct Ember
        {
            public float X, Y, Size, Twinkle, Phase, Drift;
        }
    }
}
