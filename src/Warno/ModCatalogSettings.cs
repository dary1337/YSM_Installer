using System;

namespace YSMInstaller {
    internal static class ModCatalogSettings {
        public static ModCatalogSourceKind SelectedSource {
            get {
                string value = Properties.Settings.Default.ModCatalogSourceKind;
                return Enum.TryParse(value, out ModCatalogSourceKind sourceKind)
                    ? sourceKind
                    : ModCatalogSourceKind.OfficialModsList;
            }
            set {
                Properties.Settings.Default.ModCatalogSourceKind = value.ToString();
                Properties.Settings.Default.Save();
            }
        }
    }
}
