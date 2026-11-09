namespace Froststrap.Utility
{
    /// <summary>
    /// Checks GitHub for a newer stable Spectre release and installs Spectre.exe.
    /// The settings window, launch menu, and tray watcher are not installers and must not block this.
    /// </summary>
    public static class AppUpdater
    {
        private const string LOG_IDENT = "AppUpdater";
        private static int _checked;

        public static async Task<bool> TryUpdateAsync(Action<string>? setStatus = null, IBootstrapperDialog? dialog = null)
        {
            if (Interlocked.Exchange(ref _checked, 1) == 1)
                return false;

            if (App.Settings.Prop.UpdateChecks == UpdateCheck.Disabled)
            {
                App.Logger.WriteLine(LOG_IDENT, "Update checking is disabled in settings");
                return false;
            }

            if (AnotherBootstrapperIsRunning())
            {
                App.Logger.WriteLine(LOG_IDENT, "Another Roblox launch is already in progress, skipping this update check");
                Interlocked.Exchange(ref _checked, 0);
                return false;
            }

            setStatus?.Invoke(Strings.Bootstrapper_Status_CheckingUpdates);
            App.Logger.WriteLine(LOG_IDENT, "Checking for updates...");

            GithubRelease? releaseInfo;
            try
            {
                releaseInfo = await App.GetLatestRelease(includePreRelease: false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Interlocked.Exchange(ref _checked, 0);
                return false;
            }

            if (releaseInfo is null || string.IsNullOrWhiteSpace(releaseInfo.TagName))
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to get release information");
                Interlocked.Exchange(ref _checked, 0);
                return false;
            }

            string currentVer = App.Version;
            string releaseVer = releaseInfo.TagName;
            VersionComparison versionComparison;
            try
            {
                versionComparison = Utilities.CompareVersions(currentVer, releaseVer);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }

            if (versionComparison == VersionComparison.Equal || versionComparison == VersionComparison.GreaterThan)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No updates found. Current: {currentVer}, Latest: {releaseVer}");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Update available: {currentVer} -> {releaseVer}");

            if (OperatingSystem.IsLinux())
            {
                var results = await Frontend.ShowMessageBox(
                    string.Format(Strings.Update_Linux_Available, releaseVer),
                    MessageBoxImage.Information,
                    MessageBoxButton.YesNo);

                if (results == MessageBoxResult.Yes)
                    Utilities.ShellExecute(App.ProjectDownloadLink);

                return false;
            }

            var asset = Bootstrapper.FindPlatformAsset(releaseInfo.Assets);
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                App.Logger.WriteLine(LOG_IDENT, "No suitable asset found for this platform");
                await Frontend.ShowMessageBox(
                    string.Format(Strings.Update_NoPackageAvailable, "Windows"),
                    MessageBoxImage.Warning);
                Utilities.ShellExecute(App.ProjectDownloadLink);
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Found matching asset: {asset.Name}");

            if (!App.LaunchSettings.QuietFlag.Active)
            {
                string releaseType = releaseInfo.Prerelease ? "pre-release" : "stable";
                var result = await Frontend.ShowMessageBox(
                    string.Format(Strings.Update_Available, releaseType, releaseVer, "\n\nWould you like to update now?"),
                    MessageBoxImage.Question,
                    MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    App.Logger.WriteLine(LOG_IDENT, "User declined the update");
                    return false;
                }
            }

            IBootstrapperDialog? ownedDialog = null;
            if (dialog is null && !App.LaunchSettings.QuietFlag.Active)
            {
                ownedDialog = await App.Settings.Prop.BootstrapperStyle.GetNew();
                ownedDialog.CancelEnabled = false;
                ownedDialog.ProgressIndeterminate = true;
                ownedDialog.ShowBootstrapper();
                dialog = ownedDialog;
            }

            try
            {
                string status = string.Format(Strings.Bootstrapper_Status_DownloadingUpdate, releaseVer);
                setStatus?.Invoke(status);
                if (dialog is not null)
                    dialog.Message = status;

                string downloadPath = Path.Combine(Paths.TempUpdates, asset.Name ?? "Spectre.exe");
                Directory.CreateDirectory(Paths.TempUpdates);
                App.Logger.WriteLine(LOG_IDENT, $"Downloading update from {asset.BrowserDownloadUrl}");

                await DownloadAsync(asset.BrowserDownloadUrl, downloadPath, dialog);

                App.Logger.WriteLine(LOG_IDENT, $"Download complete: {downloadPath}");
                if (dialog is not null)
                    dialog.ProgressIndeterminate = true;

                status = string.Format(Strings.Bootstrapper_Status_InstallingUpdate, releaseVer);
                setStatus?.Invoke(status);
                if (dialog is not null)
                    dialog.Message = status;

                bool updateApplied = await Bootstrapper.ApplyUpdate(downloadPath);
                if (!updateApplied)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Update application failed");
                    ownedDialog?.CloseBootstrapper();
                    await Frontend.ShowMessageBox(
                        string.Format(Strings.Bootstrapper_AutoUpdateFailed, releaseVer),
                        MessageBoxImage.Information);
                    Utilities.ShellExecute(App.ProjectDownloadLink);
                    return false;
                }

                App.Logger.WriteLine(LOG_IDENT, "Update applied successfully");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "An exception occurred during update");
                App.Logger.WriteException(LOG_IDENT, ex);
                ownedDialog?.CloseBootstrapper();

                if (!App.LaunchSettings.QuietFlag.Active)
                    await Frontend.ShowMessageBox(Strings.Bootstrapper_AutoUpdateFailed, MessageBoxImage.Information);

                Interlocked.Exchange(ref _checked, 0);
                return false;
            }
        }

        /// <summary>
        /// A second Spectre window that is only the menu, settings, or tray must not cancel the update.
        /// </summary>
        private static bool AnotherBootstrapperIsRunning()
        {
            int self = Environment.ProcessId;
            string install = Paths.Application;

            foreach (string name in new[] { App.ExecutableName, App.ProjectName, "Eclipse" })
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch { continue; }

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.Id == self)
                            continue;

                        string? path = null;
                        try { path = process.MainModule?.FileName; }
                        catch { continue; }

                        if (string.IsNullOrEmpty(path) || !path.Equals(install, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string? command = ReadProcessCommandLine(process.Id);
                        if (command is null)
                            continue;

                        if (IsRobloxLaunch(command))
                            return true;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        private static bool IsRobloxLaunch(string command)
        {
            return command.Contains("-player", StringComparison.OrdinalIgnoreCase)
                || command.Contains("-studio", StringComparison.OrdinalIgnoreCase)
                || command.Contains("roblox-player:", StringComparison.OrdinalIgnoreCase)
                || command.Contains("roblox-studio", StringComparison.OrdinalIgnoreCase)
                || command.Contains("roblox:", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadProcessCommandLine(int pid)
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
                if (locatorType is null)
                    return null;

                object locator = Activator.CreateInstance(locatorType)!;
                dynamic service = locatorType.InvokeMember(
                    "ConnectServer",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    locator,
                    new object[] { ".", @"root\cimv2" })!;
                dynamic results = service.ExecQuery($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                foreach (dynamic item in results)
                    return item.CommandLine as string;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::ReadProcessCommandLine", ex);
            }

            return null;
        }

        private static async Task DownloadAsync(string url, string destination, IBootstrapperDialog? dialog)
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Spectre");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            if (dialog is not null)
            {
                dialog.ProgressIndeterminate = totalBytes <= 0;
                dialog.ProgressMaximum = 1000;
                dialog.ProgressValue = 0;
                dialog.TaskbarProgressState = totalBytes <= 0
                    ? TaskbarItemProgressState.Indeterminate
                    : TaskbarItemProgressState.Normal;
                dialog.TaskbarProgressValue = 0;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (dialog is null || totalBytes <= 0)
                    continue;

                int progress = (int)Math.Clamp(totalRead * 1000 / totalBytes, 0, 1000);
                dialog.ProgressValue = progress;
                dialog.TaskbarProgressValue = progress / 1000d;
            }

            if (totalBytes > 0 && totalRead != totalBytes)
                throw new IOException($"Downloaded {totalRead} bytes but expected {totalBytes}");

            if (new FileInfo(destination).Length < 1024 * 1024)
                throw new IOException("The downloaded update is too small to be Spectre.");
        }
    }
}
