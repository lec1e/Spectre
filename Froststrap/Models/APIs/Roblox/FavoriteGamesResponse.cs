namespace Froststrap.Models.APIs.Roblox;

internal class FavoriteGamesResponse
{
    [JsonPropertyName("previousPageCursor")]
    public string? PreviousPageCursor { get; set; }

    [JsonPropertyName("nextPageCursor")]
    public string? NextPageCursor { get; set; }

    [JsonPropertyName("data")]
    public List<FavoriteGameData>? Data { get; set; }
}

internal class FavoriteGameData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("creator")]
    public CreatorData? Creator { get; set; }

    [JsonPropertyName("rootPlace")]
    public RootPlaceData? RootPlace { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("updated")]
    public DateTime Updated { get; set; }

    [JsonPropertyName("placeVisits")]
    public long PlaceVisits { get; set; }
}

internal class CreatorData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class RootPlaceData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}