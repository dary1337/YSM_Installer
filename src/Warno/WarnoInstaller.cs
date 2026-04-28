using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class WarnoInstaller {
        private static bool _isInstalling;

        public static async Task<InstallModResult> InstallAsync(ModMetadata modMetadata, IProgress<int>? progress = null) {
            if (_isInstalling) {
                AppLogger.Info("Install request ignored because another installation is already running.");
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

            try {
                AppLogger.Info($"Starting mod installation. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}.");
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

                string modConfig = Path.Combine(tempModPath, "Config.ini");
                Dictionary<string, string> ysmConfig = IniFile.ReadValues(modConfig);
                Dictionary<string, string> gameConfigData = IniFile.ReadValues(gameConfig);

                string deckFormatVersion = IniFile.GetRequiredValue(ysmConfig, "DeckFormatVersion", modConfig);
                string manualVersion = $"{ModTypes.ToDisplayName(modMetadata.ModType)} (v{deckFormatVersion}) (Installer)";
                string finalModPath = Path.Combine(modFolder, manualVersion);

                CloseRunningGame();
                File.Delete(lockFile);

                AppLogger.Info("Removing previously installed YSM mods.");
                RemoveInstalledYsmMods(modFolder);
                DeleteDirectoryIfExists(finalModPath);

                gameConfigData["ActivatedMods"] = $"{manualVersion}|";
                ysmConfig["ID"] = manualVersion;
                ysmConfig["Name"] = manualVersion;

                IniFile.WriteValues(gameConfig, gameConfigData);
                IniFile.WriteValues(modConfig, ysmConfig);

                Directory.Move(tempModPath, finalModPath);
                AppLogger.Info($"Mod installation completed. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}.");
                return InstallModResult.Installed;
            }
            finally {
                DeleteFileIfExists(modArchivePath);
                DeleteDirectoryIfExists(tempModPath);
                _isInstalling = false;
            }
        }

        private static void CloseRunningGame() {
            foreach (var process in Process.GetProcessesByName("WARNO")) {
                try {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception exception) {
                    throw new InvalidOperationException($"Failed to close WARNO process {process.Id}.", exception);
                }
                finally {
                    process.Dispose();
                }
            }
        }

        private static void RemoveInstalledYsmMods(string modFolder) {
            foreach (string directory in Directory.GetDirectories(modFolder)) {
                string configPath = Path.Combine(directory, "Config.ini");
                if (!File.Exists(configPath)) {
                    continue;
                }

                try {
                    Dictionary<string, string> modConfig = IniFile.ReadValues(configPath);

                    if (modConfig.TryGetValue("Name", out string name) &&
                        name.StartsWith("YSM", StringComparison.OrdinalIgnoreCase)) {
                        Directory.Delete(directory, true);
                    }
                }
                catch (Exception exception) {
                    AppLogger.Error($"Failed to inspect installed mod: {directory}", exception);
                }
            }
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
    }
}
