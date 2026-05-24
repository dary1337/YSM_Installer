using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class ModCatalogService {
        private static readonly Version CurrentInstallerVersion =
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

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
            var validMods = new List<ModMetadata>();

            foreach (ModMetadata mod in mods) {
                if (!IsValidModType(mod.ModType)) {
                    AppLogger.Error(
                        $"Catalog entry is skipped because mod_type '{mod.ModType}' is invalid (game version {mod.GameVersion})."
                    );
                    continue;
                }

                if (!MeetsMinInstallerVersion(mod, out string? requiredVersion)) {
                    AppLogger.Info(
                        $"Catalog entry is skipped because it requires installer {requiredVersion} (current {CurrentInstallerVersion}); mod_type '{mod.ModType}', game version {mod.GameVersion}."
                    );
                    continue;
                }

                if (!HasExactlyOneDownloadSource(mod)) {
                    AppLogger.Error(
                        $"Catalog entry is skipped because it has neither or both of download_url / download_url_parts set (mod_type '{mod.ModType}', game version {mod.GameVersion})."
                    );
                    continue;
                }

                validMods.Add(mod);
            }

            return validMods;
        }

        private static bool HasExactlyOneDownloadSource(ModMetadata mod) {
            bool hasSingle = !string.IsNullOrWhiteSpace(mod.DownloadUrl);
            // Reject entries where any part is blank — concatenating around an empty URL
            // would silently corrupt the cached archive on download.
            bool hasParts = mod.DownloadUrlParts != null
                && mod.DownloadUrlParts.Length > 0
                && AllNonBlank(mod.DownloadUrlParts);
            return hasSingle ^ hasParts;
        }

        private static bool AllNonBlank(string[] values) {
            for (int i = 0; i < values.Length; i++) {
                if (string.IsNullOrWhiteSpace(values[i])) {
                    return false;
                }
            }
            return true;
        }

        private static bool IsValidModType(string modType) {
            return string.Equals(modType, ModTypes.Ysm, StringComparison.Ordinal)
                || string.Equals(modType, ModTypes.YsmWif, StringComparison.Ordinal)
                || string.Equals(modType, ModTypes.YsmWifWto, StringComparison.Ordinal)
                || string.Equals(modType, ModTypes.Wto, StringComparison.Ordinal);
        }

        // Unparseable min_installer_version is treated as "no requirement" rather than blocking —
        // a typo in the catalog shouldn't hide entries from every user.
        private static bool MeetsMinInstallerVersion(ModMetadata mod, out string? requiredVersion) {
            requiredVersion = null;
            if (string.IsNullOrWhiteSpace(mod.MinInstallerVersion)) {
                return true;
            }
            if (!Version.TryParse(mod.MinInstallerVersion, out Version required)) {
                AppLogger.Error(
                    $"Catalog entry has unparseable min_installer_version '{mod.MinInstallerVersion}' — ignoring constraint."
                );
                return true;
            }
            requiredVersion = required.ToString();
            return CurrentInstallerVersion >= required;
        }
    }
}
