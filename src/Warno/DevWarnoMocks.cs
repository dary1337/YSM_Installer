using System;
using System.Collections.Generic;
using System.Linq;

namespace YSMInstaller {
    internal static class DevWarnoMocks {
        public static bool IsEnabled {
            get { return DevService.IsMockWarnoPathsEnabled; }
        }

        public static bool TryCreateScanResult(out ScanResult scanResult) {
            if (!IsEnabled) {
                scanResult = EmptyScanResult();
                return false;
            }

            var supportedMods = CreateSupportedMods();
            var entries = CreateEntries(supportedMods);
            scanResult = new ScanResult(supportedMods, entries.Count, entries);
            AppLogger.Info("Using mocked WARNO scan result for UI development.");
            return true;
        }

        private static ScanResult EmptyScanResult() {
            return new ScanResult(new List<ModMetadata>(), 0, new List<WarnoEntry>());
        }

        private static List<ModMetadata> CreateSupportedMods() {
            return new List<ModMetadata> {
                new ModMetadata {
                    ModType = ModTypes.Ysm,
                    GameVersion = 146198,
                    DownloadUrl = "https://example.invalid/dev/ysm_146198.zip",
                    KnownIssuesUrl =
                        "https://steamcommunity.com/workshop/filedetails/discussion/3296415395/4509876644765422685/",
                },
                new ModMetadata {
                    ModType = ModTypes.Ysm,
                    GameVersion = 188908,
                    DownloadUrl = "https://example.invalid/dev/ysm_188908.zip",
                },
                new ModMetadata {
                    ModType = ModTypes.YsmWif,
                    GameVersion = 188908,
                    DownloadUrl = "https://example.invalid/dev/ysm_wif_188908.zip",
                },
            };
        }

        private static List<WarnoEntry> CreateEntries(List<ModMetadata> supportedMods) {
            int latestSupportedVersion = supportedMods.Max(mod => mod.GameVersion);

            return new List<WarnoEntry> {
                CreateEntry(
                    @"C:\Games\WARNO\Warno.exe",
                    188908,
                    "Mock Steam",
                    supportedMods,
                    latestSupportedVersion
                ),
                CreateEntry(
                    @"D:\SteamLibrary\steamapps\common\WARNO\Warno.exe",
                    200000,
                    "Mock future version",
                    supportedMods,
                    latestSupportedVersion
                ),
                CreateEntry(
                    @"E:\Portable\WARNO\Warno.exe",
                    146198,
                    "Mock known issues",
                    supportedMods,
                    latestSupportedVersion
                ),
                CreateEntry(
                    @"F:\OldGames\WARNO\Warno.exe",
                    120000,
                    "Mock unsupported",
                    supportedMods,
                    latestSupportedVersion
                ),
            };
        }

        private static WarnoEntry CreateEntry(
            string exePath,
            int version,
            string sourceLabel,
            List<ModMetadata> supportedMods,
            int latestSupportedVersion
        ) {
            return new WarnoEntry {
                ExePath = exePath,
                SourceLabel = sourceLabel,
                Version = version,
                VersionMetadata = supportedMods.Find(mod => mod.GameVersion == version),
                LatestCompatibleModVersion =
                    version > latestSupportedVersion ? latestSupportedVersion : 0,
            };
        }
    }
}
