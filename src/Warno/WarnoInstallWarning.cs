using System.Diagnostics;
using System.Linq;

namespace YSMInstaller {
    public static class WarnoInstallWarning {
        public static bool IsGameRunning => Process.GetProcessesByName("WARNO").Any();

        public static string BuildMessage() {
            const string compatibilityWarning =
                "YSM Installer will disable all other mods for compatibility.\r\n\r\n";

            return IsGameRunning
                ? "WARNO is currently running and will be closed.\r\n\r\n" + compatibilityWarning
                : compatibilityWarning;
        }
    }
}
