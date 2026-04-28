using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace YSMInstaller {
    public static class WarnoFinder {
        private const string WarnoSteamAppId = "1611600";

        public static async Task<List<WarnoExecutable>> FindExecutablesAsync(bool includeSystemFolders) {
            return await Task.Run(() => FindExecutables(includeSystemFolders));
        }

        private static List<WarnoExecutable> FindExecutables(bool includeSystemFolders) {
            var foundExecutables = new ConcurrentDictionary<string, WarnoExecutable>(StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(GetLikelyExecutableCandidates(), candidate => AddExecutableIfValid(candidate, foundExecutables));

            if (foundExecutables.Count > 0 || !includeSystemFolders) {
                return foundExecutables.Values
                    .OrderBy(executable => executable.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            Parallel.ForEach(GetSearchableDrives(), drive => {
                foreach (WarnoExecutable executable in SearchDrive(drive, includeSystemFolders)) {
                    AddExecutableIfValid(executable, foundExecutables);
                }
            });

            return foundExecutables.Values
                .OrderBy(executable => executable.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<WarnoExecutable> GetLikelyExecutableCandidates() {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string steamPath in GetSteamInstallPaths()) {
                AddSteamLibrary(steamPath, libraries);

                string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                foreach (string libraryPath in ReadSteamLibraryFolders(libraryFoldersPath)) {
                    AddSteamLibrary(libraryPath, libraries);
                }
            }

            foreach (DriveInfo drive in GetSearchableDrives()) {
                string root = drive.RootDirectory.FullName;
                AddSteamLibrary(Path.Combine(root, "Steam"), libraries);
                AddSteamLibrary(Path.Combine(root, "SteamLibrary"), libraries);
                AddSteamLibrary(Path.Combine(root, "Games", "Steam"), libraries);
                AddSteamLibrary(Path.Combine(root, "Games", "SteamLibrary"), libraries);
            }

            foreach (string libraryPath in libraries) {
                string manifestPath = Path.Combine(libraryPath, "steamapps", $"appmanifest_{WarnoSteamAppId}.acf");
                string? installDir = ReadSteamManifestValue(manifestPath, "installdir");

                if (!string.IsNullOrWhiteSpace(installDir)) {
                    yield return new WarnoExecutable(
                        Path.Combine(libraryPath, "steamapps", "common", installDir!, "Warno.exe"),
                        WarnoExecutableSources.Steam
                    );
                }

                yield return new WarnoExecutable(
                    Path.Combine(libraryPath, "steamapps", "common", "WARNO", "Warno.exe"),
                    WarnoExecutableSources.Steam
                );
            }
        }

        private static IEnumerable<string> GetSteamInstallPaths() {
            string[] registryPaths = {
                @"SOFTWARE\Valve\Steam",
                @"SOFTWARE\WOW6432Node\Valve\Steam"
            };

            foreach (RegistryKey rootKey in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                foreach (string registryPath in registryPaths) {
                    string? installPath = ReadRegistryString(rootKey, registryPath, "SteamPath") ??
                                          ReadRegistryString(rootKey, registryPath, "InstallPath");

                    if (!string.IsNullOrWhiteSpace(installPath)) {
                        yield return installPath!.Replace('/', Path.DirectorySeparatorChar);
                    }
                }
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86)) {
                yield return Path.Combine(programFilesX86, "Steam");
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles)) {
                yield return Path.Combine(programFiles, "Steam");
            }
        }

        private static string? ReadRegistryString(RegistryKey rootKey, string subKeyName, string valueName) {
            try {
                using (RegistryKey? key = rootKey.OpenSubKey(subKeyName)) {
                    return Convert.ToString(key?.GetValue(valueName));
                }
            }
            catch {
                return null;
            }
        }

        private static IEnumerable<string> ReadSteamLibraryFolders(string libraryFoldersPath) {
            if (!File.Exists(libraryFoldersPath)) {
                yield break;
            }

            foreach (string line in File.ReadLines(libraryFoldersPath)) {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase) &&
                    !LooksLikeLegacyLibraryEntry(trimmed)) {
                    continue;
                }

                string? path = ExtractLastQuotedValue(trimmed);
                if (!string.IsNullOrWhiteSpace(path)) {
                    yield return path!.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
                }
            }
        }

        private static bool LooksLikeLegacyLibraryEntry(string value) {
            int firstQuote = value.IndexOf('"');
            int secondQuote = firstQuote >= 0 ? value.IndexOf('"', firstQuote + 1) : -1;

            if (firstQuote != 0 || secondQuote <= firstQuote + 1) {
                return false;
            }

            string key = value.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            return int.TryParse(key, out _);
        }

        private static string? ExtractLastQuotedValue(string value) {
            int end = value.LastIndexOf('"');
            if (end <= 0) {
                return null;
            }

            int start = value.LastIndexOf('"', end - 1);
            if (start < 0 || start == end - 1) {
                return null;
            }

            return value.Substring(start + 1, end - start - 1);
        }

        private static string? ReadSteamManifestValue(string manifestPath, string key) {
            if (!File.Exists(manifestPath)) {
                return null;
            }

            try {
                foreach (string line in File.ReadLines(manifestPath)) {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith($"\"{key}\"", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    return ExtractLastQuotedValue(trimmed);
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to read Steam manifest: {manifestPath}", exception);
            }

            return null;
        }

        private static void AddSteamLibrary(string libraryPath, HashSet<string> libraries) {
            if (string.IsNullOrWhiteSpace(libraryPath)) {
                return;
            }

            try {
                string fullPath = Path.GetFullPath(libraryPath);
                if (Directory.Exists(Path.Combine(fullPath, "steamapps"))) {
                    libraries.Add(fullPath);
                }
            }
            catch {
            }
        }

        private static IEnumerable<DriveInfo> GetSearchableDrives() {
            foreach (DriveInfo drive in DriveInfo.GetDrives()) {
                if (!drive.IsReady ||
                    (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)) {
                    continue;
                }

                yield return drive;
            }
        }

        private static List<WarnoExecutable> SearchDrive(DriveInfo drive, bool includeSystemFolders) {
            var foundExecutables = new List<WarnoExecutable>();

            try {
                if (!drive.IsReady) {
                    return foundExecutables;
                }

                var directories = new Stack<string>();
                directories.Push(drive.RootDirectory.FullName);
                int inspectedDirectories = 0;

                while (directories.Count > 0) {
                    string currentDirectory = directories.Pop();
                    try {
                        if (ShouldSkipDirectory(drive, currentDirectory, includeSystemFolders)) {
                            continue;
                        }

                        if (IsReparsePoint(currentDirectory)) {
                            continue;
                        }

                        string candidatePath = Path.Combine(currentDirectory, "Warno.exe");
                        if (File.Exists(candidatePath)) {
                            foundExecutables.Add(new WarnoExecutable(candidatePath, GetSourceLabel(candidatePath)));
                        }

                        foreach (string subDirectory in Directory.EnumerateDirectories(currentDirectory)) {
                            directories.Push(subDirectory);
                        }

                        inspectedDirectories++;
                        if (inspectedDirectories % 500 == 0) {
                            Thread.Yield();
                        }
                    }
                    catch (UnauthorizedAccessException) {
                    }
                    catch (IOException) {
                    }
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to scan drive {drive.Name}.", exception);
            }

            return foundExecutables;
        }

        private static void AddExecutableIfValid(WarnoExecutable executable, ConcurrentDictionary<string, WarnoExecutable> foundExecutables) {
            try {
                if (!File.Exists(executable.Path)) {
                    return;
                }

                string directory = Path.GetDirectoryName(executable.Path) ?? string.Empty;
                if (!File.Exists(Path.Combine(directory, "Glad.dat"))) {
                    foundExecutables.TryAdd(executable.Path, executable);
                }
            }
            catch {
            }
        }

        private static string GetSourceLabel(string executablePath) {
            return executablePath.IndexOf(
                Path.Combine("steamapps", "common"),
                StringComparison.OrdinalIgnoreCase
            ) >= 0
                ? WarnoExecutableSources.Steam
                : string.Empty;
        }

        private static bool ShouldSkipDirectory(DriveInfo drive, string path, bool includeSystemFolders) {
            return (drive.Name == "C:\\" && IsSystemFolder(path, includeSystemFolders)) ||
                   IsRestrictedFolder(path);
        }

        private static bool IsSystemFolder(string path, bool includeSystemFolders) {
            if (includeSystemFolders) {
                return false;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string usersPath = Path.Combine(Path.GetPathRoot(fullPath) ?? "C:\\", "Users");

            return IsSameOrChildPath(fullPath, windowsPath) || IsSameOrChildPath(fullPath, usersPath);
        }

        private static bool IsRestrictedFolder(string path) {
            string[] restrictedFolders = { "System Volume Information", "$recycle.bin" };
            return restrictedFolders.Any(folder => string.Equals(
                Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                folder,
                StringComparison.OrdinalIgnoreCase
            ));
        }

        private static bool IsReparsePoint(string path) {
            try {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch {
                return true;
            }
        }

        private static bool IsSameOrChildPath(string path, string parentPath) {
            if (string.IsNullOrWhiteSpace(parentPath)) {
                return false;
            }

            string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class WarnoExecutableSources {
        public const string Steam = "Steam";
    }

    public sealed class WarnoExecutable {
        public WarnoExecutable(string path, string sourceLabel) {
            Path = path;
            SourceLabel = sourceLabel;
        }

        public string Path { get; }
        public string SourceLabel { get; }
    }
}
