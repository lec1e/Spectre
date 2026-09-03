using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Froststrap.Utility.BanAsync
{
    public sealed class IdentityBackupData
    {
        public int Version { get; set; } = IdentityBackupStore.SchemaVersion;
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? ContentSha256 { get; set; }
        public bool Dirty { get; set; }
        public string? DirtyReason { get; set; }
        public SystemUuidBackup? SystemUuid { get; set; }
        public MemoryBackup? Memory { get; set; }
        public EdidBackup? Edid { get; set; }
        public SystemRegBackup? SystemReg { get; set; }

        [JsonIgnore]
        public bool AnyActive =>
            SystemUuid?.Active is not null
            || (Memory?.Active is { Count: > 0 })
            || (Edid?.Active is { Count: > 0 })
            || (SystemReg?.Active is { Count: > 0 });
    }

    public sealed class SystemUuidBackup
    {
        public string? MachineGuid { get; set; }
        public string? HwProfileGuid { get; set; }
        public string? SqmMachineId { get; set; }
        public string SmbiosUuid { get; set; } = "";
        public string SmbiosSerial { get; set; } = "";
        public string BiosSerial { get; set; } = "";
        public string BoardSerial { get; set; } = "";
        public string BiosVendor { get; set; } = "";
        public SystemUuidActive? Active { get; set; }
    }

    public sealed class SystemUuidActive
    {
        public string MachineGuid { get; set; } = "";
        public string HwProfileGuid { get; set; } = "";
        public string SqmMachineId { get; set; } = "";
        public bool AmideOk { get; set; }
    }

    public sealed class MemoryModuleSnapshot
    {
        public uint Index { get; set; }
        public uint SizeMb { get; set; }
        public string Locator { get; set; } = "";
        public string Bank { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string PartNumber { get; set; } = "";
    }

    public sealed class MemoryModuleSpoof
    {
        public uint Index { get; set; }
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string PartNumber { get; set; } = "";
    }

    public sealed class MemoryBackup
    {
        public List<MemoryModuleSnapshot> Devices { get; set; } = [];
        public List<MemoryModuleSpoof>? Active { get; set; }
    }

    public sealed class EdidMonitorBackup
    {
        public string Path { get; set; } = "";
        public string Model { get; set; } = "";
        public string Instance { get; set; } = "";
        public byte[] Edid { get; set; } = [];
        public uint SerialDword { get; set; }
        public string? SerialString { get; set; }
    }

    public sealed class EdidActive
    {
        public string Path { get; set; } = "";
        public uint SerialDword { get; set; }
        public string SerialString { get; set; } = "";
    }

    public sealed class EdidBackup
    {
        public List<EdidMonitorBackup> Monitors { get; set; } = [];
        public List<EdidActive>? Active { get; set; }
    }

    public sealed class SystemRegEntry
    {
        public string Root { get; set; } = "HKLM";
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "String";
        public string? StringValue { get; set; }
        public uint? DwordValue { get; set; }
        public ulong? QwordValue { get; set; }
        public byte[]? BinaryValue { get; set; }
        public string[]? MultiStringValue { get; set; }
        public bool Missing { get; set; }
    }

    public sealed class TreeExport
    {
        public string KeyPath { get; set; } = "";
        public string? RegBlob { get; set; }
    }

    public sealed class ActiveReg
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Display { get; set; } = "";
    }

    public sealed class SystemRegBackup
    {
        public List<SystemRegEntry> Entries { get; set; } = [];
        public List<TreeExport> Trees { get; set; } = [];
        public List<ActiveReg>? Active { get; set; }
    }

    public sealed class IdentityBackupStore
    {
        public const int SchemaVersion = 1;
        private const int LockWaitMs = 15_000;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public string Path { get; }

        public IdentityBackupStore(string? customPath = null)
        {
            Path = customPath ?? System.IO.Path.Combine(Paths.LocalAppData, App.ProjectName, "identity-backup.json");
            string? parent = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
        }

        public static IdentityBackupStore Default { get; } = new();

        public IdentityBackupData LoadOrDefault()
        {
            using var _ = AcquireLock();
            if (File.Exists(Path))
                return LoadUnlocked();
            string bak = BakPath();
            if (File.Exists(bak))
            {
                File.Copy(bak, Path, overwrite: true);
                return LoadUnlocked();
            }
            return new IdentityBackupData { Version = SchemaVersion };
        }

        public void Save(IdentityBackupData data)
        {
            using var _ = AcquireLock();
            SaveUnlocked(data);
        }

        public void MigrateFromSettings()
        {
            var data = LoadOrDefault();
            var settings = App.Settings.Prop;
            bool mutated = false;

            if (!data.AnyActive)
            {
                string machine = FirstNonEmpty(settings.HwidOriginalMachineGuid, settings.BanAsyncOriginalMachineGuid);
                string hw = settings.HwidOriginalHwProfileGuid;
                string sqm = settings.HwidOriginalMachineId;
                if (!string.IsNullOrEmpty(machine) || !string.IsNullOrEmpty(hw) || !string.IsNullOrEmpty(sqm))
                {
                    data.SystemUuid ??= new SystemUuidBackup();
                    if (string.IsNullOrEmpty(data.SystemUuid.MachineGuid) && !string.IsNullOrEmpty(machine))
                    {
                        data.SystemUuid.MachineGuid = machine;
                        mutated = true;
                    }
                    if (string.IsNullOrEmpty(data.SystemUuid.HwProfileGuid) && !string.IsNullOrEmpty(hw))
                    {
                        data.SystemUuid.HwProfileGuid = hw;
                        mutated = true;
                    }
                    if (string.IsNullOrEmpty(data.SystemUuid.SqmMachineId) && !string.IsNullOrEmpty(sqm))
                    {
                        data.SystemUuid.SqmMachineId = sqm;
                        mutated = true;
                    }
                }
            }

            void AddReg(string path, string name, string? value)
            {
                if (string.IsNullOrEmpty(value))
                    return;
                data.SystemReg ??= new SystemRegBackup();
                if (data.SystemReg.Entries.Any(e =>
                    e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return;
                data.SystemReg.Entries.Add(new SystemRegEntry
                {
                    Root = "HKLM",
                    Path = path,
                    Name = name,
                    Kind = "String",
                    StringValue = value
                });
                mutated = true;
            }

            AddReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId", settings.HwidOriginalProductId);
            AddReg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", "SusClientId", settings.HwidOriginalSusClientId);

            if (mutated)
                Save(data);
        }

        public void ExportTo(string destPath)
        {
            var data = LoadOrDefault();
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(destPath, json);
        }

        private IdentityBackupData LoadUnlocked()
        {
            string text = File.ReadAllText(Path);
            var data = JsonSerializer.Deserialize<IdentityBackupData>(text, JsonOptions)
                       ?? new IdentityBackupData { Version = SchemaVersion };
            if (data.Version > SchemaVersion)
                throw new InvalidOperationException($"Backup schema {data.Version} is newer than this Spectre build ({SchemaVersion}).");
            data.Version = SchemaVersion;
            if (!string.IsNullOrEmpty(data.ContentSha256))
            {
                string actual = ContentDigest(data);
                if (!actual.Equals(data.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Identity backup integrity check failed — file was modified outside Spectre.");
            }
            return data;
        }

        private void SaveUnlocked(IdentityBackupData data)
        {
            var now = DateTimeOffset.UtcNow;
            data.CreatedAt ??= now;
            data.UpdatedAt = now;
            data.Version = SchemaVersion;
            data.ContentSha256 = ContentDigest(data);

            string text = JsonSerializer.Serialize(data, JsonOptions);
            string tmp = Path + ".tmp";
            string bak = BakPath();
            File.WriteAllText(tmp, text);

            if (File.Exists(Path))
            {
                try { File.Delete(bak); } catch { }
                File.Copy(Path, bak, overwrite: true);
            }

            try
            {
                File.Move(tmp, Path, overwrite: true);
            }
            catch
            {
                string swap = Path + ".swap";
                try { File.Delete(swap); } catch { }
                if (File.Exists(Path))
                    File.Move(Path, swap, overwrite: true);
                File.Move(tmp, Path, overwrite: true);
                try { File.Delete(swap); } catch { }
            }
        }

        private string BakPath() => Path + ".bak";
        private string LockPath() => Path + ".lock";

        private IDisposable AcquireLock()
        {
            string lockPath = LockPath();
            var deadline = DateTime.UtcNow.AddMilliseconds(LockWaitMs);
            while (true)
            {
                try
                {
                    var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var writer = new StreamWriter(stream);
                    writer.WriteLine($"pid={Environment.ProcessId} ts={DateTimeOffset.UtcNow:O}");
                    writer.Flush();
                    return new LockHandle(lockPath, stream, writer);
                }
                catch (IOException)
                {
                    try
                    {
                        var info = new FileInfo(lockPath);
                        if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromSeconds(30))
                            File.Delete(lockPath);
                    }
                    catch { }

                    if (DateTime.UtcNow >= deadline)
                        throw new IOException($"Identity backup is locked ({lockPath}).");
                    Thread.Sleep(50);
                }
            }
        }

        private static string ContentDigest(IdentityBackupData data)
        {
            var payload = new
            {
                version = SchemaVersion,
                dirty = data.Dirty,
                dirtyReason = data.DirtyReason,
                systemUuid = data.SystemUuid,
                memory = data.Memory,
                edid = data.Edid,
                systemReg = data.SystemReg
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        private sealed class LockHandle : IDisposable
        {
            private readonly string _path;
            private Stream? _stream;
            private TextWriter? _writer;

            public LockHandle(string path, Stream stream, TextWriter writer)
            {
                _path = path;
                _stream = stream;
                _writer = writer;
            }

            public void Dispose()
            {
                try { _writer?.Dispose(); } catch { }
                try { _stream?.Dispose(); } catch { }
                _writer = null;
                _stream = null;
                try { File.Delete(_path); } catch { }
            }
        }
    }
}
