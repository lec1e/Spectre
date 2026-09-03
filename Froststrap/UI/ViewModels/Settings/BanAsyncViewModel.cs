using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Utility.BanAsync;

namespace Froststrap.UI.ViewModels.Settings
{
    public class BanAsyncViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "BanAsyncViewModel";
        private const int MaxLogEntries = 500;

        public BanAsyncViewModel()
        {
            IsElevated = CheckElevated();
            if (OperatingSystem.IsWindows())
            {
                RefreshAdapters();
                RefreshIdentityStatus();
            }
        }

        public bool IsElevated { get; }
        public bool ShowElevationWarning => !IsElevated;
        public bool ShowAdminFeatures => IsElevated;

        public string ElevationStatusText =>
            IsElevated
                ? "Running with administrator privileges. All actions on this page are available."
                : "Froststrap is NOT running as administrator. MAC spoofing, MachineGuid changes, and prefetch cleanup are disabled. Use 'Relaunch as administrator' below.";

        public ObservableCollection<NetworkAdapter> Adapters { get; } = new();

        private NetworkAdapter? _selectedAdapter;
        public NetworkAdapter? SelectedAdapter
        {
            get => _selectedAdapter;
            set
            {
                _selectedAdapter = value;
                OnPropertyChanged(nameof(SelectedAdapter));
                OnPropertyChanged(nameof(CurrentMacFormatted));
                OnPropertyChanged(nameof(OriginalMacFormatted));
            }
        }

        public string CurrentMacFormatted =>
            SelectedAdapter == null ? "(no adapter selected)" : NetworkAdapter.FormatMac(SelectedAdapter.PhysicalAddress);

        public string OriginalMacFormatted =>
            SelectedAdapter != null
            && App.Settings.Prop.BanAsyncOriginalMacByGuid.TryGetValue(SelectedAdapter.Id, out var original)
                ? NetworkAdapter.FormatMac(original)
                : "(none saved yet)";

        public bool PreserveInGameSettings
        {
            get => App.Settings.Prop.BanAsyncPreserveInGameSettings;
            set { App.Settings.Prop.BanAsyncPreserveInGameSettings = value; OnPropertyChanged(nameof(PreserveInGameSettings)); }
        }

        public bool PreserveFastFlags
        {
            get => App.Settings.Prop.BanAsyncPreserveFastFlags;
            set { App.Settings.Prop.BanAsyncPreserveFastFlags = value; OnPropertyChanged(nameof(PreserveFastFlags)); }
        }

        public bool IncludeStudioFolders
        {
            get => App.Settings.Prop.BanAsyncIncludeStudioFolders;
            set { App.Settings.Prop.BanAsyncIncludeStudioFolders = value; OnPropertyChanged(nameof(IncludeStudioFolders)); }
        }

        public bool ClearBrowserCookies
        {
            get => App.Settings.Prop.BanAsyncClearBrowserCookies;
            set { App.Settings.Prop.BanAsyncClearBrowserCookies = value; OnPropertyChanged(nameof(ClearBrowserCookies)); }
        }

        public bool CleanMrExVersions
        {
            get => App.Settings.Prop.BanAsyncCleanVersions;
            set { App.Settings.Prop.BanAsyncCleanVersions = value; OnPropertyChanged(nameof(CleanMrExVersions)); }
        }

        public bool DhcpRefreshAfterSpoof
        {
            get => App.Settings.Prop.BanAsyncDhcpRefreshAfterSpoof;
            set { App.Settings.Prop.BanAsyncDhcpRefreshAfterSpoof = value; OnPropertyChanged(nameof(DhcpRefreshAfterSpoof)); }
        }

        public bool Persistent
        {
            get => App.Settings.Prop.BanAsyncPersistent;
            set { App.Settings.Prop.BanAsyncPersistent = value; OnPropertyChanged(nameof(Persistent)); }
        }

        public bool AdvancedMode
        {
            get => App.Settings.Prop.BanAsyncAdvancedMode;
            set
            {
                App.Settings.Prop.BanAsyncAdvancedMode = value;
                OnPropertyChanged(nameof(AdvancedMode));
                OnPropertyChanged(nameof(ShowAdvanced));
            }
        }

        public bool ShowAdvanced => AdvancedMode;

        public bool OuiMirror
        {
            get => App.Settings.Prop.BanAsyncOuiMirror;
            set { App.Settings.Prop.BanAsyncOuiMirror = value; OnPropertyChanged(nameof(OuiMirror)); }
        }

        public bool MachineGuidAcknowledged
        {
            get => App.Settings.Prop.BanAsyncMachineGuidAcknowledged;
            set
            {
                App.Settings.Prop.BanAsyncMachineGuidAcknowledged = value;
                OnPropertyChanged(nameof(MachineGuidAcknowledged));
                OnPropertyChanged(nameof(MachineGuidActionsEnabled));
            }
        }

        public bool MachineGuidActionsEnabled => IsElevated && MachineGuidAcknowledged;

        private string _customMac = "";
        public string CustomMac
        {
            get => _customMac;
            set { _customMac = value ?? ""; OnPropertyChanged(nameof(CustomMac)); }
        }

        public string CurrentMachineGuid =>
            OperatingSystem.IsWindows()
                ? (MachineGuidSpoofer.ReadCurrent() ?? "(unreadable)")
                : "(Windows only)";

        public bool HasMachineGuidBackup => !string.IsNullOrEmpty(App.Settings.Prop.BanAsyncOriginalMachineGuid);

        public string MachineGuidBackupText =>
            HasMachineGuidBackup
                ? $"Original MachineGuid: {App.Settings.Prop.BanAsyncOriginalMachineGuid}"
                : "No original MachineGuid backed up yet ? the first randomize will save the current value.";

        public ObservableCollection<string> ActivityLog { get; } = new();

        private void Log(string line)
        {
            string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            App.Logger.WriteLine(LOG_IDENT, line);

            void apply()
            {
                ActivityLog.Add(stamped);
                while (ActivityLog.Count > MaxLogEntries)
                    ActivityLog.RemoveAt(0);
            }

            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(apply);
            else
                apply();
        }

        public ICommand RelaunchAsAdminCommand => new RelayCommand(RelaunchAsAdmin);
        public ICommand RefreshAdaptersCommand => new RelayCommand(RefreshAdapters);
        public ICommand CleanTracesCommand => new AsyncRelayCommand(CleanTracesAsync);
        public ICommand SpoofMacCommand => new AsyncRelayCommand(SpoofAsync);
        public ICommand RevertMacCommand => new AsyncRelayCommand(RevertAsync);
        public ICommand IdentitySpoofCommand => new AsyncRelayCommand(IdentitySpoofAsync);
        public ICommand IdentityRevertCommand => new AsyncRelayCommand(IdentityRevertAsync);
        public ICommand RestorePointCommand => new AsyncRelayCommand(RestorePointAsync);
        public ICommand PreflightCommand => new RelayCommand(() => IdentityUtilities.Preflight(Log));
        public ICommand FlushDnsCommand => new RelayCommand(() => IdentityUtilities.FlushDns(Log));
        public ICommand ClearRobloxTempCommand => new RelayCommand(() => IdentityUtilities.ClearRobloxTemp(Log));
        public ICommand ExportBackupCommand => new AsyncRelayCommand(ExportBackupAsync);
        public ICommand ViewOpLogCommand => new RelayCommand(ViewOpLog);
        public ICommand ShuffleMacCommand => new RelayCommand(ShuffleCustomMac);
        public ICommand RandomizeMachineGuidCommand => new AsyncRelayCommand(RandomizeMachineGuidAsync);
        public ICommand RestoreMachineGuidCommand => new AsyncRelayCommand(RestoreMachineGuidAsync);
        public ICommand ClearLogCommand => new RelayCommand(() => ActivityLog.Clear());

        public bool RelaunchAfterSpoof
        {
            get => App.Settings.Prop.BanAsyncRelaunchAfterSpoof;
            set { App.Settings.Prop.BanAsyncRelaunchAfterSpoof = value; OnPropertyChanged(nameof(RelaunchAfterSpoof)); }
        }

        public bool FullWipe
        {
            get => App.Settings.Prop.BanAsyncFullWipe;
            set { App.Settings.Prop.BanAsyncFullWipe = value; OnPropertyChanged(nameof(FullWipe)); }
        }

        private string _identityStatusLabel = "Ready";
        public string IdentityStatusLabel
        {
            get => _identityStatusLabel;
            set
            {
                _identityStatusLabel = value;
                OnPropertyChanged(nameof(IdentityStatusLabel));
                OnPropertyChanged(nameof(IdentityStatusBrush));
            }
        }

        public string IdentityStatusText { get; private set; } = "No identity backup yet.";

        public IBrush IdentityStatusBrush => IdentityStatusLabel switch
        {
            "Spoofed" => new SolidColorBrush(Color.FromArgb(0x88, 0xC0, 0x84, 0xFC)),
            "Dirty" => new SolidColorBrush(Color.FromArgb(0x88, 0xD9, 0x77, 0x06)),
            _ => new SolidColorBrush(Color.FromArgb(0x66, 0x22, 0xC5, 0x5E)),
        };

        private void RefreshIdentityStatus()
        {
            try
            {
                var data = IdentityBackupStore.Default.LoadOrDefault();
                IdentityStatusLabel = data.Dirty ? "Dirty" : data.AnyActive ? "Spoofed" : "Ready";
                IdentityStatusText = IdentityPipeline.DescribeStatus(data)
                    + (string.IsNullOrEmpty(data.DirtyReason) ? "" : $" ({data.DirtyReason})");
            }
            catch (Exception ex)
            {
                IdentityStatusLabel = "Dirty";
                IdentityStatusText = ex.Message;
            }
            OnPropertyChanged(nameof(IdentityStatusText));
        }

        private async Task IdentitySpoofAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated — identity spoof is disabled.");
                return;
            }

            var confirm = await Frontend.ShowMessageBox(
                "This runs the full Ghost420-style pipeline:\n" +
                "  • kill Roblox and wipe cookies / LocalAppData\\Roblox\n" +
                "  • spoof software GUIDs + AMI firmware UUID (if AMI)\n" +
                "  • write a local memory profile, spoof monitor EDID, and SystemReg\n" +
                "  • clean GameConfigStore / Control trees\n\n" +
                "Needs administrator. Continue?",
                MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
            {
                Log("Identity spoof cancelled.");
                return;
            }

            await Task.Run(() => IdentityPipeline.Spoof(Log, RelaunchAfterSpoof));
            RefreshIdentityStatus();
        }

        private async Task IdentityRevertAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated — identity revert is disabled.");
                return;
            }
            await Task.Run(() => IdentityPipeline.Revert(Log));
            RefreshIdentityStatus();
        }

        private async Task RestorePointAsync()
        {
            if (!IsElevated)
            {
                Log("Not elevated — restore point needs admin.");
                return;
            }
            await Task.Run(() => IdentityUtilities.CreateRestorePoint(Log));
        }

        private async Task ExportBackupAsync()
        {
            try
            {
                string dest = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"eclipse-identity-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                await Task.Run(() => IdentityBackupStore.Default.ExportTo(dest));
                Log($"Exported backup to {dest}");
            }
            catch (Exception ex)
            {
                Log($"Export failed: {ex.Message}");
            }
        }

        private void ViewOpLog()
        {
            foreach (string line in IdentityUtilities.ReadOperationLog(80))
                Log(line);
        }

        private void RefreshAdapters()
        {
            if (!OperatingSystem.IsWindows())
            {
                Log("BanAsync adapter tools are Windows-only.");
                return;
            }

            string? previouslySelectedId = SelectedAdapter?.Id;
            Adapters.Clear();

            foreach (var a in MacSpoofer.EnumeratePhysicalAdapters())
                Adapters.Add(a);

            SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == previouslySelectedId)
                              ?? Adapters.FirstOrDefault();
            Log($"Detected {Adapters.Count} physical adapter(s).");
        }

        private async Task CleanTracesAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Log("BanAsync cleanup is Windows-only.");
                return;
            }

            string prompt =
                "This will close Roblox and delete:\n" +
                "  ? %LocalAppData%\\Roblox\n" +
                "  ? %AppData%\\Roblox\\logs and \\http\n" +
                "  ? %ProgramData%\\Roblox\n" +
                "  ? %Temp%\\Roblox*\n" +
                "  ? Prefetch entries for Roblox (admin only)\n" +
                "  ? HKCU\\Software\\ROBLOX Corporation\n" +
                (CleanMrExVersions ? "  ? Froststrap's downloaded Roblox installs (Versions)\n" : "") +
                (ClearBrowserCookies ? "  ? Roblox cookies in installed browsers\n" : "") +
                "\n" +
                (PreserveInGameSettings ? "In-game settings will be preserved.\n" : "") +
                (PreserveFastFlags ? "Vanilla Roblox FastFlags JSON will be preserved.\n" : "") +
                "\nFroststrap's own settings, FastFlags, and themes are NOT touched.\n\nContinue?";

            var confirm = await Frontend.ShowMessageBox(prompt, MessageBoxImage.Warning,
                MessageBoxButton.YesNo, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
            {
                Log("Cleanup cancelled.");
                return;
            }

            Log("Starting trace cleanup?");
            var options = new CleanupEngine.CleanupOptions
            {
                PreserveInGameSettings = PreserveInGameSettings,
                PreserveFastFlags = PreserveFastFlags,
                IncludeStudioFolders = IncludeStudioFolders,
                CleanMrExVersions = CleanMrExVersions
            };

            CleanupEngine.CleanupResult result = await Task.Run(() => CleanupEngine.RunCleanup(options, Log));

            Log($"Cleanup done. Removed {result.DeletedDirectories} dir(s), {result.DeletedFiles} file(s), {result.RegistryKeysRemoved} registry key(s). Preserved {result.PreservedFiles} file(s). Skipped {result.Skipped.Count}.");

            if (ClearBrowserCookies)
            {
                Log("Clearing Roblox cookies from installed browsers?");
                var cookieResult = await Task.Run(() => BrowserCookieCleaner.ClearRobloxCookies(Log));
                Log($"Browser cookies: cleared {cookieResult.CookiesDeleted} across {cookieResult.BrowsersScanned} browser(s).");
            }
        }

        private async Task SpoofAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated ? spoof is disabled.");
                return;
            }

            var targets = AdvancedMode
                ? (SelectedAdapter == null ? new List<NetworkAdapter>() : new List<NetworkAdapter> { SelectedAdapter })
                : Adapters.ToList();

            if (targets.Count == 0)
            {
                Log("No adapters to spoof.");
                return;
            }

            string? customNormalized = null;
            if (AdvancedMode && !string.IsNullOrWhiteSpace(CustomMac))
            {
                if (!MacSpoofer.IsValidMacHex(CustomMac))
                {
                    await Frontend.ShowMessageBox("That MAC address isn't valid. Use 12 hex characters (separators optional).",
                        MessageBoxImage.Warning);
                    return;
                }
                customNormalized = MacSpoofer.NormalizeMacHex(CustomMac);
            }

            await Task.Run(() =>
            {
                foreach (var adapter in targets)
                {
                    string newMac = customNormalized ??
                                    MacSpoofer.GenerateRandomMac(OuiMirror ? adapter.PhysicalAddress : null);

                    if (!string.IsNullOrEmpty(adapter.PhysicalAddress)
                        && !App.Settings.Prop.BanAsyncOriginalMacByGuid.ContainsKey(adapter.Id))
                    {
                        string original = adapter.PhysicalAddress;
                        Dispatcher.UIThread.Post(() =>
                            App.Settings.Prop.BanAsyncOriginalMacByGuid[adapter.Id] = original);
                    }

                    Log($"Spoofing {adapter.Name} ? {NetworkAdapter.FormatMac(newMac)}?");
                    bool ok = MacSpoofer.SpoofAdapter(adapter, newMac, Log);

                    if (ok && !App.Settings.Prop.BanAsyncSpoofedAdapterGuids.Contains(adapter.Id))
                    {
                        Dispatcher.UIThread.Post(() =>
                            App.Settings.Prop.BanAsyncSpoofedAdapterGuids.Add(adapter.Id));
                    }

                    if (ok && DhcpRefreshAfterSpoof)
                        MacSpoofer.DhcpRefresh(adapter.Name, Log);
                }
            });

            Log("Spoof pass finished. Refreshing adapter list?");
            RefreshAdapters();
        }

        private async Task RevertAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated ? revert is disabled.");
                return;
            }

            var ids = App.Settings.Prop.BanAsyncSpoofedAdapterGuids.ToList();
            var toRevert = Adapters.Where(a => ids.Contains(a.Id) || HasNetworkAddressOverride(a)).ToList();

            if (toRevert.Count == 0)
            {
                Log("No adapters appear to be spoofed.");
                return;
            }

            await Task.Run(() =>
            {
                foreach (var adapter in toRevert)
                {
                    Log($"Reverting {adapter.Name}?");
                    bool ok = MacSpoofer.RevertAdapter(adapter, Log);
                    if (ok)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            App.Settings.Prop.BanAsyncSpoofedAdapterGuids.Remove(adapter.Id);
                            App.Settings.Prop.BanAsyncOriginalMacByGuid.Remove(adapter.Id);
                        });
                    }
                }
            });

            Log("Revert pass finished.");
            RefreshAdapters();
        }

        private static bool HasNetworkAddressOverride(NetworkAdapter a)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(a.ClassRegistryPath, writable: false);
                return key?.GetValue("NetworkAddress") != null;
            }
            catch { return false; }
        }

        private void ShuffleCustomMac()
        {
            string? seed = OuiMirror && SelectedAdapter != null ? SelectedAdapter.PhysicalAddress : null;
            CustomMac = NetworkAdapter.FormatMac(MacSpoofer.GenerateRandomMac(seed));
        }

        private async Task RandomizeMachineGuidAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated ? MachineGuid change is disabled.");
                return;
            }
            if (!MachineGuidAcknowledged)
            {
                Log("Tick the acknowledgement first.");
                return;
            }

            string prompt =
                "Changing MachineGuid is Windows-wide. Office activation, some app licenses, " +
                "and telemetry pairing key off this value. You can lose activation or break apps until you restore it.\n\n" +
                "The current value will be saved so you can restore it from this page. Continue?";

            var confirm = await Frontend.ShowMessageBox(prompt, MessageBoxImage.Warning,
                MessageBoxButton.YesNo, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
            {
                Log("MachineGuid randomize cancelled.");
                return;
            }

            await Task.Run(() =>
            {
                string? current = MachineGuidSpoofer.ReadCurrent();
                if (!string.IsNullOrEmpty(current) && string.IsNullOrEmpty(App.Settings.Prop.BanAsyncOriginalMachineGuid))
                {
                    App.Settings.Prop.BanAsyncOriginalMachineGuid = current!;
                    Log($"Saved original MachineGuid: {current}");
                }

                string newGuid = MachineGuidSpoofer.GenerateRandom();
                bool ok = MachineGuidSpoofer.Apply(newGuid, Log);
                if (ok)
                    Log("MachineGuid randomized. Some apps may need a relaunch to notice.");
            });

            OnPropertyChanged(nameof(CurrentMachineGuid));
            OnPropertyChanged(nameof(HasMachineGuidBackup));
            OnPropertyChanged(nameof(MachineGuidBackupText));
        }

        private async Task RestoreMachineGuidAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated ? MachineGuid restore is disabled.");
                return;
            }

            string original = App.Settings.Prop.BanAsyncOriginalMachineGuid;
            if (string.IsNullOrEmpty(original))
            {
                await Frontend.ShowMessageBox(
                    "No original MachineGuid is stored. There's nothing to restore from here.",
                    MessageBoxImage.Information);
                return;
            }

            await Task.Run(() =>
            {
                bool ok = MachineGuidSpoofer.Apply(original, Log);
                if (ok)
                {
                    App.Settings.Prop.BanAsyncOriginalMachineGuid = "";
                    Log("Restored MachineGuid to its original value.");
                }
            });

            OnPropertyChanged(nameof(CurrentMachineGuid));
            OnPropertyChanged(nameof(HasMachineGuidBackup));
            OnPropertyChanged(nameof(MachineGuidBackupText));
        }

        private static bool CheckElevated()
        {
            if (!OperatingSystem.IsWindows())
                return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void RelaunchAsAdmin()
        {
            const string ident = "BanAsyncViewModel::RelaunchAsAdmin";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Paths.Process,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "-menu",
                };
                Process.Start(psi);
                App.Logger.WriteLine(ident, "Spawned elevated instance, terminating current process.");
                App.Terminate();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                App.Logger.WriteLine(ident, "UAC prompt was cancelled ? staying on current process.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(ident, ex);
                _ = Frontend.ShowMessageBox(
                    $"Couldn't relaunch as administrator.\n\nReason: {ex.GetType().Name}: {ex.Message}\n\n" +
                    "Close Froststrap and right-click the exe ? 'Run as administrator' instead.",
                    MessageBoxImage.Warning);
            }
        }
    }
}
