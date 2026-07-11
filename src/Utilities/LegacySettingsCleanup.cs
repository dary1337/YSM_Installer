using System;
using System.IO;

namespace YSMInstaller {
    internal static class LegacySettingsCleanup {
        // What .NET's settings provider derived from AssemblyCompany("yuri & Yokaiste") — spaces
        // become underscores. Nothing writes there since the final release dropped user settings.
        private const string LegacyFolderName = "yuri_&_Yokaiste";

        public static void Run() {
            try {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    LegacyFolderName
                );
                if (!Directory.Exists(folder)) {
                    return;
                }

                Directory.Delete(folder, recursive: true);
                AppLogger.Info($"Removed leftover settings folder: {folder}");
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to remove the leftover settings folder.", exception);
            }
        }
    }
}
