using System;
using System.IO;

namespace YSMInstaller {
    /// <summary>The mod folder is per-user (Saved Games), not per-install — mods don't live next to Warno.exe.</summary>
    internal static class WarnoPaths {
        public static string SavedGamesRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games",
            "EugenSystems",
            "WARNO"
        );

        public static string ModFolder => Path.Combine(SavedGamesRoot, "mod");
    }
}
