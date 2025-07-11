using Newtonsoft.Json;

public class ModMetadata {

    [JsonProperty("mod_type")]
    public string ModType { get; set; }

    [JsonProperty("game_version")]
    public int GameVersion { get; set; }

    [JsonProperty("download_url")]
    public string DownloadUrl { get; set; }

    [JsonProperty("known_issues_url")]
    public string? KnownIssuesUrl { get; set; }
}
