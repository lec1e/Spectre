using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Froststrap.UI.Utility
{
    /// <summary>
    /// Shared window chrome: opaque themed surface with Windows rounded corners.
    /// </summary>
    public static class SpectreChrome
    {
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        public static readonly IReadOnlyList<WindowTransparencyLevel> Opaque = [WindowTransparencyLevel.None];

        public static void Apply(Window window)
        {
            window.TransparencyLevelHint = Opaque;
            RoundCorners(window);
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
