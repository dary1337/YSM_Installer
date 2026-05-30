using System.IO;

namespace YSMInstaller {
    public static class PathFormatting {
        // Compresses long paths so they fit in a single header line without truncation: `C:\…\parent\leaf`.
        // Paths that already fit pass through unchanged.
        public static string Shorten(string path, int maxLength = 44) {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength) {
                return path;
            }
            string root = Path.GetPathRoot(path) ?? string.Empty;
            string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            return $"{root.TrimEnd('\\')}\\…\\{parent}\\{leaf}";
        }
    }
}
