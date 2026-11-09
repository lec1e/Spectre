using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.Converters;
using LucideAvalonia.Enum;

namespace Froststrap.UI.ViewModels.Settings
{
    public sealed record HomeAction(string Tag, string Title, string Description, LucideIconNames Icon);

    public sealed record HomeStat(string Label, string Value, string? Detail, LucideIconNames Icon);

    public sealed class HomeRecentGame : NotifyPropertyChangedViewModel
    {
        public required long PlaceId { get; init; }
        public required string Name { get; init; }
        public required string LastPlayed { get; init; }

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
    }

    public class HomeViewModel : NotifyPropertyChangedViewModel
    {
        private static readonly JsonSerializerOptions HistoryOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly MainWindowViewModel _shell;
        private bool _active;
        private bool _recentLoaded;

        public HomeViewModel(MainWindowViewModel shell)
        {
            _shell = shell;

            NavigateCommand = new RelayCommand<string>(tag => { if (tag is not null) _shell.NavigateTo(tag); });
            LaunchPlayerCommand = new RelayCommand(() => Launch(LaunchMode.Player));
            LaunchStudioCommand = new RelayCommand(() => Launch(LaunchMode.Studio));
            ReinstallRobloxCommand = new AsyncRelayCommand(ReinstallRobloxAsync);
            PlayGameCommand = new RelayCommand<HomeRecentGame>(PlayGame);
            OpenFolderCommand = new RelayCommand(OpenFolder);

            Stats = BuildStats();
        }

        public ICommand NavigateCommand { get; }
        public ICommand LaunchPlayerCommand { get; }
        public ICommand LaunchStudioCommand { get; }
        public ICommand ReinstallRobloxCommand { get; }
        public ICommand PlayGameCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public string VersionBadge => $"{App.BrandName.ToUpperInvariant()} v{App.Version}";

        public static string Greeting => DateTime.Now.Hour switch
        {
            < 5 => "Good evening",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening"
        };

        public string HeroTitle => App.Cookies.User is { } user && App.Cookies.State == CookieState.Success
            ? $"{Greeting}, {user.DisplayName}"
            : $"{Greeting}.";

        public string HeroSubtitle => App.Cookies.State switch
        {
            CookieState.Success when App.Cookies.User is { } user => $"@{user.Username} is connected. Pick up where you left off or tune Roblox below.",
            CookieState.NotAllowed or CookieState.Unknown when !App.Settings.Prop.AllowCookieAccess =>
                "Everything you need to launch, mod and tune Roblox lives here. Turn on cookie access in Bootstrapper to see your avatar and games.",
            _ => "Everything you need to launch, mod and tune Roblox lives here."
        };

        public string PlayerLaunchText => MainWindowViewModel.IsPlayerInstalled ? "Launch Roblox" : "Install Roblox";
        public string StudioLaunchText => MainWindowViewModel.IsStudioInstalled ? "Studio" : "Install Studio";

        private Bitmap? _avatar;
        public Bitmap? Avatar
        {
            get => _avatar;
            private set
            {
                if (SetProperty(ref _avatar, value))
                    OnPropertyChanged(nameof(HasAvatar));
            }
        }
        public bool HasAvatar => Avatar is not null;

        private IReadOnlyList<HomeStat> _stats = [];
        public IReadOnlyList<HomeStat> Stats { get => _stats; private set => SetProperty(ref _stats, value); }

        public IReadOnlyList<HomeAction> Actions { get; } =
        [
            new("quickplay", "Quick Play", "Search games and jump straight in", LucideIconNames.Gamepad2),
            new("mods", "Mods", "Fonts, cursors, sounds and textures", LucideIconNames.Puzzle),
            new("fastflags", "FastFlags", "Performance and rendering tweaks", LucideIconNames.Flag),
            new("versions", "Versions", "Pin builds per executor", LucideIconNames.Layers),
            new("integrations", "Integrations", "Discord, activity and server info", LucideIconNames.Plug),
            new("appearance", "Appearance", "Animated backgrounds, accent and fonts", LucideIconNames.Palette),
        ];

        public ObservableCollection<HomeRecentGame> RecentGames { get; } = [];

        private bool _hasRecentGames;
        public bool HasRecentGames { get => _hasRecentGames; private set => SetProperty(ref _hasRecentGames, value); }

        public void Activate()
        {
            if (_active)
                return;

            _active = true;
            App.Cookies.StateChanged += OnCookieStateChanged;
            RefreshSession();
            Stats = BuildStats();
            _ = RefreshModCountAsync();

            if (!_recentLoaded)
            {
                _recentLoaded = true;
                _ = LoadRecentGamesAsync();
            }
        }

        public void Deactivate()
        {
            if (!_active)
                return;

            _active = false;
            App.Cookies.StateChanged -= OnCookieStateChanged;
        }

        private void OnCookieStateChanged(object? sender, CookieState state) => Dispatcher.UIThread.Post(RefreshSession);

        private void RefreshSession()
        {
            OnPropertyChanged(nameof(HeroTitle));
            OnPropertyChanged(nameof(HeroSubtitle));

            if (App.Cookies.State == CookieState.Success && App.Cookies.User is { } user)
                _ = LoadAvatarAsync(user.Id);
            else
                Avatar = null;
        }

        private async Task LoadAvatarAsync(long userId)
        {
            try
            {
                var urls = await Thumbnails.GetThumbnailUrlsAsync(
                    [new ThumbnailRequest { TargetId = (ulong)userId, Type = ThumbnailType.AvatarHeadShot, Size = "150x150", IsCircular = true }],
                    CancellationToken.None);

                if (urls.FirstOrDefault() is { Length: > 0 } url)
                    Avatar = await UrlToBitmapConverter.GetBitmapFromCacheOrDownloadAsync(url);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeViewModel::LoadAvatar", ex.Message);
            }
        }

        private int? _modCount;

        private List<HomeStat> BuildStats()
        {
            string guid = App.PlayerState.Prop.VersionGuid;
            string build = string.IsNullOrEmpty(guid) ? "Not installed" : guid.Replace("version-", "");

            var profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == App.Settings.Prop.ActiveVersionProfileId);
            int flags = App.FastFlags.Prop.Count;

            return
            [
                new("Roblox build", build, string.IsNullOrEmpty(guid) ? "Launch once to install" : "Installed", LucideIconNames.Box),
                new("Version profile", profile?.Name ?? "Live", profile?.ExecutorTitle ?? (profile?.IsBuiltIn == true ? "Built in" : "Custom"), LucideIconNames.Layers),
                new("FastFlags", flags.ToString(CultureInfo.InvariantCulture), flags == 1 ? "flag applied" : "flags applied", LucideIconNames.Flag),
                new("Mod files", _modCount?.ToString(CultureInfo.InvariantCulture) ?? "…", "in your mods folder", LucideIconNames.Puzzle),
            ];
        }

        private async Task RefreshModCountAsync()
        {
            _modCount = await Task.Run(() =>
            {
                try
                {
                    return Directory.Exists(Paths.Modifications)
                        ? Directory.EnumerateFiles(Paths.Modifications, "*", SearchOption.AllDirectories).Count()
                        : 0;
                }
                catch
                {
                    return 0;
                }
            });

            Stats = BuildStats();
        }

        private async Task LoadRecentGamesAsync()
        {
            try
            {
                string path = Path.Combine(Paths.Cache, "GameHistory.json");
                var history = await Task.Run(() =>
                {
                    if (!File.Exists(path))
                        return [];

                    var entries = JsonSerializer.Deserialize<List<GameHistoryEntry>>(File.ReadAllText(path), HistoryOptions) ?? [];
                    return entries
                        .Where(e => e.UniverseId > 0 && e.PlaceId > 0)
                        .Select(e => (Entry: e, Last: e.Servers.Count > 0 ? e.Servers.Max(s => s.JoinedAt) : DateTime.MinValue))
                        .OrderByDescending(x => x.Last)
                        .Take(6)
                        .ToList();
                });

                if (history.Count == 0)
                    return;

                await UniverseDetails.FetchBulk(string.Join(",", history.Select(h => h.Entry.UniverseId)));

                foreach (var (entry, last) in history)
                {
                    var details = UniverseDetails.LoadFromCache(entry.UniverseId);
                    RecentGames.Add(new HomeRecentGame
                    {
                        PlaceId = entry.PlaceId,
                        Name = details?.Data?.Name ?? "Unknown experience",
                        LastPlayed = last == DateTime.MinValue ? "" : Relative(last)
                    });
                }

                HasRecentGames = RecentGames.Count > 0;

                var urls = await Thumbnails.GetThumbnailUrlsAsync(
                    history.Select(h => new ThumbnailRequest { TargetId = (ulong)h.Entry.UniverseId, Type = ThumbnailType.GameIcon, Size = "150x150" }).ToList(),
                    CancellationToken.None);

                for (int i = 0; i < urls.Length && i < RecentGames.Count; i++)
                {
                    if (urls[i] is { Length: > 0 } url)
                        RecentGames[i].Thumbnail = await UrlToBitmapConverter.GetBitmapFromCacheOrDownloadAsync(url);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeViewModel::LoadRecentGames", ex.Message);
            }
        }

        private static string Relative(DateTime time)
        {
            var span = DateTime.Now - (time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time);
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
            return time.ToString("MMM d", CultureInfo.CurrentCulture);
        }

        private static void OpenFolder()
        {
            string path = Paths.Initialized && Directory.Exists(Paths.Base) ? Paths.Base : Paths.Roblox;
            if (!Directory.Exists(path))
                return;

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        private async Task ReinstallRobloxAsync()
        {
            if (!await RobloxReinstaller.ConfirmAndPrepareAsync())
                return;

            Launch(LaunchMode.Player);
        }

        private void Launch(LaunchMode mode)
        {
            _shell.SelectedLaunchMode = mode;
            _shell.SaveAndLaunchSettings();
        }

        private static void PlayGame(HomeRecentGame? game)
        {
            if (game is null)
                return;

            Process.Start(new ProcessStartInfo($"roblox://experiences/start?placeId={game.PlaceId}") { UseShellExecute = true });
        }
    }
}
