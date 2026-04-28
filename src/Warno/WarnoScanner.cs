using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YSMInstaller
{
    public static class WarnoScanner
    {
        public static List<WarnoEntry> Scan(List<WarnoExecutable> executables, List<ModMetadata> supportedVersions)
        {
            var entries = new List<WarnoEntry>();

            int latestSupportedVersion = supportedVersions
                .Select(version => version.GameVersion)
                .DefaultIfEmpty(0)
                .Max();

            foreach (WarnoExecutable executable in executables)
            {
                try
                {
                    var entry = new WarnoEntry
                    {
                        ExePath = executable.Path,
                        SourceLabel = executable.SourceLabel
                    };

                    if (!Directory.Exists(entry.ModsPath) || !Directory.Exists(entry.VersionPath))
                    {
                        continue;
                    }

                    var highestVersion = Directory
                        .GetDirectories(entry.VersionPath)
                        .Select(Path.GetFileName)
                        .Where(name => int.TryParse(name, out _))
                        .Select(int.Parse)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (highestVersion == 0)
                    {
                        continue;
                    }

                    entry.Version = highestVersion;
                    entry.VersionMetadata = supportedVersions.Find(version => version.GameVersion == highestVersion);
                    entry.LatestCompatibleModVersion = entry.Version > latestSupportedVersion
                        ? latestSupportedVersion
                        : 0;
                    entries.Add(entry);
                }
                catch (Exception exception)
                {
                    AppLogger.Error($"Failed to inspect WARNO executable: {executable.Path}", exception);
                }
            }

            return entries;
        }
    }
}
