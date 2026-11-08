using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Froststrap.UI.ViewModels.Settings
{
    // Multi-instance mutex + window tiling + live Roblox process list.
    public class MultiInstanceViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "MultiInstanceViewModel";

        public ObservableCollection<RobloxInstanceInfo> RunningInstances { get; } = new();

        public MultiInstanceViewModel()
        {
            RefreshRunningInstances();
        }

        public bool MultiInstanceEnabled
        {
            get => App.Settings.Prop.MultiInstanceEnabled;
            set
            {
                App.Settings.Prop.MultiInstanceEnabled = value;
                OnPropertyChanged(nameof(MultiInstanceEnabled));
                if (value && OperatingSystem.IsWindows())
                    MultiInstance.HoldSingletonMutex();
            }
        }

        public bool WindowTilingEnabled
        {
            get => App.Settings.Prop.WindowTilingEnabled;
            set { App.Settings.Prop.WindowTilingEnabled = value; OnPropertyChanged(nameof(WindowTilingEnabled)); }
        }

        public WindowTilingLayout SelectedTilingLayout
        {
            get => App.Settings.Prop.WindowTilingLayout;
            set { App.Settings.Prop.WindowTilingLayout = value; OnPropertyChanged(nameof(SelectedTilingLayout)); }
        }

        public IReadOnlyList<WindowTilingLayout> TilingLayouts { get; } =
            Enum.GetValues<WindowTilingLayout>().ToList();

        public string RunningInstancesHeader => RunningInstances.Count switch
        {
            0 => "Running Roblox instances (none)",
            1 => "Running Roblox instances (1)",
            _ => $"Running Roblox instances ({RunningInstances.Count})"
        };

        public bool HasNoRunningInstances => RunningInstances.Count == 0;

        public ICommand RefreshRunningInstancesCommand => new RelayCommand(RefreshRunningInstances);
        public ICommand KillInstanceCommand => new RelayCommand<int?>(pid => { if (pid is int p) KillInstance(p); });
        public ICommand TileNowCommand => new RelayCommand(TileNow);

        private void TileNow()
        {
            if (!OperatingSystem.IsWindows())
                return;
            WindowTiler.TileNow(SelectedTilingLayout);
            RefreshRunningInstances();
        }

        private void RefreshRunningInstances()
        {
            RunningInstances.Clear();

            try
            {
                foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    string uptime;
                    long memMb = 0;
                    try
                    {
                        uptime = FormatUptime(DateTime.Now - p.StartTime);
                        memMb = p.WorkingSet64 / 1024 / 1024;
                    }
                    catch
                    {
                        uptime = "?";
                    }

                    string title = "";
                    try { if (OperatingSystem.IsWindows()) title = GetMainWindowTitle(p.Id); }
                    catch { }

                    RunningInstances.Add(new RobloxInstanceInfo(p.Id, uptime, memMb, title));
                    p.Dispose();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RefreshRunningInstances", ex);
            }

            OnPropertyChanged(nameof(HasNoRunningInstances));
            OnPropertyChanged(nameof(RunningInstancesHeader));
        }

        private void KillInstance(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill();
                proc.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::KillInstance", ex);
            }
            RefreshRunningInstances();
        }

        private static string FormatUptime(TimeSpan t)
        {
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{(int)t.TotalSeconds}s";
        }

        private static string GetMainWindowTitle(int pid)
        {
            string result = "";
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint winPid);
                if ((int)winPid != pid) return true;
                if (!IsWindowVisible(hWnd)) return true;

                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                result = title;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
    }
}
