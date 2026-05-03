using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller {
    public sealed class ScanCoordinator {
        public async Task<ScanResult> ScanAsync(bool includeSystemFolders) {
            if (DevWarnoMocks.TryCreateScanResult(out ScanResult mockScanResult)) {
                return mockScanResult;
            }

            AppLogger.Info("Starting WARNO scan.");
            ModCatalogDownloadResult catalogResult =
                await ModCatalogService.DownloadSupportedModsAsync();
            List<ModMetadata> supportedVersions = catalogResult.Mods;
            List<WarnoExecutable> warnoPaths = await WarnoFinder.FindExecutablesAsync(
                includeSystemFolders
            );
            List<WarnoEntry> entries = WarnoScanner.Scan(warnoPaths, supportedVersions);

            AppLogger.Info(
                $"WARNO scan completed. Executables found: {warnoPaths.Count}, valid entries: {entries.Count}."
            );
            return new ScanResult(
                supportedVersions,
                warnoPaths.Count,
                entries,
                catalogResult.SourceName,
                catalogResult.UsedFallback,
                catalogResult.FallbackReason
            );
        }
    }

    public sealed class ScanResult {
        public ScanResult(
            List<ModMetadata> supportedVersions,
            int executableCount,
            List<WarnoEntry> entries,
            string catalogSourceName = "",
            bool usedCatalogFallback = false,
            string? catalogFallbackReason = null
        ) {
            SupportedVersions = supportedVersions;
            ExecutableCount = executableCount;
            Entries = entries;
            CatalogSourceName = catalogSourceName;
            UsedCatalogFallback = usedCatalogFallback;
            CatalogFallbackReason = catalogFallbackReason;
        }

        public List<ModMetadata> SupportedVersions { get; }
        public int ExecutableCount { get; }
        public List<WarnoEntry> Entries { get; }
        public string CatalogSourceName { get; }
        public bool UsedCatalogFallback { get; }
        public string? CatalogFallbackReason { get; }
    }
}
