using System.IO;

namespace YSMInstaller {
    public sealed class WarnoEntry {
        public string ExePath { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public string GamePath => Path.GetDirectoryName(ExePath) ?? string.Empty;
        public string ModsPath => Path.Combine(GamePath, "Mods");
        public string VersionPath => Path.Combine(GamePath, "Data", "PC");
        public int Version { get; set; }
        public int LatestCompatibleModVersion { get; set; }
        public ModMetadata? VersionMetadata { get; set; }
    }
}
