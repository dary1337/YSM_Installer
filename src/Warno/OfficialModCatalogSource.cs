using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace YSMInstaller {
    internal sealed class OfficialModCatalogSource : IModCatalogSource {
        private const string ModListUrl =
            "https://raw.githubusercontent.com/dary1337/YSM_Installer/master/mods-list.json";

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        public ModCatalogSourceKind Kind => ModCatalogSourceKind.OfficialModsList;
        public string Name => ModCatalogSources.OfficialModsListName;

        public async Task<List<ModMetadata>> DownloadAsync() {
            string url = ResolveModListUrl();
            string cacheKey = $"catalog:Official:{url}";
            if (MemoryCache.TryGet(cacheKey, out List<ModMetadata> cached)) {
                AppLogger.Info($"Reusing cached mod list from {url}.");
                return new List<ModMetadata>(cached);
            }
            AppLogger.Info($"Downloading supported mod list from {url}.");
            string json = await HttpService.GetStringAsync(url);
            // Newtonsoft's first-character error on HTML is cryptic; pre-check so the dev test
            // override surface reports a useful hint about github.com vs raw.githubusercontent.com.
            if (json.TrimStart().StartsWith("<")) {
                throw new InvalidDataException(
                    $"Mod list URL returned HTML instead of JSON ({url}). " +
                    "If you pasted a github.com URL, use the raw.githubusercontent.com form instead."
                );
            }
            List<ModMetadata> mods = JsonConvert.DeserializeObject<List<ModMetadata>>(json)
                ?? new List<ModMetadata>();
            // Don't cache an empty catalog — would lock the user out for the full TTL if a
            // deploy accidentally publishes empty mods-list.json.
            if (mods.Count > 0) {
                MemoryCache.Set(cacheKey, new List<ModMetadata>(mods), CacheTtl);
            }
            return mods;
        }

        private static string ResolveModListUrl() {
            string? @override = DevService.ModListUrlOverride;
            if (string.IsNullOrWhiteSpace(@override)) {
                return ModListUrl;
            }
            // Reject non-http(s) / non-absolute overrides — a stray file:// or gibberish would
            // either crash HttpClient or behave unexpectedly; better to fall back loudly.
            if (!Uri.TryCreate(@override, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
                AppLogger.Error(
                    $"Ignoring invalid ModListUrlOverride '{@override}': use an absolute http(s) URL."
                );
                return ModListUrl;
            }
            return @override!;
        }
    }
}
