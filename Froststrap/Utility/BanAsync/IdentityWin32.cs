using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    internal static class IdentityWin32
    {
        public static ProcessStartInfo Hidden(string fileName, string arguments) => new()
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        public static (int Exit, string StdOut, string StdErr) Run(string fileName, string arguments, string? workingDir = null)
        {
            var psi = Hidden(fileName, arguments);
            if (!string.IsNullOrEmpty(workingDir))
                psi.WorkingDirectory = workingDir;
            using var proc = Process.Start(psi);
            if (proc is null)
                return (-1, "", "failed to start");
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, stdout, stderr);
        }

        public static (int Exit, string StdOut, string StdErr) PowerShell(string command)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return Run("powershell", $"-NoProfile -EncodedCommand {encoded}");
        }

        public static string Sha256Hex(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        public static string NewGuid() => Guid.NewGuid().ToString();
        public static string NewBracedGuid() => "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        public static string NewLowerGuid() => Guid.NewGuid().ToString();

        public static string RandomAlnum(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<byte> raw = stackalloc byte[length];
            RandomNumberGenerator.Fill(raw);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(chars[raw[i] % chars.Length]);
            return sb.ToString();
        }

        public static string ProductId()
        {
            // 5 groups of 5 alnum, matching Windows ProductId shape.
            return string.Join("-", Enumerable.Range(0, 5).Select(_ => RandomAlnum(5)));
        }

        public static uint RandomUInt()
        {
            Span<byte> b = stackalloc byte[4];
            RandomNumberGenerator.Fill(b);
            return BitConverter.ToUInt32(b);
        }

        public static RegistryKey? OpenRoot(string root, bool writable)
        {
            return root.ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => writable ? Registry.LocalMachine : Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => writable ? Registry.CurrentUser : Registry.CurrentUser,
                _ => null
            };
        }

        public static SystemRegEntry Capture(string root, string path, string name)
        {
            var entry = new SystemRegEntry { Root = root, Path = path, Name = name, Missing = true };
            try
            {
                using var hive = OpenRoot(root, false);
                using var key = hive?.OpenSubKey(path, writable: false);
                if (key is null)
                    return entry;
                object? value = key.GetValue(name);
                if (value is null)
                    return entry;
                entry.Missing = false;
                var kind = key.GetValueKind(name);
                entry.Kind = kind.ToString();
                switch (kind)
                {
                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString:
                        entry.StringValue = value as string;
                        break;
                    case RegistryValueKind.DWord:
                        entry.DwordValue = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                        break;
                    case RegistryValueKind.QWord:
                        entry.QwordValue = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                        break;
                    case RegistryValueKind.Binary:
                        entry.BinaryValue = value as byte[];
                        break;
                    case RegistryValueKind.MultiString:
                        entry.MultiStringValue = value as string[];
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("IdentityWin32::Capture", ex);
            }
            return entry;
        }

        public static bool WriteEntry(SystemRegEntry entry, Action<string>? log = null)
        {
            try
            {
                using var hive = OpenRoot(entry.Root, true);
                if (hive is null)
                    return false;
                using var key = hive.CreateSubKey(entry.Path, writable: true);
                if (key is null)
                    return false;
                if (entry.Missing)
                {
                    key.DeleteValue(entry.Name, throwOnMissingValue: false);
                    return true;
                }
                switch (entry.Kind)
                {
                    case nameof(RegistryValueKind.ExpandString):
                        key.SetValue(entry.Name, entry.StringValue ?? "", RegistryValueKind.ExpandString);
                        break;
                    case nameof(RegistryValueKind.DWord):
                        key.SetValue(entry.Name, (int)(entry.DwordValue ?? 0), RegistryValueKind.DWord);
                        break;
                    case nameof(RegistryValueKind.QWord):
                        key.SetValue(entry.Name, (long)(entry.QwordValue ?? 0), RegistryValueKind.QWord);
                        break;
                    case nameof(RegistryValueKind.Binary):
                        key.SetValue(entry.Name, entry.BinaryValue ?? [], RegistryValueKind.Binary);
                        break;
                    case nameof(RegistryValueKind.MultiString):
                        key.SetValue(entry.Name, entry.MultiStringValue ?? [], RegistryValueKind.MultiString);
                        break;
                    default:
                        key.SetValue(entry.Name, entry.StringValue ?? "", RegistryValueKind.String);
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Registry write {entry.Root}\\{entry.Path}\\{entry.Name}: {ex.Message}");
                App.Logger.WriteException("IdentityWin32::WriteEntry", ex);
                return false;
            }
        }

        public static bool WriteString(string root, string path, string name, string value, Action<string>? log = null)
        {
            return WriteEntry(new SystemRegEntry
            {
                Root = root,
                Path = path,
                Name = name,
                Kind = nameof(RegistryValueKind.String),
                StringValue = value
            }, log);
        }

        public static bool WriteBinary(string root, string path, string name, byte[] value, Action<string>? log = null)
        {
            return WriteEntry(new SystemRegEntry
            {
                Root = root,
                Path = path,
                Name = name,
                Kind = nameof(RegistryValueKind.Binary),
                BinaryValue = value
            }, log);
        }

        public static string? ReadString(string root, string path, string name)
        {
            try
            {
                using var hive = OpenRoot(root, false);
                using var key = hive?.OpenSubKey(path, writable: false);
                return key?.GetValue(name) as string;
            }
            catch
            {
                return null;
            }
        }

        public static IEnumerable<string> ListSubkeys(string root, string path)
        {
            using var hive = OpenRoot(root, false);
            using var key = hive?.OpenSubKey(path, writable: false);
            if (key is null)
                yield break;
            foreach (string name in key.GetSubKeyNames())
                yield return name;
        }

        public static string? CsvQuery(string className, string properties)
        {
            var (exit, stdout, _) = PowerShell(
                $"Get-CimInstance {className} | Select-Object {properties} | ConvertTo-Csv -NoTypeInformation");
            return exit == 0 ? stdout : null;
        }

        public static bool LooksLikePe(byte[] bytes) =>
            bytes.Length >= 0x40 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';

        public static string? HwProfileKeyPath()
        {
            const string basePath = @"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(basePath, writable: false);
                if (key is null)
                    return basePath + @"\0001";
                string[] names = key.GetSubKeyNames();
                if (names.Contains("0001"))
                    return basePath + @"\0001";
                return names.Length > 0 ? basePath + @"\" + names[0] : basePath + @"\0001";
            }
            catch
            {
                return basePath + @"\0001";
            }
        }

        public static bool IsAmiBios(string vendor)
        {
            string v = vendor.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(v))
                return false;
            return v.Contains("american megatrends")
                   || v.Contains("amibios")
                   || v == "ami"
                   || v.StartsWith("ami ")
                   || v.StartsWith("ami,")
                   || v.Contains("ami corporation");
        }
    }
}
