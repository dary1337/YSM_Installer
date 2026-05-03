using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace YSMInstaller {
    internal sealed class OfficialModCatalogSource : IModCatalogSource {
        private const string ModListUrl =
            "https://raw.githubusercontent.com/dary1337/YSM_Installer/master/mods-list.json";

        public ModCatalogSourceKind Kind => ModCatalogSourceKind.OfficialModsList;
        public string Name => ModCatalogSources.OfficialModsListName;

        public async Task<List<ModMetadata>> DownloadAsync() {
            AppLogger.Info("Downloading official supported mod list.");
            string json = await HttpService.GetStringAsync(ModListUrl);
            return JsonConvert.DeserializeObject<List<ModMetadata>>(json)
                ?? new List<ModMetadata>();
        }
    }
}
