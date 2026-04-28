using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller
{
    public static class ModCatalogService
    {
        private const string ModListUrl = "https://raw.githubusercontent.com/dary1337/YSM_Installer/master/mods-list.json";

        public static async Task<List<ModMetadata>> DownloadSupportedModsAsync()
        {
            try
            {
                AppLogger.Info("Downloading supported mod list.");
                var json = await HttpService.GetStringAsync(ModListUrl);
                var mods = JsonConvert.DeserializeObject<List<ModMetadata>>(json);
                var result = ValidateSupportedMods(mods ?? new List<ModMetadata>());
                AppLogger.Info($"Loaded {result.Count} supported mod entries.");
                return result;
            }
            catch (Exception exception)
            {
                AppLogger.Error("Failed to download supported mod list.", exception);
                return new List<ModMetadata>();
            }
        }

        private static List<ModMetadata> ValidateSupportedMods(List<ModMetadata> mods)
        {
            var brokenVersions = new HashSet<int>();
            var validMods = new List<ModMetadata>();

            foreach (ModMetadata mod in mods)
            {
                if (!IsValidModType(mod.ModType))
                {
                    brokenVersions.Add(mod.GameVersion);
                    AppLogger.Error($"Catalog version {mod.GameVersion} is marked broken because mod_type '{mod.ModType}' is invalid.");
                    continue;
                }

                validMods.Add(mod);
            }

            if (brokenVersions.Count == 0)
            {
                return validMods;
            }

            var result = new List<ModMetadata>();
            foreach (ModMetadata mod in validMods)
            {
                if (!brokenVersions.Contains(mod.GameVersion))
                {
                    result.Add(mod);
                }
            }

            return result;
        }

        private static bool IsValidModType(string modType)
        {
            return string.Equals(modType, ModTypes.Ysm, StringComparison.Ordinal) ||
                   string.Equals(modType, ModTypes.YsmWif, StringComparison.Ordinal);
        }
    }
}
