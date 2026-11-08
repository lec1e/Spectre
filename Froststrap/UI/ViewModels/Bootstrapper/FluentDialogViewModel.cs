using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.RobloxInterfaces;

namespace Froststrap.UI.ViewModels.Bootstrapper
{
    public class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public List<WindowTransparencyLevel> WindowBackdropType { get; set; }
        public string VersionText { get; set; }
        public string ChannelText { get; set; }
        public string ProductVersion => $"Version {App.Version}";
        public bool IsAero { get; }

        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            IsAero = aero;
            WindowBackdropType = [WindowTransparencyLevel.None];

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
