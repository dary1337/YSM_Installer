using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class WarnoInstaller {
        private static bool _isInstalling;

        public static async Task<InstallModResult> InstallAsync(
            ModMetadata modMetadata,
            IProgress<int>? progress = null
        ) {
            if (DevWarnoMocks.IsEnabled) {
                return await SimulateInstallAsync(modMetadata, progress);
            }

            if (_isInstalling) {
                AppLogger.Info(
                    "Install request ignored because another installation is already running."
                );
                return InstallModResult.AlreadyRunning;
            }

            _isInstalling = true;

            string warnoSavedGames = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games",
                "EugenSystems",
                "WARNO"
            );
            string modFolder = Path.Combine(warnoSavedGames, "mod");
            string lockFile = Path.Combine(warnoSavedGames, "EugGame.lock");
            string gameConfig = Path.Combine(modFolder, "Config.ini");
            string modArchivePath = Path.Combine(modFolder, $"YSM_{modMetadata.GameVersion}.zip");
            string tempModPath = Path.Combine(modFolder, $"YSM_Temp_{Guid.NewGuid():N}");
            string gameConfigBackupPath = Path.Combine(
                modFolder,
                $"Config.backup.{Guid.NewGuid():N}.ini"
            );
            string previousModsBackupPath = Path.Combine(
                modFolder,
                $"YSM_Previous_{Guid.NewGuid():N}"
            );
            var previousModBackups = new List<DirectoryBackup>();
            string finalModPath = string.Empty;
            bool finalModCreated = false;
            bool installationSucceeded = false;

            try {
                AppLogger.Info(
                    $"Starting mod installation. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );
                Directory.CreateDirectory(modFolder);

                AppLogger.Info("Downloading mod archive.");
                await HttpService.DownloadFileAsync(
                    modMetadata.DownloadUrl,
                    modArchivePath,
                    progress
                );

                Directory.CreateDirectory(tempModPath);
                AppLogger.Info("Extracting mod archive.");
                SafeZipExtractor.ExtractToDirectory(modArchivePath, tempModPath);
                string extractedModPath = ResolveExtractedModPath(tempModPath);
                AppLogger.Info($"Resolved extracted mod root: {extractedModPath}");

                EnsureGameModConfigExists(gameConfig);

                string modConfig = Path.Combine(extractedModPath, "Config.ini");
                Dictionary<string, string> ysmConfig = IniFile.ReadValues(modConfig);
                Dictionary<string, string> gameConfigData = IniFile.ReadValues(gameConfig);

                string deckFormatVersion = IniFile.GetRequiredValue(
                    ysmConfig,
                    "DeckFormatVersion",
                    modConfig
                );
                string manualVersion =
                    $"{ModTypes.ToDisplayName(modMetadata.ModType)} (v{deckFormatVersion}) (Installer)";
                finalModPath = Path.Combine(modFolder, manualVersion);

                ysmConfig["ID"] = manualVersion;
                ysmConfig["Name"] = manualVersion;
                IniFile.WriteValues(modConfig, ysmConfig);

                CloseRunningGame();
                File.Delete(lockFile);

                AppLogger.Info("Backing up current WARNO mod configuration.");
                File.Copy(gameConfig, gameConfigBackupPath, true);

                AppLogger.Info("Backing up previously installed YSM mods.");
                BackupInstalledYsmMods(
                    modFolder,
                    previousModsBackupPath,
                    previousModBackups,
                    finalModPath,
                    tempModPath,
                    extractedModPath
                );

                Directory.Move(extractedModPath, finalModPath);
                finalModCreated = true;

                gameConfigData["ActivatedMods"] = $"{manualVersion}|";
                IniFile.WriteValues(gameConfig, gameConfigData);

                installationSucceeded = true;
                AppLogger.Info(
                    $"Mod installation completed. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );
                return InstallModResult.Installed;
            }
            catch {
                RollbackInstallation(
                    gameConfig,
                    gameConfigBackupPath,
                    previousModBackups,
                    previousModsBackupPath,
                    finalModPath,
                    finalModCreated
                );
                throw;
            }
            finally {
                DeleteFileIfExists(modArchivePath);
                DeleteDirectoryIfExists(tempModPath);
                if (installationSucceeded) {
                    DeleteFileIfExists(gameConfigBackupPath);
                    DeleteDirectoryIfExists(previousModsBackupPath);
                }
                _isInstalling = false;
            }
        }

        private static async Task<InstallModResult> SimulateInstallAsync(
            ModMetadata modMetadata,
            IProgress<int>? progress
        ) {
            if (_isInstalling) {
                AppLogger.Info(
                    "Mock install request ignored because another installation is already running."
                );
                return InstallModResult.AlreadyRunning;
            }

            _isInstalling = true;
            try {
                AppLogger.Info(
                    $"Starting mock mod installation. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );

                int[] checkpoints = { 5, 12, 20, 30, 45, 60, 75, 88, 96, 100 };
                foreach (int value in checkpoints) {
                    progress?.Report(value);
                    await Task.Delay(90);
                }

                AppLogger.Info(
                    $"Mock mod installation completed. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );
                return InstallModResult.Installed;
            }
            finally {
                _isInstalling = false;
            }
        }

        private static void EnsureGameModConfigExists(string gameConfig) {
            if (File.Exists(gameConfig)) {
                return;
            }

            AppLogger.Info("WARNO mod Config.ini is missing. Creating default mod configuration.");
            string? directory = Path.GetDirectoryName(gameConfig);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(gameConfig, new[] { "[mod]", "ActivatedMods =" });
        }

        private static string ResolveExtractedModPath(string extractedRoot) {
            string currentPath = extractedRoot;
            const int maxDepth = 64;

            for (int depth = 0; depth < maxDepth; depth++) {
                if (File.Exists(Path.Combine(currentPath, "Config.ini"))) {
                    return currentPath;
                }

                string[] childDirectories = Directory.GetDirectories(currentPath);
                if (childDirectories.Length != 1) {
                    break;
                }

                currentPath = childDirectories[0];
            }

            string[] configMatches = Directory.GetFiles(
                extractedRoot,
                "Config.ini",
                SearchOption.AllDirectories
            );

            if (configMatches.Length == 1) {
                return Path.GetDirectoryName(configMatches[0])
                    ?? throw new InvalidOperationException(
                        "Extracted archive contains Config.ini with invalid path."
                    );
            }

            if (configMatches.Length > 1) {
                throw new InvalidOperationException(
                    "Extracted archive contains multiple Config.ini files and cannot determine mod root."
                );
            }

            throw new InvalidOperationException(
                "Extracted archive does not contain Config.ini in any folder."
            );
        }

        private static void CloseRunningGame() {
            foreach (var process in Process.GetProcessesByName("WARNO")) {
                try {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception exception) {
                    throw new InvalidOperationException(
                        $"Failed to close WARNO process {process.Id}.",
                        exception
                    );
                }
                finally {
                    process.Dispose();
                }
            }
        }

        private static void BackupInstalledYsmMods(
            string modFolder,
            string backupRoot,
            List<DirectoryBackup> backups,
            string finalModPath,
            string tempModPath,
            string extractedModPath
        ) {
            foreach (string directory in Directory.GetDirectories(modFolder)) {
                if (
                    IsSamePath(directory, backupRoot)
                    || IsSamePath(directory, tempModPath)
                    || IsSamePath(directory, extractedModPath)
                ) {
                    continue;
                }

                string configPath = Path.Combine(directory, "Config.ini");
                if (!File.Exists(configPath) && !IsSamePath(directory, finalModPath)) {
                    continue;
                }

                try {
                    bool shouldBackup = IsSamePath(directory, finalModPath);
                    if (File.Exists(configPath)) {
                        Dictionary<string, string> modConfig = IniFile.ReadValues(configPath);
                        shouldBackup =
                            shouldBackup
                            || modConfig.TryGetValue("Name", out string name)
                                && name.StartsWith("YSM", StringComparison.OrdinalIgnoreCase);
                    }

                    if (shouldBackup) {
                        BackupDirectory(directory, backupRoot, backups);
                    }
                }
                catch (Exception exception) {
                    AppLogger.Error($"Failed to inspect installed mod: {directory}", exception);
                }
            }
        }

        private static void BackupDirectory(
            string directory,
            string backupRoot,
            List<DirectoryBackup> backups
        ) {
            Directory.CreateDirectory(backupRoot);

            string backupPath = Path.Combine(
                backupRoot,
                $"{Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}_{Guid.NewGuid():N}"
            );

            Directory.Move(directory, backupPath);
            backups.Add(new DirectoryBackup(directory, backupPath));
        }

        private static void RollbackInstallation(
            string gameConfig,
            string gameConfigBackupPath,
            List<DirectoryBackup> previousModBackups,
            string previousModsBackupPath,
            string finalModPath,
            bool finalModCreated
        ) {
            try {
                if (finalModCreated && !string.IsNullOrWhiteSpace(finalModPath)) {
                    DeleteDirectoryIfExists(finalModPath);
                }

                if (File.Exists(gameConfigBackupPath)) {
                    File.Copy(gameConfigBackupPath, gameConfig, true);
                }

                for (int i = previousModBackups.Count - 1; i >= 0; i--) {
                    DirectoryBackup backup = previousModBackups[i];
                    DeleteDirectoryIfExists(backup.OriginalPath);

                    if (Directory.Exists(backup.BackupPath)) {
                        Directory.Move(backup.BackupPath, backup.OriginalPath);
                    }
                }

                DeleteFileIfExists(gameConfigBackupPath);
                DeleteDirectoryIfExists(previousModsBackupPath);
            }
            catch (Exception exception) {
                AppLogger.Critical("Failed to roll back mod installation.", exception);
            }
        }

        private static bool IsSamePath(string firstPath, string secondPath) {
            return string.Equals(
                Path.GetFullPath(firstPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(secondPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static void DeleteFileIfExists(string path) {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }

        private static void DeleteDirectoryIfExists(string path) {
            if (Directory.Exists(path)) {
                Directory.Delete(path, true);
            }
        }

        private sealed class DirectoryBackup {
            public DirectoryBackup(string originalPath, string backupPath) {
                OriginalPath = originalPath;
                BackupPath = backupPath;
            }

            public string OriginalPath { get; }
            public string BackupPath { get; }
        }
    }
}
