using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using WindowState = Avalonia.Controls.WindowState;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>The Spectre ribbon mark, recoloured from the active background with a soft glow and a slow flowing gradient.</summary>
    public class SpectreLogo : Control
    {
        public static readonly StyledProperty<string?> SceneIdProperty =
            AvaloniaProperty.Register<SpectreLogo, string?>(nameof(SceneId));

        public static readonly StyledProperty<bool> IsAnimatedProperty =
            AvaloniaProperty.Register<SpectreLogo, bool>(nameof(IsAnimated), true);

        public static readonly StyledProperty<double> GlowProperty =
            AvaloniaProperty.Register<SpectreLogo, double>(nameof(Glow), 0.45);

        /// <summary>Scene to take colours from. Leave empty to follow the current background.</summary>
        public string? SceneId { get => GetValue(SceneIdProperty); set => SetValue(SceneIdProperty, value); }
        public bool IsAnimated { get => GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }
        public double Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }

        private const double FrameInterval = 1.0 / 24;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private static readonly Dictionary<string, WindowIcon> IconCache = [];
        private static WindowIcon? _brandIcon;
        private static SKImage? _mark;

        private TopLevel? _topLevel;
        private bool _frameRequested;
        private double _lastFrame;

        static SpectreLogo()
        {
            AffectsRender<SpectreLogo>(SceneIdProperty, GlowProperty, IsAnimatedProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<SpectreLogo>(false);
        }

        private static SKImage? Mark
        {
            get
            {
                if (_mark is null)
                {
                    try
                    {
                        using var stream = AssetLoader.Open(new Uri("avares://Spectre/SpectreMark.png"));
                        _mark = SKImage.FromEncodedData(stream);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("SpectreLogo", $"Failed to load mark: {ex.Message}");
                    }
                }
                return _mark;
            }
        }

        private SceneSpec Spec => string.IsNullOrEmpty(SceneId) ? ThemeManager.Scene : SceneCatalog.Get(SceneId);

        private bool AnimationAllowed => IsAnimated && App.Settings?.Prop.DisableAnimations != true;

        protected override Size MeasureOverride(Size availableSize) => new(
            double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, 32) : 32,
            double.IsFinite(availableSize.Height) ? Math.Min(availableSize.Height, 32) : 32);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _topLevel = TopLevel.GetTopLevel(this);
            ThemeManager.ThemeChanged += OnThemeChanged;
            RequestFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _topLevel = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsAnimatedProperty || change.Property == IsVisibleProperty)
                RequestFrame();
        }

        private void OnThemeChanged()
        {
            InvalidateVisual();
            RequestFrame();
        }

        private void RequestFrame()
        {
            if (_frameRequested || _topLevel is null || !AnimationAllowed)
                return;

            _frameRequested = true;
            _topLevel.RequestAnimationFrame(OnFrame);
        }

        private void OnFrame(TimeSpan _)
        {
            _frameRequested = false;
            if (_topLevel is null || !AnimationAllowed)
                return;

            bool active = IsEffectivelyVisible && (_topLevel is not Window w || w.WindowState != WindowState.Minimized);
            double now = Clock.Elapsed.TotalSeconds;
            if (active && now - _lastFrame >= FrameInterval)
            {
                _lastFrame = now;
                InvalidateVisual();
            }

            if (active)
                RequestFrame();
            else
                Avalonia.Threading.DispatcherTimer.RunOnce(RequestFrame, TimeSpan.FromMilliseconds(500));
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0 || Mark is not { } mark)
                return;

            double side = Math.Min(size.Width, size.Height);
            var dest = new Rect((size.Width - side) / 2, (size.Height - side) / 2, side, side);
            float time = AnimationAllowed ? (float)Clock.Elapsed.TotalSeconds : 8f;
            context.Custom(new LogoDrawOperation(dest, mark, Spec, time, Math.Clamp(Glow, 0, 1)));
        }

        /// <summary>Draws the mark into <paramref name="dest"/>: blurred accent glow first, then the recoloured ribbon.</summary>
        private static void DrawMark(SKCanvas canvas, SKImage mark, SceneSpec spec, SKRect dest, float time, double glow)
        {
            if (glow > 0)
            {
                var accent = SKColor.Parse(spec.Accent);
                float sigma = Math.Max(1.5f, dest.Width * 0.08f);
                using var glowPaint = new SKPaint
                {
                    ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
                    ColorFilter = SKColorFilter.CreateBlendMode(accent.WithAlpha((byte)(glow * 255)), SKBlendMode.SrcIn)
                };
                canvas.DrawImage(mark, dest, Sampling, glowPaint);
            }

            var local = SKMatrix.CreateScale(dest.Width / mark.Width, dest.Height / mark.Height);
            using var markShader = mark.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, Sampling, local);
            using var shader = SceneShaders.CreateLogoShader(spec, markShader, dest.Width, dest.Height, time);
            if (shader is null)
            {
                canvas.DrawImage(mark, dest, Sampling);
                return;
            }

            canvas.Save();
            canvas.Translate(dest.Left, dest.Top);
            using (var paint = new SKPaint { Shader = shader })
                canvas.DrawRect(0, 0, dest.Width, dest.Height, paint);
            canvas.Restore();
        }

        /// <summary>White ribbon for the exe, taskbar and tray. The in-app mark stays coloured by the background.</summary>
        public static WindowIcon? CreateBrandIcon()
        {
            if (_brandIcon is not null)
                return _brandIcon;

            if (Mark is not { } mark)
                return null;

            try
            {
                const int Size = 256;
                using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(new[]
                    {
                        0f, 0f, 0f, 0f, 1f,
                        0f, 0f, 0f, 0f, 1f,
                        0f, 0f, 0f, 0f, 1f,
                        0f, 0f, 0f, 1f, 0f
                    })
                };
                surface.Canvas.DrawImage(mark, SKRect.Create(0, 0, Size, Size), Sampling, paint);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(data.ToArray());
                _brandIcon = new WindowIcon(new Bitmap(stream));
                return _brandIcon;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("SpectreLogo", $"Failed to build brand icon: {ex.Message}");
                return null;
            }
        }

        /// <summary>Window/taskbar icon tinted for <paramref name="spec"/>.</summary>
        public static WindowIcon? CreateWindowIcon(SceneSpec spec)
        {
            if (IconCache.TryGetValue(spec.Id, out var cached))
                return cached;

            if (Mark is not { } mark)
                return null;

            try
            {
                const int Size = 256;
                using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Transparent);
                DrawMark(surface.Canvas, mark, spec, SKRect.Create(0, 0, Size, Size), 8f, 0);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(data.ToArray());
                var icon = new WindowIcon(new Bitmap(stream));
                IconCache[spec.Id] = icon;
                return icon;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("SpectreLogo", $"Failed to build window icon: {ex.Message}");
                return null;
            }
        }

        private sealed class LogoDrawOperation(Rect dest, SKImage mark, SceneSpec spec, float time, double glow) : ICustomDrawOperation
        {
            public Rect Bounds { get; } = dest.Inflate(dest.Width * 0.3);

            public bool HitTest(Point p) => false;

            public bool Equals(ICustomDrawOperation? other) => false;

            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                    return;

                using var lease = feature.Lease();
                DrawMark(lease.SkCanvas, mark, spec, SKRect.Create((float)dest.X, (float)dest.Y, (float)dest.Width, (float)dest.Height), time, glow);
            }
        }
    }
}
