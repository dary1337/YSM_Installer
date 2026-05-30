using System;
using System.Diagnostics;
using System.IO;

namespace YSMInstaller {
    public static class ShellOpen {
        // Reveals the target in Explorer: selects the file inside its folder when it exists, opens the
        // folder when the target is a directory, falls back to the parent folder otherwise. Failures
        // (shell unavailable, target gone) are logged and swallowed so a misclick on a missing path
        // doesn't crash the UI.
        public static void RevealInExplorer(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    return;
                }
                if (File.Exists(path)) {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                else if (Directory.Exists(path)) {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else {
                    string parent = Path.GetDirectoryName(path);
                    if (Directory.Exists(parent)) {
                        Process.Start(new ProcessStartInfo(parent) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception exception) {
                // Best-effort UX action — log at Error so a missing path or shell hiccup doesn't
                // get triaged as a critical failure alongside actual install / network breakage.
                AppLogger.Error($"Failed to open path: {path}", exception);
            }
        }
    }
}
