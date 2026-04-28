using System;
using System.IO;
using System.IO.Compression;

namespace YSMInstaller {
    public static class SafeZipExtractor {
        public static void ExtractToDirectory(string archivePath, string destinationDirectory) {
            string fullDestinationPath = Path.GetFullPath(destinationDirectory);

            using (var archive = ZipFile.OpenRead(archivePath)) {
                foreach (var entry in archive.Entries) {
                    string entryPath = Path.GetFullPath(Path.Combine(fullDestinationPath, entry.FullName));

                    if (!entryPath.StartsWith(fullDestinationPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(entryPath, fullDestinationPath, StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidDataException($"Archive entry points outside destination: {entry.FullName}");
                    }

                    if (string.IsNullOrEmpty(entry.Name)) {
                        Directory.CreateDirectory(entryPath);
                        continue;
                    }

                    string entryDirectory = Path.GetDirectoryName(entryPath)
                        ?? throw new InvalidDataException($"Archive entry has an invalid path: {entry.FullName}");
                    Directory.CreateDirectory(entryDirectory);
                    entry.ExtractToFile(entryPath, true);
                }
            }
        }
    }
}
