using System;
using System.IO;

namespace YSMInstaller {
    // Single source of truth for the archive formats the installer accepts as a mod source — used
    // by both the manual-archive picker and Yokaiste release-asset detection. Deliberately narrower
    // than what the bundled 7z.dll can decode: we only advertise the formats mods actually ship as.
    public static class ModArchiveFormats {
        public static readonly string[] Extensions = { ".zip", ".7z", ".rar" };

        // First-volume sentinels for byte-split archives. `.7z.001 + .7z.002 + ...` is a 7-Zip
        // "split" — bytes of the original archive divided into equal chunks. The native 7z layer
        // reads them as one virtual stream (see MultiVolumeStream), so no temp file is needed.
        // `.rar.001` is included for the common case of 7z-style splits of a .rar; true WinRAR
        // multi-volume rar (per-volume headers) would also land here and fail at open with a
        // 7z error, which is acceptable surface for that rare case.
        public static readonly string[] MultiPartFirstVolumeExtensions = { ".7z.001", ".zip.001", ".rar.001" };

        // OpenFileDialog.Filter, e.g. "Mod archive|*.zip;*.7z;*.rar;*.7z.001;...".
        public static string OpenFileDialogFilter {
            get {
                string[] all = new string[Extensions.Length + MultiPartFirstVolumeExtensions.Length];
                int i = 0;
                foreach (string ext in Extensions) all[i++] = "*" + ext;
                foreach (string ext in MultiPartFirstVolumeExtensions) all[i++] = "*" + ext;
                return "Mod archive|" + string.Join(";", all);
            }
        }

        public static bool HasSupportedExtension(string path) {
            foreach (string extension in Extensions) {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            foreach (string extension in MultiPartFirstVolumeExtensions) {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        // True for `<name>.001` (and only `.001`) — `.002+` are reachable only via the first volume,
        // not selectable directly. Checked by the manual picker to nudge the user toward the first
        // part when they double-click `.002` by mistake.
        public static bool IsMultiPartFirstVolume(string path) {
            foreach (string extension in MultiPartFirstVolumeExtensions) {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        // True for any `.NNN` (NNN ≥ 002) suffix on a supported inner extension — used to give a
        // clearer "pick the .001 instead" error when the user selected a continuation part.
        public static bool IsMultiPartContinuationVolume(string path) {
            string name = Path.GetFileName(path);
            int dot = name.LastIndexOf('.');
            if (dot < 0) return false;
            string suffix = name.Substring(dot + 1);
            if (suffix.Length < 2 || !int.TryParse(suffix, out int n) || n < 2) {
                return false;
            }
            // Strip the numeric suffix and verify the remainder ends in a known inner extension.
            string inner = name.Substring(0, dot);
            foreach (string ext in Extensions) {
                if (inner.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }
    }
}
