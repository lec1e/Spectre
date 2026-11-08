namespace Froststrap.Models
{
    public class ServerInfo
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("joinedAt")]
        public DateTime JoinedAt { get; set; }

        [JsonPropertyName("timeLeft")]
        public DateTime? TimeLeft { get; set; }

        [JsonPropertyName("ServerType")]
        public ServerType ServerType { get; set; }

        [JsonPropertyName("accessCode")]
        public string AccessCode { get; set; } = string.Empty;

        [JsonPropertyName("region")]
        public string Region { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsLatest { get; set; }

        [JsonPropertyName("playing")]
        public int Playing { get; set; }

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }

        [JsonIgnore]
        public string PlayerCount => $"{Playing}/{MaxPlayers}";

        [JsonPropertyName("uptime")]
        public string Uptime { get; set; } = string.Empty;

        [JsonIgnore]
        public System.Collections.ObjectModel.ObservableCollection<Avalonia.Media.Imaging.Bitmap> PlayerAvatarThumbnails { get; } = [];

        [JsonIgnore]
        public System.Collections.ObjectModel.Collection<string> PlayerTokens { get; } = [];

        [JsonIgnore]
        public string ExtraPlayersText { get; set; } = string.Empty;

        [JsonIgnore]
        public bool HasExtraPlayers { get; set; }

        [JsonIgnore]
        public string DurationText => $"From {JoinedAt:HH:mm} to {TimeLeft:HH:mm}";
    }
}