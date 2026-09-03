using Microsoft.Win32;

namespace Froststrap.Utility.BanAsync
{
    public static class EdidSpoofer
    {
        private const string DisplayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

        public static EdidBackup Capture()
        {
            return new EdidBackup { Monitors = Discover() };
        }

        public static bool Spoof(IdentityBackupData data, Action<string> log)
        {
            data.Edid ??= Capture();
            var backup = data.Edid;
            if (backup.Monitors.Count == 0)
            {
                log("No EDID blobs found under DISPLAY.");
                backup.Active = null;
                return true;
            }

            var pending = new List<(EdidMonitorBackup Mon, byte[] Original, byte[] NewEdid, uint Serial, string SerialStr)>();
            foreach (var mon in backup.Monitors)
            {
                if (mon.Edid is not { Length: >= 128 })
                {
                    log($"Skip {mon.Model}\\{mon.Instance} — no binary EDID in backup.");
                    continue;
                }
                var (newEdid, serial, serialStr) = SpoofBlob(mon.Edid);
                pending.Add((mon, mon.Edid, newEdid, serial, serialStr));
            }

            var written = new List<(string Path, byte[] Original)>();
            foreach (var p in pending)
            {
                if (!IdentityWin32.WriteBinary("HKLM", p.Mon.Path, "EDID", p.NewEdid, log))
                {
                    foreach (var done in written.AsEnumerable().Reverse())
                        IdentityWin32.WriteBinary("HKLM", done.Path, "EDID", done.Original, log);
                    log("EDID write failed — rolled back already-written monitors.");
                    return false;
                }
                written.Add((p.Mon.Path, p.Original));
                log($"{p.Mon.Model}\\{p.Mon.Instance} serial {p.Mon.SerialDword:X8} → {p.Serial:X8} ({p.SerialStr})");
            }

            backup.Active = pending.Select(p => new EdidActive
            {
                Path = p.Mon.Path,
                SerialDword = p.Serial,
                SerialString = p.SerialStr
            }).ToList();
            log("Reconnect the display or reboot for all consumers to re-read EDID.");
            return true;
        }

        public static bool Revert(IdentityBackupData data, Action<string> log)
        {
            if (data.Edid is null)
            {
                log("Monitor EDID backup missing.");
                return false;
            }

            bool ok = true;
            foreach (var mon in data.Edid.Monitors)
            {
                if (mon.Edid is not { Length: > 0 })
                    continue;
                if (!IdentityWin32.WriteBinary("HKLM", mon.Path, "EDID", mon.Edid, log))
                {
                    log($"EDID restore failed for {mon.Model}\\{mon.Instance}");
                    ok = false;
                    continue;
                }
                log($"{mon.Model}\\{mon.Instance} serial {mon.SerialDword:X8} restored.");
            }
            data.Edid.Active = null;
            return ok;
        }

        private static List<EdidMonitorBackup> Discover()
        {
            var outList = new List<EdidMonitorBackup>();
            foreach (string model in IdentityWin32.ListSubkeys("HKLM", DisplayRoot))
            {
                string modelPath = $@"{DisplayRoot}\{model}";
                foreach (string instance in IdentityWin32.ListSubkeys("HKLM", modelPath))
                {
                    string dp = $@"{modelPath}\{instance}\Device Parameters";
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(dp, writable: false);
                        if (key?.GetValue("EDID") is not byte[] bytes || bytes.Length < 128)
                            continue;
                        uint serial = BitConverter.ToUInt32(bytes, 12);
                        outList.Add(new EdidMonitorBackup
                        {
                            Path = dp,
                            Model = model,
                            Instance = instance,
                            Edid = bytes,
                            SerialDword = serial,
                            SerialString = ExtractSerialString(bytes)
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException("EdidSpoofer::Discover", ex);
                    }
                }
            }
            return outList;
        }

        private static (byte[] Edid, uint Serial, string SerialStr) SpoofBlob(byte[] original)
        {
            var edid = (byte[])original.Clone();
            uint serial = IdentityWin32.RandomUInt();
            if (serial == 0) serial = 1;
            byte[] serialBytes = BitConverter.GetBytes(serial);
            edid[12] = serialBytes[0];
            edid[13] = serialBytes[1];
            edid[14] = serialBytes[2];
            edid[15] = serialBytes[3];
            string serialStr = IdentityWin32.RandomAlnum(10);
            SetSerialString(edid.AsSpan(0, 128), serialStr);
            FixChecksum(edid.AsSpan(0, 128));
            return (edid, serial, serialStr);
        }

        private static string? ExtractSerialString(byte[] edid)
        {
            if (edid.Length < 128)
                return null;
            foreach (int b in new[] { 54, 72, 90, 108 })
            {
                if (edid[b] == 0 && edid[b + 1] == 0 && edid[b + 2] == 0 && edid[b + 3] == 0xFF && edid[b + 4] == 0)
                {
                    var raw = edid.AsSpan(b + 5, 13);
                    var chars = new List<char>();
                    foreach (byte c in raw)
                    {
                        if (c is 0x0A or 0x00)
                            break;
                        chars.Add((char)c);
                    }
                    string s = new string(chars.ToArray()).Trim();
                    if (!string.IsNullOrEmpty(s))
                        return s;
                }
            }
            return null;
        }

        private static void SetSerialString(Span<byte> edid, string serial)
        {
            if (edid.Length < 128)
                return;
            foreach (int b in new[] { 54, 72, 90, 108 })
            {
                if (edid[b] == 0 && edid[b + 1] == 0 && edid[b + 2] == 0 && edid[b + 3] == 0xFF && edid[b + 4] == 0)
                {
                    for (int i = b + 5; i < b + 18; i++)
                        edid[i] = (byte)' ';
                    byte[] chars = System.Text.Encoding.ASCII.GetBytes(serial.Length > 13 ? serial[..13] : serial);
                    chars.CopyTo(edid[(b + 5)..]);
                    if (chars.Length < 13)
                        edid[b + 5 + chars.Length] = 0x0A;
                    return;
                }
            }
        }

        private static void FixChecksum(Span<byte> edid)
        {
            if (edid.Length < 128)
                return;
            int sum = 0;
            for (int i = 0; i < 127; i++)
                sum += edid[i];
            edid[127] = (byte)((256 - (sum % 256)) % 256);
        }
    }
}
