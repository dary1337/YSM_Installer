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

        // Catalog entries can declare a floor; installers older than this skip the entry rather
        // than attempt a download they can't handle (e.g. Google Drive URLs added in 1.1.0).
        [JsonProperty("min_installer_version")]
        public string? MinInstallerVersion { get; set; }

        [JsonIgnore]
        public string? LocalSourceFolder { get; set; }

        [JsonIgnore]
        public string? LocalSourceArchive { get; set; }

        // Manual installs use the mod's real Name from Config.ini so the UI doesn't read "Manual install".
        [JsonIgnore]
        public string? DisplayNameOverride { get; set; }
    }
}
