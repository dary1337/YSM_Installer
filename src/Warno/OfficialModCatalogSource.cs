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
            string url = ResolveModListUrl();
            AppLogger.Info($"Downloading supported mod list from {url}.");
            string json = await HttpService.GetStringAsync(url);
            return JsonConvert.DeserializeObject<List<ModMetadata>>(json)
                ?? new List<ModMetadata>();
        }

        private static string ResolveModListUrl() {
            string? @override = DevService.ModListUrlOverride;
            return string.IsNullOrWhiteSpace(@override) ? ModListUrl : @override!;
        }
    }
}
