using System;
using System.Diagnostics;
using System.IO;

namespace YSMInstaller {
    public static class ShellOpen {
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
                AppLogger.Error($"Failed to open path: {path}", exception);
            }
        }
    }
}
