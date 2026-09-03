using System.Text;

namespace Froststrap.Utility.BanAsync
{
    public static class IdentityUtilities
    {
        private const long LogRotateBytes = 1_000_000;

        public static string OperationLogPath =>
            Path.Combine(Paths.LocalAppData, App.ProjectName, "identity.log");

        public static void AppendOperationLog(string op, IEnumerable<string> lines, bool ok)
        {
            try
            {
                string path = OperationLogPath;
                string? parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                if (File.Exists(path) && new FileInfo(path).Length > LogRotateBytes)
                {
                    string old = Path.ChangeExtension(path, ".log.old");
                    try { File.Delete(old); } catch { }
                    File.Move(path, old, overwrite: true);
                }
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===== {op} ({(ok ? "ok" : "FAILED")}) =====");
                foreach (string line in lines)
                    sb.AppendLine(line);
                File.AppendAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("IdentityUtilities::AppendOperationLog", ex);
            }
        }

        public static IReadOnlyList<string> ReadOperationLog(int maxLines = 400)
        {
            try
            {
                if (!File.Exists(OperationLogPath))
                    return ["No operations recorded yet."];
                string[] all = File.ReadAllLines(OperationLogPath);
                int start = Math.Max(0, all.Length - maxLines);
                var tail = all.Skip(start).ToArray();
                return tail.Length == 0 ? ["No operations recorded yet."] : tail;
            }
            catch
            {
                return ["No operations recorded yet."];
            }
        }

        public static void CreateRestorePoint(Action<string> log)
        {
            string desc = $"Spectre {DateTime.Now:yyyy-MM-dd HH:mm}".Replace("'", "''");
            log("Creating System Restore Point…");
            var (exit, stdout, stderr) = IdentityWin32.PowerShell(
                $"try {{ Checkpoint-Computer -Description '{desc}' -RestorePointType 'MODIFY_SETTINGS'; exit 0 }} catch {{ Write-Error $_; exit 1 }}");
            if (exit == 0)
                log($"Restore point created: {desc}");
            else
            {
                log("Could not create a restore point — System Restore may be disabled, or restore points are rate-limited to one per 24h.");
                if (!string.IsNullOrWhiteSpace(stderr))
                    log($"Detail: {stderr.Trim()}");
            }
        }

        public static void Preflight(Action<string> log)
        {
            log("── Preflight checks ──");
            bool admin = IsElevated();
            log($"Administrator: {(admin ? "elevated" : "NOT elevated — spoof/revert need admin")}");
            log($"OS: {Environment.OSVersion}");
            log($"CPU: {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");

            var store = IdentityBackupStore.Default;
            log($"Backup: {(File.Exists(store.Path) ? store.Path : "none")}");
            try
            {
                var data = store.LoadOrDefault();
                log(data.Dirty
                    ? $"State: DIRTY — Revert recommended ({data.DirtyReason})"
                    : data.AnyActive ? "State: SPOOFED" : "State: clean");
            }
            catch (Exception ex)
            {
                log($"Backup load failed: {ex.Message}");
            }

            var smbios = FirmwareUuidSpoofer.ReadSmbios();
            log($"SMBIOS UUID: {smbios.Uuid}");
            log($"BIOS vendor: {smbios.BiosVendor}{(IdentityWin32.IsAmiBios(smbios.BiosVendor) ? " (AMI)" : "")}");
            log($"Roblox folder: {(Directory.Exists(Path.Combine(Paths.LocalAppData, "Roblox")) ? "present" : "absent")}");
        }

        public static void FlushDns(Action<string> log)
        {
            log("Flushing DNS…");
            var (exit, stdout, stderr) = IdentityWin32.Run("ipconfig", "/flushdns");
            log(exit == 0 ? "DNS cache flushed." : $"ipconfig /flushdns failed: {stderr}".Trim());
        }

        public static void ClearRobloxTemp(Action<string> log)
        {
            string temp = Path.GetTempPath();
            int n = 0;
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(temp, "Roblox*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        n++;
                        log($"Deleted {dir}");
                    }
                    catch (Exception ex)
                    {
                        log($"Skipped {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Temp glob failed: {ex.Message}");
            }
            log(n == 0 ? "No Roblox temp folders." : $"Cleared {n} Roblox temp folder(s).");
        }

        public static bool IsElevated()
        {
            if (!OperatingSystem.IsWindows())
                return false;
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
