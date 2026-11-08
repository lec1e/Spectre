using System.Net.Http.Json;

namespace Froststrap.Utility
{
    public sealed record PlayingAccount(long UserId, string Name, string DisplayName);

    /// <summary>Reads the accounts currently inside running Roblox clients from their logs.</summary>
    public static class PlayingAccounts
    {
        private static readonly Regex UserIdPattern = new(@"rbxuid=(\d+)|userid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Dictionary<long, PlayingAccount> Cache = [];

        public static async Task<IReadOnlyList<PlayingAccount>> GetAsync()
        {
            if (!OperatingSystem.IsWindows())
                return [];

            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length == 0)
                return [];

            var ids = ReadUserIds(processes);
            if (ids.Count == 0)
                return [];

            var missing = ids.Where(id => !Cache.ContainsKey(id)).ToArray();
            if (missing.Length > 0)
                await ResolveAsync(missing);

            return ids.Select(id => Cache.TryGetValue(id, out var account)
                    ? account
                    : new PlayingAccount(id, id.ToString(), $"User {id}"))
                .ToArray();
        }

        private static List<long> ReadUserIds(Process[] processes)
        {
            var ids = new List<long>();
            if (!Directory.Exists(Paths.RobloxLogs))
                return ids;

            var started = DateTime.UtcNow;
            foreach (var process in processes)
            {
                try { if (process.StartTime.ToUniversalTime() < started) started = process.StartTime.ToUniversalTime(); }
                catch { }
            }

            started = started.AddSeconds(-30);
            foreach (string log in Directory.GetFiles(Paths.RobloxLogs, "*.log")
                         .Where(path => !path.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase)
                             && File.GetLastWriteTimeUtc(path) >= started)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                long id = ReadLastUserId(log);
                if (id > 0 && !ids.Contains(id))
                    ids.Add(id);
                if (ids.Count >= processes.Length)
                    break;
            }

            return ids;
        }

        private static long ReadLastUserId(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int take = (int)Math.Min(stream.Length, 256 * 1024);
                stream.Seek(-take, SeekOrigin.End);
                var buffer = new byte[take];
                stream.ReadExactly(buffer);
                string text = Encoding.UTF8.GetString(buffer);
                Match last = null!;
                foreach (Match match in UserIdPattern.Matches(text))
                    last = match;
                string value = last is { Success: true }
                    ? (last.Groups[1].Success ? last.Groups[1].Value : last.Groups[2].Value)
                    : "";
                return long.TryParse(value, out long id) ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static async Task ResolveAsync(long[] ids)
        {
            try
            {
                using var response = await App.HttpClient.PostAsJsonAsync(
                    "https://users.roblox.com/v1/users",
                    new { userIds = ids, excludeBannedUsers = false });
                if (!response.IsSuccessStatusCode)
                    return;

                var body = await response.Content.ReadFromJsonAsync<UserBatch>();
                foreach (var user in body?.Data ?? [])
                {
                    if (user.Id <= 0 || string.IsNullOrWhiteSpace(user.Name))
                        continue;
                    Cache[user.Id] = new PlayingAccount(user.Id, user.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Name : user.DisplayName);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PlayingAccounts", ex);
            }
        }

        private sealed class UserBatch
        {
            public List<UserRow> Data { get; set; } = [];
        }

        private sealed class UserRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public string DisplayName { get; set; } = "";
        }
    }
}
