using System.Collections.ObjectModel;
using Froststrap.Models;

namespace Froststrap.Models.Persistable
{
    public class Settings
    {

        // Integration Page
        public bool EnableActivityTracking { get; set; } = true;
        public bool ShowServerDetails { get; set; } = true;
        public bool AutoRejoin { get; set; } = false;
        public bool ShowGameHistoryMenu { get; set; } = true;
        public bool PlaytimeCounter { get; set; } = true;
        public TrayDoubleClickAction DoubleClickAction { get; set; } = TrayDoubleClickAction.ServerInfo;
        public bool UseDisableAppPatch { get; set; } = false;
        // Kept for settings migration — Close To Desktop now always force-kills Roblox.
        public bool FullyCloseRobloxOnExit { get; set; } = false;
        public bool AutoChangeTitle { get; set; } = false;
        public bool AutoChangeIcon { get; set; } = false;
        public bool ShowUsingFroststrapRPC { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool EnableCustomStatusDisplay { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;
        public bool StudioRPC { get; set; } = false;
        public bool StudioThumbnailChanging { get; set; } = false;
        public bool StudioEditingInfo { get; set; } = false;
        public bool StudioWorkspaceInfo { get; set; } = false;
        public bool StudioShowTesting { get; set; } = false;
        public bool StudioGameButton { get; set; } = false;
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = [];

        // Bootstrapper Page
        public bool ConfirmLaunches { get; set; } = true;
        public bool AllowCookieAccess { get; set; } = false;
        public bool AutoCloseCrashHandler { get; set; } = false;
        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.Never;
        public List<string> CleanerDirectories { get; set; } = [];
        public bool BackgroundUpdatesEnabled { get; set; } = false;
        public bool EnableBetterMatchmaking { get; set; } = false;
        public bool EnableAntiAfk { get; set; } = false;
        public bool JoinSmallerServer { get; set; } = false;
        public int BestRegionAmounts { get; set; } = 5;
        public int MaxServerCheck { get; set; } = 25;
        public string SelectedRegion { get; set; } = Strings.Common_Auto;
        public ProcessPriorityOption SelectedProcessPriority { get; set; } = ProcessPriorityOption.Normal;

        // FastFlag Editor/Settings
        public bool UseFastFlagManager { get; set; } = true;
        public Dictionary<string, List<string>> ProfilePlaceIds { get; set; } = [];

        // Appearance Page
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentAeroDialog;
        public string? SelectedCustomTheme { get; set; } = null;
        public bool CycleEnabled { get; set; }
        public CycleFrequency CycleFrequency { get; set; } = CycleFrequency.EveryLaunch;
        public int CycleIntervalValue { get; set; } = 1;
        public List<string> CycleEnabledCustomThemes { get; set; } = [];
        public int CycleCurrentIndex { get; set; }
        public DateTime CycleLastCycleTime { get; set; } = DateTime.MinValue;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconFroststrap;
        public WindowsBackdrops SelectedBackdrop { get; set; } = WindowsBackdrops.Acrylic;
        public NavigationViewPaneDisplayMode NavigationPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;
        public string Locale { get; set; } = "nil";
        public List<GradientStops> CustomGradientStops { get; set; } =
        [
            new GradientStops { Offset = 0.0, Color = "#4D5560" },
            new GradientStops { Offset = 0.5, Color = "#383F47" },
            new GradientStops { Offset = 1.0, Color = "#252A30" }
        ];
        public double GradientAngle { get; set; } = 0;
        public BackgroundMode BackgroundType { get; set; } = BackgroundMode.Gradient;
        public string? BackgroundImagePath { get; set; } = "";
        public BackgroundStretch BackgroundStretch { get; set; } = BackgroundStretch.UniformToFill;
        public double BackgroundOpacity { get; set; } = 1.0;
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public int MaxThreadDownload { get; set; } = 3;
        public Theme Theme { get; set; } = Theme.Dark;

        // Eclipse brand theming (ported from MrEx ThemeManager)
        public ThemePalette Palette { get; set; } = new();
        public string SelectedThemePreset { get; set; } = "Eclipse";
        public bool EnableAurora { get; set; } = true;
        public bool EnableGlass { get; set; } = true;
        public bool EnableGlow { get; set; } = true;
        public bool EnableLiquidCursor { get; set; } = true;

        // Versions Manager (MrEx port)
        public bool UseCustomVersion { get; set; } = false;
        public string CustomVersionGuid { get; set; } = "";
        public bool PreferRobloxScriptsApi { get; set; } = false;
        public ObservableCollection<VersionProfile> VersionProfiles { get; set; } = [];
        public string ActiveVersionProfileId { get; set; } = "";
        public bool ShowVersionPickerOnLaunch { get; set; } = false;
        public bool ConfirmNonLiveLaunch { get; set; } = true;
        public bool ShowLiveChannelToast { get; set; } = true;
        public bool EnablePrivacyMode { get; set; } = false;
        public bool EnableStreamMode { get; set; } = false;
        public bool EnableTrayLauncher { get; set; } = false;
        public bool NotifyOnLiveChange { get; set; } = false;
        public bool NotifyOnExecutorUpdate { get; set; } = false;
        public bool NotifyOnAppUpdate { get; set; } = true;
        public bool MultiInstanceEnabled { get; set; } = false;
        public bool MultiInstanceLaunchToHome { get; set; } = false;
        public bool ForceLiveChannel { get; set; } = true;
        public bool ShowVipPickerOnLaunch { get; set; } = false;
        public bool EnableVipServerPrompt { get; set; } = false;

        // BanAsync (Windows-only)
        public bool BanAsyncPersistent { get; set; } = true;
        public bool BanAsyncPreserveInGameSettings { get; set; } = true;
        public bool BanAsyncPreserveFastFlags { get; set; } = true;
        public bool BanAsyncIncludeStudioFolders { get; set; } = false;
        public bool BanAsyncCleanVersions { get; set; } = false;
        public bool BanAsyncClearBrowserCookies { get; set; } = false;
        public bool BanAsyncDhcpRefreshAfterSpoof { get; set; } = false;
        public bool BanAsyncAdvancedMode { get; set; } = false;
        public bool BanAsyncOuiMirror { get; set; } = true;
        public bool BanAsyncMachineGuidAcknowledged { get; set; } = false;
        public string BanAsyncOriginalMachineGuid { get; set; } = "";
        public List<string> BanAsyncSpoofedAdapterGuids { get; set; } = [];
        public Dictionary<string, string> BanAsyncOriginalMacByGuid { get; set; } = [];

        // HWID Spoofer (Windows-only) — backups for machine-wide identifiers
        public string HwidOriginalMachineGuid { get; set; } = "";
        public string HwidOriginalHwProfileGuid { get; set; } = "";
        public string HwidOriginalMachineId { get; set; } = "";
        public string HwidOriginalProductId { get; set; } = "";
        public string HwidOriginalComputerName { get; set; } = "";
        public string HwidOriginalActiveComputerName { get; set; } = "";
        public string HwidOriginalSusClientId { get; set; } = "";
        public List<string> HwidSpoofedAdapterGuids { get; set; } = [];
        public Dictionary<string, string> HwidOriginalMacByGuid { get; set; } = [];

        // AltMan
        public bool AltManKillRobloxOnLaunch { get; set; } = false;
        public bool AltManClearCacheOnLaunch { get; set; } = false;
        public int AltManStatusRefreshIntervalMinutes { get; set; } = 5;
        public List<string> AltManFavoriteGames { get; set; } = [];
        public List<string> AltManFavoriteGameNames { get; set; } = [];
        public string AltManBackupPasswordHint { get; set; } = "";

        public bool WindowTilingEnabled { get; set; } = false;
        public WindowTilingLayout WindowTilingLayout { get; set; } = WindowTilingLayout.Auto;
        public string LastBulkPlaceId { get; set; } = "";
        public string LastBulkJobId { get; set; } = "";
        public int BulkLaunchDelaySeconds { get; set; } = 5;
        public bool JoinEmptiestServerOnLaunch { get; set; } = false;
        public string BloxGenApiKey { get; set; } = "";
        public string LuaObfuscatorApiKey { get; set; } = "";
        public string BypassToolsApiKey { get; set; } = "";

        // Deployment Page
        public UpdateCheck UpdateChecks { get; set; } = UpdateCheck.Stable;
        public bool UpdateRoblox { get; set; } = true;
        public bool AutomaticallyUpdateSober { get; set; } = true;
        public string RobloxDomain { get; set; } = RobloxInterfaces.Deployment.DefaultRobloxDomain;
        public bool StaticDirectory { get; set; } = false;
        public string PlayerChannel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public string StudioChannel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Prompt;
        public bool StudioVersionOverrideEnabled { get; set; } = false;
        public string StudioVersionOverrideHash { get; set; } = string.Empty;

        // Linux Settings page
        public bool EnableWebView2 { get; set; } = true;
        public string? StudioVirtualDesktop { get; set; } = string.Empty;
        public string? StudioLauncher { get; set; } = string.Empty;
        public StudioRenderer StudioRenderer { get; set; } = StudioRenderer.DXVK;
        public bool StudioGameMode { get; set; } = false;
        public bool StudioDebug { get; set; } = false;
        public Dictionary<string, string> StudioEnvironmentVariables { get; set; } = [];

        // Misc Stuff
        public bool GameSearch { get; set; } = true;
        public bool ForceLocalData { get; set; } = false;
        public bool DebugDisableVersionPackageCleanup { get; set; } = false;
        public LaunchMode DefaultSaveAndLaunchMode { get; set; } = LaunchMode.Player;
    }
}
