using Avalonia.Threading;
using Froststrap.Utility;
using Froststrap.UI.ViewModels;
using System.Collections.ObjectModel;

namespace Froststrap.UI.ViewModels.Dialogs
{
    public sealed class VipServerPickerViewModel : NotifyPropertyChangedViewModel
    {
        public sealed class VipServerEntry
        {
            public required string Name { get; init; }
            public required string AccessCode { get; init; }
        }

        private const string LOG_IDENT = "VipServerPickerViewModel";

        private readonly long _placeId;

        public ObservableCollection<VipServerEntry> Servers { get; } = [];

        private VipServerEntry? _selectedServer;
        public VipServerEntry? SelectedServer
        {
            get => _selectedServer;
            set => SetProperty(ref _selectedServer, value);
        }

        private string _gameName = "Loading VIP servers...";
        public string GameName
        {
            get => _gameName;
            set => SetProperty(ref _gameName, value);
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    // Ensure empty/loading state updates together.
                    OnPropertyChanged(nameof(ShowEmptyState));
                }
            }
        }

        private bool _showEmptyState;
        public bool ShowEmptyState
        {
            get => _showEmptyState;
            set => SetProperty(ref _showEmptyState, value);
        }

        public VipServerPickerViewModel(long placeId)
        {
            _placeId = placeId;

            // Fire-and-forget: dialog opens immediately, list fills in after the HTML fetch.
            if (_placeId > 0)
                _ = LoadAsync();
            else
            {
                GameName = "VIP servers";
                IsLoading = false;
                ShowEmptyState = true;
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                ShowEmptyState = false;

                var vipList = await RbxServersClient.FetchVipServersAsync(_placeId);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    GameName = vipList.GameName;
                    Servers.Clear();

                    foreach (var s in vipList.Servers)
                        Servers.Add(new VipServerEntry { Name = s.Name, AccessCode = s.AccessCode });

                    SelectedServer = Servers.FirstOrDefault();
                    IsLoading = false;
                    ShowEmptyState = Servers.Count == 0;
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    GameName = $"VIP servers for place {_placeId}";
                    Servers.Clear();
                    SelectedServer = null;
                    IsLoading = false;
                    ShowEmptyState = true;
                });
            }
        }
    }
}

