namespace YSMInstaller {
    public static class ModCatalogSources {
        public const string OfficialModsListName = "Official mods list";
        public const string YokaisteGitHubReleasesName = "Yokaiste GitHub releases";

        public static string ToDisplayName(ModCatalogSourceKind sourceKind) {
            return sourceKind == ModCatalogSourceKind.YokaisteGitHubReleases
                ? YokaisteGitHubReleasesName
                : OfficialModsListName;
        }
    }
}
