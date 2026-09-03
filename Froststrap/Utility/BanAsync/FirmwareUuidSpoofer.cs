using System.Net.Http;
using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    public static class FirmwareUuidSpoofer
    {
        private const string MachineGuidPath = @"SOFTWARE\Microsoft\Cryptography";
        private const string SqmPath = @"SOFTWARE\Microsoft\SQMClient";
        private const string LogIdent = "FirmwareUuidSpoofer";

        private sealed record ToolSpec(string Name, string Url, long MinBytes, long MaxBytes, string Sha256);

        private static readonly ToolSpec[] Tools =
        [
            new("AMIDEWINx64.EXE",
                "https://github.com/xxdotdos/amidewinpast/raw/main/AMIDEWINx64.EXE",
                20_000, 5_000_000,
                "2c67fb6eb81630c917f08295e4ff3b5f777cb41b26f7b09dc36d79f089e61bc4"),
            new("amigendrv64.sys",
                "https://github.com/xxdotdos/amidewinpast/raw/main/amigendrv64.sys",
                5_000, 2_000_000,
                "38d87b51f4b69ba2dae1477684a1415f1a3b578eee5e1126673b1beaefee9a20"),
            new("amifldrv64.sys",
                "https://github.com/xxdotdos/amidewinpast/raw/main/amifldrv64.sys",
                5_000, 2_000_000,
                "20f11a64bc4548f4edb47e3d3418da0f6d54a83158224b71662a6292bf45b5fb"),
        ];

        public static SystemUuidBackup Capture()
        {
            var info = ReadSmbios();
            string hwPath = IdentityWin32.HwProfileKeyPath()!;
            return new SystemUuidBackup
            {
                MachineGuid = IdentityWin32.ReadString("HKLM", MachineGuidPath, "MachineGuid"),
                HwProfileGuid = IdentityWin32.ReadString("HKLM", hwPath, "HwProfileGuid"),
                SqmMachineId = IdentityWin32.ReadString("HKLM", SqmPath, "MachineId"),
                SmbiosUuid = info.Uuid,
                SmbiosSerial = info.Serial,
                BiosSerial = info.BiosSerial,
                BoardSerial = info.BoardSerial,
                BiosVendor = info.BiosVendor
            };
        }

        public static bool Spoof(IdentityBackupData data, Action<string> log)
        {
            data.SystemUuid ??= Capture();
            var backup = data.SystemUuid;

            string machine = IdentityWin32.NewLowerGuid();
            string hw = IdentityWin32.NewBracedGuid();
            string sqm = IdentityWin32.NewBracedGuid();
            string hwPath = IdentityWin32.HwProfileKeyPath()!;

            if (!IdentityWin32.WriteString("HKLM", MachineGuidPath, "MachineGuid", machine, log)
                || !IdentityWin32.WriteString("HKLM", hwPath, "HwProfileGuid", hw, log)
                || !IdentityWin32.WriteString("HKLM", SqmPath, "MachineId", sqm, log))
            {
                RollbackRegistry(backup);
                log("System UUID registry write failed — rolled back.");
                return false;
            }

            log($"MachineGuid → {machine}");
            log($"HwProfileGuid → {hw}");
            log($"SQM MachineId → {sqm}");

            bool amideOk = false;
            if (!IsRestorableUuid(backup.SmbiosUuid))
            {
                log("Skipping firmware UUID — original SMBIOS UUID unavailable/unrestorable.");
            }
            else if (FirmwareSkipReason(backup.BiosVendor) is string reason)
            {
                log($"Skipping firmware UUID — {reason}");
            }
            else
            {
                log("Applying firmware SMBIOS UUID via AMIDEWIN (/SU Auto)…");
                try
                {
                    string dir = EnsureTools(log);
                    var (exit, stdout, stderr) = IdentityWin32.Run(
                        Path.Combine(dir, "AMIDEWINx64.EXE"), "/SU Auto", dir);
                    if (exit == 0)
                    {
                        amideOk = true;
                        RestartWmi();
                        log($"AMIDEWIN OK → {ReadSmbios().Uuid}");
                    }
                    else
                    {
                        log($"AMIDEWIN failed: {Condense(stdout, stderr)}");
                        log("Firmware lock, non-AMI BIOS, or Core Isolation can block the AMI driver — registry layer still applies.");
                    }
                }
                catch (Exception ex)
                {
                    log($"Could not prepare AMIDEWIN tools: {ex.Message}");
                    App.Logger.WriteException(LogIdent, ex);
                }
            }

            backup.Active = new SystemUuidActive
            {
                MachineGuid = machine,
                HwProfileGuid = hw,
                SqmMachineId = sqm,
                AmideOk = amideOk
            };
            return true;
        }

        public static bool Revert(IdentityBackupData data, Action<string> log)
        {
            if (data.SystemUuid is null)
            {
                log("System UUID backup missing.");
                return false;
            }

            var backup = data.SystemUuid;
            bool appliedAmide = backup.Active?.AmideOk == true;
            bool errors = false;

            if (IsRestorableUuid(backup.SmbiosUuid) && (appliedAmide || data.Dirty))
            {
                string? skip = FirmwareSkipReason(backup.BiosVendor);
                if (!appliedAmide && skip is not null)
                {
                    log("Firmware was never modified — nothing to restore.");
                }
                else
                {
                    log($"Restoring SMBIOS UUID via AMIDEWIN → {backup.SmbiosUuid}");
                    try
                    {
                        string dir = EnsureTools(log);
                        string cleaned = backup.SmbiosUuid.Trim().Trim('{', '}').ToUpperInvariant();
                        var (exit, stdout, stderr) = IdentityWin32.Run(
                            Path.Combine(dir, "AMIDEWINx64.EXE"), $"/SU {cleaned}", dir);
                        if (exit == 0)
                        {
                            RestartWmi();
                            log("Firmware UUID restored.");
                        }
                        else
                        {
                            log($"Firmware restore: {Condense(stdout, stderr)}");
                            if (appliedAmide)
                                errors = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        log($"AMIDEWIN unavailable: {ex.Message}");
                        if (appliedAmide)
                            errors = true;
                    }
                }
            }

            string hwPath = IdentityWin32.HwProfileKeyPath()!;
            if (!string.IsNullOrEmpty(backup.MachineGuid))
                IdentityWin32.WriteString("HKLM", MachineGuidPath, "MachineGuid", backup.MachineGuid, log);
            if (!string.IsNullOrEmpty(backup.HwProfileGuid))
                IdentityWin32.WriteString("HKLM", hwPath, "HwProfileGuid", backup.HwProfileGuid, log);
            if (!string.IsNullOrEmpty(backup.SqmMachineId))
                IdentityWin32.WriteString("HKLM", SqmPath, "MachineId", backup.SqmMachineId, log);

            log($"MachineGuid → {backup.MachineGuid}");
            backup.Active = null;
            return !errors;
        }

        private static void RollbackRegistry(SystemUuidBackup backup)
        {
            string hwPath = IdentityWin32.HwProfileKeyPath()!;
            if (!string.IsNullOrEmpty(backup.MachineGuid))
                IdentityWin32.WriteString("HKLM", MachineGuidPath, "MachineGuid", backup.MachineGuid);
            if (!string.IsNullOrEmpty(backup.HwProfileGuid))
                IdentityWin32.WriteString("HKLM", hwPath, "HwProfileGuid", backup.HwProfileGuid);
            if (!string.IsNullOrEmpty(backup.SqmMachineId))
                IdentityWin32.WriteString("HKLM", SqmPath, "MachineId", backup.SqmMachineId);
        }

        private static bool IsRestorableUuid(string uuid)
        {
            uuid = uuid.Trim();
            return !string.IsNullOrEmpty(uuid)
                   && uuid != "(unavailable)"
                   && !uuid.Equals("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase)
                   && !uuid.StartsWith("00000000", StringComparison.OrdinalIgnoreCase)
                   && !uuid.StartsWith("FFFFFFFF", StringComparison.OrdinalIgnoreCase)
                   && uuid.Length >= 32;
        }

        private static string? FirmwareSkipReason(string vendor)
        {
            if (!Environment.Is64BitOperatingSystem)
                return "AMIDEWIN is x64-only";
            if (string.IsNullOrWhiteSpace(vendor))
                return null;
            if (IdentityWin32.IsAmiBios(vendor))
                return null;
            return $"BIOS vendor is {vendor} — AMIDEWIN only talks to AMI firmware";
        }

        private static string EnsureTools(Action<string> log)
        {
            string dir = Path.Combine(Paths.LocalAppData, App.ProjectName, "amidewin");
            Directory.CreateDirectory(dir);
            foreach (var tool in Tools)
            {
                string dest = Path.Combine(dir, tool.Name);
                if (ToolOk(dest, tool))
                    continue;
                try { File.Delete(dest); } catch { }
                log($"Downloading {tool.Name}…");
                Download(tool, dest);
            }
            return dir;
        }

        private static bool ToolOk(string path, ToolSpec tool)
        {
            if (!File.Exists(path))
                return false;
            var info = new FileInfo(path);
            if (info.Length < tool.MinBytes || info.Length > tool.MaxBytes)
                return false;
            byte[] bytes = File.ReadAllBytes(path);
            if (!IdentityWin32.LooksLikePe(bytes))
                return false;
            return IdentityWin32.Sha256Hex(bytes).Equals(tool.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        private static void Download(ToolSpec tool, string dest)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            byte[] bytes = client.GetByteArrayAsync(tool.Url).GetAwaiter().GetResult();
            if (bytes.Length < tool.MinBytes || bytes.Length > tool.MaxBytes)
                throw new InvalidOperationException($"Downloaded {tool.Name} size out of range ({bytes.Length}).");
            if (!IdentityWin32.LooksLikePe(bytes))
                throw new InvalidOperationException($"Downloaded {tool.Name} is not a PE image.");
            string hex = IdentityWin32.Sha256Hex(bytes);
            if (!hex.Equals(tool.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Downloaded {tool.Name} SHA-256 mismatch.");
            File.WriteAllBytes(dest, bytes);
        }

        private static void RestartWmi()
        {
            IdentityWin32.Run("net", "stop winmgmt /y");
            Thread.Sleep(2000);
            IdentityWin32.Run("net", "start winmgmt");
            Thread.Sleep(1000);
        }

        private static string Condense(string stdout, string stderr)
        {
            foreach (string chunk in new[] { stderr, stdout })
            {
                foreach (string line in chunk.Split('\n'))
                {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t) || t.All(c => "+-=|*_ ".Contains(c)))
                        continue;
                    if (t.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("fail", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("denied", StringComparison.OrdinalIgnoreCase))
                        return t.Length > 200 ? t[..200] + "…" : t;
                }
            }
            return (stderr + " " + stdout).Trim();
        }

        public readonly record struct SmbiosInfo(string Uuid, string Serial, string BiosSerial, string BoardSerial, string BiosVendor);

        public static SmbiosInfo ReadSmbios()
        {
            string uuid = QueryExpand("Win32_ComputerSystemProduct", "UUID") ?? "(unavailable)";
            string serial = QueryExpand("Win32_ComputerSystemProduct", "IdentifyingNumber") ?? "";
            string biosVendor = QueryExpand("Win32_BIOS", "Manufacturer") ?? "";
            string biosSerial = QueryExpand("Win32_BIOS", "SerialNumber") ?? "";
            string boardSerial = QueryExpand("Win32_BaseBoard", "SerialNumber") ?? "";
            return new SmbiosInfo(uuid, serial, biosSerial, boardSerial, biosVendor);
        }

        private static string? QueryExpand(string cls, string prop)
        {
            var (exit, stdout, _) = IdentityWin32.PowerShell(
                $"(Get-CimInstance {cls} | Select-Object -ExpandProperty {prop} -ErrorAction SilentlyContinue)");
            if (exit != 0)
                return null;
            string value = stdout.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
