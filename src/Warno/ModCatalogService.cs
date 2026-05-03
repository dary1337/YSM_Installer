using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class ModCatalogService {
        private static readonly IModCatalogSource OfficialSource = new OfficialModCatalogSource();
        private static readonly IModCatalogSource YokaisteSource =
            new YokaisteReleaseCatalogSource();

        public static async Task<ModCatalogDownloadResult> DownloadSupportedModsAsync() {
            ModCatalogSourceKind selectedSourceKind = ModCatalogSettings.SelectedSource;
            IModCatalogSource selectedSource = GetSource(selectedSourceKind);

            if (selectedSource.Kind == OfficialSource.Kind) {
                return await DownloadFromOfficialAsync();
            }

            try {
                return await DownloadFromSelectedSourceAsync(selectedSource);
            }
            catch (Exception exception) {
                AppLogger.Error(
                    $"Catalog source '{selectedSource.Name}' failed. Falling back to {OfficialSource.Name}.",
                    exception
                );
                return await DownloadFallbackAsync(exception.Message);
            }
        }

        private static async Task<ModCatalogDownloadResult> DownloadFromSelectedSourceAsync(
            IModCatalogSource source
        ) {
            List<ModMetadata> mods = await source.DownloadAsync();
            List<ModMetadata> result = ValidateSupportedMods(mods);

            if (result.Count == 0) {
                throw new InvalidOperationException(
                    $"Catalog source '{source.Name}' returned no valid mod entries."
                );
            }

            AppLogger.Info($"Loaded {result.Count} supported mod entries from {source.Name}.");
            return new ModCatalogDownloadResult(
                result,
                source.Kind,
                source.Name,
                usedFallback: false,
                fallbackReason: null
            );
        }

        private static async Task<ModCatalogDownloadResult> DownloadFallbackAsync(string reason) {
            ModCatalogDownloadResult result = await DownloadFromOfficialAsync();
            return new ModCatalogDownloadResult(
                result.Mods,
                result.SourceKind,
                result.SourceName,
                usedFallback: true,
                fallbackReason: reason
            );
        }

        private static async Task<ModCatalogDownloadResult> DownloadFromOfficialAsync() {
            try {
                List<ModMetadata> mods = await OfficialSource.DownloadAsync();
                List<ModMetadata> result = ValidateSupportedMods(mods);
                AppLogger.Info(
                    $"Loaded {result.Count} supported mod entries from {OfficialSource.Name}."
                );
                return new ModCatalogDownloadResult(
                    result,
                    OfficialSource.Kind,
                    OfficialSource.Name,
                    usedFallback: false,
                    fallbackReason: null
                );
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to download supported mod list.", exception);
                return new ModCatalogDownloadResult(
                    new List<ModMetadata>(),
                    OfficialSource.Kind,
                    OfficialSource.Name,
                    usedFallback: false,
                    fallbackReason: exception.Message
                );
            }
        }

        private static IModCatalogSource GetSource(ModCatalogSourceKind sourceKind) {
            return sourceKind == ModCatalogSourceKind.YokaisteGitHubReleases
                ? YokaisteSource
                : OfficialSource;
        }

        private static List<ModMetadata> ValidateSupportedMods(List<ModMetadata> mods) {
            var brokenVersions = new HashSet<int>();
            var validMods = new List<ModMetadata>();

            foreach (ModMetadata mod in mods) {
                if (!IsValidModType(mod.ModType)) {
                    brokenVersions.Add(mod.GameVersion);
                    AppLogger.Error(
                        $"Catalog version {mod.GameVersion} is marked broken because mod_type '{mod.ModType}' is invalid."
                    );
                    continue;
                }

                validMods.Add(mod);
            }

            if (brokenVersions.Count == 0) {
                return validMods;
            }

            var result = new List<ModMetadata>();
            foreach (ModMetadata mod in validMods) {
                if (!brokenVersions.Contains(mod.GameVersion)) {
                    result.Add(mod);
                }
            }

            return result;
        }

        private static bool IsValidModType(string modType) {
            return string.Equals(modType, ModTypes.Ysm, StringComparison.Ordinal)
                || string.Equals(modType, ModTypes.YsmWif, StringComparison.Ordinal);
        }
    }
}
