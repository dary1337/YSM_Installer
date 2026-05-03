using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller
{
    public static class WarnoFinder
    {
        private const int MaxParallelDriveBranches = 2;
        private const string WarnoSteamAppId = "1611600";

        public static async Task<List<WarnoExecutable>> FindExecutablesAsync(bool includeSystemFolders)
        {
            return await Task.Run(() => FindExecutables(includeSystemFolders));
        }

        private static List<WarnoExecutable> FindExecutables(bool includeSystemFolders)
        {
            var foundExecutables = new ConcurrentDictionary<string, WarnoExecutable>(StringComparer.OrdinalIgnoreCase);

            AddExecutableCandidates(GetCachedExecutableCandidates(), foundExecutables);
            List<WarnoExecutable> cachedResults = ToSortedResults(foundExecutables);
            if (cachedResults.Count > 0)
            {
                SaveLastWarnoExecutablePath(cachedResults);
                return cachedResults;
            }

            AddExecutableCandidates(GetSteamExecutableCandidates(), foundExecutables);
            List<WarnoExecutable> steamResults = ToSortedResults(foundExecutables);
            if (steamResults.Count > 0)
            {
                SaveLastWarnoExecutablePath(steamResults);
                return steamResults;
            }

            AddExecutableCandidates(GetCommonFolderCandidates(), foundExecutables);
            List<WarnoExecutable> commonFolderResults = ToSortedResults(foundExecutables);
            if (commonFolderResults.Count > 0)
            {
                SaveLastWarnoExecutablePath(commonFolderResults);
                return commonFolderResults;
            }

            AddExecutableCandidates(GetUninstallRegistryCandidates(), foundExecutables);
            List<WarnoExecutable> registryResults = ToSortedResults(foundExecutables);
            if (registryResults.Count > 0)
            {
                SaveLastWarnoExecutablePath(registryResults);
                return registryResults;
            }

            AddExecutableCandidates(GetShortcutCandidates(), foundExecutables);
            List<WarnoExecutable> shortcutResults = ToSortedResults(foundExecutables);
            if (shortcutResults.Count > 0 || !includeSystemFolders)
            {
                SaveLastWarnoExecutablePath(shortcutResults);
                return shortcutResults;
            }

            foreach (DriveInfo drive in GetSearchableDrives())
            {
                foreach (WarnoExecutable executable in SearchDrive(drive, includeSystemFolders))
                {
                    AddExecutableIfValid(executable, foundExecutables);
                }
            }

            var deepScanResults = ToSortedResults(foundExecutables);
            SaveLastWarnoExecutablePath(deepScanResults);
            return deepScanResults;
        }

        private static void AddExecutableCandidates(
            IEnumerable<WarnoExecutable> candidates,
            ConcurrentDictionary<string, WarnoExecutable> foundExecutables
        )
        {
            foreach (WarnoExecutable candidate in candidates)
            {
                AddExecutableIfValid(candidate, foundExecutables);
            }
        }

        private static List<WarnoExecutable> ToSortedResults(ConcurrentDictionary<string, WarnoExecutable> foundExecutables)
        {
            return foundExecutables.Values
                .OrderBy(executable => executable.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<WarnoExecutable> GetCachedExecutableCandidates()
        {
            string cachedPath = Properties.Settings.Default.LastWarnoExecutablePath;
            if (string.IsNullOrWhiteSpace(cachedPath))
            {
                yield break;
            }

            yield return new WarnoExecutable(cachedPath, GetSourceLabel(cachedPath));
        }

        private static IEnumerable<WarnoExecutable> GetSteamExecutableCandidates()
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string steamPath in GetSteamInstallPaths())
            {
                AddSteamLibrary(steamPath, libraries);

                string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                foreach (string libraryPath in ReadSteamLibraryFolders(libraryFoldersPath))
                {
                    AddSteamLibrary(libraryPath, libraries);
                }
            }

            foreach (DriveInfo drive in GetSearchableDrives())
            {
                string root = drive.RootDirectory.FullName;
                AddSteamLibrary(Path.Combine(root, "Steam"), libraries);
                AddSteamLibrary(Path.Combine(root, "SteamLibrary"), libraries);
                AddSteamLibrary(Path.Combine(root, "Games", "Steam"), libraries);
                AddSteamLibrary(Path.Combine(root, "Games", "SteamLibrary"), libraries);
            }

            foreach (string libraryPath in libraries)
            {
                string manifestPath = Path.Combine(libraryPath, "steamapps", $"appmanifest_{WarnoSteamAppId}.acf");
                string? installDir = ReadSteamManifestValue(manifestPath, "installdir");

                if (!string.IsNullOrWhiteSpace(installDir))
                {
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

        private static IEnumerable<WarnoExecutable> GetUninstallRegistryCandidates()
        {
            string[] uninstallPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (RegistryKey rootKey in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string uninstallPath in uninstallPaths)
                {
                    using (RegistryKey? uninstallKey = OpenRegistryKey(rootKey, uninstallPath))
                    {
                        if (uninstallKey == null)
                        {
                            continue;
                        }

                        foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            using (RegistryKey? appKey = OpenRegistryKey(uninstallKey, subKeyName))
                            {
                                if (!IsWarnoUninstallEntry(appKey))
                                {
                                    continue;
                                }

                                foreach (string path in GetExecutablePathsFromUninstallEntry(appKey!))
                                {
                                    yield return new WarnoExecutable(path, WarnoExecutableSources.Registry);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerable<string> GetExecutablePathsFromUninstallEntry(RegistryKey appKey)
        {
            string? installLocation = ReadRegistryString(appKey, "InstallLocation");
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                yield return Path.Combine(ExpandPath(installLocation!), "Warno.exe");
            }

            foreach (string valueName in new[] { "DisplayIcon", "UninstallString" })
            {
                string? commandPath = ExtractExecutablePath(ReadRegistryString(appKey, valueName));
                if (string.IsNullOrWhiteSpace(commandPath))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileName(commandPath), "Warno.exe", StringComparison.OrdinalIgnoreCase))
                {
                    yield return commandPath!;
                }

                string? directory = Path.GetDirectoryName(commandPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    yield return Path.Combine(directory!, "Warno.exe");
                }
            }
        }

        private static bool IsWarnoUninstallEntry(RegistryKey? appKey)
        {
            string? displayName = ReadRegistryString(appKey, "DisplayName");
            return !string.IsNullOrWhiteSpace(displayName) &&
                   displayName!.IndexOf("WARNO", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<WarnoExecutable> GetCommonFolderCandidates()
        {
            foreach (DriveInfo drive in GetSearchableDrives())
            {
                string root = drive.RootDirectory.FullName;

                foreach (string path in GetCommonWarnoPaths(root))
                {
                    yield return new WarnoExecutable(path, WarnoExecutableSources.CommonFolder);
                }

                foreach (string path in GetLikelyChildWarnoPaths(Path.Combine(root, "Games")))
                {
                    yield return new WarnoExecutable(path, WarnoExecutableSources.CommonFolder);
                }
            }
        }

        private static IEnumerable<string> GetCommonWarnoPaths(string root)
        {
            string[] directories = {
                "WARNO",
                Path.Combine("Games", "WARNO"),
                Path.Combine("Games", "Warno"),
                Path.Combine("Games", "Eugen Systems", "WARNO"),
                Path.Combine("Program Files", "WARNO"),
                Path.Combine("Program Files (x86)", "WARNO"),
                Path.Combine("SteamLibrary", "steamapps", "common", "WARNO")
            };

            foreach (string directory in directories)
            {
                yield return Path.Combine(root, directory, "Warno.exe");
            }
        }

        private static IEnumerable<string> GetLikelyChildWarnoPaths(string parentDirectory)
        {
            if (!Directory.Exists(parentDirectory))
            {
                yield break;
            }

            string[] likelyNames = { "warno", "eugen" };

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(parentDirectory).ToList();
            }
            catch
            {
                yield break;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (likelyNames.Any(likelyName => name.IndexOf(likelyName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return Path.Combine(child, "Warno.exe");
                }
            }
        }

        private static IEnumerable<WarnoExecutable> GetShortcutCandidates()
        {
            foreach (string shortcutDirectory in GetShortcutSearchDirectories())
            {
                if (!Directory.Exists(shortcutDirectory))
                {
                    continue;
                }

                IEnumerable<string> shortcuts;
                try
                {
                    shortcuts = GetShortcutFiles(shortcutDirectory).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (string shortcutPath in shortcuts)
                {
                    string shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
                    if (shortcutName.IndexOf("WARNO", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string? targetPath = ResolveShortcutTarget(shortcutPath);
                    if (string.Equals(Path.GetFileName(targetPath), "Warno.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new WarnoExecutable(targetPath!, WarnoExecutableSources.Shortcut);
                    }
                }
            }
        }

        private static IEnumerable<string> GetShortcutFiles(string shortcutDirectory)
        {
            foreach (string shortcutPath in Directory.EnumerateFiles(shortcutDirectory, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                yield return shortcutPath;
            }

            string programsDirectory = Path.Combine(shortcutDirectory, "Programs");
            if (!Directory.Exists(programsDirectory))
            {
                yield break;
            }

            foreach (string shortcutPath in Directory.EnumerateFiles(programsDirectory, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                yield return shortcutPath;
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(programsDirectory))
            {
                string childName = Path.GetFileName(childDirectory);
                if (childName.IndexOf("WARNO", StringComparison.OrdinalIgnoreCase) < 0 &&
                    childName.IndexOf("Eugen", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foreach (string shortcutPath in Directory.EnumerateFiles(childDirectory, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    yield return shortcutPath;
                }
            }
        }

        private static IEnumerable<string> GetShortcutSearchDirectories()
        {
            foreach (Environment.SpecialFolder folder in new[] {
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu
            })
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }

        private static IEnumerable<string> GetSteamInstallPaths()
        {
            string[] registryPaths = {
                @"SOFTWARE\Valve\Steam",
                @"SOFTWARE\WOW6432Node\Valve\Steam"
            };

            foreach (RegistryKey rootKey in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string registryPath in registryPaths)
                {
                    string? installPath = ReadRegistryString(rootKey, registryPath, "SteamPath") ??
                                          ReadRegistryString(rootKey, registryPath, "InstallPath");

                    if (!string.IsNullOrWhiteSpace(installPath))
                    {
                        yield return installPath!.Replace('/', Path.DirectorySeparatorChar);
                    }
                }
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "Steam");
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Steam");
            }
        }

        private static string? ReadRegistryString(RegistryKey rootKey, string subKeyName, string valueName)
        {
            try
            {
                using (RegistryKey? key = rootKey.OpenSubKey(subKeyName))
                {
                    return ReadRegistryString(key, valueName);
                }
            }
            catch
            {
                return null;
            }
        }

        private static RegistryKey? OpenRegistryKey(RegistryKey rootKey, string subKeyName)
        {
            try
            {
                return rootKey.OpenSubKey(subKeyName);
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadRegistryString(RegistryKey? key, string valueName)
        {
            try
            {
                return Convert.ToString(key?.GetValue(valueName));
            }
            catch
            {
                return null;
            }
        }

        private static string ExpandPath(string path)
        {
            return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        }

        private static string? ExtractExecutablePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(value!.Trim());
            if (expanded.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = expanded.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    return expanded.Substring(1, closingQuote - 1);
                }
            }

            int exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
            {
                return null;
            }

            return expanded.Substring(0, exeIndex + 4).Trim().Trim('"');
        }

        private static IEnumerable<string> ReadSteamLibraryFolders(string libraryFoldersPath)
        {
            if (!File.Exists(libraryFoldersPath))
            {
                yield break;
            }

            foreach (string line in File.ReadLines(libraryFoldersPath))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase) &&
                    !LooksLikeLegacyLibraryEntry(trimmed))
                {
                    continue;
                }

                string? path = ExtractLastQuotedValue(trimmed);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path!.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
                }
            }
        }

        private static bool LooksLikeLegacyLibraryEntry(string value)
        {
            int firstQuote = value.IndexOf('"');
            int secondQuote = firstQuote >= 0 ? value.IndexOf('"', firstQuote + 1) : -1;

            if (firstQuote != 0 || secondQuote <= firstQuote + 1)
            {
                return false;
            }

            string key = value.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            return int.TryParse(key, out _);
        }

        private static string? ExtractLastQuotedValue(string value)
        {
            int end = value.LastIndexOf('"');
            if (end <= 0)
            {
                return null;
            }

            int start = value.LastIndexOf('"', end - 1);
            if (start < 0 || start == end - 1)
            {
                return null;
            }

            return value.Substring(start + 1, end - start - 1);
        }

        private static string? ReadSteamManifestValue(string manifestPath, string key)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                foreach (string line in File.ReadLines(manifestPath))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith($"\"{key}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return ExtractLastQuotedValue(trimmed);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Error($"Failed to read Steam manifest: {manifestPath}", exception);
            }

            return null;
        }

        private static void AddSteamLibrary(string libraryPath, HashSet<string> libraries)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(libraryPath);
                if (Directory.Exists(Path.Combine(fullPath, "steamapps")))
                {
                    libraries.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static IEnumerable<DriveInfo> GetSearchableDrives()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady ||
                    (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                {
                    continue;
                }

                yield return drive;
            }
        }

        private static List<WarnoExecutable> SearchDrive(DriveInfo drive, bool includeSystemFolders)
        {
            var foundExecutables = new List<WarnoExecutable>();

            try
            {
                if (!drive.IsReady)
                {
                    return foundExecutables;
                }

                string rootCandidatePath = Path.Combine(drive.RootDirectory.FullName, "Warno.exe");
                if (File.Exists(rootCandidatePath))
                {
                    foundExecutables.Add(new WarnoExecutable(rootCandidatePath, GetSourceLabel(rootCandidatePath)));
                }

                var searchRoots = GetDriveSearchRoots(drive, includeSystemFolders);
                var searchOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelDriveBranches
                };

                Parallel.ForEach(searchRoots, searchOptions, searchRoot =>
                {
                    foreach (WarnoExecutable executable in SearchDirectoryTree(drive, searchRoot, includeSystemFolders))
                    {
                        lock (foundExecutables)
                        {
                            foundExecutables.Add(executable);
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                AppLogger.Error($"Failed to scan drive {drive.Name}.", exception);
            }

            return foundExecutables;
        }

        private static List<string> GetDriveSearchRoots(DriveInfo drive, bool includeSystemFolders)
        {
            var searchRoots = new List<string>();
            string rootPath = drive.RootDirectory.FullName;

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(rootPath))
                {
                    if (!ShouldSkipDirectory(drive, directory, includeSystemFolders) && !IsReparsePoint(directory))
                    {
                        searchRoots.Add(directory);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            return searchRoots;
        }

        private static List<WarnoExecutable> SearchDirectoryTree(DriveInfo drive, string rootPath, bool includeSystemFolders)
        {
            var foundExecutables = new List<WarnoExecutable>();
            var directories = new Stack<string>();
            directories.Push(rootPath);
            int inspectedDirectories = 0;

            while (directories.Count > 0)
            {
                string currentDirectory = directories.Pop();
                try
                {
                    if (ShouldSkipDirectory(drive, currentDirectory, includeSystemFolders))
                    {
                        continue;
                    }

                    if (IsReparsePoint(currentDirectory))
                    {
                        continue;
                    }

                    string candidatePath = Path.Combine(currentDirectory, "Warno.exe");
                    if (File.Exists(candidatePath))
                    {
                        foundExecutables.Add(new WarnoExecutable(candidatePath, GetSourceLabel(candidatePath)));
                    }

                    foreach (string subDirectory in Directory.EnumerateDirectories(currentDirectory))
                    {
                        directories.Push(subDirectory);
                    }

                    inspectedDirectories++;
                    if (inspectedDirectories % 500 == 0)
                    {
                        Thread.Yield();
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return foundExecutables;
        }

        private static bool AddExecutableIfValid(WarnoExecutable executable, ConcurrentDictionary<string, WarnoExecutable> foundExecutables)
        {
            try
            {
                if (!File.Exists(executable.Path))
                {
                    return false;
                }

                string directory = Path.GetDirectoryName(executable.Path) ?? string.Empty;
                if (!File.Exists(Path.Combine(directory, "Glad.dat")))
                {
                    return foundExecutables.TryAdd(executable.Path, executable);
                }
            }
            catch
            {
            }

            return false;
        }

        private static void SaveLastWarnoExecutablePath(List<WarnoExecutable> executables)
        {
            string path = executables.FirstOrDefault()?.Path ?? string.Empty;
            SaveLastWarnoExecutablePath(path);
        }

        public static void SaveLastWarnoExecutablePath(string path)
        {
            if (string.Equals(Properties.Settings.Default.LastWarnoExecutablePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Properties.Settings.Default.LastWarnoExecutablePath = path;
                Properties.Settings.Default.Save();
            }
            catch (Exception exception)
            {
                AppLogger.Error("Failed to save last WARNO executable path.", exception);
            }
        }

        private static string? ResolveShortcutTarget(string shortcutPath)
        {
            IShellLinkW? shellLink = null;
            try
            {
                shellLink = (IShellLinkW)new ShellLink();
                ((IPersistFile)shellLink).Load(shortcutPath, 0);

                var targetPath = new StringBuilder(260);
                shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, 0);
                string result = targetPath.ToString();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch (Exception exception)
            {
                AppLogger.Error($"Failed to read shortcut target: {shortcutPath}", exception);
                return null;
            }
            finally
            {
                if (shellLink != null)
                {
                    Marshal.FinalReleaseComObject(shellLink);
                }
            }
        }

        private static string GetSourceLabel(string executablePath)
        {
            return executablePath.IndexOf(
                Path.Combine("steamapps", "common"),
                StringComparison.OrdinalIgnoreCase
            ) >= 0
                ? WarnoExecutableSources.Steam
                : string.Empty;
        }

        private static bool ShouldSkipDirectory(DriveInfo drive, string path, bool includeSystemFolders)
        {
            return (drive.Name == "C:\\" && IsSystemFolder(path, includeSystemFolders)) ||
                   IsRestrictedFolder(path);
        }

        private static bool IsSystemFolder(string path, bool includeSystemFolders)
        {
            if (includeSystemFolders)
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string usersPath = Path.Combine(Path.GetPathRoot(fullPath) ?? "C:\\", "Users");

            return IsSameOrChildPath(fullPath, windowsPath) || IsSameOrChildPath(fullPath, usersPath);
        }

        private static bool IsRestrictedFolder(string path)
        {
            string[] restrictedFolders = { "System Volume Information", "$recycle.bin" };
            return restrictedFolders.Any(folder => string.Equals(
                Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                folder,
                StringComparison.OrdinalIgnoreCase
            ));
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSameOrChildPath(string path, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class WarnoExecutableSources
    {
        public const string Steam = "Steam";
        public const string Registry = "Registry";
        public const string CommonFolder = "Folder";
        public const string Shortcut = "Shortcut";
        public const string Manual = "Manual";
    }

    public sealed class WarnoExecutable
    {
        public WarnoExecutable(string path, string sourceLabel)
        {
            Path = path;
            SourceLabel = sourceLabel;
        }

        public string Path { get; }
        public string SourceLabel { get; }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            IntPtr pfd,
            uint fFlags
        );

        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
