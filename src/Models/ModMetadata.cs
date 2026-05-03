using Newtonsoft.Json;

namespace YSMInstaller {
    public sealed class ModMetadata {
        [JsonProperty("mod_type")]
        public string ModType { get; set; } = string.Empty;

        [JsonProperty("game_version")]
        public int GameVersion { get; set; }

        [JsonProperty("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonProperty("known_issues_url")]
        public string? KnownIssuesUrl { get; set; }
    }
}
