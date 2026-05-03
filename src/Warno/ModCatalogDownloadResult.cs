using System.Collections.Generic;

namespace YSMInstaller {
    public sealed class ModCatalogDownloadResult {
        public ModCatalogDownloadResult(
            List<ModMetadata> mods,
            ModCatalogSourceKind sourceKind,
            string sourceName,
            bool usedFallback,
            string? fallbackReason
        ) {
            Mods = mods;
            SourceKind = sourceKind;
            SourceName = sourceName;
            UsedFallback = usedFallback;
            FallbackReason = fallbackReason;
        }

        public List<ModMetadata> Mods { get; }
        public ModCatalogSourceKind SourceKind { get; }
        public string SourceName { get; }
        public bool UsedFallback { get; }
        public string? FallbackReason { get; }
    }
}
