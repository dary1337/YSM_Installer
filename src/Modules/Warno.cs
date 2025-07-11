using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public static class Warno {

        public static bool searchInSystemFolders = false;
        
        private static bool isInstalling = false;

        public static int selectedVersion;




        public static async Task<List<string>> Find() {
            DriveInfo[] allDrives = DriveInfo.GetDrives();
            HashSet<string> processedDrives = new HashSet<string>();
            List<Task<List<string>>> tasks = new List<Task<List<string>>>();

            foreach (DriveInfo drive in allDrives) {
                if (drive.IsReady) {
                    string rootPath = drive.RootDirectory.FullName;
                    string volumeLabel = drive.VolumeLabel;
                    string uniqueId = rootPath + volumeLabel;

                    if (!processedDrives.Contains(uniqueId)) {
                        processedDrives.Add(uniqueId);
                        tasks.Add(Task.Run(() => SearchDrives(drive)));
                    }
                }
            }

            List<string>[] results = await Task.WhenAll(tasks);
            return results.SelectMany(r => r).ToList();
        }

        public static List<string> SearchDrives(DriveInfo drive) {
            List<string> foundPaths = new List<string>();

            try {
                if (!drive.IsReady) return foundPaths;

                Stack<string> directories = new Stack<string>();
                directories.Push(drive.RootDirectory.FullName);

                while (directories.Count > 0) {
                    string currentDir = directories.Pop();
                    try {
                        if ((drive.Name == "C:\\" && (IsSystemFolder(currentDir)) || IsRestrictedFolder(currentDir))) continue;

                        foreach (string file in Directory.GetFiles(currentDir, "warno.exe")) {
                            if (!File.Exists(Path.Combine(currentDir, "Glad.dat"))) {
                                foundPaths.Add(file);
                            }
                        }

                        foreach (string subDir in Directory.GetDirectories(currentDir)) {
                            directories.Push(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (Exception) { }


            return foundPaths;
        }

        public static bool IsSystemFolder(string path) {

            if (searchInSystemFolders) 
                return false;
            
            string[] restrictedFolders = { "Windows", "Users" };
            return restrictedFolders.Any(r => path.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsRestrictedFolder(string path) {
            string[] restrictedFolders = { "System Volume Information", "$recycle.bin" };
            return restrictedFolders.Any(r => path.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static async Task InstallMod(ModMetadata modMetadata, IProgress<int> progress = null) {

            if (isInstalling)
                return;

            isInstalling = true;

            string warnoSavedGames = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "EugenSystems", "WARNO");
            string modFolder = Path.Combine(warnoSavedGames, "mod");
            string lockFile = Path.Combine(warnoSavedGames, "EugGame.lock");
            string gameConfig = Path.Combine(warnoSavedGames, "mod", "Config.ini");

            foreach (string dir in Directory.GetDirectories(modFolder)) {
                try {
                    Dictionary<string, string> anyModConfig = ReadConfig(Path.Combine(dir, "Config.ini"));

                    if (anyModConfig["Name"].StartsWith("YSM")) {
                        Directory.Delete(dir, true);
                    }
                }
                catch { }
            }

            string modArchivePath = Path.Combine(modFolder, $"YSM_{modMetadata.GameVersion}.zip");

            try {
                await HttpService.DownloadFileAsync(
                    modMetadata.DownloadUrl,
                    modArchivePath,
                    progress
                );
            }
            catch {
                MessageBox.Show("Cant download the mod. Check your internet connection");
                return;
            }


            // byte[] modArchive = modMetadata.file;

            string modPath = Path.Combine(modFolder, $"YSM_Temp_{modMetadata.GameVersion}");
            string modConfig = Path.Combine(modPath, "Config.ini");

            try {

                Process.GetProcessesByName("WARNO").AsParallel().ForAll(p => p.Kill());

                File.Delete(lockFile);
            }
            catch { }

            try {
                Directory.Delete(modPath, true);
            }
            catch { }

            Directory.CreateDirectory(modPath);

            //File.WriteAllBytes(modArchivePath, modArchive);
            ZipFile.ExtractToDirectory(modArchivePath, modPath);

            File.Delete(modArchivePath);

            Dictionary<string, string> ysmConfig = ReadConfig(modConfig);
            Dictionary<string, string> gameConfigData = ReadConfig(gameConfig);

            string manualVersion = $"{(modMetadata.ModType == "ysm" ? "YSM" : "YSM x WiF")} (v{ysmConfig["DeckFormatVersion"]}) (Installer)";

            gameConfigData["ActivatedMods"] = $"{manualVersion}|";
            ysmConfig["ID"] = manualVersion;
            ysmConfig["Name"] = manualVersion;

            WriteConfig(gameConfig, gameConfigData);
            WriteConfig(modConfig, ysmConfig);

            Directory.Move(modPath, Path.Combine(modFolder, manualVersion));

            isInstalling = false;
        }

        static Dictionary<string, string> ReadConfig(string path) {
            var config = new Dictionary<string, string>();

            foreach (var line in File.ReadLines(path)) {
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("["))
                    continue;

                var parts = trimmed.Split('=');
                if (parts.Length == 2) {
                    string key = parts[0].Trim();
                    string value = parts[1].Split(';')[0].Trim();

                    config[key] = value;
                }
            }
            return config;
        }

        static void WriteConfig(string path, Dictionary<string, string> configData) {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("["))
                    continue;

                var parts = trimmed.Split('=');
                if (parts.Length == 2) {
                    string key = parts[0].Trim();

                    if (configData.ContainsKey(key)) {
                        string comment = lines[i].Contains(";") ? lines[i].Substring(lines[i].IndexOf(";")) : "";
                        lines[i] = $"{key} = {configData[key]} {comment}";
                    }
                }
            }

            File.WriteAllLines(path, lines);
        }

    }
}
