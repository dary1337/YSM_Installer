using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller
{
    public sealed class ScanCoordinator
    {
        public async Task<ScanResult> ScanAsync(bool includeSystemFolders)
        {
            AppLogger.Info("Starting WARNO scan.");
            List<ModMetadata> supportedVersions = await ModCatalogService.DownloadSupportedModsAsync();
            List<WarnoExecutable> warnoPaths = await WarnoFinder.FindExecutablesAsync(includeSystemFolders);
            List<WarnoEntry> entries = WarnoScanner.Scan(warnoPaths, supportedVersions);

            AppLogger.Info($"WARNO scan completed. Executables found: {warnoPaths.Count}, valid entries: {entries.Count}.");
            return new ScanResult(supportedVersions, warnoPaths.Count, entries);
        }
    }

    public sealed class ScanResult
    {
        public ScanResult(List<ModMetadata> supportedVersions, int executableCount, List<WarnoEntry> entries)
        {
            SupportedVersions = supportedVersions;
            ExecutableCount = executableCount;
            Entries = entries;
        }

        public List<ModMetadata> SupportedVersions { get; }
        public int ExecutableCount { get; }
        public List<WarnoEntry> Entries { get; }
    }
}
