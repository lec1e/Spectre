namespace Froststrap.Utility.BanAsync
{
    public static class IdentityPipeline
    {
        public sealed class Result
        {
            public bool Ok { get; set; }
            public bool Dirty { get; set; }
            public string Status { get; set; } = "Ready";
            public List<string> Log { get; } = [];
        }

        public static string DescribeStatus(IdentityBackupData data)
        {
            if (data.Dirty)
                return string.IsNullOrEmpty(data.DirtyReason) ? "Dirty" : $"Dirty — {data.DirtyReason}";
            if (data.AnyActive)
                return "Spoofed";
            return "Ready";
        }

        public static Result Spoof(Action<string> log, bool relaunchEclipse)
        {
            var result = new Result();
            Action<string> capture = line =>
            {
                result.Log.Add(line);
                log(line);
            };
            log = capture;
            var store = IdentityBackupStore.Default;
            IdentityBackupData data;
            try
            {
                store.MigrateFromSettings();
                data = store.LoadOrDefault();
            }
            catch (Exception ex)
            {
                log($"Backup load failed: {ex.Message}");
                result.Status = "Dirty";
                result.Dirty = true;
                return result;
            }

            if (data.AnyActive && !data.Dirty)
            {
                log("Already spoofed — Revert first, or continue to re-apply remaining modules.");
            }

            try
            {
                if (!data.AnyActive)
                {
                    log("Capturing identity baseline…");
                    data.SystemUuid ??= FirmwareUuidSpoofer.Capture();
                    data.Memory ??= MemoryProfileSpoofer.Capture();
                    data.Edid ??= EdidSpoofer.Capture();
                    data.SystemReg ??= SystemRegSpoofer.Capture();
                    store.Save(data);
                }

                log("── Step 1: Clear Roblox cookies ──");
                CleanupEngine.KillRobloxTree(log);
                Thread.Sleep(600);
                WipeRobloxCookies(log);

                log("── Step 2: Delete Roblox folders ──");
                bool fullWipe = App.Settings.Prop.BanAsyncFullWipe;
                if (fullWipe)
                    log("Full wipe mode — ignoring preserve toggles, including Studio and versions.");
                var options = new CleanupEngine.CleanupOptions
                {
                    PreserveInGameSettings = !fullWipe && App.Settings.Prop.BanAsyncPreserveInGameSettings,
                    PreserveFastFlags = !fullWipe && App.Settings.Prop.BanAsyncPreserveFastFlags,
                    IncludeStudioFolders = fullWipe || App.Settings.Prop.BanAsyncIncludeStudioFolders,
                    CleanMrExVersions = fullWipe || App.Settings.Prop.BanAsyncCleanVersions
                };
                CleanupEngine.RunCleanup(options, log);
                store.Save(data);

                log("── Step 3: System UUID ──");
                if (!FirmwareUuidSpoofer.Spoof(data, log))
                    return Fail(store, data, result, log, "UUID spoof failed");
                store.Save(data);

                log("── Step 4: Memory profile ──");
                if (!MemoryProfileSpoofer.Spoof(data, log))
                    return Fail(store, data, result, log, "Memory profile failed");
                store.Save(data);

                log("── Step 5: Monitor EDID ──");
                if (!EdidSpoofer.Spoof(data, log))
                    return Fail(store, data, result, log, "EDID spoof failed");
                store.Save(data);

                log("── Step 6: SystemReg ──");
                if (!SystemRegSpoofer.Spoof(data, log))
                    return Fail(store, data, result, log, "SystemReg spoof failed");

                data.Dirty = false;
                data.DirtyReason = null;
                store.Save(data);

                if (relaunchEclipse)
                {
                    log("── Step 7: Relaunch Spectre ──");
                    RelaunchEclipse(log);
                }

                result.Ok = true;
                result.Status = "Spoofed";
                log("Spoof pipeline finished.");
                IdentityUtilities.AppendOperationLog("Spoof", result.Log, true);
                return result;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("IdentityPipeline::Spoof", ex);
                return Fail(store, data, result, log, ex.Message);
            }
        }

        public static Result Revert(Action<string> log)
        {
            var result = new Result();
            Action<string> capture = line =>
            {
                result.Log.Add(line);
                log(line);
            };
            log = capture;
            var store = IdentityBackupStore.Default;
            IdentityBackupData data;
            try
            {
                data = store.LoadOrDefault();
            }
            catch (Exception ex)
            {
                log($"Backup load failed: {ex.Message}");
                result.Dirty = true;
                result.Status = "Dirty";
                return result;
            }

            bool ok = true;
            log("── Revert System UUID ──");
            ok &= FirmwareUuidSpoofer.Revert(data, log);
            log("── Revert Memory profile ──");
            ok &= MemoryProfileSpoofer.Revert(data, log);
            log("── Revert EDID ──");
            ok &= EdidSpoofer.Revert(data, log);
            log("── Revert SystemReg ──");
            ok &= SystemRegSpoofer.Revert(data, log);

            if (ok)
            {
                data.Dirty = false;
                data.DirtyReason = null;
                result.Ok = true;
                result.Status = "Ready";
                log("Revert finished.");
            }
            else
            {
                data.Dirty = true;
                data.DirtyReason = "Revert incomplete";
                result.Dirty = true;
                result.Status = "Dirty";
                log("Revert incomplete — some modules failed.");
            }

            try { store.Save(data); }
            catch (Exception ex) { log($"Backup save failed: {ex.Message}"); }

            IdentityUtilities.AppendOperationLog("Revert", result.Log, ok);
            return result;
        }

        public static void HandleProcessExit()
        {
            if (!OperatingSystem.IsWindows())
                return;
            try
            {
                if (App.Settings.Prop.BanAsyncPersistent)
                    return;
                foreach (string guid in App.Settings.Prop.BanAsyncSpoofedAdapterGuids.ToList())
                    MacSpoofer.DeleteNetworkAddressByGuid(guid);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("IdentityPipeline::HandleProcessExit", ex);
            }
        }

        private static Result Fail(IdentityBackupStore store, IdentityBackupData data, Result result, Action<string> log, string reason)
        {
            data.Dirty = true;
            data.DirtyReason = reason;
            try { store.Save(data); } catch { }
            result.Ok = false;
            result.Dirty = true;
            result.Status = "Dirty";
            log($"Pipeline aborted: {reason}");
            IdentityUtilities.AppendOperationLog("Spoof", result.Log, false);
            return result;
        }

        private static void WipeRobloxCookies(Action<string> log)
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

        private static void RelaunchEclipse(Action<string> log)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{Paths.Process}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                log($"Launched Spectre unelevated via Explorer ({Paths.Process}).");
            }
            catch (Exception ex)
            {
                log($"Could not relaunch Spectre: {ex.Message}");
            }
        }
    }
}
