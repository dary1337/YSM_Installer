using System.Collections.Generic;
using System.Linq;

namespace YSMInstaller {
    internal static class DevWarnoMocks {
        public static bool IsEnabled => DevService.IsMockWarnoPathsEnabled;

        /// <summary>One-shot flag: when set, the next simulated install throws to exercise the failure UI.</summary>
        public static bool SimulateInstallFailure { get; set; }

        // Counter consumed by HttpService.DownloadFilePartsAsync before each chunk attempt.
        // Each non-zero value injects a synthetic IOException so the retry path is exercised
        // without real network failures. Always-readable in Release (counter starts at 0 and
        // can only be set from Form1.Dev which is #if DEBUG-gated).
        private static int _chunkFailuresRemaining;
        private static string _chunkFailureReason = string.Empty;

        public static void QueueChunkFailures(int count, string reason) {
            _chunkFailureReason = reason ?? string.Empty;
            _chunkFailuresRemaining = count < 0 ? 0 : count;
        }

        public static bool TryConsumeChunkFailure(out string reason) {
            if (_chunkFailuresRemaining > 0) {
                _chunkFailuresRemaining--;
                reason = _chunkFailureReason;
                return true;
            }
            reason = string.Empty;
            return false;
        }

        public static bool TryCreateScanResult(out ScanResult scanResult) {
            if (!IsEnabled) {
                scanResult = EmptyScanResult();
                return false;
            }

            scanResult = new ScanResult(Catalog(), 4, MixedInstalls(), ModCatalogSources.OfficialModsListName);
            AppLogger.Info("Using mocked WARNO scan result for UI development.");
            return true;
        }

        private static ScanResult EmptyScanResult() {
            return new ScanResult(new List<ModMetadata>(), 0, new List<WarnoEntry>());
        }

        // ---- Catalog ----
        public static List<ModMetadata> Catalog() {
            return new List<ModMetadata> {
                Mod(ModTypes.Ysm, 146198, "https://steamcommunity.com/workshop/filedetails/discussion/3296415395/4509876644765422685/"),
                Mod(ModTypes.Ysm, 188908),
                Mod(ModTypes.YsmWif, 188908),
                Mod(ModTypes.YsmWifWto, 188908),
                Mod(ModTypes.Wto, 188908),
            };
        }

        private static ModMetadata Mod(string type, int version, string? knownIssues = null) {
            return new ModMetadata {
                ModType = type,
                GameVersion = version,
                DownloadUrl = $"https://example.invalid/dev/{type}_{version}.zip",
                KnownIssuesUrl = knownIssues,
            };
        }

        // ---- Scenarios (for the dev Test menu) ----
        public static List<WarnoEntry> MixedInstalls() {
            var catalog = Catalog();
            return new List<WarnoEntry> {
                Entry(@"C:\Program Files (x86)\Steam\steamapps\common\WARNO\Warno.exe", 188908, WarnoExecutableSources.Steam, catalog),
                Entry(@"D:\SteamLibrary\steamapps\common\WARNO\Warno.exe", 200000, WarnoExecutableSources.Steam, catalog),
                Entry(@"E:\Portable\WARNO\Warno.exe", 146198, WarnoExecutableSources.CommonFolder, catalog),
                Entry(@"F:\OldGames\WARNO\Warno.exe", 120000, WarnoExecutableSources.Manual, catalog),
            };
        }

        public static List<WarnoEntry> SingleSupported() {
            var catalog = Catalog();
            return new List<WarnoEntry> {
                Entry(@"D:\SteamLibrary\steamapps\common\WARNO\Warno.exe", 188908, WarnoExecutableSources.Steam, catalog),
            };
        }

        public static List<WarnoEntry> AllUnsupported() {
            var catalog = Catalog();
            return new List<WarnoEntry> {
                Entry(@"F:\OldGames\WARNO\Warno.exe", 120000, WarnoExecutableSources.CommonFolder, catalog),
                Entry(@"G:\Legacy\WARNO\Warno.exe", 100000, WarnoExecutableSources.Manual, catalog),
            };
        }

        public static List<WarnoEntry> KnownIssues() {
            var catalog = Catalog();
            return new List<WarnoEntry> {
                Entry(@"E:\Portable\WARNO\Warno.exe", 146198, WarnoExecutableSources.CommonFolder, catalog),
            };
        }

        public static List<WarnoEntry> FutureVersions() {
            var catalog = Catalog();
            return new List<WarnoEntry> {
                Entry(@"D:\SteamLibrary\steamapps\common\WARNO\Warno.exe", 210000, WarnoExecutableSources.Steam, catalog),
                Entry(@"C:\Games\WARNO\Warno.exe", 205000, WarnoExecutableSources.Registry, catalog),
            };
        }

        private static WarnoEntry Entry(string exePath, int version, string sourceLabel, List<ModMetadata> catalog) {
            int latestSupported = catalog.Select(m => m.GameVersion).DefaultIfEmpty(0).Max();
            return new WarnoEntry {
                ExePath = exePath,
                SourceLabel = sourceLabel,
                Version = version,
                VersionMetadata = catalog.Find(m => m.GameVersion == version),
                LatestCompatibleModVersion = version > latestSupported ? latestSupported : 0,
            };
        }
    }
}
