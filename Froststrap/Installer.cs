using System.Runtime.Versioning;
using Microsoft.Win32;
using Froststrap.Utility;

namespace Froststrap
{
    internal class Installer
    {
        /// <summary>
        /// Should this version automatically open the release notes page?
        /// Recommended for major updates only.
        /// </summary>
        private const bool OpenReleaseNotes = false;

        private static string DesktopShortcut => Path.Combine(Paths.Desktop, $"{App.ProjectName}.lnk");
        private static string StartMenuShortcut => Path.Combine(Paths.WindowsStartMenu, $"{App.ProjectName}.lnk");

        /// <summary>Default install root: %LOCALAPPDATA%\Eclipse (same pattern as Froststrap).</summary>
        public string InstallLocation { get; set; } = Path.Combine(Paths.LocalAppData, App.ProjectName);

        public bool CreateDesktopShortcuts { get; set; } = true;
        public bool CreateStartMenuShortcuts { get; set; } = true;
        public bool IsImplicitInstall { get; set; }
        public string InstallLocationError { get; set; } = "";

        /// <summary>
        /// Copies the running EXE into LocalAppData, writes Apps &amp; features uninstall
        /// entries, and creates Start Menu / Desktop shortcuts — same as Froststrap.
        /// </summary>
        public async Task DoInstall()
        {
            const string LOG_IDENT = "Installer::DoInstall";

            App.Logger.WriteLine(LOG_IDENT, $"Beginning installation to '{InstallLocation}'");

            Directory.CreateDirectory(InstallLocation);
            Paths.Initialize(InstallLocation);

            if (!IsImplicitInstall)
            {
                Filesystem.AssertReadOnly(Paths.Application);

                try
                {
                    File.Copy(Paths.Process, Paths.Application, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not overwrite executable");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    await Frontend.ShowMessageBox(
                        "Eclipse could not be installed because the existing file could not be overwritten. Close any running Eclipse processes and try again.",
                        MessageBoxImage.Error);
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                    return;
                }
            }

            if (OperatingSystem.IsWindows())
            {
                using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
                {
                    uninstallKey.SetValueSafe("DisplayIcon", $"{Paths.Application},0");
                    uninstallKey.SetValueSafe("DisplayName", App.ProjectName);
                    uninstallKey.SetValueSafe("DisplayVersion", App.Version);

                    if (uninstallKey.GetValue("InstallDate") is null)
                        uninstallKey.SetValueSafe("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                    uninstallKey.SetValueSafe("InstallLocation", Paths.Base);
                    uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValueSafe("Publisher", App.ProjectOwner);
                    uninstallKey.SetValueSafe("ModifyPath", $"\"{Paths.Application}\" -settings");
                    uninstallKey.SetValueSafe("QuietUninstallString", $"\"{Paths.Application}\" -uninstall -quiet");
                    uninstallKey.SetValueSafe("UninstallString", $"\"{Paths.Application}\" -uninstall");
                    uninstallKey.SetValueSafe("HelpLink", App.ProjectHelpLink);
                    uninstallKey.SetValueSafe("URLInfoAbout", App.ProjectSupportLink);
                    uninstallKey.SetValueSafe("URLUpdateInfo", App.ProjectDownloadLink);
                }

                WindowsRegistry.RegisterApis();
                WindowsRegistry.RegisterPlayer();

                if (App.IsStudioInstalled)
                    WindowsRegistry.RegisterStudio();

                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Froststrap",
                        throwOnMissingSubKey: false);
                }
                catch { /* ignore */ }
            }

            if (CreateDesktopShortcuts)
                Shortcut.Create(Paths.Application, "", DesktopShortcut);

            if (CreateStartMenuShortcuts)
                Shortcut.Create(Paths.Application, "", StartMenuShortcut);

            App.Settings.Load(false);
            App.State.Load(false);
            App.FastFlags.Load(false);
            App.Settings.Save();

            App.Logger.WriteLine(LOG_IDENT, "Installation finished");
        }

        public bool CheckInstallLocation()
        {
            InstallLocationError = "";

            if (string.IsNullOrWhiteSpace(InstallLocation))
            {
                InstallLocationError = "Install location is not set.";
                return false;
            }

            if (InstallLocation.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || InstallLocation.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
                || InstallLocation == Path.GetPathRoot(InstallLocation))
            {
                InstallLocationError = "Eclipse cannot be installed to this location.";
                return false;
            }

            try
            {
                string testFile = Path.Combine(InstallLocation, $"{App.ProjectName}WriteTest.txt");
                Directory.CreateDirectory(InstallLocation);
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                InstallLocationError = $"No write permission: {ex.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Uninstalls Eclipse: restores Roblox protocols, removes shortcuts / registry /
        /// app files under LocalAppData, then schedules deletion of the running EXE
        /// (and the whole install folder when keepData is false).
        /// </summary>
        public static async Task DoUninstall(bool keepData)
        {
            const string LOG_IDENT = "Installer::DoUninstall";

            KillOtherEclipseProcesses();

            var processes = new List<Process>();

            if (App.IsPlayerInstalled)
                processes.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName)));

            if (App.IsStudioInstalled)
                processes.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxStudioAppName)));

            // Also catch common Roblox process names that may still hold files.
            processes.AddRange(Process.GetProcessesByName("RobloxPlayerBeta"));
            processes.AddRange(Process.GetProcessesByName("RobloxCrashHandler"));
            processes.AddRange(Process.GetProcessesByName("Roblox"));

            processes = processes
                .Where(p => { try { return !p.HasExited; } catch { return false; } })
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToList();

            if (processes.Count != 0)
            {
                var result = await Frontend.ShowMessageBox(
                    Strings.Bootstrapper_Uninstall_RobloxRunning,
                    MessageBoxImage.Information,
                    MessageBoxButton.OKCancel,
                    MessageBoxResult.OK
                );

                if (result != MessageBoxResult.OK)
                {
                    App.Terminate(ErrorCode.ERROR_CANCELLED);
                    return;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to close process: {ex}");
                    }
                }
            }

            if (OperatingSystem.IsWindows())
                RestoreRobloxRegistryHandlers();

            // When invoked by NSIS (-nsis flag), stop here (NSIS finishes the rest).
            if (App.LaunchSettings.NsisFlag.Active)
                return;

            string installRoot = Paths.Base;
            string applicationPath = Paths.Application;

            // Always remove launcher leftovers + regenerable cache / versions.
            try { Shortcut.Delete(DesktopShortcut); } catch { /* ignore */ }
            try { Shortcut.Delete(StartMenuShortcut); } catch { /* ignore */ }
            // Legacy shortcut names from earlier branding.
            try { Shortcut.Delete(Path.Combine(Paths.Desktop, "Froststrap.lnk")); } catch { /* ignore */ }
            try { Shortcut.Delete(Path.Combine(Paths.WindowsStartMenu, "Froststrap.lnk")); } catch { /* ignore */ }
            try { Shortcut.Delete(Path.Combine(Paths.Desktop, "Eclipse-QA.lnk")); } catch { /* ignore */ }
            try { Shortcut.Delete(Path.Combine(Paths.WindowsStartMenu, "Eclipse-QA.lnk")); } catch { /* ignore */ }

            // Froststrap-style: remove any desktop .lnk that still targets our EXE.
            TryDeleteShortcutsTargetingApplication();

            // Prefer wiping the real install root under LocalAppData\Eclipse.
            string defaultInstallRoot = Path.Combine(Paths.LocalAppData, App.ProjectName);
            if (string.IsNullOrWhiteSpace(installRoot))
                installRoot = defaultInstallRoot;

            TryDeleteDirectory(Paths.Versions);
            TryDeleteDirectory(Paths.Downloads);
            TryDeleteDirectory(Paths.Cache);
            TryDeleteDirectory(Paths.Logs);
            TryDeleteDirectory(Paths.Integrations);
            TryDeleteDirectory(Paths.TempUpdates);
            TryDeleteDirectory(Paths.Temp);
            TryDeleteDirectory(Path.Combine(installRoot, "Updates"));
            TryDeleteDirectory(Path.Combine(installRoot, "WebViewProfiles"));
            TryDeleteDirectory(Path.Combine(installRoot, "Wine"));
            TryDeleteDirectory(Path.Combine(installRoot, "ModBackup"));
            TryDeleteDirectory(Path.Combine(defaultInstallRoot, "Updates"));

            TryDeleteFile(App.State.FileLocation);
            TryDeleteFile(Path.Combine(installRoot, "State.json.bak"));
            TryDeleteFile(Path.Combine(installRoot, "Data.json"));
            TryDeleteFile(Path.Combine(installRoot, "Profiles.json"));
            TryDeleteFile(Path.Combine(installRoot, "ModManifest.txt"));
            TryDeleteFile(Path.Combine(installRoot, ".version"));
            TryDeleteFile(Path.Combine(installRoot, "eclipse.png"));
            TryDeleteFile(Path.Combine(installRoot, "froststrap.png"));
            TryDeleteFile(Path.Combine(installRoot, "PlayerState.json"));
            TryDeleteFile(Path.Combine(installRoot, "StudioState.json"));
            TryDeleteFile(Path.Combine(installRoot, "AppStorage.json"));
            TryDeleteFile(Path.Combine(installRoot, "GlobalSettings.json"));
            TryDeleteFile(Path.Combine(installRoot, "FastFlags.json"));
            TryDeleteFile(Path.Combine(installRoot, "accounts.v2.json"));
            TryDeleteFile(Path.Combine(installRoot, "accounts.json"));

            if (Paths.Roblox == Path.Combine(installRoot, "Roblox"))
                TryDeleteDirectory(Paths.Roblox);

            if (!keepData)
            {
                // Wipe user data so nothing Eclipse-owned remains under LocalAppData.
                TryDeleteDirectory(Paths.Modifications);
                TryDeleteDirectory(Paths.CustomCursors);
                TryDeleteDirectory(Paths.CustomThemes);
                TryDeleteDirectory(Paths.FastFlagProfiles);
                TryDeleteDirectory(Paths.SavedFlagProfiles);
                TryDeleteDirectory(Path.Combine(installRoot, "AltMan"));
                TryDeleteDirectory(Path.Combine(installRoot, "CustomThemes"));
                TryDeleteDirectory(Path.Combine(installRoot, "CustomCursorsSets"));
                TryDeleteDirectory(Path.Combine(installRoot, "SavedFlagProfiles"));
                TryDeleteDirectory(Path.Combine(installRoot, "FastFlagProfiles"));
                TryDeleteDirectory(Path.Combine(installRoot, "Modifications"));

                TryDeleteFile(App.Settings.FileLocation);
                TryDeleteFile(Path.Combine(installRoot, "Settings.json.bak"));
                TryDeleteFile(App.State.FileLocation);

                // Best-effort wipe now (EXE may still be locked).
                TryDeleteLooseFiles(installRoot, preserveExe: true);
                if (!string.Equals(installRoot, defaultInstallRoot, StringComparison.OrdinalIgnoreCase))
                    TryDeleteLooseFiles(defaultInstallRoot, preserveExe: true);
            }

            if (OperatingSystem.IsWindows())
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(App.ApisKey, throwOnMissingSubKey: false); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Failed to remove ApisKey: {ex.Message}"); }

                try { Registry.CurrentUser.DeleteSubKeyTree(App.UninstallKey, throwOnMissingSubKey: false); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Failed to remove UninstallKey: {ex.Message}"); }

                // Remove Apps & features entries left by older brands / QA builds.
                foreach (string legacyName in new[] { "Froststrap", "Eclipse-QA", "Bloxstrap" })
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{legacyName}",
                            throwOnMissingSubKey: false);
                    }
                    catch { /* ignore */ }
                }
            }

            // EXE (and optionally the whole install folder) is locked while we run —
            // finish cleanup after this process exits.
            if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(installRoot))
                SchedulePostExitCleanup(installRoot, applicationPath, wipeFolder: !keepData);
            else if (!OperatingSystem.IsWindows() && !keepData)
                TryDeleteDirectory(installRoot);
        }

        private static void KillOtherEclipseProcesses()
        {
            const string LOG_IDENT = "Installer::KillOtherEclipseProcesses";
            int self = Environment.ProcessId;
            bool killedAny = false;

            foreach (string name in new[] { App.ProjectName, "Eclipse", "Eclipse-QA", "Froststrap" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (proc.Id == self)
                            continue;
                        App.Logger.WriteLine(LOG_IDENT, $"Killing leftover {proc.ProcessName} ({proc.Id})");
                        proc.Kill(entireProcessTree: true);
                        killedAny = true;
                        try { proc.WaitForExit(3000); } catch { /* ignore */ }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not kill {name}: {ex.Message}");
                    }
                    finally
                    {
                        try { proc.Dispose(); } catch { /* ignore */ }
                    }
                }
            }

            // Give Windows a moment to release file locks on Eclipse.exe / Versions.
            if (killedAny)
                Thread.Sleep(750);
        }

        private static void TryDeleteFile(string path)
        {
            const string LOG_IDENT = "Installer::TryDeleteFile";
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;
                Filesystem.AssertReadOnly(path);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not delete file '{path}': {ex.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            const string LOG_IDENT = "Installer::TryDeleteDirectory";
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;

                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { Filesystem.AssertReadOnly(file); } catch { /* ignore */ }
                }

                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not delete directory '{path}': {ex.Message}");
            }
        }

        private static void TryDeleteLooseFiles(string directory, bool preserveExe)
        {
            if (!Directory.Exists(directory))
                return;

            string? appName = Path.GetFileName(Paths.Application);

            foreach (string file in Directory.GetFiles(directory))
            {
                if (preserveExe && appName is not null
                    && Path.GetFileName(file).Equals(appName, StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDeleteFile(file);
            }

            foreach (string dir in Directory.GetDirectories(directory))
                TryDeleteDirectory(dir);
        }

        private static void TryDeleteShortcutsTargetingApplication()
        {
            const string LOG_IDENT = "Installer::TryDeleteShortcutsTargetingApplication";
            try
            {
                if (!Directory.Exists(Paths.Desktop))
                    return;

                foreach (string file in Directory.GetFiles(Paths.Desktop, "*.lnk"))
                {
                    try
                    {
                        var shortcut = ShellLink.Shortcut.ReadFromFile(file);
                        string? target = shortcut.ExtraData.EnvironmentVariableDataBlock?.TargetUnicode
                            ?? shortcut.LinkTargetIDList?.Path;
                        if (string.IsNullOrEmpty(target))
                            continue;

                        if (target.Equals(Paths.Application, StringComparison.OrdinalIgnoreCase)
                            || target.Equals(Paths.Process, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Skip shortcut '{file}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Desktop shortcut scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Froststrap-style post-exit delete: detach a cmd that waits for this PID,
        /// then removes Eclipse.exe and (when wipeFolder) the whole install folder.
        /// See https://github.com/Froststrap/Froststrap — Installer.DoUninstall.
        /// </summary>
        private static void SchedulePostExitCleanup(string installRoot, string applicationPath, bool wipeFolder)
        {
            const string LOG_IDENT = "Installer::SchedulePostExitCleanup";

            try
            {
                string scriptPath = Path.Combine(Path.GetTempPath(), $"eclipse-uninstall-{Guid.NewGuid():N}.cmd");
                int pid = Environment.ProcessId;
                string processPath = Paths.Process;
                string defaultRoot = Path.Combine(Paths.LocalAppData, App.ProjectName);
                string tempAppDir = Paths.Temp;
                string tempUpdates = Paths.TempUpdates;

                string Safe(string p) => (p ?? "").Replace("\"", "");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("setlocal EnableExtensions");
                // Wait until Eclipse has fully exited (fixes Froststrap's fixed timeout race).
                sb.AppendLine(":wait");
                sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL");
                sb.AppendLine("if not errorlevel 1 (");
                sb.AppendLine("  ping 127.0.0.1 -n 2 >NUL");
                sb.AppendLine("  goto wait");
                sb.AppendLine(")");
                sb.AppendLine("ping 127.0.0.1 -n 3 >NUL");

                // Always delete the installed EXE and the running process path (Froststrap deletes Application).
                sb.AppendLine($"set \"EXE1={Safe(applicationPath)}\"");
                sb.AppendLine($"set \"EXE2={Safe(processPath)}\"");
                sb.AppendLine("for /L %%i in (1,1,40) do (");
                sb.AppendLine("  if exist \"%EXE1%\" (");
                sb.AppendLine("    attrib -r -s -h \"%EXE1%\" >NUL 2>&1");
                sb.AppendLine("    del /f /q \"%EXE1%\" >NUL 2>&1");
                sb.AppendLine("  )");
                sb.AppendLine("  if exist \"%EXE2%\" (");
                sb.AppendLine("    attrib -r -s -h \"%EXE2%\" >NUL 2>&1");
                sb.AppendLine("    del /f /q \"%EXE2%\" >NUL 2>&1");
                sb.AppendLine("  )");
                sb.AppendLine("  if not exist \"%EXE1%\" if not exist \"%EXE2%\" goto exe_done");
                sb.AppendLine("  ping 127.0.0.1 -n 2 >NUL");
                sb.AppendLine(")");
                sb.AppendLine(":exe_done");

                if (wipeFolder)
                {
                    void AppendWipe(string label, string path)
                    {
                        if (string.IsNullOrWhiteSpace(path))
                            return;
                        sb.AppendLine($"set \"{label}={Safe(path)}\"");
                        sb.AppendLine($"for /L %%i in (1,1,40) do (");
                        sb.AppendLine($"  if exist \"%{label}%\" (");
                        sb.AppendLine($"    attrib -r -s -h \"%{label}%\\*.*\" /s /d >NUL 2>&1");
                        sb.AppendLine($"    del /f /q \"%{label}%\\*.*\" >NUL 2>&1");
                        sb.AppendLine($"    for /d %%D in (\"%{label}%\\*\") do rd /s /q \"%%~fD\" >NUL 2>&1");
                        sb.AppendLine($"    rd /s /q \"%{label}%\" >NUL 2>&1");
                        sb.AppendLine($"  )");
                        sb.AppendLine($"  if not exist \"%{label}%\" goto {label}_done");
                        sb.AppendLine($"  ping 127.0.0.1 -n 2 >NUL");
                        sb.AppendLine($")");
                        sb.AppendLine($":{label}_done");
                    }

                    AppendWipe("TARGET", installRoot);
                    if (!string.Equals(installRoot, defaultRoot, StringComparison.OrdinalIgnoreCase))
                        AppendWipe("TARGET2", defaultRoot);
                }

                sb.AppendLine($"if exist \"{Safe(tempAppDir)}\" rd /s /q \"{Safe(tempAppDir)}\" >NUL 2>&1");
                sb.AppendLine($"if exist \"{Safe(tempUpdates)}\" rd /s /q \"{Safe(tempUpdates)}\" >NUL 2>&1");
                sb.AppendLine("del \"%~f0\" >NUL 2>&1");

                File.WriteAllText(scriptPath, sb.ToString());

                // Detach from Eclipse's process/job so Avalonia shutdown cannot kill the cleaner.
                // `start "" /min` creates an independent process that outlives this EXE.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" /min cmd.exe /c \"{scriptPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                // Backup delayed wipe in its own script file (avoids nested-quote breakage).
                string backupPath = Path.Combine(Path.GetTempPath(), $"eclipse-uninstall-backup-{Guid.NewGuid():N}.cmd");
                var backup = new System.Text.StringBuilder();
                backup.AppendLine("@echo off");
                backup.AppendLine("timeout /t 10 /nobreak >NUL");
                backup.AppendLine($"del /f /q \"{Safe(applicationPath)}\" >NUL 2>&1");
                backup.AppendLine($"del /f /q \"{Safe(processPath)}\" >NUL 2>&1");
                if (wipeFolder)
                {
                    backup.AppendLine($"rd /s /q \"{Safe(installRoot)}\" >NUL 2>&1");
                    if (!string.Equals(installRoot, defaultRoot, StringComparison.OrdinalIgnoreCase))
                        backup.AppendLine($"rd /s /q \"{Safe(defaultRoot)}\" >NUL 2>&1");
                }
                backup.AppendLine("del \"%~f0\" >NUL 2>&1");
                File.WriteAllText(backupPath, backup.ToString());

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" /min cmd.exe /c \"{backupPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                App.Logger.WriteLine(LOG_IDENT, $"Scheduled post-exit cleanup (wipeFolder={wipeFolder}) via {scriptPath}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to schedule post-exit cleanup: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void RestoreRobloxRegistryHandlers()
        {
            using var playerKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player");
            var playerFolder = playerKey?.GetValue("InstallLocation");

            if (playerKey is null || playerFolder is not string playerFolderStr)
            {
                WindowsRegistry.Unregister("roblox");
                WindowsRegistry.Unregister("roblox-player");
            }
            else
            {
                string playerPath = Path.Combine(playerFolderStr, App.RobloxPlayerAppName);
                WindowsRegistry.RegisterPlayer(playerPath, "%1");
            }

            using var studioKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio");
            var studioFolder = studioKey?.GetValue("InstallLocation");

            if (studioKey is null || studioFolder is not string studioFolderStr)
            {
                WindowsRegistry.Unregister("roblox-studio");
                WindowsRegistry.Unregister("roblox-studio-auth");
                WindowsRegistry.Unregister("Roblox.Place");
                WindowsRegistry.Unregister(".rbxl");
                WindowsRegistry.Unregister(".rbxlx");
            }
            else
            {
                string studioPath = Path.Combine(studioFolderStr, App.RobloxStudioAppName);
                WindowsRegistry.RegisterStudioProtocol(studioPath, "%1");
                WindowsRegistry.RegisterStudioFileClass(studioPath, "-ide \"%1\"");
            }
        }

        public static async Task HandleUpgrade()
        {
            const string LOG_IDENT = "Installer::HandleUpgrade";

            if (!File.Exists(Paths.Application) || Paths.Process == Paths.Application)
                return;

            bool isAutoUpgrade = App.LaunchSettings.UpgradeFlag.Active
                || Paths.Process.StartsWith(Path.Combine(Paths.Base, "Updates"))
                || Paths.Process.StartsWith(Path.Combine(Paths.Temp, "Updates"))
                || Paths.Process.StartsWith(Paths.TempUpdates);

            var existingVer = GetVersionInfo(Paths.Application);
            var currentVer = GetVersionInfo(Paths.Process);

            if (MD5Hash.FromFile(Paths.Process) == MD5Hash.FromFile(Paths.Application))
                return;

            if (currentVer is not null && existingVer is not null)
            {
                var comparison = Utilities.CompareVersions(currentVer, existingVer);

                if (comparison == VersionComparison.LessThan)
                {
                    var result = await Frontend.ShowMessageBox(
                        Strings.InstallChecker_VersionLessThanInstalled,
                        MessageBoxImage.Question,
                        MessageBoxButton.YesNo
                    );

                    if (result != MessageBoxResult.Yes)
                        return;
                }
            }

            if (!isAutoUpgrade)
            {
                var result = await Frontend.ShowMessageBox(
                    Strings.InstallChecker_VersionDifferentThanInstalled,
                    MessageBoxImage.Question,
                    MessageBoxButton.YesNo
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Starting upgrade process...");

            bool copySuccess = await CopyExecutableWithRetry();
            if (!copySuccess)
                return;

            await UpdateVersionInfo();

            await RunMigrations(existingVer);

            App.Settings.Save();
            App.FastFlags.Save();
            App.State.Save();
            App.PlayerState.Save();
            App.StudioState.Save();

            if (isAutoUpgrade && OpenReleaseNotes)
            {
                Utilities.ShellExecute($"https://github.com/{App.ProjectRepository}/releases/tag/{currentVer ?? App.Version}");
            }
            else if (!isAutoUpgrade)
            {
                await Frontend.ShowMessageBox(
                    string.Format(Strings.InstallChecker_Updated, currentVer ?? App.Version),
                    MessageBoxImage.Information
                );
            }

            App.Logger.WriteLine(LOG_IDENT, "Upgrade completed successfully");
        }

        private static string? GetVersionInfo(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var versionInfo = FileVersionInfo.GetVersionInfo(filePath);

                if (!string.IsNullOrEmpty(versionInfo.ProductVersion))
                    return versionInfo.ProductVersion;

                if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                    return versionInfo.FileVersion;

                if (OperatingSystem.IsMacOS())
                {
                    string infoPlist = Path.Combine(Path.GetDirectoryName(filePath) ?? "", "..", "Info.plist");
                    if (File.Exists(infoPlist))
                    {
                        var plist = new System.Xml.XmlDocument();
                        plist.Load(infoPlist);
                        var node = plist.SelectSingleNode("//key[text()='CFBundleShortVersionString']/following-sibling::string");
                        if (node != null)
                            return node.InnerText;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> CopyExecutableWithRetry()
        {
            const string LOG_IDENT = "Installer::CopyExecutableWithRetry";

            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    if (File.Exists(Paths.Application))
                    {
                        var fileInfo = new FileInfo(Paths.Application) { IsReadOnly = false };
                        if (OperatingSystem.IsLinux())
                        {
                            var psi = new ProcessStartInfo("chmod", $"+w \"{Paths.Application}\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var process = Process.Start(psi);
                            await process!.WaitForExitAsync();
                        }
                    }
                }

                for (int i = 1; i <= 10; i++)
                {
                    try
                    {
                        File.Copy(Paths.Process, Paths.Application, true);

                        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                        {
                            var psi = new ProcessStartInfo("chmod", $"+x \"{Paths.Application}\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var process = Process.Start(psi);
                            await process!.WaitForExitAsync();
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        if (i == 10)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Failed to copy after 10 attempts: {ex.Message}");
                            App.Logger.WriteException(LOG_IDENT, ex);
                            return false;
                        }

                        await Task.Delay(500);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to copy executable: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        private static async Task UpdateVersionInfo()
        {
            const string LOG_IDENT = "Installer::UpdateVersionInfo";

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowsRegistry.RegisterUninstallEntry();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    string appPath = Paths.Application;
                    string infoPlist = Path.Combine(Path.GetDirectoryName(appPath) ?? "", "..", "Info.plist");

                    if (File.Exists(infoPlist))
                    {
                        var plist = new System.Xml.XmlDocument();
                        plist.Load(infoPlist);

                        var versionNode = plist.SelectSingleNode("//key[text()='CFBundleShortVersionString']/following-sibling::string");
                        if (versionNode != null)
                        {
                            versionNode.InnerText = App.Version;
                            plist.Save(infoPlist);
                        }
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    string versionFile = Path.Combine(Paths.Base, ".version");
                    await File.WriteAllTextAsync(versionFile, App.Version);

                    string desktopFile = Path.Combine(Paths.UserProfile, ".local", "share", "applications",
                        $"{App.ProjectName.ToLower()}.desktop");
                    if (File.Exists(desktopFile))
                    {
                        var content = await File.ReadAllTextAsync(desktopFile);
                    }
                }

                App.Logger.WriteLine(LOG_IDENT, $"Version info updated to {App.Version}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to update version info: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static async Task RunMigrations(string? previousVersion = null)
        {
            const string LOG_IDENT = "Installer::RunMigrations";

            if (OperatingSystem.IsLinux())
                SetupSoberSymlink();

            string currentVer = App.Version;
            string? existingVer = previousVersion ?? App.State.Prop.LastMigratedVersion;

            if (existingVer is null && !App.Settings.IsSaved)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Fresh install detected — stamping LastMigratedVersion as {currentVer}");
                App.State.Prop.LastMigratedVersion = currentVer;
                App.State.Save();
                return;
            }

            if (existingVer is null)
            {
                var legacyStateCheck = new JsonManager<RobloxState>();
                if (!legacyStateCheck.IsSaved)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No LastMigratedVersion but no legacy data found — treating as already migrated");
                    App.State.Prop.LastMigratedVersion = currentVer;
                    App.State.Save();
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, "Legacy RobloxState data found — treating as pre-migration install");
                existingVer = "0.0.0";
            }

            if (Utilities.CompareVersions(existingVer, currentVer) != VersionComparison.LessThan)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Migrations up to date (last={existingVer}, current={currentVer})");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Running migrations: {existingVer} -> {currentVer}");

            if (Utilities.CompareVersions(existingVer, "1.4.0.0") == VersionComparison.LessThan)
            {
                JsonManager<RobloxState> legacyRobloxState = new();

                if (legacyRobloxState.IsSaved)
                {
                    if (legacyRobloxState.Load(false))
                    {
                        App.PlayerState.Prop.VersionGuid = legacyRobloxState.Prop.Player.VersionGuid;
                        App.PlayerState.Prop.PackageHashes = legacyRobloxState.Prop.Player.PackageHashes;
                        App.PlayerState.Prop.Size = legacyRobloxState.Prop.Player.Size;
                        App.PlayerState.Prop.ModManifest = legacyRobloxState.Prop.ModManifest;

                        App.StudioState.Prop.VersionGuid = legacyRobloxState.Prop.Studio.VersionGuid;
                        App.StudioState.Prop.PackageHashes = legacyRobloxState.Prop.Studio.PackageHashes;
                        App.StudioState.Prop.Size = legacyRobloxState.Prop.Studio.Size;
                    }

                    legacyRobloxState.Delete();
                }

                if (App.Settings.Prop.Theme == Theme.Custom)
                    App.Settings.Prop.Theme = Theme.Default;

                TryDelete(Path.Combine(Paths.Cache, "GameHistory.json"));
            }
            if (Utilities.CompareVersions(existingVer, "1.4.2") == VersionComparison.LessThan)
            {
                string genCacheDir = Path.Combine(Path.GetTempPath(), App.ProjectName, "mod-generator");
                string pluginCacheDir = Path.Combine(Paths.Roblox, "Plugins", "FroststrapStudioRPC.rbxmx");

                if (Directory.Exists(genCacheDir))
                {
                    Directory.Delete(genCacheDir, true);
                    App.Logger.WriteLine(LOG_IDENT, "Deleted mod-generator cache for migration.");
                }

                if (Directory.Exists(pluginCacheDir))
                {
                    Directory.Delete(pluginCacheDir, true);
                    App.Logger.WriteLine(LOG_IDENT, "Deleted studio plugin for migration.");
                }

                TryDelete(Path.Combine(Paths.Cache, "channelCache.json"));
                TryDelete(Path.Combine(Paths.Cache, "channelCacheMeta.json"));
                TryDelete(Path.Combine(Paths.Cache, "datacenters_cache.json"));
            }

            if (Utilities.CompareVersions(existingVer, "1.5.1") == VersionComparison.LessThan)
            {
                App.Settings.Prop.BootstrapperStyle = BootstrapperStyle.FluentAeroDialog;
                App.Settings.Prop.SelectedBackdrop = WindowsBackdrops.None;
            }

            App.State.Prop.LastMigratedVersion = currentVer;
            App.State.Save();

            if (App.PlayerState.Loaded) App.PlayerState.Save();
            if (App.StudioState.Loaded) App.StudioState.Save();

            App.Logger.WriteLine(LOG_IDENT, $"Migrations complete — LastMigratedVersion set to {currentVer}");
        }

        [SupportedOSPlatform("windows")]
        public static void UpdateUninstallRegistryVersion()
        {
            WindowsRegistry.RegisterUninstallEntry();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static void SetupSoberSymlink()
        {
            const string LOG_IDENT = "Installer::SetupSoberSymlink";

            string flatpakId = "org.vinegarhq.Sober";
            string flatpakDataPath = Path.Combine(Paths.UserProfile, ".var", "app", flatpakId);
            string soberTarget = Path.Combine(Paths.Versions, "Sober");

            if (IsSymlinkPointingAt(flatpakDataPath, soberTarget))
            {
                App.Logger.WriteLine(LOG_IDENT, "Sober symlink already in place, skipping.");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Setting up Sober symlink: {flatpakDataPath} -> {soberTarget}");

            Directory.CreateDirectory(soberTarget);

            if (Directory.Exists(flatpakDataPath) && !IsSymlink(flatpakDataPath))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Copying existing Sober data from {flatpakDataPath} to {soberTarget}");

                var cp = new ProcessStartInfo("cp", $"-a \"{flatpakDataPath}/.\" \"{soberTarget}/\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(cp))
                    proc?.WaitForExit();

                App.Logger.WriteLine(LOG_IDENT, $"Removing original Sober data directory at {flatpakDataPath}");

                // rm -rf handles locked subdirs that Directory.Delete can't remove.
                var rm = new ProcessStartInfo("rm", $"-rf \"{flatpakDataPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(rm))
                    proc?.WaitForExit();
            }
            else if (IsSymlink(flatpakDataPath))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Removing stale symlink at {flatpakDataPath}");
                Directory.Delete(flatpakDataPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(flatpakDataPath)!);

            Directory.CreateSymbolicLink(flatpakDataPath, soberTarget);
            App.Logger.WriteLine(LOG_IDENT, $"Created symlink: {flatpakDataPath} -> {soberTarget}");
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static bool IsSymlink(string path)
        {
            if (!Path.Exists(path))
                return false;

            try
            {
                var attributes = File.GetAttributes(path);
                return attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch { return false; }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static bool IsSymlinkPointingAt(string path, string expectedTarget)
        {
          if (!IsSymlink(path)) 
                return false;

          try
          {
               string? actual = Directory.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
              return actual == expectedTarget;
           }
           catch { return false; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }
    }
}
