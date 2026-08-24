/*
*  Froststrap
*  Copyright (c) Froststrap Team
*
*  This file is part of Froststrap and is distributed under the terms of the
*  GNU Affero General Public License, version 3 or later.
*
*  SPDX-License-Identifier: AGPL-3.0-or-later
*/

using Froststrap.RobloxInterfaces;
using Froststrap.Utility;

namespace Froststrap.Models.Entities
{
    public static class GameSearching
    {
        private const string LOG_IDENT = "GameSearching";

        public static async Task<List<OmniSearchContent>> GetGameSearchResultsAsync(string searchQuery)
        {
            var results = new List<OmniSearchContent>();

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                App.Logger.WriteLine(LOG_IDENT, "Search query is empty.");
                return results;
            }

            try
            {
                Uri omniSearchUrl = new($"https://apis.{Deployment.RobloxDomain}/search-api/omni-search?searchQuery={Uri.EscapeDataString(searchQuery)}&sessionid=0&pageType=Game");

                var response = await Http.GetJson<OmniSearchResponse>(omniSearchUrl);

                if (response?.SearchResults is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Search API returned no results.");
                    return results;
                }

                var seenUniverses = new HashSet<ulong>();

                foreach (var group in response.SearchResults)
                {
                    if (group.Contents is null)
                        continue;

                    foreach (var item in group.Contents)
                    {
                        if (item.UniverseId == 0 || !seenUniverses.Add(item.UniverseId))
                            continue;

                        results.Add(new OmniSearchContent
                        {
                            UniverseId = item.UniverseId,
                            RootPlaceId = item.RootPlaceId,
                            Name = item.Name ?? $"Game {item.UniverseId}",
                            PlayerCount = item.PlayerCount
                        });
                    }
                }

                // Prefer names that actually match the typed words (not just Roblox popularity order).
                var ranked = results
                    .Select(r => (Item: r, Score: GameNameMatch.Score(r.Name, searchQuery)))
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Item.PlayerCount ?? 0)
                    .ToList();

                if (ranked.Any(x => x.Score > 0))
                    ranked = ranked.Where(x => x.Score > 0).ToList();

                return ranked.Take(12).Select(x => x.Item).ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error fetching search results: {ex.Message}");
            }

            return results;
        }

        public static async Task<(List<OmniSearchContent> Results, string NextCursor)> GetDetailedGameSearchResultsAsync(string keyword, string cursor = "")
        {
            var results = new List<OmniSearchContent>();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return (results, "");
            }

            try
            {
                string cursorParam = string.IsNullOrEmpty(cursor) ? "" : $"&pageToken={Uri.EscapeDataString(cursor)}";
                Uri url = new($"https://apis.{Deployment.RobloxDomain}/search-api/omni-search?searchQuery={Uri.EscapeDataString(keyword)}&sessionId=0&pageType=Game{cursorParam}");

                var response = await Http.GetJson<System.Text.Json.JsonDocument>(url);

                if (response != null && response.RootElement.TryGetProperty("searchResults", out var groupsArray) && groupsArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    string nextCursor = "";
                    if (response.RootElement.TryGetProperty("nextPageToken", out var nextCursorProp) && nextCursorProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        nextCursor = nextCursorProp.GetString() ?? "";
                    }

                    var seenUniverses = new HashSet<ulong>();

                    foreach (var group in groupsArray.EnumerateArray())
                    {
                        if (group.TryGetProperty("contents", out var contentsArray) && contentsArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in contentsArray.EnumerateArray())
                            {
                                ulong universeId = item.TryGetProperty("universeId", out var u) ? (ulong)u.GetInt64() : 0;
                                long placeId = item.TryGetProperty("rootPlaceId", out var p) ? p.GetInt64() : 0;
                                string name = item.TryGetProperty("name", out var n) ? (n.GetString() ?? $"Game {universeId}") : $"Game {universeId}";
                                int playerCount = item.TryGetProperty("playerCount", out var pc) ? pc.GetInt32() : 0;

                                if (universeId == 0 || !seenUniverses.Add(universeId))
                                    continue;

                                results.Add(new OmniSearchContent
                                {
                                    UniverseId = universeId,
                                    RootPlaceId = placeId,
                                    Name = name,
                                    PlayerCount = playerCount
                                });
                            }
                        }
                    }

                    var ranked = results
                        .Select(r => (Item: r, Score: GameNameMatch.Score(r.Name, keyword)))
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.Item.PlayerCount ?? 0)
                        .Select(x => x.Item)
                        .ToList();

                    return (ranked, nextCursor);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error fetching detailed search results: {ex.Message}");
            }

            return (results, "");
        }
    }
}
