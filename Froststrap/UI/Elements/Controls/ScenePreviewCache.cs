using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>Still frames of each animated scene, rendered once off the UI thread for cards and menus.</summary>
    internal static class ScenePreviewCache
    {
        public const int Width = 440;
        public const int Height = 220;
        public const float Time = 8f;

        private static readonly ConcurrentDictionary<string, Bitmap?> Frames = new();
        private static readonly ConcurrentDictionary<string, byte> Pending = new();
        private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 6));

        /// <summary>Raised on the UI thread with the scene id once its frame is available.</summary>
        public static event Action<string>? FrameReady;

        public static Bitmap? Get(SceneSpec spec)
        {
            if (Frames.TryGetValue(spec.Id, out var frame))
                return frame;

            if (Pending.TryAdd(spec.Id, 0))
                _ = Task.Run(() => RenderAsync(spec));

            return null;
        }

        private static async Task RenderAsync(SceneSpec spec)
        {
            await Gate.WaitAsync();
            Bitmap? bitmap = null;

            try
            {
                using var shader = SceneShaders.CreateShader(spec, Width, Height, Time);
                if (shader is not null)
                {
                    using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                    using (var paint = new SKPaint { Shader = shader })
                        surface.Canvas.DrawRect(0, 0, Width, Height, paint);

                    using var image = surface.Snapshot();
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = new MemoryStream(data.ToArray());
                    bitmap = new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ScenePreviewCache", $"Failed to render {spec.Id}: {ex.Message}");
            }
            finally
            {
                Gate.Release();
            }

            Frames[spec.Id] = bitmap;
            Dispatcher.UIThread.Post(() => FrameReady?.Invoke(spec.Id));
        }
    }
}
