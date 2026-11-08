using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform;
using Avalonia.Threading;
using Froststrap.Integrations;
using Froststrap.Models.Entities;
using Froststrap.UI.Elements.ContextMenu;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Elements.Dialogs;

namespace Froststrap.UI
{
    public class NotifyIconWrapper : IDisposable
    {
        private bool _isDisposed = false;
        private readonly TrayIcon _trayIcon;
        private readonly MenuContainer _menuContainer;
        private readonly Watcher _watcher;
        private ActivityWatcher? ActivityWatcher => _watcher.ActivityWatcher;

        private DateTime _lastClickTime = DateTime.MinValue;
        private const int DoubleClickThresholdMs = 300;

        public NotifyIconWrapper(Watcher watcher)
        {
            App.Logger.WriteLine("NotifyIconWrapper::NotifyIconWrapper", "Initializing Avalonia TrayIcon");

            _watcher = watcher;
            _menuContainer = new MenuContainer(_watcher);

            var nativeMenu = NativeMenu.GetMenu(_menuContainer);

            _trayIcon = new TrayIcon
            {
                Icon = SpectreLogo.CreateBrandIcon()
                    ?? new WindowIcon(AssetLoader.Open(new Uri("avares://Spectre/Spectre.ico"))),
                ToolTipText = App.BrandName,
                Menu = nativeMenu
            };

            _trayIcon.Clicked += OnTrayIconClicked;
            RefreshTip();

            if (ActivityWatcher is not null)
            {
                ActivityWatcher.OnGameJoin += (_, _) => Dispatcher.UIThread.Post(RefreshTip);
                ActivityWatcher.OnGameLeave += (_, _) => Dispatcher.UIThread.Post(RefreshTip);
                var tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                tipTimer.Tick += (_, _) => RefreshTip();
                tipTimer.Start();
            }

            if (ActivityWatcher is not null && App.Settings.Prop.ShowServerDetails && !OperatingSystem.IsMacOS())
                ActivityWatcher.ShowNotif += ShowNotif;

            TrayIcon.GetIcons(Application.Current!)?.Add(_trayIcon);
        }


        // On macos simply clicking the icon instantly opens the menu so double click action isnt possible
        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            if (OperatingSystem.IsMacOS())
                return;

            HandleWindowsDoubleClickLogic();
        }

        private void HandleWindowsDoubleClickLogic()
        {
            DateTime now = DateTime.Now;
            double elapsed = (now - _lastClickTime).TotalMilliseconds;

            if (elapsed <= DoubleClickThresholdMs)
            {
                _lastClickTime = DateTime.MinValue;
                HandleDoubleClickAction();
            }
            else
            {
                _lastClickTime = now;
            }
        }

        private void HandleDoubleClickAction()
        {
            switch (App.Settings.Prop.DoubleClickAction)
            {
                case TrayDoubleClickAction.None:
                    _ = Frontend.ShowMessageBox("You don't have the double-click action set to anything.", MessageBoxImage.Information);
                    break;

                case TrayDoubleClickAction.GameHistory:
                    if (!App.Settings.Prop.ShowGameHistoryMenu)
                    {
                        _ = Frontend.ShowMessageBox("Enable 'Game History' in settings to use this feature.", MessageBoxImage.Information);
                        return;
                    }
                    var history = new ServerHistory(ActivityWatcher!);
                    history.Show();
                    break;

                case TrayDoubleClickAction.ServerInfo:
                    if (!App.Settings.Prop.ShowServerDetails)
                    {
                        _ = Frontend.ShowMessageBox("Enable 'Query Server Location' in settings to use this feature.", MessageBoxImage.Information);
                        return;
                    }

                    if (ActivityWatcher?.InGame == true)
                        _menuContainer.ShowServerInformationWindow();
                    else
                        _ = Frontend.ShowMessageBox("Join a game first to view server information.", MessageBoxImage.Information);
                    break;
            }
        }

        private void RefreshTip()
        {
            if (_isDisposed)
                return;

            try
            {
                string tip = App.BrandName;
                if (ActivityWatcher?.InGame == true)
                {
                    string? name = ActivityWatcher.Data.UniverseDetails?.Data?.Name;
                    tip = string.IsNullOrWhiteSpace(name) ? $"{App.BrandName} · Playing" : $"Playing {name}";
                }
                else if (ActivityWatcher is not null)
                {
                    int count = Froststrap.Utility.InstallSession.Clients(LaunchMode.Player).Count;
                    tip = count > 1 ? $"{App.BrandName} · {count} Roblox" : $"{App.BrandName} · Roblox";
                }

                _trayIcon.ToolTipText = tip;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NotifyIconWrapper::RefreshTip", ex);
            }
        }

        public async void ShowNotif(object? sender, EventArgs e)
        {
            if (ActivityWatcher?.Data == null) return;

            var activity = ActivityWatcher.Data;
            string kind = activity.ServerType switch
            {
                ServerType.Public => "Public server",
                ServerType.Private => "Private server",
                ServerType.Reserved => "Reserved server",
                _ => "Server"
            };

            string? serverLocation = null;
            try
            {
                if (ActivityWatcher.Data.MachineAddressValid)
                    serverLocation = await ActivityWatcher.Data.QueryServerLocation();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NotifyIconWrapper::ShowNotif", $"Location query failed: {ex.Message}");
            }

            string? serverUptime;
            DateTime? serverTime = ActivityWatcher.Data.StartTime;

            if (serverTime is not null)
            {
                TimeSpan _serverUptime = DateTime.UtcNow - serverTime.Value;
                
                if (_serverUptime.TotalMinutes < 1)
                    serverUptime = "0 minutes";
                else
                    serverUptime = Time.FormatTimeSpan(_serverUptime);
            }
            else
            {
                serverUptime = "0 minutes";
            }

            string location = string.IsNullOrEmpty(serverLocation) ? "Location unavailable" : serverLocation;
            string detail = App.Settings.Prop.ShowServerUptime
                ? $"{kind} · {location} · up {serverUptime}"
                : $"{kind} · {location}";

            ShowAlert(kind, detail, activity: activity);
        }

        public void ShowAlert(string title, string message, int duration = 8, NotificationType category = NotificationType.Information, ActivityData? activity = null)
        {
            if (_isDisposed) return;
            DesktopToastWindow.ShowToast(title, message, duration, activity);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            App.Logger.WriteLine("NotifyIconWrapper::Dispose", "Cleaning up TrayIcon and MenuContainer");

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _trayIcon.IsVisible = false;

                    var trayIcons = TrayIcon.GetIcons(Application.Current!);
                    trayIcons?.Remove(_trayIcon);

                    _menuContainer.Close();

                    _trayIcon.Dispose();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("NotifyIconWrapper::Dispose", $"Error during cleanup: {ex.Message}");
                }
            });

            GC.SuppressFinalize(this);
        }
    }
}
