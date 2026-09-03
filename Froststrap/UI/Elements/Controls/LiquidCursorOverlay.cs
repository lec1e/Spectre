using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Crimson light-beam that pins to the pointer and trails as a ribbon tail.
    /// Head is the cursor; the rest of the nodes catch up behind it.
    /// </summary>
    public class LiquidCursorOverlay : Control
    {
        private const int NodeCount = 18;
        private readonly Node[] _nodes = new Node[NodeCount];
        private DispatcherTimer? _timer;

        private float _tx, _ty;
        private bool _active;
        private bool _ready;
        private float _fade;

        private SKColor _head = new(254, 226, 226);
        private SKColor _mid = new(225, 29, 72);
        private SKColor _tail = new(26, 6, 10);

        public static readonly StyledProperty<bool> IsEffectEnabledProperty =
            AvaloniaProperty.Register<LiquidCursorOverlay, bool>(nameof(IsEffectEnabled), true);

        public bool IsEffectEnabled
        {
            get => GetValue(IsEffectEnabledProperty);
            set => SetValue(IsEffectEnabledProperty, value);
        }

        public LiquidCursorOverlay()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            Opacity = 1;

            Loaded += (_, _) =>
            {
                SyncColorsFromTheme();
                Start();
            };
            Unloaded += (_, _) => Stop();
        }

        public void SetPointer(Point local, bool inside)
        {
            _tx = (float)local.X;
            _ty = (float)local.Y;
            _active = inside && IsEffectEnabled;

            if (inside && !_ready)
            {
                for (int i = 0; i < NodeCount; i++)
                    _nodes[i] = new Node { X = _tx, Y = _ty };
                _ready = true;
            }
        }

        public void SyncColorsFromTheme()
        {
            if (Application.Current?.Resources is not { } res)
                return;

            _head = new SKColor(254, 226, 226);
            if (res.TryGetValue("BrandAccentColor", out var a) && a is Color accent)
                _mid = new SKColor(accent.R, accent.G, accent.B);
            if (res.TryGetValue("BrandInkColor", out var ink) && ink is Color voidColor)
                _tail = new SKColor(voidColor.R, voidColor.G, voidColor.B);
            else if (res.TryGetValue("BrandGradientStart", out var g) && g is Color wine)
                _tail = new SKColor(wine.R, wine.G, wine.B);

            InvalidateVisual();
        }

        private void Start()
        {
            Stop();
            if (!IsEffectEnabled)
                return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        private void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        private void Tick()
        {
            if (!IsEffectEnabled)
            {
                _fade *= 0.82f;
                InvalidateVisual();
                return;
            }

            _fade += ((_active ? 1f : 0f) - _fade) * (_active ? 0.45f : 0.14f);
            if (_fade < 0.02f && !_active)
            {
                InvalidateVisual();
                return;
            }

            if (!_ready)
                return;

            // Head is locked to the pointer so the beam never lags the cursor.
            _nodes[0].X = _tx;
            _nodes[0].Y = _ty;
            _nodes[0].Vx = 0;
            _nodes[0].Vy = 0;

            for (int i = 1; i < NodeCount; i++)
            {
                ref var n = ref _nodes[i];
                ref var p = ref _nodes[i - 1];
                float follow = Math.Clamp(0.72f - i * 0.018f, 0.34f, 0.78f);
                n.X += (p.X - n.X) * follow;
                n.Y += (p.Y - n.Y) * follow;
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (!IsEffectEnabled || _fade < 0.03f || !_ready)
                return;

            var pts = new SKPoint[NodeCount];
            for (int i = 0; i < NodeCount; i++)
                pts[i] = new SKPoint(_nodes[i].X, _nodes[i].Y);

            context.Custom(new GlowTrailDrawOp(
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                pts,
                _head, _mid, _tail,
                _fade));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsEffectEnabledProperty)
            {
                if (IsEffectEnabled) Start();
                else { Stop(); InvalidateVisual(); }
            }
        }

        private struct Node
        {
            public float X, Y, Vx, Vy;
        }

        private sealed class GlowTrailDrawOp : ICustomDrawOperation
        {
            private readonly SKPoint[] _pts;
            private readonly SKColor _head;
            private readonly SKColor _mid;
            private readonly SKColor _tail;
            private readonly float _fade;

            public GlowTrailDrawOp(Rect bounds, SKPoint[] pts, SKColor head, SKColor mid, SKColor tail, float fade)
            {
                Bounds = bounds;
                _pts = pts;
                _head = head;
                _mid = mid;
                _tail = tail;
                _fade = fade;
            }

            public Rect Bounds { get; }
            public void Dispose() { }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature is null || _pts.Length < 3)
                    return;

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;
                canvas.Save();

                float fade = Math.Clamp(_fade, 0f, 1f);
                using var path = BuildPath(_pts);

                using (var blur = SKImageFilter.CreateBlur(10f, 10f))
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeWidth = 14f,
                    ImageFilter = blur,
                    Shader = SKShader.CreateLinearGradient(
                        _pts[0], _pts[^1],
                        [
                            WithAlpha(_mid, fade * 0.42f),
                            WithAlpha(_mid, fade * 0.28f),
                            WithAlpha(_tail, fade * 0.08f)
                        ],
                        null,
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawPath(path, paint);
                }

                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeWidth = 4.2f,
                    Shader = SKShader.CreateLinearGradient(
                        _pts[0], _pts[^1],
                        [
                            WithAlpha(_head, fade * 0.95f),
                            WithAlpha(_mid, fade * 0.85f),
                            WithAlpha(_tail, fade * 0.20f)
                        ],
                        null,
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawPath(path, paint);
                }

                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeWidth = 1.5f,
                    Shader = SKShader.CreateLinearGradient(
                        _pts[0], _pts[^1],
                        [
                            WithAlpha(new SKColor(255, 255, 255), fade * 0.70f),
                            WithAlpha(_head, fade * 0.35f),
                            WithAlpha(_mid, 0)
                        ],
                        null,
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawPath(path, paint);
                }

                var tip = _pts[0];
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Shader = SKShader.CreateRadialGradient(
                        tip,
                        16f,
                        [
                            WithAlpha(new SKColor(255, 255, 255), fade * 0.70f),
                            WithAlpha(_head, fade * 0.45f),
                            WithAlpha(_mid, fade * 0.18f),
                            WithAlpha(_mid, 0)
                        ],
                        [0f, 0.22f, 0.55f, 1f],
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawCircle(tip, 16f, paint);
                }

                canvas.Restore();
            }

            private static SKColor WithAlpha(SKColor c, float a)
                => new(c.Red, c.Green, c.Blue, (byte)(Math.Clamp(a, 0f, 1f) * 255));

            private static SKPath BuildPath(SKPoint[] pts)
            {
                var path = new SKPath();
                path.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length - 2; i++)
                {
                    var mid = new SKPoint(
                        (pts[i].X + pts[i + 1].X) * 0.5f,
                        (pts[i].Y + pts[i + 1].Y) * 0.5f);
                    path.QuadTo(pts[i], mid);
                }
                path.QuadTo(pts[^2], pts[^1]);
                return path;
            }
        }
    }
}
