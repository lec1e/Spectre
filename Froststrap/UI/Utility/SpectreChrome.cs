using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.UI.Elements.Controls;

namespace Froststrap.UI.Utility
{
    /// <summary>
    /// Shared launch-menu chrome: acrylic glass, nebula, and Windows rounded corners.
    /// </summary>
    public static class SpectreChrome
    {
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        public static readonly IReadOnlyList<WindowTransparencyLevel> Acrylic =
        [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.None
        ];

        public static void Apply(Window window, LiquidGlassPanel? glass = null, AbyssGlowBackground? abyss = null)
        {
            window.Background = Brushes.Transparent;
            window.TransparencyLevelHint = Acrylic;
            glass?.ApplyFromSettings();
            abyss?.SyncFromTheme();
            abyss?.SyncFromSettings();
            RoundCorners(window);
        }

        public static void AttachPointer(Window window, AbyssGlowBackground? abyss)
        {
            if (abyss is null)
                return;

            window.PointerMoved += (_, e) => abyss.SetPointer(e.GetPosition(abyss), true);
            window.PointerExited += (_, _) => abyss.SetPointer(new Point(-40, -40), false);
        }

        public static void RoundCorners(Window window)
        {
            if (!OperatingSystem.IsWindows())
                return;

            nint hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero)
                return;

            int pref = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
    }
}
