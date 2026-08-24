using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.RobloxInterfaces;

namespace Froststrap.UI.ViewModels.Bootstrapper
{
    public class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public List<WindowTransparencyLevel> WindowBackdropType { get; set; }
        public IBrush BackgroundColourBrush { get; set; } = Brushes.Transparent;
        public string VersionText { get; set; }
        public string ChannelText { get; set; }

        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            WindowBackdropType =
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ];

            // Keep bootstrapper backdrop transparent so LiquidGlassPanel acrylic can show through.
            BackgroundColourBrush = Brushes.Transparent;

            VersionText = $"Version: V{ExtractMajorVersion(version)}";
            ChannelText = $"Channel: {Deployment.Channel}";

            Deployment.ChannelChanged += (_, newChannel) =>
            {
                ChannelText = $"Channel: {newChannel}";
                OnPropertyChanged(nameof(ChannelText));
            };
        }

        private static string ExtractMajorVersion(string versionStr)
        {
            string[] parts = versionStr.Split('.');
            return (parts.Length >= 2) ? parts[1] : "???";
        }
    }
}
