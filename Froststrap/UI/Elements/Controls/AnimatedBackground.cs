using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using GradientStop = Avalonia.Media.GradientStop;
using WindowState = Avalonia.Controls.WindowState;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>Renders a <see cref="SceneCatalog"/> background with its shader and particle layer.</summary>
    public class AnimatedBackground : Control
    {
        public static readonly StyledProperty<string> SceneIdProperty =
            AvaloniaProperty.Register<AnimatedBackground, string>(nameof(SceneId), SceneCatalog.DefaultId);

        public static readonly StyledProperty<bool> IsAnimatedProperty =
            AvaloniaProperty.Register<AnimatedBackground, bool>(nameof(IsAnimated), true);

        public static readonly StyledProperty<bool> IsPreviewProperty =
            AvaloniaProperty.Register<AnimatedBackground, bool>(nameof(IsPreview));

        public static readonly StyledProperty<double> DimProperty =
            AvaloniaProperty.Register<AnimatedBackground, double>(nameof(Dim));

        public static readonly StyledProperty<Color> DimColorProperty =
            AvaloniaProperty.Register<AnimatedBackground, Color>(nameof(DimColor), Colors.Black);

        public string SceneId { get => GetValue(SceneIdProperty); set => SetValue(SceneIdProperty, value); }
        public bool IsAnimated { get => GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }

        /// <summary>Small card/menu thumbnail: shows a cached still frame and only runs the shader while animated.</summary>
        public bool IsPreview { get => GetValue(IsPreviewProperty); set => SetValue(IsPreviewProperty, value); }

        public double Dim { get => GetValue(DimProperty); set => SetValue(DimProperty, value); }
        public Color DimColor { get => GetValue(DimColorProperty); set => SetValue(DimColorProperty, value); }

        private const double BackdropFrameInterval = 1.0 / 30;
        private const double PreviewFrameInterval = 1.0 / 40;
        private const double BackdropQuality = 0.5;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private struct Particle
        {
            public double X, Y, Size, Speed, Phase, Twinkle, Depth, Alpha;
            public bool Alt;
        }

        private SceneSpec _spec = SceneCatalog.Get(SceneCatalog.DefaultId);
        private Particle[] _particles = [];
        private IBrush _fallbackBrush = Brushes.Black;
        private IBrush? _glowBrush;
        private IBrush[] _particleBrushes = [];
        private IBrush[] _particleAltBrushes = [];

        private static readonly IBrush VignetteBrush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5),
                new GradientStop(Color.FromArgb(0x66, 0, 0, 0), 1),
            }
        }.ToImmutable();

        private double _animBase = Clock.Elapsed.TotalSeconds - ScenePreviewCache.Time;
        private double _frozenTime = ScenePreviewCache.Time;

        private TopLevel? _topLevel;
        private bool _frameRequested;
        private double _lastFrame;

        static AnimatedBackground()
        {
            AffectsRender<AnimatedBackground>(DimProperty, DimColorProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<AnimatedBackground>(false);
            ClipToBoundsProperty.OverrideDefaultValue<AnimatedBackground>(true);
        }

        public AnimatedBackground() => BuildScene();

        /// <summary>Applies the current scene, dim, and motion settings. Used by every window that has a backdrop.</summary>
        public void ApplyFromSettings()
        {
            var settings = App.Settings?.Prop;
            if (settings is null)
                return;

            bool isLight = ThemeManager.Current?.IsLight ?? false;
            var scene = ThemeManager.Scene;
            IsVisible = ThemeManager.CustomBackground is null && scene.IsAnimated;
            SceneId = scene.Id;
            IsAnimated = settings.AnimateBackground && !settings.DisableAnimations;
            DimColor = isLight ? Colors.White : Colors.Black;
            Dim = isLight ? Math.Max(0.72, settings.BackgroundDim) : settings.BackgroundDim;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SceneIdProperty)
            {
                BuildScene();
                InvalidateVisual();
                RequestFrame();
            }
            else if (change.Property == IsAnimatedProperty || change.Property == IsVisibleProperty)
            {
                if (change.Property == IsAnimatedProperty)
                {
                    double now = Clock.Elapsed.TotalSeconds;
                    if (IsAnimated)
                        _animBase = now - _frozenTime;
                    else
                        _frozenTime = IsPreview ? ScenePreviewCache.Time : now - _animBase;
                }

                InvalidateVisual();
                RequestFrame();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _topLevel = TopLevel.GetTopLevel(this);
            ScenePreviewCache.FrameReady += OnPreviewFrameReady;
            RequestFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ScenePreviewCache.FrameReady -= OnPreviewFrameReady;
            _topLevel = null;
        }

        private void OnPreviewFrameReady(string id)
        {
            if (IsPreview && id == _spec.Id)
                InvalidateVisual();
        }

        private bool ShouldAnimate =>
            IsAnimated && IsEffectivelyVisible && _spec.IsAnimated && _topLevel is not null
            && (_topLevel is not Window window || window.WindowState != WindowState.Minimized);

        private void RequestFrame()
        {
            if (_frameRequested || _topLevel is null || !IsAnimated || !_spec.IsAnimated)
                return;

            _frameRequested = true;
            _topLevel.RequestAnimationFrame(OnFrame);
        }

        private void OnFrame(TimeSpan _)
        {
            _frameRequested = false;

            if (_topLevel is null || !IsAnimated || !_spec.IsAnimated)
                return;

            double now = Clock.Elapsed.TotalSeconds;
            double interval = IsPreview ? PreviewFrameInterval : BackdropFrameInterval;

            if (ShouldAnimate && now - _lastFrame >= interval)
            {
                _lastFrame = now;
                InvalidateVisual();
            }

            if (ShouldAnimate)
                RequestFrame();
            else
                Avalonia.Threading.DispatcherTimer.RunOnce(RequestFrame, TimeSpan.FromMilliseconds(500));
        }

        private void BuildScene()
        {
            _spec = SceneCatalog.Get(SceneId);

            Color c0 = Color.Parse(_spec.Colors[0]), c1 = Color.Parse(_spec.Colors[1]);
            _fallbackBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(c1, 0), new GradientStop(c0, 1) }
            }.ToImmutable();

            Color accent = Color.Parse(_spec.Accent);
            _glowBrush = _spec.IsAnimated ? null : new RadialGradientBrush
            {
                Center = new RelativePoint(0.85, 0.1, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.85, 0.1, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x40, accent.R, accent.G, accent.B), 0),
                    new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
                }
            }.ToImmutable();

            _particleBrushes = BuildAlphaRamp(Color.Parse(_spec.ParticleColor));
            _particleAltBrushes = BuildAlphaRamp(Color.Parse(_spec.ParticleAltColor));

            var rng = new Random(_spec.Id.Aggregate(17, (h, c) => h * 31 + c));
            _particles = new Particle[_spec.ParticleCount];

            for (int i = 0; i < _particles.Length; i++)
            {
                double depth = rng.NextDouble();
                _particles[i] = _spec.Particles switch
                {
                    SceneParticles.Stars => new Particle
                    {
                        X = rng.NextDouble(), Y = rng.NextDouble(),
                        Size = 0.35 + Math.Pow(rng.NextDouble(), 3) * 1.25,
                        Twinkle = 0.6 + rng.NextDouble() * 1.8, Phase = rng.NextDouble() * Math.Tau,
                        Alpha = 0.3 + rng.NextDouble() * 0.6, Speed = 0.002 + rng.NextDouble() * 0.004
                    },
                    SceneParticles.Embers => new Particle
                    {
                        X = rng.NextDouble(), Y = rng.NextDouble(),
                        Size = 0.7 + rng.NextDouble() * 1.5, Speed = 0.012 + rng.NextDouble() * 0.03,
                        Phase = rng.NextDouble() * Math.Tau, Twinkle = 0.8 + rng.NextDouble() * 2,
                        Alpha = 0.4 + rng.NextDouble() * 0.55, Alt = rng.NextDouble() < 0.35
                    },
                    SceneParticles.Drift => new Particle
                    {
                        X = rng.NextDouble(), Y = rng.NextDouble(), Depth = depth,
                        Size = 0.3 + depth * depth * 1.5, Speed = 0.004 + depth * 0.02,
                        Phase = rng.NextDouble() * Math.Tau, Twinkle = 0.5 + rng.NextDouble() * 1.5,
                        Alpha = 0.25 + depth * 0.7
                    },
                    SceneParticles.Bubbles => new Particle
                    {
                        X = rng.NextDouble(), Y = rng.NextDouble(), Depth = depth,
                        Size = 1.5 + depth * 5, Speed = 0.008 + depth * 0.02,
                        Phase = rng.NextDouble() * Math.Tau, Alpha = 0.16 + depth * 0.28
                    },
                    _ => default
                };
            }
        }

        private static IBrush[] BuildAlphaRamp(Color color)
        {
            var ramp = new IBrush[33];
            for (int i = 0; i < ramp.Length; i++)
                ramp[i] = new ImmutableSolidColorBrush(Color.FromArgb((byte)(i * 255 / 32), color.R, color.G, color.B));
            return ramp;
        }

        private static IBrush Pick(IBrush[] ramp, double alpha) =>
            ramp[Math.Clamp((int)Math.Round(alpha * 32), 0, 32)];

        private static double Frac(double v) => v - Math.Floor(v);

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0)
                return;

            var bounds = new Rect(size);
            double t = IsAnimated ? Clock.Elapsed.TotalSeconds - _animBase : _frozenTime;

            using (context.PushClip(bounds))
            {
                if (!DrawScene(context, bounds, t))
                {
                    context.FillRectangle(_fallbackBrush, bounds);
                    if (_glowBrush is not null)
                        context.FillRectangle(_glowBrush, bounds);
                }

                DrawParticles(context, size.Width, size.Height, t);

                if (_spec.IsAnimated && !_spec.IsLight)
                    context.FillRectangle(VignetteBrush, bounds);

                double dim = Math.Clamp(Dim, 0, 1);
                if (dim > 0)
                {
                    var c = DimColor;
                    context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(dim * 255), c.R, c.G, c.B)), bounds);
                }
            }
        }

        private bool DrawScene(DrawingContext context, Rect bounds, double t)
        {
            if (!_spec.IsAnimated)
                return false;

            if (IsPreview && !IsAnimated)
            {
                if (ScenePreviewCache.Get(_spec) is not { } frame)
                    return false;

                DrawCover(context, frame, bounds);
                return true;
            }

            if (SceneShaders.GetEffect(_spec.Kind) is null)
                return false;

            context.Custom(new ShaderDrawOperation(bounds, _spec, (float)t, IsPreview ? 1.0 : BackdropQuality));
            return true;
        }

        private static void DrawCover(DrawingContext context, Bitmap frame, Rect bounds)
        {
            var source = frame.Size;
            double scale = Math.Max(bounds.Width / source.Width, bounds.Height / source.Height);
            double w = bounds.Width / scale, h = bounds.Height / scale;
            var sourceRect = new Rect((source.Width - w) / 2, (source.Height - h) / 2, w, h);
            context.DrawImage(frame, sourceRect, bounds);
        }

        private void DrawParticles(DrawingContext context, double w, double h, double t)
        {
            switch (_spec.Particles)
            {
                case SceneParticles.Stars:
                    foreach (ref readonly var p in _particles.AsSpan())
                    {
                        double alpha = p.Alpha * (0.55 + 0.45 * Math.Sin(t * p.Twinkle + p.Phase));
                        var center = new Point(w * p.X, h * Frac(p.Y + t * p.Speed * 0.15));
                        if (p.Size > 1.1)
                            context.DrawEllipse(Pick(_particleBrushes, alpha * 0.14), null, center, p.Size * 3.2, p.Size * 3.2);
                        context.DrawEllipse(Pick(_particleBrushes, alpha), null, center, p.Size, p.Size);
                    }
                    break;

                case SceneParticles.Embers:
                    foreach (ref readonly var p in _particles.AsSpan())
                    {
                        double y = Frac(p.Y - t * p.Speed);
                        double life = Math.Sin(Math.PI * (1 - y));
                        double flicker = 0.75 + 0.25 * Math.Sin(t * p.Twinkle * 3 + p.Phase);
                        double alpha = p.Alpha * life * flicker;
                        double x = w * (p.X + 0.018 * Math.Sin(t * p.Twinkle * 0.6 + p.Phase));
                        var center = new Point(x, h * y);
                        var ramp = p.Alt ? _particleAltBrushes : _particleBrushes;
                        context.DrawEllipse(Pick(ramp, alpha * 0.16), null, center, p.Size * 4.5, p.Size * 4.5);
                        context.DrawEllipse(Pick(ramp, alpha), null, center, p.Size, p.Size);
                    }
                    break;

                case SceneParticles.Drift:
                    foreach (ref readonly var p in _particles.AsSpan())
                    {
                        double alpha = p.Alpha * (0.7 + 0.3 * Math.Sin(t * p.Twinkle + p.Phase));
                        var center = new Point(w * Frac(p.X - t * p.Speed), h * p.Y);
                        if (p.Depth > 0.85)
                            context.DrawEllipse(Pick(_particleBrushes, alpha * 0.12), null, center, p.Size * 3.5, p.Size * 3.5);
                        context.DrawEllipse(Pick(_particleBrushes, alpha), null, center, p.Size, p.Size);
                    }
                    break;

                case SceneParticles.Bubbles:
                    foreach (ref readonly var p in _particles.AsSpan())
                    {
                        double y = Frac(p.Y - t * p.Speed);
                        double alpha = p.Alpha * Math.Sin(Math.PI * (1 - y));
                        double x = w * (p.X + 0.012 * Math.Sin(t * 0.9 + p.Phase));
                        var center = new Point(x, h * y);
                        var pen = new ImmutablePen((IImmutableBrush)Pick(_particleBrushes, alpha), 1);
                        context.DrawEllipse(Pick(_particleBrushes, alpha * 0.12), pen, center, p.Size, p.Size);
                    }
                    break;
            }
        }

        /// <summary>
        /// Draws the scene shader with Skia. The full-window backdrop is rendered into a smaller
        /// offscreen surface and scaled up, which keeps the per-pixel noise cost manageable.
        /// </summary>
        private sealed class ShaderDrawOperation(Rect bounds, SceneSpec spec, float time, double quality) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;

            public bool HitTest(Point p) => false;

            public bool Equals(ICustomDrawOperation? other) => false;

            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                    return;

                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;
                float w = (float)bounds.Width, h = (float)bounds.Height;
                var dest = SKRect.Create((float)bounds.X, (float)bounds.Y, w, h);

                if (quality >= 1)
                {
                    using var shader = SceneShaders.CreateShader(spec, w, h, time);
                    if (shader is null)
                        return;

                    canvas.Save();
                    canvas.Translate(dest.Left, dest.Top);
                    using (var paint = new SKPaint { Shader = shader })
                        canvas.DrawRect(0, 0, w, h, paint);
                    canvas.Restore();
                    return;
                }

                var gr = lease.GrContext;
                double q = gr is null ? Math.Min(quality, 0.22) : quality;
                float device = Math.Max(1f, Math.Abs(canvas.TotalMatrix.ScaleX));
                int pw = Math.Max(1, (int)Math.Ceiling(w * device * q));
                int ph = Math.Max(1, (int)Math.Ceiling(h * device * q));
                var info = new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul);

                using var surface = gr is not null ? SKSurface.Create(gr, false, info) : SKSurface.Create(info);
                if (surface is null)
                    return;

                using (var shader = SceneShaders.CreateShader(spec, pw, ph, time))
                {
                    if (shader is null)
                        return;

                    using var paint = new SKPaint { Shader = shader };
                    surface.Canvas.DrawRect(0, 0, pw, ph, paint);
                }

                using var image = surface.Snapshot();
                canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }
        }
    }
}
