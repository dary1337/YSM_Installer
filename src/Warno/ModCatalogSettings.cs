namespace YSMInstaller {
    // Session-scoped: the final release persists nothing to disk, so a source picked in Settings
    // lasts until the app closes and then falls back to the official mods list.
    internal static class ModCatalogSettings {
        public static ModCatalogSourceKind SelectedSource { get; set; } = ModCatalogSourceKind.OfficialModsList;
    }
}
