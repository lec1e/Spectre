using Microsoft.Win32;

namespace Froststrap.Utility
{
    /// <summary>
    /// Removes the installed Roblox client, its files, and its registry entries,
    /// then marks Spectre to download a new copy. Browser cookie databases are never opened.
    /// </summary>
    public static class RobloxReinstaller
    {
        private const string LOG_IDENT = "RobloxReinstaller";

        public const string ConfirmMessage =
            "Reinstall Roblox?\n\n" +
            "Spectre will close Roblox, delete the current Roblox installation, and remove Roblox registry keys. " +
            "Browser cookies are not touched. A new copy of Roblox is downloaded after that.\n\n" +
            "Spectre settings and mods stay.";

        public static async Task<bool> ConfirmAndPrepareAsync()
        {
            var confirm = await Frontend.ShowMessageBox(
                ConfirmMessage,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return false;

            bool ready = await Task.Run(Prepare);
            if (!ready)
            {
                await Frontend.ShowMessageBox(
                    "Spectre could not remove the current Roblox installation. Close Roblox completely, including the system tray, and try again.",
                    MessageBoxImage.Error);
            }

            return ready;
        }

        public static bool Prepare()
        {
            App.Logger.WriteLine(LOG_IDENT, "Preparing a fresh Roblox install. Browser cookies are left untouched.");

            BanAsync.CleanupEngine.KillRobloxTree(message => App.Logger.WriteLine(LOG_IDENT, message));
            Thread.Sleep(400);

            bool installCleared = ClearCurrentInstallation();
            ClearRobloxFiles();
            ClearRobloxRegistry();
            ForgetInstalledPlayer();

            if (OperatingSystem.IsWindows())
            {
                WindowsRegistry.RegisterPlayer();
                WindowsRegistry.RegisterStudio();
            }

            App.Logger.WriteLine(LOG_IDENT, installCleared
                ? "Roblox files and registry keys were cleared. Browser cookies were not modified."
                : "The current Roblox installation is still on disk.");

            return installCleared;
        }

        private static bool ClearCurrentInstallation()
        {
            if (!Paths.Initialized)
                return true;

            var targets = new List<string>();
            string? guid = null;
            try { guid = App.PlayerState.Prop.VersionGuid; }
            catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }

            if (!string.IsNullOrEmpty(guid))
            {
                string versionDir = Path.Combine(Paths.Versions, guid);
                if (VersionJunctionManager.IsJunction(versionDir))
                {
                    string? target = VersionJunctionManager.GetJunctionTargetName(versionDir);
                    if (!string.IsNullOrEmpty(target))
                        targets.Add(target);
                    targets.Add(versionDir);
                }
                else if (Directory.Exists(versionDir))
                {
                    targets.Add(versionDir);
                }
            }

            string? profileId = App.Settings.Prop.ActiveVersionProfileId;
            if (!string.IsNullOrEmpty(profileId))
            {
                string profileDir = VersionJunctionManager.GetProfileDirectory(profileId);
                if (!targets.Any(path => string.Equals(path, profileDir, StringComparison.OrdinalIgnoreCase)))
                    targets.Add(profileDir);
            }

            foreach (string folder in targets)
            {
                if (!DeleteTree(folder))
                    return false;
            }

            ClearDirectoryContents(Paths.Downloads);
            return true;
        }

        private static void ClearRobloxFiles()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (string path in new[]
            {
                Path.Combine(localAppData, "Roblox"),
                Path.Combine(roaming, "Roblox"),
                Path.Combine(programData, "Roblox"),
                Path.Combine(programFiles, "Roblox"),
                Path.Combine(programFilesX86, "Roblox"),
                Paths.RobloxLogs,
                Paths.RobloxCache
            })
            {
                DeleteTree(path);
            }

            try
            {
                string tempRoot = Path.GetTempPath();
                foreach (string dir in Directory.EnumerateDirectories(tempRoot, "Roblox*", SearchOption.TopDirectoryOnly))
                    DeleteTree(dir);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Temp", ex);
            }

            RemoveOfficialShortcuts();
        }

        private static void RemoveOfficialShortcuts()
        {
            string[] names = ["Roblox Player.lnk", "Roblox Studio.lnk", "Roblox Player.url"];
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder))
                    continue;

                foreach (string name in names)
                {
                    try
                    {
                        string path = Path.Combine(folder, name);
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT + "::Shortcut", ex);
                    }
                }

                DeleteTree(Path.Combine(folder, "Programs", "Roblox"));
                DeleteTree(Path.Combine(folder, "Roblox"));
            }
        }

        private static void ClearRobloxRegistry()
        {
            if (!OperatingSystem.IsWindows())
                return;

            string[] userKeys =
            [
                @"Software\ROBLOX Corporation",
                @"Software\Roblox",
                @"Software\Roblox Corporation",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Roblox"
            ];

            string[] machineKeys =
            [
                @"SOFTWARE\ROBLOX Corporation",
                @"SOFTWARE\WOW6432Node\ROBLOX Corporation",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Roblox",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Roblox",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio"
            ];

            foreach (string key in userKeys)
                DeleteRegistryTree(Registry.CurrentUser, key);

            try
            {
                foreach (string key in machineKeys)
                    DeleteRegistryTree(Registry.LocalMachine, key);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Machine registry cleanup needs administrator rights: {ex.Message}");
            }

            DeleteRobloxProtocolKeys();
            DeleteRobloxAppCompatEntries();
        }

        private static void DeleteRobloxProtocolKeys()
        {
            foreach (string name in new[] { "roblox", "roblox-player", "roblox-studio", "roblox-studio-auth" })
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{name}", throwOnMissingSubKey: false);
                    App.Logger.WriteLine(LOG_IDENT, $@"Removed HKCU\Software\Classes\{name}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::Protocol", ex);
                }
            }
        }

        private static void DeleteRobloxAppCompatEntries()
        {
            const string layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(layers, writable: true);
                if (key is null)
                    return;

                foreach (string name in key.GetValueNames())
                {
                    if (!name.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                        continue;

                    key.DeleteValue(name, throwOnMissingValue: false);
                    App.Logger.WriteLine(LOG_IDENT, $"Removed AppCompat entry {name}");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::AppCompat", ex);
            }
        }

        private static void ForgetInstalledPlayer()
        {
            try
            {
                var state = App.PlayerState.Prop;
                state.PackageHashes.Clear();
                state.Size = 0;
                state.ModManifest.Clear();
                App.PlayerState.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::PlayerState", ex);
            }

            App.State.Prop.ForceReinstall = true;
            App.State.Save();

            if (OperatingSystem.IsWindows())
                WindowsRegistry.RegisterClientLocation(isStudio: false, clientPath: "");
        }

        private static void DeleteRegistryTree(RegistryKey root, string subKey)
        {
            try
            {
                root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                App.Logger.WriteLine(LOG_IDENT, $"Removed {root.Name}\\{subKey}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't remove {root.Name}\\{subKey}: {ex.Message}");
            }
        }

        private static void ClearDirectoryContents(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || IsProtectedPath(path))
                return;

            foreach (string file in Directory.GetFiles(path))
            {
                try { File.Delete(file); }
                catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
            }

            foreach (string directory in Directory.GetDirectories(path))
                DeleteTree(directory);
        }

        private static bool DeleteTree(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return true;

            if (IsProtectedPath(path))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refusing to delete protected path {path}");
                return false;
            }

            bool junction = VersionJunctionManager.IsJunction(path);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: !junction);
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                    Thread.Sleep(400);
                }
            }

            bool gone = !Directory.Exists(path);
            if (!gone)
                App.Logger.WriteLine(LOG_IDENT, $"Still present after delete: {path}");
            return gone;
        }

        private static bool IsProtectedPath(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return true; }

            if (Paths.Initialized)
            {
                string root = Path.GetFullPath(Paths.Base);
                string mods = Path.GetFullPath(Paths.Modifications);
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (full.StartsWith(mods, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string[] blocked =
            [
                @"\Google\Chrome",
                @"\Microsoft\Edge",
                @"\Mozilla\Firefox",
                @"\BraveSoftware\",
                @"\Opera Software\",
                @"\Vivaldi\",
                @"\Chromium\User Data"
            ];

            return blocked.Any(part => full.Contains(part, StringComparison.OrdinalIgnoreCase));
        }
    }
}
