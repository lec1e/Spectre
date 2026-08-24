using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Froststrap.Utility
{
    internal sealed class RbxServersVipList
    {
        public required string GameName { get; init; }
        public List<RbxServersVipServer> Servers { get; init; } = [];
    }

    internal sealed class RbxServersVipServer
    {
        public required string Name { get; init; }
        public required string AccessCode { get; init; }
    }

    internal sealed class RbxServersFeaturedGame
    {
        public required long PlaceId { get; init; }
        public required string Name { get; init; }
        public required string ThumbnailUrl { get; init; }
        public int ServerCount { get; init; }
    }

    internal static class RbxServersClient
    {
        private const string LOG_IDENT = "RbxServersClient";

        // Example anchor:
        // <a href="/embedded/quicklaunch/<guid>"> ... <span>Display Name</span> ... </a>
        private static readonly Regex QuicklaunchAnchorRegex = new(
            @"<a[^>]*href\s*=\s*""\/embedded\/quicklaunch\/(?<code>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})""[^>]*>(?<inner>.*?)<\/a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Game name is on the page in an <h1> tag.
        private static readonly Regex GameNameRegex = new(
            @"<h1[^>]*>(?<name>.*?)<\/h1>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpanNameRegex = new(
            @"<span[^>]*>(?<name>.*?)<\/span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IndexRowRegex = new(
            @"<a[^>]*class=""index-row""[^>]*href=""\/games\/(?<placeId>\d{3,19})""[^>]*>(?<inner>.*?)<\/a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IndexNameRegex = new(
            @"<span[^>]*class=""index-name""[^>]*>(?<name>.*?)<\/span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IndexCountRegex = new(
            @"<span[^>]*class=""index-count""[^>]*>(?<count>[\d,]+)<\/span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ImgSrcRegex = new(
            @"<img[^>]*src=""(?<src>https:\/\/tr\.rbxcdn\.com\/[^""]+)""",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ServerCountTextRegex = new(
            @"(?<count>\d[\d,]*)\s+Servers",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<RbxServersVipList> FetchVipServersAsync(
            long placeId,
            CancellationToken cancellationToken = default)
        {
            string url = $"https://rbxservers.xyz/embedded/game/{placeId}";

            try
            {
                using var http = CreateClient();

                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                // rbxservers HTML is usually large; cap it so we don't hang on slow connections.
                string html = await ReadLimitedStringAsync(response.Content, maxChars: 2_000_000, cancellationToken);

                return ParseVipServersHtml(placeId, html);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return new RbxServersVipList
                {
                    GameName = $"VIP servers for place {placeId}",
                    Servers = []
                };
            }
        }

        public static async Task<List<RbxServersFeaturedGame>> FetchFeaturedGamesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var http = CreateClient();
                using var response = await http.GetAsync("https://rbxservers.xyz/", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                string html = await ReadLimitedStringAsync(response.Content, maxChars: 2_000_000, cancellationToken);
                return ParseFeaturedGamesHtml(html);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return [];
            }
        }

        /// <summary>
        /// Looks up a single place on rbxservers.xyz. Returns null when the game is not listed.
        /// </summary>
        public static async Task<RbxServersFeaturedGame?> FetchGameSummaryAsync(
            long placeId,
            CancellationToken cancellationToken = default)
        {
            if (placeId <= 0)
                return null;

            try
            {
                using var http = CreateClient();
                using var response = await http.GetAsync(
                    $"https://rbxservers.xyz/games/{placeId}",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                string html = await ReadLimitedStringAsync(response.Content, maxChars: 500_000, cancellationToken);
                string name = "";
                var nameMatch = GameNameRegex.Match(html);
                if (nameMatch.Success)
                    name = DecodeAndStripTags(nameMatch.Groups["name"].Value).Trim();

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                string thumbnail = "";
                var imageMatch = ImgSrcRegex.Match(html);
                if (imageMatch.Success)
                    thumbnail = WebUtility.HtmlDecode(imageMatch.Groups["src"].Value);

                int serverCount = 0;
                var countMatch = ServerCountTextRegex.Match(html);
                if (countMatch.Success)
                    int.TryParse(countMatch.Groups["count"].Value.Replace(",", ""), out serverCount);

                return new RbxServersFeaturedGame
                {
                    PlaceId = placeId,
                    Name = name,
                    ThumbnailUrl = thumbnail,
                    ServerCount = serverCount
                };
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Eclipse/RbxServersClient)");
            return http;
        }

        private static RbxServersVipList ParseVipServersHtml(long placeId, string html)
        {
            string gameName = "";
            var gameMatch = GameNameRegex.Match(html);
            if (gameMatch.Success)
                gameName = DecodeAndStripTags(gameMatch.Groups["name"].Value).Trim();

            if (string.IsNullOrWhiteSpace(gameName))
                gameName = $"VIP servers for place {placeId}";

            var servers = new List<RbxServersVipServer>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in QuicklaunchAnchorRegex.Matches(html))
            {
                var accessCode = m.Groups["code"].Value;
                if (string.IsNullOrWhiteSpace(accessCode) || !seen.Add(accessCode))
                    continue;

                var inner = m.Groups["inner"].Value;
                var spanMatch = SpanNameRegex.Match(inner);
                var displayName = spanMatch.Success
                    ? DecodeAndStripTags(spanMatch.Groups["name"].Value).Trim()
                    : "";

                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = "VIP server";

                servers.Add(new RbxServersVipServer
                {
                    Name = displayName,
                    AccessCode = accessCode
                });
            }

            return new RbxServersVipList
            {
                GameName = gameName,
                Servers = servers
            };
        }

        private static List<RbxServersFeaturedGame> ParseFeaturedGamesHtml(string html)
        {
            var games = new List<RbxServersFeaturedGame>();
            var seen = new HashSet<long>();

            foreach (Match match in IndexRowRegex.Matches(html))
            {
                if (!long.TryParse(match.Groups["placeId"].Value, out long placeId) || !seen.Add(placeId))
                    continue;

                string inner = match.Groups["inner"].Value;
                string name = "";
                string thumbnailUrl = "";
                int serverCount = 0;

                var nameMatch = IndexNameRegex.Match(inner);
                if (nameMatch.Success)
                    name = DecodeAndStripTags(nameMatch.Groups["name"].Value).Trim();

                var countMatch = IndexCountRegex.Match(inner);
                if (countMatch.Success)
                    int.TryParse(countMatch.Groups["count"].Value.Replace(",", ""), out serverCount);

                var imageMatch = ImgSrcRegex.Match(inner);
                if (imageMatch.Success)
                    thumbnailUrl = WebUtility.HtmlDecode(imageMatch.Groups["src"].Value);

                if (string.IsNullOrWhiteSpace(name))
                    name = $"Game {placeId}";

                games.Add(new RbxServersFeaturedGame
                {
                    PlaceId = placeId,
                    Name = name,
                    ThumbnailUrl = thumbnailUrl,
                    ServerCount = serverCount
                });
            }

            return games;
        }

        private static string DecodeAndStripTags(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            // Remove any markup inside the captured group first (then decode entities).
            string stripped = Regex.Replace(input, "<.*?>", "", RegexOptions.Singleline);
            return WebUtility.HtmlDecode(stripped);
        }

        private static async Task<string> ReadLimitedStringAsync(
            HttpContent content,
            int maxChars,
            CancellationToken cancellationToken)
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192);

            var sb = new StringBuilder();
            char[] buffer = new char[8192];
            int total = 0;

            while (total < maxChars)
            {
                int toRead = Math.Min(buffer.Length, maxChars - total);
                int read = await reader.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read <= 0)
                    break;
                sb.Append(buffer, 0, read);
                total += read;
            }

            return sb.ToString();
        }
    }
}

