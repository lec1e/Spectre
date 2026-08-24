using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using Froststrap.Utility.RoValra;
using System.Collections.ObjectModel;

namespace Froststrap.UI.ViewModels.Settings
{
    public class SortOrderComboBoxItem
    {
        public string Content { get; set; } = "";
        public int Tag { get; set; }
    }

    public class RegionSelectorViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "RegionSelectorViewModel";
        private const string BestPingKey = "__BEST_PING__";
        private readonly HashSet<string> _displayedServerIds = [];
        private RobloxServerFetcher? _fetcher;
        private CancellationTokenSource? _searchDebounceCts;
        private string? _regionCursor;

        #region Fields
        private bool _hasSearched;
        private string _placeId = "";
        private RegionPingInfo? _selectedRegion;
        private bool _isLoading;
        private bool _isGameSearchLoading;
        private string _loadingMessage = "";
        private string? _roblosecurity;
        private bool _hasValidCookies;
        private string _searchQuery = "";
        private OmniSearchContent? _selectedSearchResult;
        private int _selectedSortOrder = 2;
        private SortOrderComboBoxItem? _selectedSortOrderItem;
        private int _lastFetchProcessedCount;
        private string? _thumbnailUrl;
        private string _selectedGameName = "";
        private bool _isSearchFlyoutOpen;
        private string _userLocationHint = "";
        #endregion

        #region Properties
        public bool HasSearched
        {
            get => _hasSearched;
            set => SetProperty(ref _hasSearched, value);
        }

        public string PlaceId
        {
            get => _placeId;
            set
            {
                if (SetProperty(ref _placeId, value))
                    SearchCommand.NotifyCanExecuteChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(ServerListMessage));
                    OnPropertyChanged(nameof(IsServerListEmptyAndNotLoading));
                    OnPropertyChanged(nameof(ShowLoadingIndicator));
                    SearchCommand.NotifyCanExecuteChanged();
                    LoadMoreCommand.NotifyCanExecuteChanged();
                    SearchGamesCommand.NotifyCanExecuteChanged();
                    JoinBestCommand.NotifyCanExecuteChanged();
                    RefreshPingsCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsGameSearchLoading
        {
            get => _isGameSearchLoading;
            set
            {
                if (SetProperty(ref _isGameSearchLoading, value))
                {
                    OnPropertyChanged(nameof(ShowLoadingIndicator));
                    SearchGamesCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public string? Roblosecurity
        {
            get => _roblosecurity;
            set => SetProperty(ref _roblosecurity, value);
        }

        public bool HasValidCookies
        {
            get => _hasValidCookies;
            set
            {
                if (SetProperty(ref _hasValidCookies, value))
                    OnPropertyChanged(nameof(ServerListMessage));
            }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    OnSearchQueryChanged(value);
                    SearchGamesCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public OmniSearchContent? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set
            {
                if (SetProperty(ref _selectedSearchResult, value))
                    OnSelectedSearchResultChanged(value);
            }
        }

        public int SelectedSortOrder
        {
            get => _selectedSortOrder;
            set => SetProperty(ref _selectedSortOrder, value);
        }

        public SortOrderComboBoxItem? SelectedSortOrderItem
        {
            get => _selectedSortOrderItem;
            set
            {
                if (SetProperty(ref _selectedSortOrderItem, value))
                    OnSelectedSortOrderItemChanged(value);
            }
        }

        public int LastFetchProcessedCount
        {
            get => _lastFetchProcessedCount;
            set => SetProperty(ref _lastFetchProcessedCount, value);
        }

        public string? ThumbnailUrl
        {
            get => _thumbnailUrl;
            set
            {
                if (SetProperty(ref _thumbnailUrl, value))
                    OnPropertyChanged(nameof(HasThumbnailPreview));
            }
        }

        public string SelectedGameName
        {
            get => _selectedGameName;
            set => SetProperty(ref _selectedGameName, value);
        }

        public bool IsSearchFlyoutOpen
        {
            get => _isSearchFlyoutOpen;
            set => SetProperty(ref _isSearchFlyoutOpen, value);
        }

        public string UserLocationHint
        {
            get => _userLocationHint;
            set => SetProperty(ref _userLocationHint, value);
        }

        public ObservableCollection<RegionPingInfo> Regions { get; } = [];
        public ObservableCollection<ServerEntry> Servers { get; } = [];
        public ObservableCollection<OmniSearchContent> SearchResults { get; } = [];

        public List<SortOrderComboBoxItem> SortOrderOptions { get; } =
        [
            new() { Content = Strings.Menu_RegionSelector_LargeServers, Tag = 2 },
            new() { Content = Strings.Menu_RegionSelector_SmallServers, Tag = 1 }
        ];

        public bool IsServerListEmpty => Servers.Count == 0;
        public bool IsServerListEmptyAndNotLoading => IsServerListEmpty && !IsLoading;
        public bool ShowLoadingIndicator => IsLoading && !IsGameSearchLoading;
        public bool HasThumbnailPreview => !string.IsNullOrWhiteSpace(ThumbnailUrl);
        public bool CanLoadMore => !string.IsNullOrWhiteSpace(_regionCursor);

        public string ServerListMessage =>
            IsLoading ? "" :
            !HasSearched ? "Pick a game and region (or Best ping), then Search. Powered by RoValra datacenter data." :
            IsServerListEmpty ? (LastFetchProcessedCount == 0 ? Strings.Menu_RegionSelector_NoPublicServers : Strings.Menu_RegionSelector_NoServersForRegion) : "";

        public RegionPingInfo? SelectedRegion
        {
            get => _selectedRegion;
            set
            {
                if (SetProperty(ref _selectedRegion, value))
                {
                    SearchCommand.NotifyCanExecuteChanged();
                    JoinBestCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand LoadMoreCommand { get; }
        public IAsyncRelayCommand SearchGamesCommand { get; }
        public IAsyncRelayCommand JoinBestCommand { get; }
        public IAsyncRelayCommand RefreshPingsCommand { get; }
        #endregion

        public RegionSelectorViewModel()
        {
            Servers.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsServerListEmpty));
                OnPropertyChanged(nameof(IsServerListEmptyAndNotLoading));
            };

            SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(PlaceId) && SelectedRegion is not null);
            SearchGamesCommand = new AsyncRelayCommand(SearchGamesAsync, () => !IsLoading && !IsGameSearchLoading && !string.IsNullOrWhiteSpace(SearchQuery));
            LoadMoreCommand = new AsyncRelayCommand(LoadMoreServersAsync, () => !IsLoading && CanLoadMore);
            JoinBestCommand = new AsyncRelayCommand(JoinBestAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(PlaceId));
            RefreshPingsCommand = new AsyncRelayCommand(RefreshPingsAsync, () => !IsLoading);

            _ = InitializeAsync();
            SelectedSortOrderItem = SortOrderOptions.FirstOrDefault(x => x.Tag == 2);
        }

        private void OnSearchQueryChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                IsSearchFlyoutOpen = false;
                SearchResults.Clear();
            }

            if (long.TryParse(value, out _))
                PlaceId = value;

            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            _searchDebounceCts = new CancellationTokenSource();
            _ = DebouncedSearchTriggerAsync(_searchDebounceCts.Token);
        }

        private void OnSelectedSearchResultChanged(OmniSearchContent? value)
        {
            if (value == null) return;
            PlaceId = value.RootPlaceId.ToString();
            SearchQuery = value.RootPlaceId.ToString();
            SelectedGameName = value.Name ?? "";
            ThumbnailUrl = value.ThumbnailUrl;
            IsSearchFlyoutOpen = false;
        }

        private void OnSelectedSortOrderItemChanged(SortOrderComboBoxItem? value)
        {
            if (value != null)
                SelectedSortOrder = value.Tag;
        }

        private async Task DebouncedSearchTriggerAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(600, token);
                if (!token.IsCancellationRequested && !IsLoading && !string.IsNullOrWhiteSpace(SearchQuery))
                    await SearchGamesAsync(token);
            }
            catch (OperationCanceledException) { }
        }

        private async Task InitializeAsync()
        {
            try
            {
                _fetcher = new RobloxServerFetcher();
                Roblosecurity = await _fetcher.ResolveCookieAsync();
                HasValidCookies = !string.IsNullOrWhiteSpace(Roblosecurity);
                await LoadRegionsAsync(measureLive: false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static RegionPingInfo CreateBestPingOption() => new()
        {
            Key = BestPingKey,
            DisplayName = "Best ping (automatic)",
            City = "",
            Country = "",
            PingMs = 0,
            IsMeasured = false,
            DistanceKm = 0
        };

        private async Task LoadRegionsAsync(bool measureLive)
        {
            IsLoading = true;
            LoadingMessage = measureLive
                ? "Measuring region latency (RoValra)…"
                : "Loading RoValra datacenters…";

            try
            {
                var ranked = await RegionPingService.GetRegionsAsync(forceRefresh: true, measureLive: measureLive);
                Regions.Clear();
                Regions.Add(CreateBestPingOption());
                foreach (var r in ranked)
                    Regions.Add(r);

                SelectedRegion = Regions.FirstOrDefault();
                if (ranked.Count > 0)
                {
                    var best = ranked[0];
                    UserLocationHint = $"Closest: {best.DisplayName} ({best.PingText}) · {ranked.Count} regions via RoValra";
                }
                else
                {
                    UserLocationHint = "Could not load RoValra datacenters.";
                }

                LoadingMessage = string.Format(Strings.Menu_RegionSelector_LoadedRegions, ranked.Count);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                LoadingMessage = Strings.Menu_RegionSelector_FailedToLoadDatacenters;
            }

            IsLoading = false;
            await Task.Delay(600);
            LoadingMessage = "";
        }

        private async Task RefreshPingsAsync()
        {
            string? keepKey = SelectedRegion?.Key;
            await LoadRegionsAsync(measureLive: true);
            if (!string.IsNullOrEmpty(keepKey))
                SelectedRegion = Regions.FirstOrDefault(r => r.Key == keepKey) ?? Regions.FirstOrDefault();
        }

        private async Task SearchAsync()
        {
            if (SelectedRegion is null)
            {
                _ = Frontend.ShowMessageBox(Strings.Menu_RegionSelector_PleaseSelectRegion, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(PlaceId, out long placeId))
                return;

            HasSearched = true;
            IsLoading = true;
            LoadingMessage = Strings.Menu_RegionSelector_SearchingServers;
            Servers.Clear();
            _displayedServerIds.Clear();
            _regionCursor = null;
            LastFetchProcessedCount = 0;
            OnPropertyChanged(nameof(CanLoadMore));

            try
            {
                RegionPingInfo? target = SelectedRegion.Key == BestPingKey
                    ? await FindBestRegionWithServersAsync(placeId)
                    : SelectedRegion;

                if (target is null || target.Key == BestPingKey)
                {
                    LoadingMessage = Strings.Menu_RegionSelector_NoServersForRegion;
                    LastFetchProcessedCount = 0;
                    return;
                }

                // Keep combo selection on the concrete region we resolved for Best ping.
                if (SelectedRegion.Key == BestPingKey)
                    SelectedRegion = Regions.FirstOrDefault(r => r.Key == target.Key) ?? target;

                await LoadRegionServersAsync(placeId, target, resetCursor: true);
            }
            finally
            {
                IsLoading = false;
                await Task.Delay(400);
                LoadingMessage = "";
            }
        }

        private async Task<RegionPingInfo?> FindBestRegionWithServersAsync(long placeId)
        {
            foreach (var region in Regions.Where(r => r.Key != BestPingKey))
            {
                LoadingMessage = $"Checking {region.DisplayName} ({region.PingText})…";
                var resp = await RoValraApi.GetServersByRegionAsync(placeId, region.Country, region.City, "0");
                if (resp?.Servers is { Count: > 0 })
                    return region;
            }
            return null;
        }

        private async Task LoadRegionServersAsync(long placeId, RegionPingInfo region, bool resetCursor)
        {
            if (resetCursor)
                _regionCursor = "0";

            var resp = await RoValraApi.GetServersByRegionAsync(
                placeId, region.Country, region.City, _regionCursor ?? "0");

            if (resp?.Servers is null)
            {
                LastFetchProcessedCount = 0;
                _regionCursor = null;
                OnPropertyChanged(nameof(CanLoadMore));
                return;
            }

            LastFetchProcessedCount = resp.Servers.Count;
            int number = Servers.Count + 1;

            // Optional player counts from Roblox public list (matched by job id).
            Dictionary<string, (int playing, int max)>? playerMap = null;
            try
            {
                var publicList = await ServerBrowserClient.FetchServersAsync(placeId, null, SelectedSortOrder);
                if (publicList?.Data is not null)
                {
                    playerMap = publicList.Data
                        .Where(s => !string.IsNullOrEmpty(s.Id))
                        .GroupBy(s => s.Id)
                        .ToDictionary(g => g.Key, g => (g.First().Playing, g.First().MaxPlayers));
                }
            }
            catch { /* non-fatal */ }

            foreach (var s in resp.Servers)
            {
                if (string.IsNullOrEmpty(s.ServerId) || !_displayedServerIds.Add(s.ServerId))
                    continue;

                string players = "—";
                if (playerMap is not null && playerMap.TryGetValue(s.ServerId, out var p))
                    players = $"{p.playing}/{p.max}";
                else if (s.Playing.HasValue && s.MaxPlayers.HasValue)
                    players = $"{s.Playing}/{s.MaxPlayers}";

                string uptime = "—";
                if (s.FirstSeen.HasValue)
                {
                    var span = DateTime.UtcNow - (s.FirstSeen.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(s.FirstSeen.Value, DateTimeKind.Utc)
                        : s.FirstSeen.Value.ToUniversalTime());
                    uptime = span.TotalSeconds < 60
                        ? "Just started"
                        : $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
                }

                Servers.Add(new ServerEntry
                {
                    Number = number++,
                    ServerId = s.ServerId,
                    Players = players,
                    Region = region.DisplayName,
                    DataCenterId = s.DatacenterId,
                    Uptime = uptime,
                    PingMs = region.PingMs,
                    PingIsMeasured = region.IsMeasured,
                    IpAddress = s.IpAddress,
                    JoinCommand = new RelayCommand(() => JoinServer(s.ServerId!))
                });
            }

            _regionCursor = string.IsNullOrWhiteSpace(resp.NextCursor) ? null : resp.NextCursor;
            OnPropertyChanged(nameof(CanLoadMore));
            LoadMoreCommand.NotifyCanExecuteChanged();
        }

        private void JoinServer(string serverId)
        {
            if (!long.TryParse(PlaceId, out var placeId)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"roblox://experiences/start?placeId={placeId}&gameInstanceId={serverId}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private async Task JoinBestAsync()
        {
            if (!long.TryParse(PlaceId, out long placeId))
            {
                _ = Frontend.ShowMessageBox("Enter a Place ID or search for a game first.", MessageBoxImage.Warning);
                return;
            }

            IsLoading = true;
            LoadingMessage = "Finding lowest-ping region with open servers…";
            try
            {
                var best = await FindBestRegionWithServersAsync(placeId);
                if (best is null)
                {
                    _ = Frontend.ShowMessageBox(Strings.Menu_RegionSelector_NoServersForRegion, MessageBoxImage.Information);
                    return;
                }

                SelectedRegion = Regions.FirstOrDefault(r => r.Key == best.Key) ?? best;
                var resp = await RoValraApi.GetServersByRegionAsync(placeId, best.Country, best.City, "0");
                string? jobId = resp?.Servers?.FirstOrDefault()?.ServerId;
                if (string.IsNullOrEmpty(jobId))
                {
                    _ = Frontend.ShowMessageBox(Strings.Menu_RegionSelector_NoServersForRegion, MessageBoxImage.Information);
                    return;
                }

                JoinServer(jobId);
                LoadingMessage = $"Joining {best.DisplayName} ({best.PingText})…";
            }
            finally
            {
                IsLoading = false;
                await Task.Delay(500);
                LoadingMessage = "";
            }
        }

        private async Task SearchGamesAsync(CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(SearchQuery) || long.TryParse(SearchQuery, out _)) return;

            IsGameSearchLoading = true;
            try
            {
                var results = await GameSearching.GetGameSearchResultsAsync(SearchQuery);
                if (token.IsCancellationRequested || results == null || results.Count == 0) return;

                var thumbRequests = results.Select(r => new ThumbnailRequest
                {
                    Type = ThumbnailType.GameIcon,
                    TargetId = r.UniverseId,
                    Size = "128x128"
                }).ToList();

                var fetchedUrls = await Thumbnails.GetThumbnailUrlsAsync(thumbRequests, token);
                if (token.IsCancellationRequested) return;

                for (int i = 0; i < results.Count; i++)
                {
                    if (fetchedUrls != null && i < fetchedUrls.Length && !string.IsNullOrEmpty(fetchedUrls[i]))
                    {
                        results[i].ThumbnailUrl = fetchedUrls[i];
                        try
                        {
                            var response = await App.HttpClient.GetByteArrayAsync(fetchedUrls[i], token);
                            using var ms = new MemoryStream(response);
                            results[i].ThumbnailBitmap = new Bitmap(ms);
                        }
                        catch { /* ignore thumb failures */ }
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    SearchResults.Clear();
                    foreach (var res in results) SearchResults.Add(res);
                    IsSearchFlyoutOpen = SearchResults.Count > 0 && !string.IsNullOrWhiteSpace(SearchQuery);
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Search error: {ex.Message}");
            }
            finally
            {
                IsGameSearchLoading = false;
            }
        }

        private async Task LoadMoreServersAsync()
        {
            if (SelectedRegion is null || SelectedRegion.Key == BestPingKey || !long.TryParse(PlaceId, out long placeId))
                return;

            IsLoading = true;
            try
            {
                await LoadRegionServersAsync(placeId, SelectedRegion, resetCursor: false);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
