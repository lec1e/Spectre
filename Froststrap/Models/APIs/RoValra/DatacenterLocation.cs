namespace Froststrap.Models.APIs.RoValra
{
    public class DatacenterLocation
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = "";

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [JsonPropertyName("country_name")]
        public string? CountryName { get; set; }

        [JsonPropertyName("latLong")]
        // RoValra may return lat/long as either JSON numbers or strings; use JsonElement to tolerate both.
        public JsonElement[] LatLong { get; set; } = [];
    }
}
