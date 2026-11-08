using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using System.Windows;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.ContextMenu
{
    internal class ServerInformationViewModel : NotifyPropertyChangedViewModel
    {
        private readonly ActivityWatcher _activityWatcher;

        public string InstanceId => _activityWatcher.Data.JobId;

        public string ServerType => _activityWatcher.Data.ServerType.ToTranslatedString();

        public string ServerLocation { get; private set; } = Strings.Common_Loading;

        public string ServerUptime { get; private set; } = Strings.Common_Loading;

        public static bool ServerLocationVisibility => App.Settings.Prop.ShowServerDetails;
        public static bool ServerUptimeVisibility => App.Settings.Prop.ShowServerUptime;

        public ICommand CopyInstanceIdCommand => new RelayCommand<Visual>(CopyInstanceId);

        public ServerInformationViewModel(Watcher watcher)
        {
            _activityWatcher = watcher.ActivityWatcher!;

            if (ServerLocationVisibility)
                QueryServerLocation();

            if (ServerUptimeVisibility)
                QueryServerUptime();
        }

        public async void QueryServerLocation()
        {
            try
            {
                string? location = await _activityWatcher.Data.QueryServerLocation();
                ServerLocation = string.IsNullOrEmpty(location) ? Strings.Common_NotAvailable : location;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ServerInformationViewModel::QueryServerLocation", ex);
                ServerLocation = Strings.Common_NotAvailable;
            }

            OnPropertyChanged(nameof(ServerLocation));
        }

        public async void QueryServerUptime()
        {
            DateTime? serverTime = _activityWatcher.Data.StartTime;
            TimeSpan _serverUptime = TimeSpan.Zero;
            if (serverTime is not null)
                _serverUptime = DateTime.UtcNow - serverTime.Value;

            ServerUptime = Time.FormatTimeSpan(_serverUptime);

            OnPropertyChanged(nameof(ServerUptime));
        }

        private async void CopyInstanceId(Visual? visual)
        {
            var topLevel = TopLevel.GetTopLevel(visual);

            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(InstanceId);
            }
        }

        public static ICommand CloseCommand => new RelayCommand<Window>(window => window?.Close());

    }
}