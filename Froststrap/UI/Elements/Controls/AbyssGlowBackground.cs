using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Crimson-to-black wash with a small fan of god rays that ease toward the pointer.
    /// </summary>
    public class AbyssGlowBackground : Control
    {
        private const int EmberCount = 22;
        private const int BlobCount = 6;
        private const int RibbonCount = 5;
        private const int RayCount = 7;

        private readonly Blob[] _blobs = new Blob[BlobCount];
        private readonly Ribbon[] _ribbons = new Ribbon[RibbonCount];
        private readonly Ember[] _embers = new Ember[EmberCount];
        private readonly Random _rng = new(42);

        private DispatcherTimer? _timer;
        private float _time;
        private bool _aurora = true;
        private bool _glow = true;
        private bool _parallaxEnabled = true;
        private bool _pointerInside;

        private float _tx = 0.5f, _ty = 0.5f;
        private float _px = 0.5f, _py = 0.5f;
        private float _rx = 0.5f, _ry = 0.22f;

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
                    Radius = 0.28f + (float)_rng.NextDouble() * 0.34f,
                    Speed = 0.10f + (float)_rng.NextDouble() * 0.16f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Kind = i % 3,
                    Parallax = 0.06f + (float)_rng.NextDouble() * 0.08f
                };
            }

            _ribbons[0] = new Ribbon(0.00f, 0.22f, 0.28f, 0.02f, 0.62f, 0.38f, 1.05f, 0.18f, 8.5f, 0.06f, 0.14f);
            _ribbons[1] = new Ribbon(-0.04f, 0.62f, 0.30f, 0.44f, 0.58f, 0.78f, 1.06f, 0.52f, 7.2f, 0.08f, 0.12f);
            _ribbons[2] = new Ribbon(0.08f, 1.02f, 0.36f, 0.70f, 0.55f, 0.32f, 0.96f, 0.04f, 6.5f, 0.05f, 0.16f);
            _ribbons[3] = new Ribbon(0.04f, 0.00f, 0.22f, 0.40f, 0.50f, 0.58f, 0.82f, 0.96f, 7.8f, 0.07f, 0.13f);
            _ribbons[4] = new Ribbon(0.30f, -0.04f, 0.48f, 0.26f, 0.76f, 0.14f, 1.08f, 0.46f, 5.8f, 0.04f, 0.18f);

            for (int i = 0; i < EmberCount; i++)
            {
                _embers[i] = new Ember
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Size = 0.9f + (float)_rng.NextDouble() * 2.1f,
                    Twinkle = 0.45f + (float)_rng.NextDouble() * 1.5f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Drift = 0.005f + (float)_rng.NextDouble() * 0.012f
                };
            }
        }

        public void SyncFromSettings()
        {
            var s = App.Settings?.Prop;
            _aurora = s?.EnableAurora ?? true;
            _glow = s?.EnableGlow ?? true;
            _parallaxEnabled = s?.EnableLiquidCursor ?? true;
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

                float follow = _parallaxEnabled && _pointerInside ? 0.12f : 0.045f;
                float rayFollow = _parallaxEnabled && _pointerInside ? 0.07f : 0.03f;
                float restX = _parallaxEnabled ? _tx : 0.5f;
                float restY = _parallaxEnabled ? _ty : 0.22f;
                _px += (restX - _px) * follow;
                _py += (restY - _py) * follow;
                _rx += (restX - _rx) * rayFollow;
                _ry += (restY - _ry) * rayFollow;

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

            double ox = (_px - 0.5) * w * 0.10;
            double oy = (_py - 0.5) * h * 0.08;
            double idleX = Math.Sin(_time * 0.14) * w * 0.010;
            double idleY = Math.Cos(_time * 0.11) * h * 0.008;

            if (_glow)
            {
                var cursorBloom = new RadialGradientBrush
                {
                    Center = new RelativePoint(_px, _py, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(_px, _py, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(0.28, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.24, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x28, _ember.R, _ember.G, _ember.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x12, _wine.R, _wine.G, _wine.B), 0.5),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0x00, _void.R, _void.G, _void.B), 1)
                    ]
                };
                context.FillRectangle(cursorBloom, new Rect(0, 0, w, h));
            }

            DrawCursorRays(context, w, h);

            for (int i = 0; i < BlobCount; i++)
            {
                var b = _blobs[i];
                double bx = (b.X + Math.Sin(_time * b.Speed + b.Phase) * 0.05) * w + ox * (0.7 + b.Parallax * 6) + idleX;
                double by = (b.Y + Math.Cos(_time * b.Speed * 0.8f + b.Phase) * 0.04) * h + oy * (0.7 + b.Parallax * 6) + idleY;
                double radius = Math.Min(w, h) * b.Radius;
                Color c = b.Kind switch
                {
                    0 => _ember,
                    1 => _wine,
                    _ => _rose
                };
                byte a = (byte)((_glow ? 48 : 32) + b.Kind * 8);
                var brush = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, c.R, c.G, c.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.5), c.R, c.G, c.B), 0.38),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                    ]
                };
                context.DrawEllipse(brush, null, new Point(bx, by), radius, radius * 0.76);
            }

            if (_aurora)
            {
                for (int i = 0; i < RibbonCount; i++)
                {
                    var r = _ribbons[i];
                    double vx = ox * r.Parallax * 5 + idleX * (0.4 + r.Sway);
                    double vy = oy * r.Parallax * 5 + idleY * (0.4 + r.Sway);
                    double wave = Math.Sin(_time * (0.22 + r.Sway) + i) * 8;
                    vx += wave;
                    Color c = i % 3 == 0 ? _ember : i % 3 == 1 ? _rose : _wine;
                    DrawRibbon(context, w, h, vx, vy, r, c, _glow);
                }
            }

            if (!_glow)
                return;

            for (int i = 0; i < EmberCount; i++)
            {
                var p = _embers[i];
                double tw = 0.22 + 0.78 * (0.5 + 0.5 * Math.Sin(_time * p.Twinkle + p.Phase));
                if (tw < 0.22)
                    continue;

                byte a = (byte)(tw * 90);
                double px = p.X * w + ox * 0.45;
                double py = p.Y * h + oy * 0.45;
                double rad = p.Size * (0.8 + tw * 0.7);
                var spark = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, 0xFE, 0xE2, 0xE2), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.55), _ember.R, _ember.G, _ember.B), 0.28),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.18), _wine.R, _wine.G, _wine.B), 0.62),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, _ember.R, _ember.G, _ember.B), 1)
                    ]
                };
                context.DrawEllipse(spark, null, new Point(px, py), rad * 4.4, rad * 4.4);
            }
        }

        private void DrawCursorRays(DrawingContext context, double w, double h)
        {
            if (!_aurora && !_glow)
                return;

            // Classic god-rays: a fan from just above the window. The cursor only
            // eases the origin a little and tilts the fan — not a flashlight.
            double originX = w * (0.50 + (_rx - 0.5) * 0.10);
            double originY = h * (-0.12 + (_ry - 0.5) * 0.06);
            var origin = new Point(originX, originY);

            double tilt = (_rx - 0.5) * 0.28;
            double baseAngle = Math.PI * 0.52 + tilt;
            double spread = 0.70 + Math.Sin(_time * 0.18) * 0.03;
            double length = Math.Max(w, h) * 1.15;
            byte peak = (byte)(_glow ? 0x2A : 0x18);

            for (int i = 0; i < RayCount; i++)
            {
                double t = (i + 0.5) / RayCount - 0.5;
                double wobble = Math.Sin(_time * 0.21 + i * 0.9) * 0.025;
                double ang = baseAngle + t * spread + wobble;
                double half = 0.016 + (i % 2) * 0.007;
                double len = length * (0.88 + 0.08 * Math.Sin(_time * 0.15 + i));

                var left = new Point(
                    originX + Math.Cos(ang - half) * len,
                    originY + Math.Sin(ang - half) * len);
                var right = new Point(
                    originX + Math.Cos(ang + half) * len,
                    originY + Math.Sin(ang + half) * len);
                var tip = new Point(
                    originX + Math.Cos(ang) * len,
                    originY + Math.Sin(ang) * len);

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(origin, true);
                    ctx.LineTo(left);
                    ctx.LineTo(right);
                    ctx.EndFigure(true);
                }

                Color c = i % 2 == 0 ? _ember : _wine;
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(originX, originY, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(tip.X, tip.Y, RelativeUnit.Absolute),
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(peak, c.R, c.G, c.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(peak * 0.45), c.R, c.G, c.B), 0.35),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                    ]
                };
                context.DrawGeometry(brush, null, geo);
            }
        }

        private static void DrawRibbon(DrawingContext context, double w, double h,
            double ox, double oy, Ribbon r, Color color, bool glow)
        {
            var p0 = new Point(r.X0 * w + ox, r.Y0 * h + oy);
            var c1 = new Point(r.C1x * w + ox, r.C1y * h + oy);
            var c2 = new Point(r.C2x * w + ox, r.C2y * h + oy);
            var p1 = new Point(r.X1 * w + ox, r.Y1 * h + oy);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(p0, false);
                ctx.CubicBezierTo(c1, c2, p1);
                ctx.EndFigure(false);
            }

            if (glow)
            {
                DrawStroke(context, geo, color, 0x16, r.Thickness * 5.0);
                DrawStroke(context, geo, color, 0x2C, r.Thickness * 2.4);
            }

            DrawStroke(context, geo, color, glow ? (byte)0x88 : (byte)0x66, r.Thickness);
            DrawStroke(context, geo, Color.FromRgb(0xFE, 0xE2, 0xE2), glow ? (byte)0x28 : (byte)0x14, Math.Max(1.2, r.Thickness * 0.24));

            if (!glow)
                return;

            for (int s = 1; s <= 2; s++)
            {
                double t = s / 3.0;
                var pt = Cubic(p0, c1, c2, p1, t);
                byte a = (byte)(22 + s * 8);
                double rad = r.Thickness * (2.4 + s * 0.3);
                var bloom = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, color.R, color.G, color.B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                    ]
                };
                context.DrawEllipse(bloom, null, pt, rad, rad * 0.7);
            }
        }

        private static void DrawStroke(DrawingContext context, StreamGeometry geo, Color color, byte alpha, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), thickness)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            context.DrawGeometry(null, pen, geo);
        }

        private static Point Cubic(Point p0, Point c1, Point c2, Point p1, double t)
        {
            double u = 1 - t;
            double uu = u * u;
            double tt = t * t;
            return new Point(
                uu * u * p0.X + 3 * uu * t * c1.X + 3 * u * tt * c2.X + tt * t * p1.X,
                uu * u * p0.Y + 3 * uu * t * c1.Y + 3 * u * tt * c2.Y + tt * t * p1.Y);
        }

        private struct Blob
        {
            public float X, Y, Radius, Speed, Phase, Parallax;
            public int Kind;
        }

        private readonly struct Ribbon
        {
            public readonly float X0, Y0, C1x, C1y, C2x, C2y, X1, Y1, Thickness, Parallax, Sway;

            public Ribbon(float x0, float y0, float c1x, float c1y, float c2x, float c2y,
                float x1, float y1, float thickness, float parallax, float sway)
            {
                X0 = x0; Y0 = y0; C1x = c1x; C1y = c1y; C2x = c2x; C2y = c2y;
                X1 = x1; Y1 = y1; Thickness = thickness; Parallax = parallax; Sway = sway;
            }
        }

        private struct Ember
        {
            public float X, Y, Size, Twinkle, Phase, Drift;
        }
    }
}
