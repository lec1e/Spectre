using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    public static class MemoryProfileSpoofer
    {
        private const string MemKey = @"SOFTWARE\Eclipse\MemoryDevices";

        public static MemoryBackup Capture()
        {
            return new MemoryBackup { Devices = SnapshotLive() };
        }

        public static bool Spoof(IdentityBackupData data, Action<string> log)
        {
            data.Memory ??= Capture();
            var backup = data.Memory;
            if (backup.Devices.Count == 0)
            {
                log("No populated memory devices reported by SMBIOS.");
                backup.Active = null;
                ClearProfile();
                return true;
            }

            var spoofed = new List<MemoryModuleSpoof>();
            foreach (var d in backup.Devices)
            {
                string serial = IdentityWin32.RandomAlnum(12);
                string asset = IdentityWin32.RandomAlnum(8);
                int partLen = Math.Clamp(string.IsNullOrEmpty(d.PartNumber) ? 12 : d.PartNumber.Length, 8, 18);
                string part = IdentityWin32.RandomAlnum(partLen);
                log($"[{d.Index}] {d.Locator} {d.SizeMb} MiB serial {d.SerialNumber} → {serial}");
                spoofed.Add(new MemoryModuleSpoof
                {
                    Index = d.Index,
                    SerialNumber = serial,
                    AssetTag = asset,
                    PartNumber = part
                });
            }

            WriteProfile(spoofed);
            backup.Active = spoofed;
            log($"Profile written to HKLM\\{MemKey}");
            log("Live SMBIOS table is firmware-backed; this profile is local-only.");
            return true;
        }

        public static bool Revert(IdentityBackupData data, Action<string> log)
        {
            ClearProfile();
            log($"Cleared HKLM\\{MemKey}");
            if (data.Memory is not null)
            {
                foreach (var d in data.Memory.Devices)
                    log($"[{d.Index}] original serial {d.SerialNumber}");
                data.Memory.Active = null;
            }
            return true;
        }

        private static List<MemoryModuleSnapshot> SnapshotLive()
        {
            var list = new List<MemoryModuleSnapshot>();
            string? csv = IdentityWin32.CsvQuery("Win32_PhysicalMemory",
                "DeviceLocator,Capacity,Manufacturer,SerialNumber,PartNumber,BankLabel,Tag");
            if (string.IsNullOrWhiteSpace(csv))
                return list;

            using var reader = new StringReader(csv);
            string? header = reader.ReadLine();
            if (header is null)
                return list;
            uint index = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] cols = SplitCsv(line);
                if (cols.Length < 6)
                    continue;
                ulong cap = 0;
                ulong.TryParse(cols[1].Trim('"'), out cap);
                list.Add(new MemoryModuleSnapshot
                {
                    Index = index++,
                    SizeMb = (uint)(cap / (1024 * 1024)),
                    Locator = cols[0].Trim('"'),
                    Manufacturer = cols[2].Trim('"'),
                    SerialNumber = cols[3].Trim('"'),
                    PartNumber = cols[4].Trim('"'),
                    Bank = cols[5].Trim('"'),
                    AssetTag = cols.Length > 6 ? cols[6].Trim('"') : ""
                });
            }
            return list;
        }

        private static string[] SplitCsv(string line)
        {
            var parts = new List<string>();
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (c == ',' && !inQuotes)
                {
                    parts.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }
                cur.Append(c);
            }
            parts.Add(cur.ToString());
            return parts.ToArray();
        }

        private static void WriteProfile(IReadOnlyList<MemoryModuleSpoof> spoofed)
        {
            using var hklm = Registry.LocalMachine;
            try { hklm.DeleteSubKeyTree(MemKey, throwOnMissingSubKey: false); } catch { }
            using var root = hklm.CreateSubKey(MemKey, true);
            root.SetValue("Count", spoofed.Count, RegistryValueKind.DWord);
            foreach (var s in spoofed)
            {
                using var dk = root.CreateSubKey($"Device{s.Index}", true);
                dk.SetValue("SerialNumber", s.SerialNumber);
                dk.SetValue("AssetTag", s.AssetTag);
                dk.SetValue("PartNumber", s.PartNumber);
                dk.SetValue("Index", (int)s.Index, RegistryValueKind.DWord);
            }
        }

        private static void ClearProfile()
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(MemKey, throwOnMissingSubKey: false); }
            catch { }
        }
    }
}
