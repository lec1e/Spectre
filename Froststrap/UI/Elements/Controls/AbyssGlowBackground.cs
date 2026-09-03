using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Spectre banner nebula: black void in the center, drifting crimson
    /// galactic dust at the edges, ember specks. No scales.
    /// </summary>
    public class AbyssGlowBackground : Control
    {
        private const int EmberCount = 56;
        private static readonly Uri FieldUri = new("avares://Spectre/Assets/spectre-nebula-field.png");
        private static readonly Uri DustUri = new("avares://Spectre/Assets/spectre-nebula-dust.png");

        private static Bitmap? _field;
        private static Bitmap? _dust;

        private readonly Ember[] _embers = new Ember[EmberCount];
        private readonly Random _rng = new(7);

        private DispatcherTimer? _timer;
        private float _time;
        private bool _aurora = true;
        private bool _glow = true;
        private bool _pointerInside;
        private float _tx = 0.5f, _ty = 0.5f;
        private float _px = 0.5f, _py = 0.5f;

        private Color _ember = Color.FromRgb(0xE8, 0x11, 0x2D);

        public AbyssGlowBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            EnsureBitmaps();
            InitEmbers();

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

            if (res.TryGetValue("BrandAccentColor", out var a) && a is Color accent)
                _ember = accent;

            InvalidateVisual();
        }

        private static void EnsureBitmaps()
        {
            _field ??= Load(FieldUri);
            _dust ??= Load(DustUri);
        }

        private static Bitmap? Load(Uri uri)
        {
            try
            {
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private void InitEmbers()
        {
            for (int i = 0; i < EmberCount; i++)
            {
                _embers[i] = new Ember
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    Size = 0.6f + (float)_rng.NextDouble() * 1.8f,
                    Speed = 0.006f + (float)_rng.NextDouble() * 0.018f,
                    Phase = (float)_rng.NextDouble() * 6.28f,
                    Drift = ((float)_rng.NextDouble() - 0.5f) * 0.012f
                };
            }
        }

        private void Start()
        {
            Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) =>
            {
                if (_aurora)
                {
                    _time += 0.033f;
                    for (int i = 0; i < EmberCount; i++)
                    {
                        ref var p = ref _embers[i];
                        p.Y -= p.Speed * 0.018f;
                        p.X += p.Drift * 0.02f + MathF.Sin(_time * 0.35f + p.Phase) * 0.00022f;
                        if (p.Y < -0.03f)
                        {
                            p.Y = 1.03f;
                            p.X = (float)_rng.NextDouble();
                        }
                        if (p.X < -0.04f) p.X = 1.04f;
                        if (p.X > 1.04f) p.X = -0.04f;
                    }
                }

                float follow = _pointerInside ? 0.10f : 0.028f;
                _px += (_tx - _px) * follow;
                _py += (_ty - _py) * follow;
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

            context.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));

            double px = (_px - 0.5) * w * 0.04;
            double py = (_py - 0.5) * h * 0.03;
            double t = _aurora ? _time : 0;

            DrawLayer(context, _field, w, h, 1.28,
                Math.Sin(t * 0.11) * w * 0.035 + px,
                Math.Cos(t * 0.09) * h * 0.028 + py,
                1.0);

            DrawLayer(context, _dust, w, h, 1.36,
                -Math.Sin(t * 0.07) * w * 0.05 + px * 1.4,
                Math.Sin(t * 0.085) * h * 0.04 + py * 1.3,
                0.52 + 0.10 * Math.Sin(t * 0.22));

            if (_glow)
                DrawEmbers(context, w, h);
        }

        private static void DrawLayer(
            DrawingContext context,
            Bitmap? bitmap,
            double w,
            double h,
            double scale,
            double ox,
            double oy,
            double opacity)
        {
            if (bitmap is null || opacity <= 0.01)
                return;

            double dw = w * scale;
            double dh = h * scale;
            var dest = new Rect((w - dw) * 0.5 + ox, (h - dh) * 0.5 + oy, dw, dh);
            if (opacity >= 0.999)
            {
                context.DrawImage(bitmap, dest);
                return;
            }

            using (context.PushOpacity(Math.Clamp(opacity, 0, 1)))
                context.DrawImage(bitmap, dest);
        }

        private void DrawEmbers(DrawingContext context, double w, double h)
        {
            for (int i = 0; i < EmberCount; i++)
            {
                var p = _embers[i];
                double tw = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(_time * (0.7f + p.Phase * 0.15f) + p.Phase));
                if (tw < 0.32)
                    continue;

                byte a = (byte)(tw * 140);
                double rad = p.Size * (0.7 + tw * 0.8);
                var spark = new RadialGradientBrush
                {
                    GradientStops =
                    [
                        new Avalonia.Media.GradientStop(Color.FromArgb(a, 0xFF, 0x6B, 0x6B), 0),
                        new Avalonia.Media.GradientStop(Color.FromArgb((byte)(a * 0.55), _ember.R, _ember.G, _ember.B), 0.35),
                        new Avalonia.Media.GradientStop(Color.FromArgb(0, _ember.R, _ember.G, _ember.B), 1)
                    ]
                };
                context.DrawEllipse(spark, null, new Point(p.X * w, p.Y * h), rad * 2.8, rad * 2.8);
            }
        }

        private struct Ember
        {
            public float X, Y, Size, Speed, Phase, Drift;
        }
    }
}
