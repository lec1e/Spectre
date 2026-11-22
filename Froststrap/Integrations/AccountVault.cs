using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Froststrap.Integrations;

/// <summary>
/// Saves Roblox logins on this PC. The file is sealed with Windows DPAPI for the current user.
/// </summary>
public static class AccountVault
{
    private static readonly object Gate = new();
    private static VaultFile _file = new();
    private static bool _loaded;
    private static FileSystemWatcher? _watcher;
    private static CancellationTokenSource? _watchDelay;
    private static int _pollStarted;

    public static event Action? Changed;

    public static long ActiveUserId
    {
        get
        {
            EnsureLoaded();
            return _file.ActiveUserId;
        }
    }

    public static IReadOnlyList<AccountRecord> Accounts
    {
        get
        {
            EnsureLoaded();
            return _file.Accounts.Select(account => account.Copy()).ToList();
        }
    }

    public static AccountRecord? Active()
    {
        EnsureLoaded();
        return _file.Accounts.FirstOrDefault(account => account.UserId == _file.ActiveUserId)?.Copy();
    }

    public static long CurrentUserId
    {
        get
        {
            var saved = Active()?.UserId ?? 0;
            return saved > 0 ? saved : App.Cookies.User?.Id ?? 0;
        }
    }

    public static string CurrentCookie
    {
        get
        {
            var saved = Active()?.Cookie;
            return string.IsNullOrWhiteSpace(saved) ? App.Cookies.GetAuthCookie() : saved;
        }
    }

    public static string CurrentDisplayName
    {
        get
        {
            var saved = Active()?.Label;
            return string.IsNullOrWhiteSpace(saved) ? App.Cookies.User?.DisplayName ?? "" : saved;
        }
    }

    public static string CurrentUsername
    {
        get
        {
            var saved = Active()?.Username;
            return string.IsNullOrWhiteSpace(saved) ? App.Cookies.User?.Username ?? "" : saved;
        }
    }

    public static void Remember(long userId, string username, string displayName, string cookie)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(cookie))
            return;

        bool changed;
        lock (Gate)
        {
            EnsureLoaded();
            var existing = _file.Accounts.FirstOrDefault(account => account.UserId == userId);
            changed = existing is null || existing.Cookie != cookie || existing.Username != username || existing.DisplayName != displayName;
            if (existing is null)
            {
                _file.Accounts.Add(new AccountRecord
                {
                    UserId = userId,
                    Username = username,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                    Cookie = cookie,
                    SavedAt = DateTime.UtcNow
                });
            }
            else if (changed)
            {
                existing.Username = username;
                existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName;
                existing.Cookie = cookie;
                existing.SavedAt = DateTime.UtcNow;
            }

            if (_file.ActiveUserId == 0)
            {
                _file.ActiveUserId = userId;
                changed = true;
            }

            if (changed)
                Save();
        }

        if (changed)
            Changed?.Invoke();
    }

    public static bool Forget(long userId)
    {
        if (userId <= 0)
            return false;

        lock (Gate)
        {
            EnsureLoaded();
            var removed = _file.Accounts.RemoveAll(account => account.UserId == userId);
            if (removed == 0)
                return false;

            if (_file.ActiveUserId == userId)
                _file.ActiveUserId = _file.Accounts.FirstOrDefault()?.UserId ?? 0;
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    public static void Select(long userId)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_file.Accounts.All(account => account.UserId != userId) || _file.ActiveUserId == userId)
                return;

            _file.ActiveUserId = userId;
            Save();
        }

        Changed?.Invoke();
    }

    public static void WatchRobloxLogins()
    {
        if (Interlocked.Exchange(ref _pollStarted, 1) == 0)
            _ = PollLoginsAsync();

        if (_watcher is not null || !OperatingSystem.IsWindows())
            return;

        var folder = Path.Combine(Paths.Roblox, "LocalStorage");
        if (!Directory.Exists(folder))
            return;

        _watcher = new FileSystemWatcher(folder, "*RobloxCookies.dat")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => ScheduleCapture();
        _watcher.Created += (_, _) => ScheduleCapture();
    }

    private static async Task PollLoginsAsync()
    {
        await Task.Delay(2500);
        while (true)
        {
            try
            {
                if (OperatingSystem.IsWindows() && Process.GetProcessesByName("RobloxPlayerBeta").Length > 0)
                    await App.Cookies.CaptureCurrentSessionAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountVault", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(20));
        }
    }

    private static void ScheduleCapture()
    {
        _watchDelay?.Cancel();
        var delay = new CancellationTokenSource();
        _watchDelay = delay;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, delay.Token);
                await App.Cookies.CaptureCurrentSessionAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountVault", ex);
            }
        });
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            var path = VaultPath();
            if (!File.Exists(path))
                return;

            var protectedBytes = File.ReadAllBytes(path);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            _file = JsonSerializer.Deserialize<VaultFile>(json) ?? new VaultFile();
            _file.Accounts = _file.Accounts.Where(account => account.UserId > 0 && !string.IsNullOrWhiteSpace(account.Cookie)).ToList();
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("AccountVault", "Could not open the saved accounts. They were left untouched.");
            App.Logger.WriteException("AccountVault", ex);
            _file = new VaultFile();
        }
    }

    private static void Save()
    {
        var path = VaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(_file);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, path, overwrite: true);
    }

    private static string VaultPath() => Path.Combine(Paths.Base, "Accounts.bin");

    private sealed class VaultFile
    {
        public long ActiveUserId { get; set; }
        public List<AccountRecord> Accounts { get; set; } = [];
    }
}

public sealed class AccountRecord
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Cookie { get; set; } = "";
    public DateTime SavedAt { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

    public AccountRecord Copy() => new()
    {
        UserId = UserId,
        Username = Username,
        DisplayName = DisplayName,
        Cookie = Cookie,
        SavedAt = SavedAt
    };
}
