using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Models.Persistable;
using Froststrap.Utility;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings
{
    public class VersionProfileTile : NotifyPropertyChangedViewModel
    {
        public string Id { get; }
        public string Name { get; }
        public string VersionGuid { get; }
        public string? ExecutorTitle { get; }
        public string? ExecutorLogoUrl { get; }
        public bool IsBuiltIn { get; }
        public bool IsActive { get; private set; }

        private string _diskUsage = "…";
        public string DiskUsage
        {
            get => _diskUsage;
            private set { _diskUsage = value; OnPropertyChanged(nameof(DiskUsage)); }
        }

        private bool _isInstallTarget;
        public bool IsInstallTarget
        {
            get => _isInstallTarget;
            private set { _isInstallTarget = value; OnPropertyChanged(nameof(IsInstallTarget)); }
        }

        private Bitmap? _logo;
        public Bitmap? Logo
        {
            get => _logo;
            private set { _logo = value; OnPropertyChanged(nameof(Logo)); OnPropertyChanged(nameof(HasLogo)); }
        }

        public bool HasLogo => Logo is not null;
        public string Letter => string.IsNullOrEmpty(Name) ? "?" : Name[0].ToString().ToUpperInvariant();
        public string VersionLabel => string.IsNullOrEmpty(VersionGuid) ? "Latest LIVE" : VersionGuid;
        public string Subtitle => !string.IsNullOrEmpty(ExecutorTitle) ? ExecutorTitle : (IsBuiltIn ? "Built-in" : "Pinned");

        public VersionProfileTile(VersionProfile profile, bool isActive)
        {
            Id = profile.Id;
            Name = profile.Name;
            VersionGuid = profile.VersionGuid;
            ExecutorTitle = profile.ExecutorTitle;
            ExecutorLogoUrl = profile.ExecutorLogoUrl;
            IsBuiltIn = profile.IsBuiltIn;
            IsActive = isActive;
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            OnPropertyChanged(nameof(IsActive));
        }

        public void ApplyScan(long bytes, bool isInstallTarget)
        {
            DiskUsage = VersionsDiskUsage.FormatBytes(bytes);
            IsInstallTarget = isInstallTarget;
        }

        public async Task LoadLogoAsync()
        {
            try
            {
                string? path = await ExecutorLogoCache.GetLogoAsync(ExecutorLogoUrl);
                if (path is null || !File.Exists(path))
                    return;
                Logo = new Bitmap(path);
            }
            catch { /* placeholder fallback */ }
        }

        public static bool ResolveIsInstallTarget(string versionGuid, string profileId)
        {
            try
            {
                return VersionJunctionManager.IsInstallTarget(versionGuid, profileId);
            }
            catch
            {
                return false;
            }
        }
    }

    public class VersionsManagerViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "VersionsManagerViewModel";

        public ObservableCollection<VersionProfileTile> Tiles { get; } = [];

        public ICommand ActivateCommand => new RelayCommand<string>(Activate);
        public ICommand DeleteCommand => new RelayCommand<string>(DeleteProfile);
        public ICommand AddProfileCommand => new RelayCommand(AddProfile);
        public ICommand OpenVersionsFolderCommand => new RelayCommand(OpenVersionsFolder);
        public ICommand SetAsInstallTargetCommand => new RelayCommand<string>(SetAsInstallTarget);
        public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set { _isRefreshing = value; OnPropertyChanged(nameof(IsRefreshing)); OnPropertyChanged(nameof(IsNotRefreshing)); }
        }
        public bool IsNotRefreshing => !_isRefreshing;

        private string _refreshStatus = "";
        public string RefreshStatus
        {
            get => _refreshStatus;
            private set { _refreshStatus = value; OnPropertyChanged(nameof(RefreshStatus)); OnPropertyChanged(nameof(HasRefreshStatus)); }
        }
        public bool HasRefreshStatus => !string.IsNullOrEmpty(_refreshStatus);

        private string _activeName = "";
        public string ActiveName
        {
            get => _activeName;
            private set { _activeName = value; OnPropertyChanged(nameof(ActiveName)); }
        }

        private string _activeHash = "";
        public string ActiveHash
        {
            get => _activeHash;
            private set { _activeHash = value; OnPropertyChanged(nameof(ActiveHash)); }
        }

        private string _diskUsageText = "";
        public string DiskUsageText
        {
            get => _diskUsageText;
            private set { _diskUsageText = value; OnPropertyChanged(nameof(DiskUsageText)); }
        }

        public bool ShowVersionPickerOnLaunch
        {
            get => App.Settings.Prop.ShowVersionPickerOnLaunch;
            set { App.Settings.Prop.ShowVersionPickerOnLaunch = value; OnPropertyChanged(nameof(ShowVersionPickerOnLaunch)); }
        }

        public bool ConfirmNonLiveLaunch
        {
            get => App.Settings.Prop.ConfirmNonLiveLaunch;
            set { App.Settings.Prop.ConfirmNonLiveLaunch = value; OnPropertyChanged(nameof(ConfirmNonLiveLaunch)); }
        }

        public bool PreferRobloxScriptsApi
        {
            get => App.Settings.Prop.PreferRobloxScriptsApi;
            set { App.Settings.Prop.PreferRobloxScriptsApi = value; OnPropertyChanged(nameof(PreferRobloxScriptsApi)); }
        }

        public VersionsManagerViewModel()
        {
            RebuildTiles();
        }

        private async Task RefreshAsync()
        {
            if (IsRefreshing) return;

            bool anyExecutorTracked = App.Settings.Prop.VersionProfiles
                .Any(p => !string.IsNullOrWhiteSpace(p.ExecutorRefreshKey));

            if (!anyExecutorTracked)
            {
                RebuildTiles();
                RefreshStatus = "";
                return;
            }

            IsRefreshing = true;
            RefreshStatus = "Refreshing executor versions…";
            try
            {
                await ExecutorProfileRefresher.RefreshAllAsync(TimeSpan.FromSeconds(5));
                RefreshStatus = $"Refreshed at {DateTime.Now:HH:mm}.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Refresh", ex);
                RefreshStatus = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                IsRefreshing = false;
                RebuildTiles();
            }
        }

        private int _scanGeneration;

        public void RebuildTiles()
        {
            Tiles.Clear();
            string activeId = App.Settings.Prop.ActiveVersionProfileId;

            foreach (var profile in App.Settings.Prop.VersionProfiles)
            {
                var tile = new VersionProfileTile(profile, profile.Id == activeId);
                Tiles.Add(tile);
                _ = tile.LoadLogoAsync();
            }

            RefreshActiveSummary();
            _ = ScanTilesAsync(Tiles.ToArray(), ++_scanGeneration);
        }

        private async Task ScanTilesAsync(VersionProfileTile[] tiles, int generation)
        {
            try
            {
                var results = await Task.Run(() =>
                {
                    var sizeByGuid = new Dictionary<string, long>();
                    var scans = new (long Bytes, bool IsInstallTarget)[tiles.Length];

                    for (int i = 0; i < tiles.Length; i++)
                    {
                        var tile = tiles[i];
                        long bytes = 0;
                        if (!string.IsNullOrEmpty(tile.VersionGuid))
                        {
                            if (!sizeByGuid.TryGetValue(tile.VersionGuid, out bytes))
                            {
                                bytes = VersionsDiskUsage.GetUsageBytes(tile.VersionGuid);
                                sizeByGuid[tile.VersionGuid] = bytes;
                            }
                        }

                        scans[i] = (bytes, VersionProfileTile.ResolveIsInstallTarget(tile.VersionGuid, tile.Id));
                    }

                    return scans;
                });

                if (generation != _scanGeneration)
                    return;

                long total = 0;
                for (int i = 0; i < tiles.Length; i++)
                {
                    tiles[i].ApplyScan(results[i].Bytes, results[i].IsInstallTarget);
                    total += results[i].Bytes;
                }

                int profileCount = tiles.Length;
                DiskUsageText = $"Disk usage: {VersionsDiskUsage.FormatBytes(total)} across {profileCount} profile{(profileCount == 1 ? "" : "s")}";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::ScanTiles", ex);
                if (generation == _scanGeneration)
                    DiskUsageText = "Disk usage: (unavailable)";
            }
        }

        private void RefreshActiveSummary()
        {
            string activeId = App.Settings.Prop.ActiveVersionProfileId;
            var active = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == activeId);
            ActiveName = active?.Name ?? "(none)";
            ActiveHash = string.IsNullOrEmpty(active?.VersionGuid) ? "Latest LIVE" : active!.VersionGuid;
        }

        private void Activate(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;

            App.Settings.Prop.ActiveVersionProfileId = id;
            var profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == id);
            if (profile is not null)
            {
                if (string.IsNullOrEmpty(profile.VersionGuid))
                {
                    App.Settings.Prop.UseCustomVersion = false;
                    App.Settings.Prop.CustomVersionGuid = "";
                }
                else
                {
                    App.Settings.Prop.UseCustomVersion = true;
                    App.Settings.Prop.CustomVersionGuid = profile.VersionGuid;
                    try
                    {
                        VersionJunctionManager.SetInstallTarget(profile);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT + "::Activate.SetInstallTarget", ex);
                    }
                }
            }

            App.Settings.Save();
            RebuildTiles();
        }

        private void DeleteProfile(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == id);
            if (profile is null || profile.IsBuiltIn) return;

            App.Settings.Prop.VersionProfiles.Remove(profile);
            if (App.Settings.Prop.ActiveVersionProfileId == id)
            {
                var live = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.IsBuiltIn)
                    ?? App.Settings.Prop.VersionProfiles.FirstOrDefault();
                if (live is not null)
                    Activate(live.Id);
            }

            App.Settings.Save();
            RebuildTiles();
        }

        private void AddProfile()
        {
            // Dialog opened from code-behind / page
            AddProfileRequested?.Invoke();
        }

        public Action? AddProfileRequested;

        public void AddProfileFromDialog(VersionProfile profile)
        {
            App.Settings.Prop.VersionProfiles.Add(profile);
            App.Settings.Save();
            Activate(profile.Id);
        }

        private void OpenVersionsFolder()
        {
            try
            {
                if (!Directory.Exists(Paths.Versions))
                    Directory.CreateDirectory(Paths.Versions);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Paths.Versions,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::OpenVersionsFolder", ex);
            }
        }

        private void SetAsInstallTarget(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == id);
            if (profile is null) return;

            try
            {
                VersionJunctionManager.SetInstallTarget(profile);
                RebuildTiles();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::SetAsInstallTarget", ex);
            }
        }
    }
}
