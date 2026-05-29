using System;

namespace YSMInstaller {
    // Single source of truth for the archive formats the installer accepts as a mod source — used
    // by both the manual-archive picker and Yokaiste release-asset detection. Deliberately narrower
    // than what the bundled 7z.dll can decode: we only advertise the formats mods actually ship as.
    public static class ModArchiveFormats {
        public static readonly string[] Extensions = { ".zip", ".7z", ".rar" };

        // OpenFileDialog.Filter, e.g. "Mod archive|*.zip;*.7z;*.rar".
        public static string OpenFileDialogFilter {
            get {
                string patterns = string.Join(";", Array.ConvertAll(Extensions, ext => "*" + ext));
                return $"Mod archive|{patterns}";
            }
        }

        public static bool HasSupportedExtension(string path) {
            foreach (string extension in Extensions) {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }
    }
}
