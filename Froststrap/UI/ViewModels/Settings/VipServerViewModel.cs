using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Models.Entities;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.Utility;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings
{
    public sealed class VipFeaturedGameCard
    {
        public required long PlaceId { get; init; }
        public required string Name { get; init; }
        public required string ThumbnailUrl { get; init; }
        public int ServerCount { get; init; }
    }

    public class VipServerViewModel : NotifyPropertyChangedViewModel
    {
        private readonly List<VipFeaturedGameCard> _catalog = [];
        private bool _isLoading;
        private string _statusMessage = "";
        private string _searchQuery = "";
        private CancellationTokenSource? _searchCts;

        public ObservableCollection<VipFeaturedGameCard> VisibleGames { get; } = [];

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                    _ = DebouncedSearchAsync(value);
            }
        }

        public bool HasGames => VisibleGames.Count > 0;

        public ICommand RefreshGamesCommand { get; }
        public ICommand SearchGamesCommand { get; }
        public ICommand OpenVipServersCommand { get; }
        public ICommand VisitGamePageCommand { get; }

        public VipServerViewModel()
        {
            VisibleGames.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGames));

            RefreshGamesCommand = new AsyncRelayCommand(LoadFeaturedGamesAsync);
            SearchGamesCommand = new AsyncRelayCommand(SearchNowAsync);
            OpenVipServersCommand = new AsyncRelayCommand<VipFeaturedGameCard>(OpenVipServersAsync);
            VisitGamePageCommand = new RelayCommand<VipFeaturedGameCard>(game =>
            {
                if (game is null)
                    return;

                Process.Start(new ProcessStartInfo($"https://www.roblox.com/games/{game.PlaceId}") { UseShellExecute = true });
            });

            _ = LoadFeaturedGamesAsync();
        }

        private async Task DebouncedSearchAsync(string value)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await Task.Delay(350, token);
                if (!token.IsCancellationRequested)
                    await ApplySearchAsync(value, token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private Task SearchNowAsync() => ApplySearchAsync(SearchQuery, CancellationToken.None);

        private async Task LoadFeaturedGamesAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading featured VIP games from RBXServers...";

            try
            {
                var games = await RbxServersClient.FetchFeaturedGamesAsync();

                _catalog.Clear();
                foreach (var game in games)
                {
                    _catalog.Add(new VipFeaturedGameCard
                    {
                        PlaceId = game.PlaceId,
                        Name = game.Name,
                        ThumbnailUrl = game.ThumbnailUrl,
                        ServerCount = game.ServerCount
                    });
                }

                await ApplySearchAsync(SearchQuery, CancellationToken.None);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("VipServerViewModel", ex);
                StatusMessage = $"Couldn't load RBXServers games: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ApplySearchAsync(string query, CancellationToken token)
        {
            string trimmed = query?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetVisible(_catalog);
                StatusMessage = _catalog.Count == 0
                    ? "No featured VIP games were returned by RBXServers."
                    : $"Showing {_catalog.Count} featured game{(_catalog.Count == 1 ? "" : "s")} from RBXServers.";
                return;
            }

            IsLoading = true;
            StatusMessage = "Searching RBXServers...";

            try
            {
                var matches = new List<VipFeaturedGameCard>();
                var seen = new HashSet<long>();

                if (long.TryParse(trimmed, out long placeId) && placeId > 0)
                {
                    var summary = await RbxServersClient.FetchGameSummaryAsync(placeId, token);
                    token.ThrowIfCancellationRequested();
                    if (summary is not null)
                    {
                        matches.Add(ToCard(summary));
                        seen.Add(summary.PlaceId);
                    }
                }

                foreach (var game in _catalog
                             .Select(g => (Game: g, Score: GameNameMatch.Score(g.Name, trimmed)))
                             .Where(x => x.Score > 0)
                             .OrderByDescending(x => x.Score)
                             .ThenByDescending(x => x.Game.ServerCount))
                {
                    if (seen.Add(game.Game.PlaceId))
                        matches.Add(game.Game);
                }

                // Expand beyond the homepage featured list: Roblox candidates verified on RBXServers.
                if (matches.Count < 8 && !long.TryParse(trimmed, out _))
                {
                    var omni = await GameSearching.GetGameSearchResultsAsync(trimmed);
                    token.ThrowIfCancellationRequested();

                    var verifyTasks = omni
                        .Where(o => o.RootPlaceId > 0 && !seen.Contains(o.RootPlaceId))
                        .Take(8)
                        .Select(async o =>
                        {
                            var summary = await RbxServersClient.FetchGameSummaryAsync(o.RootPlaceId, token);
                            return summary is null ? null : ToCard(summary);
                        })
                        .ToList();

                    var verified = await Task.WhenAll(verifyTasks);
                    foreach (var card in verified)
                    {
                        if (card is null || !seen.Add(card.PlaceId))
                            continue;
                        matches.Add(card);
                    }
                }

                matches = matches
                    .Select(g => (Game: g, Score: GameNameMatch.Score(g.Name, trimmed)))
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Game.ServerCount)
                    .Select(x => x.Game)
                    .ToList();

                SetVisible(matches);
                StatusMessage = matches.Count == 0
                    ? $"No RBXServers games matched \"{trimmed}\"."
                    : $"Found {matches.Count} RBXServers game{(matches.Count == 1 ? "" : "s")} for \"{trimmed}\".";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("VipServerViewModel::Search", ex);
                StatusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void SetVisible(IEnumerable<VipFeaturedGameCard> games)
        {
            void apply()
            {
                VisibleGames.Clear();
                foreach (var game in games)
                    VisibleGames.Add(game);
            }

            if (Dispatcher.UIThread.CheckAccess())
                apply();
            else
                Dispatcher.UIThread.Post(apply);
        }

        private static VipFeaturedGameCard ToCard(RbxServersFeaturedGame game) => new()
        {
            PlaceId = game.PlaceId,
            Name = game.Name,
            ThumbnailUrl = game.ThumbnailUrl,
            ServerCount = game.ServerCount
        };

        private async Task OpenVipServersAsync(VipFeaturedGameCard? game)
        {
            if (game is null)
                return;

            var dialog = new VipServerPickerDialog(game.PlaceId);
            string? accessCode = null;
            var owner = GetOwnerWindow();

            if (owner is not null)
                accessCode = await dialog.ShowDialog<string?>(owner);
            else
                dialog.Show();

            if (string.IsNullOrWhiteSpace(accessCode))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = $"roblox://experiences/start?placeId={game.PlaceId}&accessCode={Uri.EscapeDataString(accessCode)}",
                UseShellExecute = true
            });
        }

        private static Window? GetOwnerWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow ?? desktop.Windows.FirstOrDefault();

            return null;
        }
    }
}
