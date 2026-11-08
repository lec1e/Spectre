using Froststrap.Models;

namespace Froststrap.Utility
{
    /// <summary>
    /// Roblox clients on this PC. Spectre's own install is preferred, and clients
    /// started by another launcher are included too.
    /// </summary>
    public static class InstallSession
    {
        public static IReadOnlyList<Process> Clients(LaunchMode mode)
        {
            if (!OperatingSystem.IsWindows())
                return [];

            string processName = mode is LaunchMode.Studio or LaunchMode.StudioAuth
                ? "RobloxStudioBeta"
                : "RobloxPlayerBeta";

            var running = new List<Process>();
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited)
                        running.Add(process);
                }
                catch { }
            }

            return running;
        }

        public static Process? Newest(LaunchMode mode)
        {
            var clients = Clients(mode);
            return clients.Where(IsInsideInstall).OrderByDescending(SafeStart).FirstOrDefault()
                ?? clients.OrderByDescending(SafeStart).FirstOrDefault();
        }

        /// <summary>
        /// The install-folder client, or a player that just started when its path cannot be read.
        /// </summary>
        public static Process? NewestOrRecent(LaunchMode mode)
        {
            var known = Newest(mode);
            if (known is not null)
                return known;

            string processName = mode is LaunchMode.Studio or LaunchMode.StudioAuth
                ? "RobloxStudioBeta"
                : "RobloxPlayerBeta";

            return Process.GetProcessesByName(processName)
                .Where(process => SafeStart(process) > DateTime.Now.AddSeconds(-20))
                .OrderByDescending(SafeStart)
                .FirstOrDefault();
        }

        public static bool IsInsideInstall(Process process)
        {
            if (!Paths.Initialized || string.IsNullOrEmpty(Paths.Base))
                return false;

            string? path = ExecutablePath(process);
            if (string.IsNullOrEmpty(path))
                return false;

            string root = Paths.Base.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}Versions{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && path.StartsWith(Paths.Base, StringComparison.OrdinalIgnoreCase);
        }

        public static string? NewestPlayerLog(DateTime notBeforeUtc)
        {
            if (!Directory.Exists(Paths.RobloxLogs))
                return null;

            return Directory.GetFiles(Paths.RobloxLogs, "*.log")
                .Select(path => new FileInfo(path))
                .Where(file => file.CreationTimeUtc >= notBeforeUtc
                    && file.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)
                    && !file.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.CreationTimeUtc)
                .FirstOrDefault()?.FullName;
        }

        private static string? ExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static int _watching;

        /// <summary>
        /// While Spectre is open, start the tray as soon as a Roblox client from this install is running.
        /// </summary>
        public static void Watch()
        {
            if (!OperatingSystem.IsWindows()
                || App.LaunchSettings.WatcherFlag.Active
                || App.LaunchSettings.UninstallFlag.Active
                || App.LaunchSettings.BackgroundUpdaterFlag.Active)
                return;

            if (Interlocked.Exchange(ref _watching, 1) != 0)
                return;

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        EnsureTray();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException("InstallSession", ex);
                    }

                    await Task.Delay(2000);
                }
            });
        }

        public static void EnsureTray()
        {
            var client = Newest(LaunchMode.Player) ?? Newest(LaunchMode.Studio);
            if (client is null)
                return;

            using (var held = new InterProcessLock("Watcher", TimeSpan.Zero))
            {
                if (!held.IsAcquired)
                    return;
            }

            using var launching = new InterProcessLock("WatcherLaunch", TimeSpan.FromSeconds(2));
            if (!launching.IsAcquired)
                return;

            using (var held = new InterProcessLock("Watcher", TimeSpan.Zero))
            {
                if (!held.IsAcquired)
                    return;
            }

            var mode = client.ProcessName.Contains("Studio", StringComparison.OrdinalIgnoreCase)
                ? LaunchMode.Studio
                : LaunchMode.Player;

            DateTime started = SafeStart(client);
            var data = new WatcherData
            {
                ProcessId = client.Id,
                LaunchMode = mode,
                LogFile = mode == LaunchMode.Player
                    ? NewestPlayerLog(started == DateTime.MinValue ? DateTime.UtcNow.AddMinutes(-2) : started.ToUniversalTime().AddSeconds(-5))
                    : null
            };

            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            Process.Start(Paths.Process, $"-watcher \"{payload}\"");
            App.Logger.WriteLine("InstallSession", $"Showing the tray for Roblox PID {client.Id}");
        }

        private static DateTime SafeStart(Process process)
        {
            try { return process.StartTime; }
            catch { return DateTime.MinValue; }
        }
    }
}
