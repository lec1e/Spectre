using Froststrap.Integrations;

namespace Froststrap.Utility
{
    // Backs the Server Browser page: public games API + optional region lookup via a saved account cookie.
    public static class ServerBrowserClient
    {
        private const string LOG_IDENT = "ServerBrowserClient";

        public static async Task<ServerListResponse?> FetchServersAsync(long placeId, string? cursor = null, int sortOrder = 2)
        {
            try
            {
                string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?limit=100&sortOrder={sortOrder}";
                if (!string.IsNullOrEmpty(cursor))
                    url += $"&cursor={Uri.EscapeDataString(cursor)}";

                return await Http.GetJson<ServerListResponse>(url);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::FetchServers", ex);
                return null;
            }
        }

        public static async Task<GameServer?> GetEmptiestServerAsync(long placeId)
        {
            var resp = await FetchServersAsync(placeId, null, sortOrder: 1);
            if (resp?.Data is null)
                return null;

            return resp.Data
                .Where(s => !string.IsNullOrEmpty(s.Id) && s.Playing < s.MaxPlayers)
                .OrderBy(s => s.Playing)
                .FirstOrDefault();
        }

        public static bool CanResolveRegions => App.Cookies.Loaded;

        public static async Task<string?> ResolveServerIpAsync(long placeId, string jobId)
        {
            if (!App.Cookies.Loaded)
                return null;

            string cookie = App.Cookies.GetAuthCookie();
            try
            {
                string body = JsonSerializer.Serialize(new
                {
                    placeId,
                    isTeleport = false,
                    gameId = jobId,
                    gameJoinAttemptId = Guid.NewGuid(),
                });

                string? raw = await PostGameJoinAsync(cookie, body, null);
                if (string.IsNullOrEmpty(raw))
                    return null;

                var dc = Regex.Match(raw, @"128\.116\.\d{1,3}\.\d{1,3}");
                if (dc.Success)
                    return dc.Value;

                var any = Regex.Match(
                    raw,
                    "\"(?:MachineAddress|Address|ip)\"\\s*:\\s*\"(\\d{1,3}(?:\\.\\d{1,3}){3})\"",
                    RegexOptions.IgnoreCase);
                return any.Success ? any.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::ResolveServerIp", ex);
                return null;
            }
        }

        private static async Task<string?> PostGameJoinAsync(string cookie, string body, string? csrf)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://gamejoin.roblox.com/v1/join-game-instance");
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            req.Headers.TryAddWithoutValidation("User-Agent", "Roblox/WinInet");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
            if (!string.IsNullOrEmpty(csrf))
                req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await App.HttpClient.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.Forbidden && string.IsNullOrEmpty(csrf)
                && resp.Headers.TryGetValues("x-csrf-token", out var values))
            {
                string? token = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(token))
                    return await PostGameJoinAsync(cookie, body, token);
            }

            if (!resp.IsSuccessStatusCode)
            {
                App.Logger.WriteLine(LOG_IDENT, $"gamejoin returned {(int)resp.StatusCode} {resp.ReasonPhrase} — region lookup skipped.");
                return null;
            }

            return await resp.Content.ReadAsStringAsync();
        }
    }
}
