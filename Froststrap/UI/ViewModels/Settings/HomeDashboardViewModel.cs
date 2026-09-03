using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Enums;
using Froststrap.Integrations;
using Froststrap.Models;
using Froststrap.Models.APIs.Roblox;
using Froststrap.Utility;
using LucideAvalonia.Enum;

namespace Froststrap.UI.ViewModels.Settings
{
    public class HomeDashboardViewModel : NotifyPropertyChangedViewModel
    {
        private static readonly string[] TintPalette =
        [
            "#7F1D1D", "#9F1239", "#4A1518", "#140808",
            "#C41E3A", "#5C101C", "#7F1D1D", "#9F1239"
        ];

        private readonly MainWindowViewModel _host;
        private readonly string _historyPath = Path.Combine(Paths.Cache, "GameHistory.json");

        public string Greeting => "Welcome back";
        public string DisplayName => App.BrandName;

        public ObservableCollection<HomeGameCard> RecentGames { get; } = [];
        public ObservableCollection<HomeFeaturedItem> FeaturedGames { get; } = [];
        public ObservableCollection<HomeNewsItem> NewsItems { get; } = [];

        public bool HasRecentGames => RecentGames.Count > 0;
        public bool HasNoRecentGames => RecentGames.Count == 0;

        public ICommand LaunchRobloxCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OpenQuickPlayCommand { get; }
        public ICommand OpenNewsCommand { get; }
        public ICommand PlayPlaceCommand { get; }

        public HomeDashboardViewModel(MainWindowViewModel host)
        {
            _host = host;
            LaunchRobloxCommand = new RelayCommand(() => _host.SaveAndLaunchSettings());
            OpenFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    if (Directory.Exists(Paths.Base))
                        Utilities.ShellExecute(Paths.Base);
                }
                catch { /* ignore */ }
            });
            OpenQuickPlayCommand = new RelayCommand(() => _host.NavigateToQuickPlayCommand.Execute(null));
            OpenNewsCommand = new RelayCommand(() => _host.NavigateToNewsCommand.Execute(null));
            PlayPlaceCommand = new RelayCommand<long?>(placeId =>
            {
                if (placeId is null or <= 0) return;
                try
                {
                    Utilities.ShellExecute($"roblox://experiences/start?placeId={placeId}");
                }
                catch { /* ignore */ }
            });

            SeedStaticContent();
            _ = LoadRecentGamesAsync();
        }

        private void SeedStaticContent()
        {
            FeaturedGames.Clear();
            NewsItems.Clear();

            FeaturedGames.Add(new HomeFeaturedItem(
                "Midnight Rail",
                "Neon trains and night city runs.",
                "94%",
                "12.4K",
                4924922222,
                "#7F1D1D"));
            FeaturedGames.Add(new HomeFeaturedItem(
                "The Strongest Battlegrounds",
                "Anime battling with flashy combat.",
                "83%",
                "45.7K",
                10450266301,
                "#7F1D1D"));

            NewsItems.Add(new HomeNewsItem($"{App.BrandName} v{App.Version} Released", "Dark crimson liquid glass shell", "just now", LucideIconNames.Sparkles));
            NewsItems.Add(new HomeNewsItem("Roblox Update", "Client and platform notes", "5d ago", LucideIconNames.Box));
            NewsItems.Add(new HomeNewsItem("Maintenance Scheduled", "Brief downtime window this week", "1w ago", LucideIconNames.Wrench));
        }

        private async Task LoadRecentGamesAsync()
        {
            try
            {
                var cards = new List<HomeGameCard>();

                var history = LoadLocalHistory();
                var universeIds = history.Select(x => x.UniverseId).Where(id => id > 0).Distinct().ToList();
                if (universeIds.Count > 0)
                {
                    try { await UniverseDetails.FetchBulk(string.Join(",", universeIds)); }
                    catch { /* ignore */ }
                }

                // 1) Local activity history (GameHistory.json)
                foreach (var entry in history.Take(8))
                {
                    if (entry.UniverseId <= 0 && entry.PlaceId <= 0)
                        continue;

                    var details = entry.UniverseId > 0
                        ? UniverseDetails.LoadFromCache(entry.UniverseId)
                        : null;
                    var last = entry.Servers.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
                    long placeId = entry.PlaceId > 0
                        ? entry.PlaceId
                        : details?.Data?.RootPlaceId ?? 0;
                    if (placeId <= 0)
                        continue;

                    cards.Add(new HomeGameCard(
                        details?.Data?.Name ?? $"Place {placeId}",
                        FormatLastPlayed(last?.JoinedAt),
                        placeId,
                        TintFor(placeId)));
                }

                // 2) Roblox "Recently Visited" when an AltMan account is active
                if (cards.Count < 4)
                {
                    foreach (var api in await FetchRecentlyVisitedFromApiAsync())
                    {
                        if (cards.Any(c => c.PlaceId == api.PlaceId))
                            continue;
                        cards.Add(api);
                        if (cards.Count >= 4)
                            break;
                    }
                }

                var top = cards.Take(4).ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RecentGames.Clear();
                    foreach (var c in top)
                        RecentGames.Add(c);
                    OnPropertyChanged(nameof(HasRecentGames));
                    OnPropertyChanged(nameof(HasNoRecentGames));
                });

                await LoadThumbnailsAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeDashboardViewModel", $"Recent games load failed: {ex.Message}");
            }
        }

        private List<GameHistoryEntry> LoadLocalHistory()
        {
            try
            {
                if (!File.Exists(_historyPath))
                    return [];
                string json = File.ReadAllText(_historyPath);
                return JsonSerializer.Deserialize<List<GameHistoryEntry>>(json) ?? [];
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeDashboardViewModel", $"GameHistory read failed: {ex.Message}");
                return [];
            }
        }

        private static async Task<List<HomeGameCard>> FetchRecentlyVisitedFromApiAsync()
        {
            try
            {
                var accountManager = AccountManager.Shared;
                var active = accountManager?.ActiveAccount;
                if (active is null)
                    return [];

                string? cookie = accountManager!.GetRoblosecurityForUser(active.UserIdLong);
                if (string.IsNullOrEmpty(cookie))
                    return [];

                var url = UrlBuilder.BuildApiUrl("apis", "search-landing-page-api/v1?sessionId=EclipseHome");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");

                var response = await Http.SendJson<RecentlyVisitedResponse>(request);
                var recentSort = response?.Sorts?.FirstOrDefault(s => s.SortId == "RecentlyVisited");
                if (recentSort?.Games is null)
                    return [];

                var list = new List<HomeGameCard>();
                for (int i = 0; i < recentSort.Games.Count && list.Count < 4; i++)
                {
                    var g = recentSort.Games[i];
                    if (g.RootPlaceId <= 0)
                        continue;
                    list.Add(new HomeGameCard(
                        string.IsNullOrWhiteSpace(g.Name) ? $"Place {g.RootPlaceId}" : g.Name,
                        "Recently visited",
                        g.RootPlaceId,
                        TintFor(g.RootPlaceId)));
                }
                return list;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeDashboardViewModel", $"API recent failed: {ex.Message}");
                return [];
            }
        }

        private static string FormatLastPlayed(DateTime? joinedAt)
        {
            if (joinedAt is null)
                return "Last played recently";

            var age = DateTime.UtcNow - joinedAt.Value.ToUniversalTime();
            if (age.TotalMinutes < 60)
                return "Last played just now";
            if (age.TotalHours < 24)
                return $"Last played {(int)age.TotalHours}h ago";
            if (age.TotalDays < 7)
                return $"Last played {(int)age.TotalDays}d ago";
            return $"Last played {joinedAt.Value.ToLocalTime():MMM d}";
        }

        private static string TintFor(long placeId)
            => TintPalette[Math.Abs(placeId.GetHashCode()) % TintPalette.Length];

        private async Task LoadThumbnailsAsync()
        {
            try
            {
                var cards = RecentGames.Cast<HomeThumbTarget>().Concat(FeaturedGames).ToList();
                if (cards.Count == 0)
                    return;

                var requests = cards.Select(c => new ThumbnailRequest
                {
                    TargetId = (ulong)c.PlaceId,
                    Type = ThumbnailType.PlaceIcon,
                    Size = "512x512",
                    Format = ThumbnailFormat.Png
                }).ToList();

                var wideRequests = cards.Select(c => new ThumbnailRequest
                {
                    TargetId = (ulong)c.PlaceId,
                    Type = ThumbnailType.GameThumbnail,
                    Size = "768x432",
                    Format = ThumbnailFormat.Png
                }).ToList();

                var urls = await Thumbnails.GetThumbnailUrlsAsync(wideRequests, CancellationToken.None);
                var missing = new List<ThumbnailRequest>();
                var missingIdx = new List<int>();
                for (int i = 0; i < cards.Count; i++)
                {
                    if (!string.IsNullOrEmpty(urls.ElementAtOrDefault(i)))
                        cards[i].ImageUrl = urls[i]!;
                    else
                    {
                        missing.Add(requests[i]);
                        missingIdx.Add(i);
                    }
                }

                if (missing.Count > 0)
                {
                    var fallback = await Thumbnails.GetThumbnailUrlsAsync(missing, CancellationToken.None);
                    for (int j = 0; j < missingIdx.Count && j < fallback.Length; j++)
                    {
                        if (!string.IsNullOrEmpty(fallback[j]))
                            cards[missingIdx[j]].ImageUrl = fallback[j]!;
                    }
                }

                foreach (var c in RecentGames) c.NotifyImage();
                foreach (var c in FeaturedGames) c.NotifyImage();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeDashboardViewModel", $"Thumbnail load failed: {ex.Message}");
            }
        }
    }

    public interface HomeThumbTarget
    {
        long PlaceId { get; }
        string ImageUrl { get; set; }
        void NotifyImage();
    }

    public sealed class HomeGameCard : NotifyPropertyChangedViewModel, HomeThumbTarget
    {
        public HomeGameCard(string name, string subtitle, long placeId, string tintHex)
        {
            Name = name;
            Subtitle = subtitle;
            PlaceId = placeId;
            TintBrush = new SolidColorBrush(Color.Parse(tintHex));
        }

        public string Name { get; }
        public string Subtitle { get; }
        public long PlaceId { get; }
        public IBrush TintBrush { get; }
        public string ImageUrl { get; set; } = "";
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
        public void NotifyImage()
        {
            OnPropertyChanged(nameof(ImageUrl));
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public sealed class HomeFeaturedItem : NotifyPropertyChangedViewModel, HomeThumbTarget
    {
        public HomeFeaturedItem(string name, string description, string rating, string players, long placeId, string tintHex)
        {
            Name = name;
            Description = description;
            Rating = rating;
            Players = players;
            PlaceId = placeId;
            TintBrush = new SolidColorBrush(Color.Parse(tintHex));
        }

        public string Name { get; }
        public string Description { get; }
        public string Rating { get; }
        public string Players { get; }
        public long PlaceId { get; }
        public IBrush TintBrush { get; }
        public string ImageUrl { get; set; } = "";
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
        public void NotifyImage()
        {
            OnPropertyChanged(nameof(ImageUrl));
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public sealed class HomeNewsItem(string title, string subtitle, string when, LucideIconNames icon)
    {
        public string Title { get; } = title;
        public string Subtitle { get; } = subtitle;
        public string When { get; } = when;
        public LucideIconNames Icon { get; } = icon;
    }
}
