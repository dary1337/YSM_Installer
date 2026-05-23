using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace YSMInstaller {
    /// <summary>
    /// Resolves the brand icon for each YSM build variant from the embedded PNG assets. Returns
    /// null when the variant has no custom artwork (the caller falls back to the YSM app logo).
    /// </summary>
    public static class BuildIcons {
        private static readonly Dictionary<string, Image?> Cache =
            new Dictionary<string, Image?>(StringComparer.Ordinal);
        private static readonly object Gate = new object();

        public static Image? ForBuild(string modType) {
            string? key = null;
            if (modType == ModTypes.YsmWif) key = "ysm_wif";
            else if (modType == ModTypes.YsmWto) key = "ysm_wto";
            if (key == null) {
                return null;
            }

            lock (Gate) {
                if (Cache.TryGetValue(key, out Image? cached)) {
                    return cached;
                }
                Image? image = Load(key);
                Cache[key] = image;
                return image;
            }
        }

        private static Image? Load(string key) {
            try {
                Assembly assembly = typeof(BuildIcons).Assembly;
                string suffix = $".icons.{key}.png";
                string? name = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (name == null) {
                    AppLogger.Error($"Build icon resource not found: {key}");
                    return null;
                }
                using (Stream? stream = assembly.GetManifestResourceStream(name)) {
                    if (stream == null) {
                        return null;
                    }
                    // Copy to a memory stream so Image doesn't hold the resource stream open.
                    var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    memory.Position = 0;
                    return Image.FromStream(memory);
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to load build icon: {key}", exception);
                return null;
            }
        }
    }
}
