using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class WarnoScanner {

        public static async Task<List<WarnoEntry>> ScanAsync(List<string> paths, List<ModMetadata> supportedVersions) {
            var entries = new List<WarnoEntry>();

            foreach (string filePath in paths) {
                var entry = new WarnoEntry { ExePath = filePath };

                if (!Directory.Exists(entry.ModsPath) || !Directory.Exists(entry.VersionPath))
                    continue;

                var highestVersion = Directory
                    .GetDirectories(entry.VersionPath)
                    .Select(Path.GetFileName)
                    .Where(name => int.TryParse(name, out _))
                    .Select(int.Parse)
                    .DefaultIfEmpty(0)
                    .Max();

                if (highestVersion == 0)
                    continue;

                entry.Version = highestVersion;
                entry.VersionMetadata = supportedVersions.Find(x => x.GameVersion == highestVersion);
                entries.Add(entry);
            }

            return entries;
        }
    }
}
