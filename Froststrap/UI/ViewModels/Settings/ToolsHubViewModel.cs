using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LucideAvalonia.Enum;

namespace Froststrap.UI.ViewModels.Settings
{
    public class ToolsHubViewModel : NotifyPropertyChangedViewModel
    {
        private readonly MainWindowViewModel _host;

        public ObservableCollection<ToolsHubItem> Items { get; } = [];
        public ICommand OpenToolCommand { get; }

        public ToolsHubViewModel(MainWindowViewModel host)
        {
            _host = host;
            OpenToolCommand = new RelayCommand<string>(Open);

            Items.Add(new("Integrations", "Activity tracking and extras", "integrations", LucideIconNames.Plus));
            Items.Add(new("Bootstrapper", "Launch behaviour", "behaviour", LucideIconNames.Play));
            Items.Add(new("Deployment", "Channels and updates", "channels", LucideIconNames.HardDriveUpload));
            Items.Add(new("Versions", "Pin Roblox builds", "versions", LucideIconNames.Layers));
            Items.Add(new("Appearance", "Theme, glass, aurora", "appearance", LucideIconNames.Palette));
            Items.Add(new("Fast Flags", "Client tuning", "fastflags", LucideIconNames.Flag));
            Items.Add(new("VIP Server", "Join shared VIP links", "vipserver", LucideIconNames.Crown));
            Items.Add(new("News", "Roblox news feed", "news", LucideIconNames.Newspaper));
            Items.Add(new("Multi Instance", "Run multiple clients", "multiinstance", LucideIconNames.Copy));
            if (OperatingSystem.IsWindows())
            {
                Items.Add(new("BanAsync", "Clean Roblox traces", "banasync", LucideIconNames.Shield));
                Items.Add(new("HWID Spoofer", "Spoof machine IDs", "hwidspoofer", LucideIconNames.FingerprintPattern));
            }
            Items.Add(new("Server Browser", "Browse by region", "regionselector", LucideIconNames.Globe));
            Items.Add(new("Shortcuts", "Desktop shortcuts", "shortcuts", LucideIconNames.Link2));
            Items.Add(new("Global Settings", "GBS editor", "globalsettings", LucideIconNames.PenLine));
            Items.Add(new("About", "Credits and version", "about", LucideIconNames.CircleAlert));
        }

        private void Open(string? tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            switch (tag)
            {
                case "integrations": _host.NavigateToIntegrationsCommand.Execute(null); break;
                case "behaviour": _host.NavigateToBehaviourCommand.Execute(null); break;
                case "channels": _host.NavigateToChannelsCommand.Execute(null); break;
                case "versions": _host.NavigateToVersionsManagerCommand.Execute(null); break;
                case "appearance": _host.NavigateToAppearanceCommand.Execute(null); break;
                case "fastflags": _host.NavigateToFastFlagsCommand.Execute(null); break;
                case "vipserver": _host.NavigateToVipServerCommand.Execute(null); break;
                case "news": _host.NavigateToNewsCommand.Execute(null); break;
                case "multiinstance": _host.NavigateToMultiInstanceCommand.Execute(null); break;
                case "banasync": _host.NavigateToBanAsyncCommand.Execute(null); break;
                case "hwidspoofer": _host.NavigateToHwidSpooferCommand.Execute(null); break;
                case "regionselector": _host.NavigateToRegionSelectorCommand.Execute(null); break;
                case "shortcuts": _host.NavigateToShortcutsCommand.Execute(null); break;
                case "globalsettings": _host.NavigateToGlobalSettingsCommand.Execute(null); break;
                case "about": _host.OpenAboutCommand.Execute(null); break;
            }
        }
    }

    public sealed class ToolsHubItem(string title, string subtitle, string tag, LucideIconNames icon)
    {
        public string Title { get; } = title;
        public string Subtitle { get; } = subtitle;
        public string Tag { get; } = tag;
        public LucideIconNames Icon { get; } = icon;
    }
}
