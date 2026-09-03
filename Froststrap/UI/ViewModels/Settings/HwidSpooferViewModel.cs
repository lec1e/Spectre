using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Utility.Hwid;

namespace Froststrap.UI.ViewModels.Settings
{
    public class HwidIdentifierRow : NotifyPropertyChangedViewModel
    {
        private bool _isSelected = true;
        private string _currentValue = "";
        private string _backupValue = "";
        private string _previewValue = "";

        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public bool IsPresent { get; init; } = true;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(nameof(CurrentValue)); }
        }

        public string BackupValue
        {
            get => _backupValue;
            set { _backupValue = value; OnPropertyChanged(nameof(BackupValue)); }
        }

        public string PreviewValue
        {
            get => _previewValue;
            set { _previewValue = value; OnPropertyChanged(nameof(PreviewValue)); }
        }
    }

    public class HwidSpooferViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "HwidSpooferViewModel";
        private const int MaxLogEntries = 500;

        public HwidSpooferViewModel()
        {
            IsElevated = CheckElevated();
            if (OperatingSystem.IsWindows())
                Refresh();
        }

        public bool IsElevated { get; }
        public bool ShowElevationWarning => !IsElevated;
        public bool ShowAdminFeatures => IsElevated;

        public string ElevationStatusText =>
            IsElevated
                ? "Running with administrator privileges. Spoof / Revert actions are available."
                : "Spectre is NOT running as administrator. HWID spoofing requires elevation. Use 'Relaunch as administrator' below.";

        public ObservableCollection<HwidIdentifierRow> Identifiers { get; } = new();
        public ObservableCollection<string> ActivityLog { get; } = new();

        public ICommand RelaunchAsAdminCommand => new RelayCommand(RelaunchAsAdmin);
        public ICommand RefreshCommand => new RelayCommand(Refresh);
        public ICommand SpoofAllCommand => new AsyncRelayCommand(SpoofAllAsync);
        public ICommand SpoofSelectedCommand => new AsyncRelayCommand(SpoofSelectedAsync);
        public ICommand RevertAllCommand => new AsyncRelayCommand(RevertAllAsync);
        public ICommand GeneratePreviewCommand => new RelayCommand(GeneratePreview);
        public ICommand ClearLogCommand => new RelayCommand(() => ActivityLog.Clear());
        public ICommand SelectAllCommand => new RelayCommand(() =>
        {
            foreach (var row in Identifiers)
                row.IsSelected = row.IsPresent;
        });
        public ICommand SelectNoneCommand => new RelayCommand(() =>
        {
            foreach (var row in Identifiers)
                row.IsSelected = false;
        });

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

        private void Refresh()
        {
            if (!OperatingSystem.IsWindows())
            {
                Log("HWID Spoofer is Windows-only.");
                return;
            }

            var previousSelection = Identifiers
                .Where(r => r.IsSelected)
                .Select(r => r.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool hadRows = Identifiers.Count > 0;
            Identifiers.Clear();

            foreach (var info in HwidSpoofer.ReadAll())
            {
                Identifiers.Add(new HwidIdentifierRow
                {
                    Key = info.Key,
                    DisplayName = info.DisplayName,
                    IsPresent = info.IsPresent,
                    IsSelected = info.IsPresent && (!hadRows || previousSelection.Contains(info.Key)),
                    CurrentValue = info.CurrentValue,
                    BackupValue = info.BackupValue,
                    PreviewValue = ""
                });
            }

            Log($"Refreshed {Identifiers.Count} identifier(s).");
        }

        private void GeneratePreview()
        {
            if (!OperatingSystem.IsWindows())
            {
                Log("HWID Spoofer is Windows-only.");
                return;
            }

            var preview = HwidSpoofer.GeneratePreview();
            var byKey = preview.ToDictionary(p => p.Key, p => p.PreviewValue, StringComparer.OrdinalIgnoreCase);

            foreach (var row in Identifiers)
            {
                row.PreviewValue = byKey.TryGetValue(row.Key, out var value) ? value : "";
            }

            Log("Generated preview values (not applied):");
            foreach (var p in preview)
                Log($"  {p.DisplayName} → {p.PreviewValue}");
        }

        private async Task SpoofAllAsync()
        {
            if (!await ConfirmSpoofAsync("Spoof ALL present identifiers?"))
                return;

            await RunElevatedAsync(() =>
            {
                int ok = HwidSpoofer.SpoofAll(Log);
                Log($"Spoof All finished — {ok} succeeded.");
            });
        }

        private async Task SpoofSelectedAsync()
        {
            var selected = Identifiers.Where(r => r.IsSelected && r.IsPresent).Select(r => r.Key).ToList();
            if (selected.Count == 0)
            {
                Log("No identifiers selected.");
                return;
            }

            if (!await ConfirmSpoofAsync($"Spoof {selected.Count} selected identifier(s)?"))
                return;

            // Prefer preview values when the user generated them for the selection.
            var previewOverrides = Identifiers
                .Where(r => r.IsSelected && !string.IsNullOrEmpty(r.PreviewValue))
                .ToDictionary(r => r.Key, r => r.PreviewValue, StringComparer.OrdinalIgnoreCase);

            await RunElevatedAsync(() =>
            {
                // Keep ComputerName pair in sync when both selected and no preview.
                string? sharedName = null;
                if (selected.Contains(HwidIdentifierKeys.ComputerName) ||
                    selected.Contains(HwidIdentifierKeys.ActiveComputerName))
                {
                    if (previewOverrides.TryGetValue(HwidIdentifierKeys.ComputerName, out var cn))
                        sharedName = cn;
                    else if (previewOverrides.TryGetValue(HwidIdentifierKeys.ActiveComputerName, out var acn))
                        sharedName = acn;
                    else
                        sharedName = HwidSpoofer.GenerateComputerName();
                }

                int ok = 0;
                foreach (string key in selected)
                {
                    string? ov = null;
                    if (key is HwidIdentifierKeys.ComputerName or HwidIdentifierKeys.ActiveComputerName)
                        ov = sharedName;
                    else if (previewOverrides.TryGetValue(key, out var preview))
                        ov = preview;

                    if (HwidSpoofer.Spoof(key, Log, ov))
                        ok++;
                }

                Log($"Spoof Selected finished — {ok}/{selected.Count} succeeded.");
            });
        }

        private async Task RevertAllAsync()
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated — revert is disabled.");
                return;
            }

            var confirm = await Frontend.ShowMessageBox(
                "Restore all backed-up HWID values from Spectre settings?\n\n" +
                "Identifiers without a backup will be skipped. A reboot may still be needed for ComputerName / some values.",
                MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
            {
                Log("Revert cancelled.");
                return;
            }

            await Task.Run(() =>
            {
                int ok = HwidSpoofer.RevertAll(Log);
                Log($"Revert All finished — {ok} restored.");
            });

            Dispatcher.UIThread.Post(Refresh);
        }

        private async Task<bool> ConfirmSpoofAsync(string actionLine)
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated — spoof is disabled.");
                return false;
            }

            var confirm = await Frontend.ShowMessageBox(
                actionLine + "\n\n" +
                "WARNING: These changes are MACHINE-WIDE and require administrator rights.\n" +
                "They can affect Windows activation, licensing, Windows Update identity, and network adapters.\n" +
                "A reboot may be required before some values take effect (especially ComputerName).\n\n" +
                "Originals are backed up in Spectre settings so you can Revert All later.\n\nContinue?",
                MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
            {
                Log("Spoof cancelled.");
                return false;
            }

            return true;
        }

        private async Task RunElevatedAsync(Action work)
        {
            if (!OperatingSystem.IsWindows() || !IsElevated)
            {
                Log("Not elevated — action is disabled.");
                return;
            }

            await Task.Run(work);
            Dispatcher.UIThread.Post(Refresh);
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
            const string ident = "HwidSpooferViewModel::RelaunchAsAdmin";
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
                App.Logger.WriteLine(ident, "UAC prompt was cancelled — staying on current process.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(ident, ex);
                _ = Frontend.ShowMessageBox(
                    $"Couldn't relaunch as administrator.\n\nReason: {ex.GetType().Name}: {ex.Message}\n\n" +
                    "Close Spectre and right-click the exe → 'Run as administrator' instead.",
                    MessageBoxImage.Warning);
            }
        }
    }
}
