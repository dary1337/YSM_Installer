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

        [JsonIgnore]
        public string? LocalSourceFolder { get; set; }

        [JsonIgnore]
        public string? LocalSourceArchive { get; set; }

        // Manual installs use the mod's real Name from Config.ini so the UI doesn't read "Manual install".
        [JsonIgnore]
        public string? DisplayNameOverride { get; set; }
    }
}
