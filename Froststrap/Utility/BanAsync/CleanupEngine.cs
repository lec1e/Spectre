using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    // Sweeps Roblox-related files, registry, and prefetch entries off the machine.
    // Modeled on focat69/rblxswap's bypass:run handler but reimplemented in C# and scoped
    // so it never touches ExploitStrap's own settings directory.
    public static class CleanupEngine
    {
        private const string LOG_IDENT = "CleanupEngine";

        // Roblox processes we'll terminate before deleting their files. We intentionally do NOT
        // kill third-party launchers (ExploitStrap, Fishstrap, Voidstrap, Wbloxstrap) — those are
        // separate apps the user installed deliberately and may be mid-flight.
        private static readonly string[] RobloxProcessNames =
        {
            "RobloxPlayerBeta",
            "RobloxStudioBeta",
            "RobloxCrashHandler",
            "RobloxPlayerLauncher",
            "RobloxPlayerInstaller",
            "Roblox",
            "RobloxStudio"
        };

        public class CleanupOptions
        {
            public bool PreserveInGameSettings { get; set; } = true;
            public bool PreserveFastFlags { get; set; } = true;
            public bool IncludeStudioFolders { get; set; } = false;

            // Also wipe ExploitStrap's own Versions folder (the downloaded Roblox
            // installs under %LocalAppData%\ExploitStrap\Versions). Forces a fresh
            // download on next launch. Off unless the user opts in.
            public bool CleanMrExVersions { get; set; } = false;
        }

        public class CleanupResult
        {
            public int DeletedDirectories { get; set; }
            public int DeletedFiles { get; set; }
            public int RegistryKeysRemoved { get; set; }
            public int PreservedFiles { get; set; }
            public List<string> Skipped { get; } = new();
        }

        public static CleanupResult RunCleanup(CleanupOptions options, Action<string> log)
        {
            var result = new CleanupResult();

            log("Closing Roblox processes…");
            int killed = KillRobloxTree(log);
            log(killed == 0 ? "No Roblox processes were running." : $"Closed {killed} Roblox process(es).");
            Thread.Sleep(400);
            TruncateRobloxCookies(log);

            // Preserve step: copy out any files the user wants kept, before we wipe their parent dirs.
            var preserveBackup = new Dictionary<string, byte[]>();
            if (options.PreserveInGameSettings || options.PreserveFastFlags)
            {
                log("Backing up files to preserve…");
                preserveBackup = SnapshotPreservedFiles(options, log);
                result.PreservedFiles = preserveBackup.Count;
                log($"Held {preserveBackup.Count} file(s) in memory for restore.");
            }

            // Directory wipes.
            foreach (string path in BuildDirectoryTargets(options))
            {
                if (!Directory.Exists(path))
                    continue;

                try
                {
                    Directory.Delete(path, recursive: true);
                    result.DeletedDirectories++;
                    log($"Deleted directory {path}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::DeleteDir", ex);
                    result.Skipped.Add(path);
                    log($"Skipped {path}: {ex.Message}");
                }
            }

            // Optional: wipe ExploitStrap's own downloaded Roblox installs (Versions).
            if (options.CleanMrExVersions)
                CleanMrExVersionsFolder(result, log);

            // Glob targets: %TEMP%\Roblox*
            string tempRoot = Path.GetTempPath();
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(tempRoot, "Roblox*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        result.DeletedDirectories++;
                        log($"Deleted {dir}");
                    }
                    catch (Exception ex)
                    {
                        result.Skipped.Add(dir);
                        log($"Skipped {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::TempGlob", ex);
            }

            // Prefetch — needs admin (system protected). We try anyway; if it fails we log.
            int prefetchDeleted = DeletePrefetch(log);
            result.DeletedFiles += prefetchDeleted;

            // Registry: HKCU\Software\ROBLOX Corporation.
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\ROBLOX Corporation", throwOnMissingSubKey: false);
                result.RegistryKeysRemoved++;
                log(@"Removed HKCU\Software\ROBLOX Corporation");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RegistryHKCU", ex);
                log($"Couldn't remove HKCU\\Software\\ROBLOX Corporation: {ex.Message}");
            }

            // Restore preserved files.
            if (preserveBackup.Count > 0)
            {
                log("Restoring preserved files…");
                foreach (var (path, bytes) in preserveBackup)
                {
                    try
                    {
                        string? dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllBytes(path, bytes);
                        log($"Restored {path}");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT + "::Restore", ex);
                        log($"Failed to restore {path}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        // Wipe ExploitStrap's own Versions folder (the downloaded Roblox installs).
        // Junction-aware: unlink reparse points without recursing into their targets so a
        // recursive delete can never clobber a per-profile dir through a junction
        // (the v420.25 lesson). Real per-profile dirs are deleted recursively.
        private static void CleanMrExVersionsFolder(CleanupResult result, Action<string> log)
        {
            string versions = Paths.Versions;
            if (string.IsNullOrEmpty(versions) || !Directory.Exists(versions))
            {
                log("No ExploitStrap Versions folder to clean.");
                return;
            }

            log($"Wiping ExploitStrap Versions folder {versions}…");

            foreach (string child in Directory.EnumerateDirectories(versions))
            {
                try
                {
                    if (VersionJunctionManager.IsJunction(child))
                        Directory.Delete(child, recursive: false);
                    else
                        Directory.Delete(child, recursive: true);

                    result.DeletedDirectories++;
                    log($"Deleted {child}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::CleanVersions", ex);
                    result.Skipped.Add(child);
                    log($"Skipped {child}: {ex.Message}");
                }
            }

            foreach (string file in Directory.EnumerateFiles(versions))
            {
                try { File.Delete(file); result.DeletedFiles++; }
                catch (Exception ex) { App.Logger.WriteException(LOG_IDENT + "::CleanVersionsFile", ex); }
            }
        }

        public static int KillRobloxTree(Action<string> log)
        {
            int killed = KillProcesses(log);
            string robloxRoot = Path.Combine(Paths.LocalAppData, "Roblox");
            if (!Directory.Exists(robloxRoot))
                return killed;

            string rootFull = Path.GetFullPath(robloxRoot);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string? image;
                    try { image = p.MainModule?.FileName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(image))
                        continue;
                    string full = Path.GetFullPath(image);
                    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill();
                    p.WaitForExit(2000);
                    killed++;
                    log($"Stopped {p.ProcessName} (pid {p.Id}) — running from the folder being deleted");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::KillTree", ex);
                }
                finally
                {
                    p.Dispose();
                }
            }
            return killed;
        }

        private static void TruncateRobloxCookies(Action<string> log)
        {
            string[] cookiePaths =
            [
                Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "RobloxCookies.dat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roblox", "LocalStorage", "RobloxCookies.dat"),
            ];
            foreach (string path in cookiePaths)
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    File.WriteAllBytes(path, []);
                    log($"Truncated {path}");
                }
                catch (Exception ex)
                {
                    log($"Cookie wipe {path}: {ex.Message}");
                }
            }
        }

        private static int KillProcesses(Action<string> log)
        {
            int killed = 0;
            foreach (string name in RobloxProcessNames)
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); }
                catch { continue; }

                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT + "::Kill", ex);
                        log($"Couldn't kill {name} (pid {p.Id}): {ex.Message}");
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            return killed;
        }

        private static IEnumerable<string> BuildDirectoryTargets(CleanupOptions options)
        {
            string localAppData = Paths.LocalAppData;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // Player install + cache + logs in the default location.
            yield return Path.Combine(localAppData, "Roblox");

            // The "logs" and "http" caches sometimes live under Roaming AppData.
            yield return Path.Combine(appData, "Roblox", "logs");
            yield return Path.Combine(appData, "Roblox", "http");

            // Machine-wide Roblox state.
            yield return Path.Combine(programData, "Roblox");

            if (options.IncludeStudioFolders)
            {
                yield return Path.Combine(localAppData, "Roblox", "Studio");
                yield return Path.Combine(appData, "Roblox", "Studio");
            }
        }

        private static Dictionary<string, byte[]> SnapshotPreservedFiles(CleanupOptions options, Action<string> log)
        {
            var snapshot = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string localAppData = Paths.LocalAppData;
            string robloxRoot = Path.Combine(localAppData, "Roblox");

            if (!Directory.Exists(robloxRoot))
                return snapshot;

            if (options.PreserveInGameSettings)
            {
                // GlobalBasicSettings_*.xml typically lives at the Roblox root.
                TryCaptureMatching(robloxRoot, "GlobalBasicSettings_*.xml", snapshot, log);
            }

            if (options.PreserveFastFlags)
            {
                // Roblox stores ClientAppSettings.json inside ClientSettings under each version dir.
                TryCaptureMatching(robloxRoot, "ClientAppSettings.json", snapshot, log);
                TryCaptureMatching(robloxRoot, "ClientSettings.json", snapshot, log);
            }

            return snapshot;
        }

        // Cap per-file size to avoid OOM on weird/corrupt files. The real config files we want
        // to preserve (GlobalBasicSettings_*.xml, ClientAppSettings.json) are typically tens of KB
        // and never legitimately above a few MB.
        private const long MaxPreserveFileBytes = 16L * 1024 * 1024;

        private static void TryCaptureMatching(string root, string pattern, Dictionary<string, byte[]> snapshot, Action<string> log)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > MaxPreserveFileBytes)
                        {
                            log($"Skipping preserve for {file} — file is {info.Length / (1024 * 1024)} MB (cap: {MaxPreserveFileBytes / (1024 * 1024)} MB)");
                            continue;
                        }

                        snapshot[file] = File.ReadAllBytes(file);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT + "::Capture", ex);
                        log($"Couldn't read {file} for preserve: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::CaptureEnum", ex);
            }
        }

        private static int DeletePrefetch(Action<string> log)
        {
            int count = 0;
            string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (!Directory.Exists(prefetchDir))
                return 0;

            string[] patterns = { "ROBLOXCRASHHANDLER.EXE-*.pf", "ROBLOXPLAYERBETA.EXE-*.pf" };

            foreach (string pattern in patterns)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(prefetchDir, pattern))
                    {
                        try
                        {
                            File.Delete(file);
                            count++;
                            log($"Deleted prefetch {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            // Prefetch is protected — admin usually required.
                            App.Logger.WriteException(LOG_IDENT + "::Prefetch", ex);
                            log($"Couldn't delete prefetch {Path.GetFileName(file)} (admin required): {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    log("Skipped prefetch — needs administrator privileges.");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::PrefetchEnum", ex);
                }
            }
            return count;
        }
    }
}
