namespace Froststrap
{
    /// <summary>
    /// Watches the official Roblox Versions folder. When a player install lands there,
    /// the next Spectre launch wipes its own copy and installs fresh.
    /// </summary>
    public static class RobloxInstallListener
    {
        private static FileSystemWatcher? _watcher;
        private static string? _notedGuid;
        private static readonly object Gate = new();

        public static void Start()
        {
            if (!OperatingSystem.IsWindows() || !Paths.Initialized)
                return;

            lock (Gate)
            {
                if (_watcher is not null)
                    return;

                string root = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                try
                {
                    Directory.CreateDirectory(root);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("RobloxInstallListener", ex);
                    return;
                }

                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (_, e) => Note(e.FullPath);
                _watcher.Changed += (_, e) => Note(e.FullPath);
                App.Logger.WriteLine("RobloxInstallListener", $"Watching {root}");
            }
        }

        private static void Note(string path)
        {
            try
            {
                string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                    return;

                string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!name.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                {
                    dir = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(dir))
                        return;
                    name = Path.GetFileName(dir);
                    if (!name.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                        return;
                }

                if (!File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")) && !File.Exists(Path.Combine(dir, "RobloxStudioBeta.exe")))
                    return;

                lock (Gate)
                {
                    if (string.Equals(_notedGuid, name, StringComparison.OrdinalIgnoreCase))
                        return;
                    _notedGuid = name;
                }

                if (App.State?.Prop is null)
                    return;

                string? current = App.PlayerState?.Prop.VersionGuid;
                if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
                    return;

                App.Logger.WriteLine("RobloxInstallListener", $"Roblox installation detected: {name}. Leaving the current client and its settings in place.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RobloxInstallListener", ex);
            }
        }
    }
}
