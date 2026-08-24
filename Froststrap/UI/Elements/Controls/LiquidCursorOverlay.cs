using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>
    /// Glowing Eclipse gradient trail — soft spring ribbon + bright head
    /// (gradient style of the earlier liquid trail). Drawn under UI chrome.
    /// </summary>
    public class LiquidCursorOverlay : Control
    {
        private const int NodeCount = 22;
        private readonly Node[] _nodes = new Node[NodeCount];
        private DispatcherTimer? _timer;

        private float _tx, _ty;
        private bool _active;
        private bool _ready;
        private float _fade;

        private SKColor _head = new(233, 213, 255);
        private SKColor _mid = new(192, 132, 252);
        private SKColor _tail = new(34, 211, 238);

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
            Opacity = 0.45;

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

            if (res.TryGetValue("BrandAccentColor", out var a) && a is Color accent)
                _mid = new SKColor(accent.R, accent.G, accent.B);
            if (res.TryGetValue("BrandPurpleColor", out var p) && p is Color purple)
                _head = new SKColor(
                    (byte)Math.Min(255, purple.R + 35),
                    (byte)Math.Min(255, purple.G + 35),
                    (byte)Math.Min(255, purple.B + 25));
            if (res.TryGetValue("BrandGradientEnd", out var g) && g is Color end)
                _tail = new SKColor(end.R, end.G, end.B);
            else if (res.TryGetValue("BrandGlowColor", out var glow) && glow is Color gl)
                _tail = new SKColor(gl.R, gl.G, gl.B);

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
                _fade *= 0.85f;
                InvalidateVisual();
                return;
            }

            _fade += ((_active ? 1f : 0f) - _fade) * (_active ? 0.3f : 0.1f);
            if (_fade < 0.02f && !_active)
            {
                InvalidateVisual();
                return;
            }

            if (!_ready)
                return;

            // Spring-style follow (from earlier liquid trail), gradient colors on draw
            float spring = 0.45f;
            const float friction = 0.55f;
            ref var head = ref _nodes[0];
            head.Vx += (_tx - head.X) * spring;
            head.Vy += (_ty - head.Y) * spring;
            head.Vx *= friction;
            head.Vy *= friction;
            head.X += head.Vx;
            head.Y += head.Vy;

            for (int i = 1; i < NodeCount; i++)
            {
                spring *= 0.965f;
                ref var n = ref _nodes[i];
                ref var p = ref _nodes[i - 1];
                n.Vx += (p.X - n.X) * spring;
                n.Vy += (p.Y - n.Y) * spring;
                n.Vx += p.Vx * 0.1f;
                n.Vy += p.Vy * 0.1f;
                n.Vx *= friction;
                n.Vy *= friction;
                n.X += n.Vx;
                n.Y += n.Vy;
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

                // Outer soft glow
                using (var blur = SKImageFilter.CreateBlur(8f, 8f))
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeWidth = 9f,
                    ImageFilter = blur,
                    Shader = SKShader.CreateLinearGradient(
                        _pts[0], _pts[^1],
                        [
                            WithAlpha(_head, fade * 0.18f),
                            WithAlpha(_mid, fade * 0.22f),
                            WithAlpha(_tail, fade * 0.12f)
                        ],
                        null,
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawPath(path, paint);
                }

                // Bright gradient core
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeWidth = 2.8f,
                    Shader = SKShader.CreateLinearGradient(
                        _pts[0], _pts[^1],
                        [
                            WithAlpha(_head, fade * 0.55f),
                            WithAlpha(_mid, fade * 0.5f),
                            WithAlpha(_tail, fade * 0.28f)
                        ],
                        null,
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawPath(path, paint);
                }

                // Specular head glow
                var tip = _pts[0];
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Shader = SKShader.CreateRadialGradient(
                        tip,
                        12f,
                        [
                            WithAlpha(new SKColor(255, 255, 255), fade * 0.45f),
                            WithAlpha(_head, fade * 0.3f),
                            WithAlpha(_mid, fade * 0.1f),
                            WithAlpha(_mid, 0)
                        ],
                        [0f, 0.25f, 0.55f, 1f],
                        SKShaderTileMode.Clamp)
                })
                {
                    canvas.DrawCircle(tip, 12f, paint);
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
