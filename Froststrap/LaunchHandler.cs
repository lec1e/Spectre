using Avalonia.Controls;
using Froststrap.Integrations;
using Froststrap.UI.Elements.Dialogs;

namespace Froststrap
{
    public static class LaunchHandler
    {
        public static void ProcessNextAction(NextAction action, bool isUnfinishedInstall = false)
        {
            const string LOG_IDENT = "LaunchHandler::ProcessNextAction";

            switch (action)
            {
                case NextAction.LaunchSettings:
                    App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                    LaunchSettings();
                    break;

                case NextAction.LaunchRoblox:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox");
                    LaunchRoblox(LaunchMode.Player);
                    break;

                case NextAction.LaunchRobloxStudio:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox Studio");
                    LaunchRoblox(LaunchMode.Studio);
                    break;

                default:
                    App.Logger.WriteLine(LOG_IDENT, "Closing");
                    App.Terminate(isUnfinishedInstall ? ErrorCode.ERROR_INSTALL_USEREXIT : ErrorCode.ERROR_SUCCESS);
                    break;
            }
        }

        public static async Task ProcessLaunchArgs()
        {
            const string LOG_IDENT = "LaunchHandler::ProcessLaunchArgs";

            // this order is specific
            if (App.LaunchSettings.UninstallFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening uninstaller");
                await LaunchUninstaller();
            }
            else if (App.LaunchSettings.MenuFlag.Active || App.LaunchSettings.PageFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                LaunchSettings();
            }
            else if (App.LaunchSettings.WatcherFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening watcher");
                LaunchWatcher();
            }
            else if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening background updater");
                LaunchBackgroundUpdater();
            }
            else if (App.LaunchSettings.RobloxLaunchMode != LaunchMode.None)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Opening bootstrapper ({App.LaunchSettings.RobloxLaunchMode})");
                LaunchRoblox(App.LaunchSettings.RobloxLaunchMode);
            }
            else if (App.LaunchSettings.BloxshadeFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening Bloxshade");
                LaunchBloxshadeConfig();
            }
            else if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening menu");
                LaunchMenu();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Closing - quiet flag active");
                App.Terminate();
            }
        }

        public static async Task LaunchUninstaller()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchUninstaller";

            // Prevent Avalonia from shutting down when the uninstall dialog closes —
            // otherwise DoUninstall / post-exit cleanup never runs and files remain.
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            using var interlock = new InterProcessLock("Uninstaller");

            if (!interlock.IsAcquired)
            {
                await Frontend.ShowMessageBox(Strings.Dialog_AlreadyRunning_Uninstaller, MessageBoxImage.Error);
                App.Terminate();
                return;
            }

            bool confirmed = false;
            bool keepData = false;

            if (App.LaunchSettings.QuietFlag.Active)
            {
                confirmed = true;
                keepData = false;
            }
            else
            {
                var dialog = new UninstallerDialog();

                var tcs = new TaskCompletionSource();
                dialog.Closed += (sender, e) => tcs.SetResult();

                dialog.Show();
                await tcs.Task;

                confirmed = dialog.Confirmed;
                keepData = dialog.KeepData;
            }

            if (!confirmed)
            {
                App.Logger.WriteLine(LOG_IDENT, "Uninstall cancelled by user.");
                App.Terminate();
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Running uninstall (keepData={keepData})");
            await Installer.DoUninstall(keepData);

            if (!App.LaunchSettings.QuietFlag.Active)
                await Frontend.ShowMessageBox(Strings.Bootstrapper_SuccessfullyUninstalled, MessageBoxImage.Information);

            // Exit immediately so the post-exit cleaner can delete Eclipse.exe.
            App.Terminate();
            Environment.Exit(0);
        }

        public static void LaunchSettings()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchSettings";

            using var interlock = new InterProcessLock("Settings");

            if (interlock.IsAcquired)
            {
                bool showAlreadyRunningWarning = App.CountOwnProcesses() > 1;

                // before we open the window, force load the distribution states
                // some menu viewmodels require the distribution states, which will result in a short freeze once the page is opened
                if (!App.PlayerState.Loaded)
                    _ = App.PlayerState.Load();
                if (!App.StudioState.Loaded)
                    _ = App.StudioState.Load();

                if (App.Settings.Prop.ShowUsingFroststrapRPC && App.FrostRPC == null)
                {
                    App.FrostRPC = new FroststrapRichPresence();
                }

                var window = new UI.Elements.Settings.MainWindow(showAlreadyRunningWarning);

                App.FrostRPC?.SetPage("Settings");

                window.Closed += (s, e) =>
                {
                    App.FrostRPC?.Dispose();
                    App.FrostRPC = null;
                };

                window.Show();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Found an already existing menu window");

                using var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Froststrap-ActivateSettingsEvent");
                activateEvent.Set();

                App.Terminate();
            }
        }

        public static void LaunchMenu()
        {
            if (App.Settings.Prop.ShowUsingFroststrapRPC && App.FrostRPC == null)
            {
                App.FrostRPC = new FroststrapRichPresence();
            }

            var dialog = new LaunchMenuDialog();
            App.FrostRPC?.SetPage("Launch Menu");

            dialog.Closed += (sender, e) =>
            {
                App.FrostRPC?.Dispose();
                App.FrostRPC = null;
                ProcessNextAction(dialog.CloseAction);
            };

            dialog.Show();
        }

        public static async void LaunchRoblox(LaunchMode launchMode)
        {
            const string LOG_IDENT = "LaunchHandler::LaunchRoblox";

            if (launchMode == LaunchMode.None)
                throw new InvalidOperationException("No Roblox launch mode set");

            if (OperatingSystem.IsWindows() && !File.Exists(Path.Combine(Paths.System, "mfplat.dll")))
            {
                await Frontend.ShowMessageBox(Strings.Bootstrapper_WMFNotFound, MessageBoxImage.Error);

                if (!App.LaunchSettings.QuietFlag.Active)
                    Utilities.ShellExecute("https://support.microsoft.com/en-us/topic/media-feature-pack-list-for-windows-n-editions-c1c6fffa-d052-8338-7a79-a4bb980a700a");

                App.Terminate(ErrorCode.ERROR_FILE_NOT_FOUND);
            }

            if (App.Settings.Prop.ConfirmLaunches && Utilities.IsRobloxRunning() && launchMode == LaunchMode.Player)
            {
                var result = await Frontend.ShowMessageBox(Strings.Bootstrapper_ConfirmLaunch, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    App.Terminate();
                    return;
                }

                if (OperatingSystem.IsLinux())
                    Utilities.KillSober();
            }

            // Refresh executor-tracked profiles before launch (best-effort, short budget)
            if (launchMode == LaunchMode.Player)
            {
                try
                {
                    await Utility.ExecutorProfileRefresher.RefreshActiveAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::ExecutorRefresh", ex);
                }

                await MaybeShowVipServerPickerAsync(launchMode);

                if (!await TryPickVersionProfileAsync())
                {
                    App.Terminate();
                    return;
                }
            }

            if (App.Settings.Prop.EnablePrivacyMode)
                Utility.PrivacyMode.TruncateRobloxCookies();

            if (App.Settings.Prop.MultiInstanceEnabled && launchMode == LaunchMode.Player && OperatingSystem.IsWindows())
                Utility.MultiInstance.PrepareForLaunch();

            // start bootstrapper and show the bootstrapper modal if we're not running silently
            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(launchMode);
            IBootstrapperDialog? dialog = null;

            if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper dialog");
                ThemeCycler.HandleLaunchCycle();
                dialog = await App.Settings.Prop.BootstrapperStyle.GetNew();
                App.Bootstrapper.Dialog = dialog;
                dialog.Bootstrapper = App.Bootstrapper;
            }

            _ = Task.Run(App.Bootstrapper.Run).ContinueWith(async t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Bootstrapper task has finished");

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the bootstrapper");

                    if (t.Exception is not null)
                        await App.FinalizeExceptionHandling(t.Exception);
                }

                App.Terminate();
            });

            if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && !App.LaunchSettings.QuietFlag.Active)
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            dialog?.ShowBootstrapper();

            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }

        public static void LaunchWatcher()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchWatcher";

            // this whole topology is a bit confusing, bear with me:
            // main thread: strictly UI only, handles showing of the notification area icon, context menu, server details dialog
            // - server information task: queries server location, invoked if either the explorer notification is shown or the server details dialog is opened
            // - discord rpc thread: handles rpc connection with discord
            //    - discord rich presence tasks: handles querying and displaying of game information, invoked on activity watcher events
            // - watcher task: runs activity watcher + waiting for roblox to close, terminates when it has

            var watcher = new Watcher();

            Task watcherTask = Task.Run(watcher.Run);

            watcherTask.ContinueWith(async t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher task has finished");

                watcher.Dispose();

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the watcher");

                    if (t.Exception is not null)
                        await App.FinalizeExceptionHandling(t.Exception);
                }

                // Shouldn't this be done after client closes?
                if (App.Settings.Prop.CleanerOptions != CleanerOptions.Never)
                    Cleaner.DoCleaning();

                App.Terminate();
            });
        }

        public static void LaunchBloxshadeConfig()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchBloxshade";

            App.Logger.WriteLine(LOG_IDENT, "Showing unsupported warning");

            new BloxshadeDialog().Show();
            App.SoftTerminate();
        }

        public static void LaunchBackgroundUpdater()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchBackgroundUpdater";

            // Activate some LaunchFlags we need
            App.LaunchSettings.QuietFlag.Active = true;
            App.LaunchSettings.NoLaunchFlag.Active = true;

            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(LaunchMode.Player)
            {
                MutexName = "Froststrap-BackgroundUpdater",
                QuitIfMutexExists = true
            };

            CancellationTokenSource cts = new();

            Task.Run(() =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Started event waiter");
                using (EventWaitHandle handle = new(false, EventResetMode.AutoReset, "Froststrap-BackgroundUpdaterKillEvent"))
                    handle.WaitOne();

                App.Logger.WriteLine(LOG_IDENT, "Received close event, killing it all!");
                App.Bootstrapper.Cancel();
            }, cts.Token);

            Task.Run(App.Bootstrapper.Run).ContinueWith(async t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Bootstrapper task has finished");
                cts.Cancel(); // stop event waiter

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the bootstrapper");

                    if (t.Exception is not null)
                        await App.FinalizeExceptionHandling(t.Exception);
                }

                App.Terminate();
            });

            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }

        private static async Task MaybeShowVipServerPickerAsync(LaunchMode launchMode)
        {
            const string LOG_IDENT = "LaunchHandler::MaybeShowVipServerPickerAsync";

            if (!App.Settings.Prop.EnableVipServerPrompt)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping VIP picker: disabled in settings.");
                return;
            }

            if (launchMode != LaunchMode.Player)
                return;

            if (App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping VIP picker: quiet launch.");
                return;
            }

            if (!OperatingSystem.IsWindows())
                return;

            string args = App.LaunchSettings.RobloxLaunchArgs;
            long? placeId = Utility.LaunchArgsUtility.TryExtractPlaceId(args);
            if (!placeId.HasValue)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping VIP picker: no placeId in launch args.");
                return;
            }

            if (Utility.LaunchArgsUtility.TryExtractAccessCode(args) is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping VIP picker: accessCode already present.");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Showing VIP server picker for place {placeId.Value}");

            try
            {
                var dialog = new VipServerPickerDialog(placeId.Value);
                string? code = await dialog.ShowDialog<string?>(GetOwnerWindow());
                if (!string.IsNullOrEmpty(code))
                {
                    App.LaunchSettings.RobloxLaunchArgs = Utility.LaunchArgsUtility.AppendAccessCode(args, code);
                    App.Logger.WriteLine(LOG_IDENT, "VIP server picked; accessCode appended.");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "VIP picker closed without a selection.");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static async Task<bool> TryPickVersionProfileAsync()
        {
            const string LOG_IDENT = "LaunchHandler::TryPickVersionProfileAsync";

            if (!App.Settings.Prop.ShowVersionPickerOnLaunch)
                return true;

            if (App.Settings.Prop.VersionProfiles.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping version picker: no profiles configured.");
                return true;
            }

            VersionProfile? picked;
            try
            {
                var dialog = new VersionPickerDialog();
                bool? ok = await dialog.ShowDialog<bool?>(GetOwnerWindow());
                if (ok != true)
                {
                    App.Logger.WriteLine(LOG_IDENT, "User cancelled version picker.");
                    return false;
                }
                picked = dialog.PickedProfile;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return true;
            }

            if (picked is null)
                return true;

            App.Settings.Prop.ActiveVersionProfileId = picked.Id;
            if (string.IsNullOrEmpty(picked.VersionGuid))
            {
                App.Settings.Prop.UseCustomVersion = false;
                App.Settings.Prop.CustomVersionGuid = "";
            }
            else
            {
                App.Settings.Prop.UseCustomVersion = true;
                App.Settings.Prop.CustomVersionGuid = picked.VersionGuid;
            }
            App.Settings.Save();

            App.Logger.WriteLine(LOG_IDENT, $"Picker activated profile '{picked.Name}' ({picked.Id})");

            if (App.Settings.Prop.ConfirmNonLiveLaunch
                && !picked.IsBuiltIn
                && !string.IsNullOrEmpty(picked.VersionGuid))
            {
                var result = await Frontend.ShowMessageBox(
                    $"Launching Roblox on a non-LIVE build:\n\nProfile: {picked.Name}\n{picked.VersionGuid}\n\nContinue?",
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    App.Logger.WriteLine(LOG_IDENT, "User declined the non-LIVE confirmation — aborting launch.");
                    return false;
                }
            }

            return true;
        }

        private static Window? GetOwnerWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow ?? desktop.Windows.FirstOrDefault();
            }
            return null;
        }
    }
}
