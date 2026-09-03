using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    public static class SystemRegSpoofer
    {
        private enum SpoofKind
        {
            ProductId, InstallDate, DigitalProductId, HardwareId, HardwareIds,
            SusClientId, BuildGuid, Owner
        }

        private readonly record struct Target(string Root, string Path, string Name, SpoofKind Kind);

        private static readonly Target[] Targets =
        [
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId", SpoofKind.ProductId),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate", SpoofKind.InstallDate),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DigitalProductId", SpoofKind.DigitalProductId),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DigitalProductId4", SpoofKind.DigitalProductId),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "BuildGUID", SpoofKind.BuildGuid),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOwner", SpoofKind.Owner),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOrganization", SpoofKind.Owner),
            new("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", "SusClientId", SpoofKind.SusClientId),
            new("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", "SusClientIdValidation", SpoofKind.DigitalProductId),
            new("HKLM", @"SYSTEM\CurrentControlSet\Control\SystemInformation", "ComputerHardwareId", SpoofKind.HardwareId),
            new("HKLM", @"SYSTEM\CurrentControlSet\Control\SystemInformation", "ComputerHardwareIds", SpoofKind.HardwareIds),
            new("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform", "BackupProductKeyDefault", SpoofKind.ProductId),
        ];

        private static readonly string[] ControlTrees =
        [
            @"HKCU\System\GameConfigStore",
            @"HKCU\System\CurrentControlSet\Control"
        ];

        public static SystemRegBackup Capture()
        {
            return new SystemRegBackup
            {
                Entries = Targets.Select(t => IdentityWin32.Capture(t.Root, t.Path, t.Name)).ToList(),
                Trees = CaptureTrees()
            };
        }

        public static bool Spoof(IdentityBackupData data, Action<string> log)
        {
            data.SystemReg ??= Capture();
            var backup = data.SystemReg;
            if (backup.Trees.Count == 0)
                backup.Trees = CaptureTrees();

            var active = new List<ActiveReg>();
            var written = new List<SystemRegEntry>();

            foreach (var t in Targets)
            {
                var original = backup.Entries.FirstOrDefault(e =>
                    e.Root.Equals(t.Root, StringComparison.OrdinalIgnoreCase)
                    && e.Path.Equals(t.Path, StringComparison.OrdinalIgnoreCase)
                    && e.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))
                    ?? new SystemRegEntry { Root = t.Root, Path = t.Path, Name = t.Name, Missing = true };

                var generated = Generate(t.Kind, original);
                if (generated.Missing)
                    continue;

                bool shouldWrite = !original.Missing
                    || t.Kind is SpoofKind.SusClientId or SpoofKind.HardwareId or SpoofKind.ProductId or SpoofKind.BuildGuid;
                if (!shouldWrite)
                    continue;

                if (!IdentityWin32.WriteEntry(generated, log))
                {
                    foreach (var w in written.AsEnumerable().Reverse())
                    {
                        var orig = backup.Entries.FirstOrDefault(e =>
                            e.Root.Equals(w.Root, StringComparison.OrdinalIgnoreCase)
                            && e.Path.Equals(w.Path, StringComparison.OrdinalIgnoreCase)
                            && e.Name.Equals(w.Name, StringComparison.OrdinalIgnoreCase));
                        if (orig is not null)
                            IdentityWin32.WriteEntry(orig, log);
                    }
                    log($"SystemReg identity write failed on {t.Name}.");
                    return false;
                }
                written.Add(generated);
                log($"{t.Name,-28} → {Display(generated)}");
                active.Add(new ActiveReg
                {
                    Path = $"{t.Root}\\{t.Path}",
                    Name = t.Name,
                    Display = Display(generated)
                });
            }

            log("Deep registry clean (GameConfigStore / Control)…");
            foreach (var tree in backup.Trees)
            {
                bool deleted = RegDeleteTreeBestEffort(tree.KeyPath, log);
                log(deleted
                    ? $"Deleted {tree.KeyPath}"
                    : $"Partially cleaned {tree.KeyPath}");
                active.Add(new ActiveReg
                {
                    Path = tree.KeyPath,
                    Name = "(tree deleted)",
                    Display = deleted ? "removed" : "partially removed"
                });
            }

            backup.Active = active;
            return true;
        }

        public static bool Revert(IdentityBackupData data, Action<string> log)
        {
            if (data.SystemReg is null)
            {
                log("SystemReg backup missing.");
                return false;
            }

            bool ok = true;
            foreach (var entry in data.SystemReg.Entries)
            {
                if (!IdentityWin32.WriteEntry(entry, log))
                {
                    log($"Restore {entry.Name} failed.");
                    ok = false;
                    continue;
                }
                log($"{entry.Name,-28} → {Display(entry)}");
            }

            foreach (var tree in data.SystemReg.Trees)
            {
                if (string.IsNullOrEmpty(tree.RegBlob))
                    continue;
                if (!ValidateRegBlobScope(tree.RegBlob, tree.KeyPath, log))
                {
                    ok = false;
                    continue;
                }
                if (!RegImportBlob(tree.RegBlob, tree.KeyPath, log))
                    ok = false;
                else
                    log($"Restored tree {tree.KeyPath}");
            }

            data.SystemReg.Active = null;
            return ok;
        }

        private static List<TreeExport> CaptureTrees()
        {
            var trees = new List<TreeExport>();
            foreach (string key in ControlTrees)
                trees.Add(new TreeExport { KeyPath = key, RegBlob = RegExport(key) });
            return trees;
        }

        private static SystemRegEntry Generate(SpoofKind kind, SystemRegEntry original)
        {
            var entry = new SystemRegEntry
            {
                Root = original.Root,
                Path = original.Path,
                Name = original.Name,
                Kind = original.Kind,
                Missing = original.Missing
            };
            if (original.Missing && kind is not SpoofKind.SusClientId and not SpoofKind.HardwareId and not SpoofKind.ProductId and not SpoofKind.BuildGuid)
                return entry;

            switch (kind)
            {
                case SpoofKind.ProductId:
                    entry.Missing = false;
                    entry.Kind = original.Kind is nameof(Microsoft.Win32.RegistryValueKind.ExpandString)
                        ? original.Kind : nameof(Microsoft.Win32.RegistryValueKind.String);
                    entry.StringValue = IdentityWin32.ProductId();
                    break;
                case SpoofKind.InstallDate:
                    entry.Missing = false;
                    uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    uint value = now - (IdentityWin32.RandomUInt() % (86400 * 730));
                    if (original.Kind == nameof(Microsoft.Win32.RegistryValueKind.QWord))
                    {
                        entry.Kind = original.Kind;
                        entry.QwordValue = value;
                    }
                    else if (original.Kind == nameof(Microsoft.Win32.RegistryValueKind.String))
                    {
                        entry.Kind = original.Kind;
                        entry.StringValue = value.ToString();
                    }
                    else
                    {
                        entry.Kind = nameof(Microsoft.Win32.RegistryValueKind.DWord);
                        entry.DwordValue = value;
                    }
                    break;
                case SpoofKind.DigitalProductId:
                    if (original.Missing || original.BinaryValue is not { Length: > 0 })
                    {
                        entry.Missing = true;
                        break;
                    }
                    entry.Missing = false;
                    var bin = (byte[])original.BinaryValue.Clone();
                    for (int i = 8; i < bin.Length; i++)
                        bin[i] = (byte)IdentityWin32.RandomUInt();
                    entry.BinaryValue = bin;
                    break;
                case SpoofKind.HardwareId:
                    entry.Missing = false;
                    entry.Kind = nameof(Microsoft.Win32.RegistryValueKind.String);
                    entry.StringValue = IdentityWin32.NewBracedGuid();
                    break;
                case SpoofKind.HardwareIds:
                    entry.Missing = false;
                    entry.Kind = nameof(Microsoft.Win32.RegistryValueKind.MultiString);
                    int n = 3 + (int)(IdentityWin32.RandomUInt() % 3);
                    entry.MultiStringValue = Enumerable.Range(0, n).Select(_ => IdentityWin32.NewBracedGuid()).ToArray();
                    break;
                case SpoofKind.SusClientId:
                case SpoofKind.BuildGuid:
                    entry.Missing = false;
                    entry.Kind = nameof(Microsoft.Win32.RegistryValueKind.String);
                    entry.StringValue = IdentityWin32.NewLowerGuid();
                    break;
                case SpoofKind.Owner:
                    entry.Missing = false;
                    entry.Kind = nameof(Microsoft.Win32.RegistryValueKind.String);
                    entry.StringValue = "User" + IdentityWin32.RandomAlnum(4);
                    break;
            }
            return entry;
        }

        private static string Display(SystemRegEntry e)
        {
            if (e.Missing) return "(missing)";
            if (e.StringValue is not null) return e.StringValue;
            if (e.DwordValue is not null) return e.DwordValue.Value.ToString();
            if (e.QwordValue is not null) return e.QwordValue.Value.ToString();
            if (e.BinaryValue is not null) return $"(binary {e.BinaryValue.Length} bytes)";
            if (e.MultiStringValue is not null) return string.Join("; ", e.MultiStringValue);
            return "";
        }

        private static string? RegExport(string keyPath)
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"eclipse-export-{Guid.NewGuid():N}.reg");
            try
            {
                var (exit, stdout, stderr) = IdentityWin32.Run("reg", $"export \"{keyPath}\" \"{tmp}\" /y");
                if (exit != 0)
                {
                    if (IsMissing(exit, stdout, stderr))
                        return null;
                    return null;
                }
                if (!File.Exists(tmp))
                    return null;
                byte[] raw = File.ReadAllBytes(tmp);
                return DecodeRegFile(raw);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static bool RegImportBlob(string content, string expectedKey, Action<string> log)
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"eclipse-import-{Guid.NewGuid():N}.reg");
            try
            {
                File.WriteAllBytes(tmp, EncodeRegFile(content));
                var (exit, stdout, stderr) = IdentityWin32.Run("reg", $"import \"{tmp}\"");
                if (exit != 0)
                {
                    log($"reg import of {expectedKey} failed: {stderr}".Trim());
                    return false;
                }
                return true;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static bool RegDeleteTreeBestEffort(string keyPath, Action<string> log)
        {
            var (exit, stdout, stderr) = IdentityWin32.Run("reg", $"delete \"{keyPath}\" /f");
            if (exit == 0 || IsMissing(exit, stdout, stderr))
                return true;

            // Partial: try children.
            string[] parts = keyPath.Split('\\', 2);
            if (parts.Length != 2)
                return false;
            foreach (string child in IdentityWin32.ListSubkeys(parts[0], parts[1]))
            {
                var childPath = $"{keyPath}\\{child}";
                var r = IdentityWin32.Run("reg", $"delete \"{childPath}\" /f");
                if (r.Exit != 0 && !IsMissing(r.Exit, r.StdOut, r.StdErr))
                    log($"Kept {childPath} (access denied)");
            }
            return false;
        }

        private static bool IsMissing(int code, string stdout, string stderr)
        {
            if (code != 1)
                return false;
            string blob = (stdout + "\n" + stderr).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(blob))
                return true;
            return blob.Contains("unable to find") || blob.Contains("cannot find") || blob.Contains("not found");
        }

        public static bool ValidateRegBlobScope(string blob, string expectedKey, Action<string>? log = null)
        {
            string expected = ExpandHive(expectedKey.Trim().TrimEnd('\\'));
            bool saw = false;
            foreach (string raw in blob.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith('[') || !line.EndsWith(']'))
                    continue;
                string inner = line[1..^1];
                string key = inner.StartsWith('-') ? inner[1..] : inner;
                if (string.IsNullOrEmpty(key))
                    continue;
                string keyShort = ExpandHive(key);
                if (!(keyShort.Equals(expected, StringComparison.OrdinalIgnoreCase)
                      || keyShort.StartsWith(expected + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    log?.Invoke($"Registry blob for {expectedKey} contains out-of-scope key [{key}]");
                    return false;
                }
                saw = true;
            }
            if (!saw)
            {
                log?.Invoke($"Registry blob for {expectedKey} contains no key sections.");
                return false;
            }
            return true;
        }

        private static string ExpandHive(string key)
        {
            string upper = key.ToUpperInvariant();
            return upper
                .Replace("HKEY_CURRENT_USER\\", "HKCU\\")
                .Replace("HKEY_LOCAL_MACHINE\\", "HKLM\\");
        }

        private static string DecodeRegFile(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).TrimStart('\uFEFF');
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).TrimStart('\uFEFF');
            return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }

        private static byte[] EncodeRegFile(string text)
        {
            string body = text.TrimStart('\uFEFF');
            var bom = new byte[] { 0xFF, 0xFE };
            byte[] payload = System.Text.Encoding.Unicode.GetBytes(body);
            var outBytes = new byte[2 + payload.Length];
            Buffer.BlockCopy(bom, 0, outBytes, 0, 2);
            Buffer.BlockCopy(payload, 0, outBytes, 2, payload.Length);
            return outBytes;
        }
    }
}
