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
        public string ProductVersion => $"Version {App.Version}";
        public bool IsAero { get; }

        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            IsAero = aero;
            WindowBackdropType = ResolveBackdrop(aero);

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

        public static List<WindowTransparencyLevel> ResolveBackdrop(bool aero)
        {
            if (aero)
            {
                return
                [
                    WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.None
                ];
            }

            var selected = App.Settings.Prop.SelectedBackdrop;
            return selected switch
            {
                Enums.WindowsBackdrops.Mica =>
                [
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.None
                ],
                Enums.WindowsBackdrops.Aero =>
                [
                    WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.None
                ],
                Enums.WindowsBackdrops.None =>
                [
                    WindowTransparencyLevel.None
                ],
                _ =>
                [
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.None
                ]
            };
        }

        private static string ExtractMajorVersion(string versionStr)
        {
            string[] parts = versionStr.Split('.');
            return (parts.Length >= 2) ? parts[1] : "???";
        }
    }
}
