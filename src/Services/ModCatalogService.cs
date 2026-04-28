using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class ModCatalogService {
        private const string ModListUrl = "https://raw.githubusercontent.com/dary1337/YSM_Installer/master/mods-list.json";

        public static async Task<List<ModMetadata>> DownloadSupportedModsAsync() {
            try {
                AppLogger.Info("Downloading supported mod list.");
                var json = await HttpService.GetStringAsync(ModListUrl);
                var mods = JsonConvert.DeserializeObject<List<ModMetadata>>(json);
                var result = mods ?? new List<ModMetadata>();
                AppLogger.Info($"Loaded {result.Count} supported mod entries.");
                return result;
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to download supported mod list.", exception);
                return new List<ModMetadata>();
            }
        }
    }
}
