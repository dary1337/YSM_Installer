using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace YSMInstaller {
    internal sealed class YokaisteReleaseCatalogSource : IModCatalogSource {
        private const string ReleasesUrl = "https://api.github.com/repos/Yokaiste/YSM/releases";
        private const string GitHubApiAcceptHeader = "application/vnd.github+json";
        private static readonly string CacheKey = $"catalog:Yokaiste:{ReleasesUrl}";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        public ModCatalogSourceKind Kind => ModCatalogSourceKind.YokaisteGitHubReleases;
        public string Name => ModCatalogSources.YokaisteGitHubReleasesName;

        public async Task<List<ModMetadata>> DownloadAsync() {
            if (MemoryCache.TryGet(CacheKey, out List<ModMetadata> cached)) {
                AppLogger.Info("Reusing cached Yokaiste GitHub releases mod list.");
                return new List<ModMetadata>(cached);
            }
            AppLogger.Info("Downloading Yokaiste GitHub releases mod list.");
            string json = await HttpService.GetStringAsync(ReleasesUrl, GitHubApiAcceptHeader);
            List<GitHubRelease> releases =
                JsonConvert.DeserializeObject<List<GitHubRelease>>(json)
                ?? throw new InvalidDataException("GitHub releases response is empty.");

            var mods = new List<ModMetadata>();
            foreach (GitHubRelease release in releases) {
                int gameVersion = ParseGameVersion(release.TagName);
                GitHubReleaseAsset asset = SelectArchiveAsset(release);

                mods.Add(
                    new ModMetadata {
                        ModType = ModTypes.Ysm,
                        GameVersion = gameVersion,
                        DownloadUrl = asset.BrowserDownloadUrl,
                        KnownIssuesUrl = null,
                    }
                );
            }

            if (mods.Count == 0) {
                throw new InvalidDataException(
                    "Yokaiste GitHub releases source did not provide any valid releases."
                );
            }

            MemoryCache.Set(CacheKey, new List<ModMetadata>(mods), CacheTtl);
            return mods;
        }

        private static int ParseGameVersion(string tagName) {
            if (
                string.IsNullOrWhiteSpace(tagName)
                || tagName.Length < 2
                || tagName[0] != 'v'
                || !tagName.Substring(1).All(char.IsDigit)
                || !int.TryParse(tagName.Substring(1), out int gameVersion)
            ) {
                throw new InvalidDataException(
                    $"Yokaiste release tag '{tagName}' must use v<gameVersion> format."
                );
            }

            return gameVersion;
        }

        private static GitHubReleaseAsset SelectArchiveAsset(GitHubRelease release) {
            List<GitHubReleaseAsset> archiveAssets = release
                .Assets.Where(asset => IsSafeArchiveAsset(asset.BrowserDownloadUrl))
                .ToList();

            if (archiveAssets.Count != 1) {
                throw new InvalidDataException(
                    $"Yokaiste release '{release.TagName}' must contain exactly one HTTPS archive asset."
                );
            }

            return archiveAssets[0];
        }

        private static bool IsSafeArchiveAsset(string value) {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps) {
                return false;
            }
            return ModArchiveFormats.HasSupportedExtension(uri.AbsolutePath);
        }

        private sealed class GitHubRelease {
            [JsonProperty("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
        }

        private sealed class GitHubReleaseAsset {
            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}
