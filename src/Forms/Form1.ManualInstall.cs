using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        // Capped so a Browse on a near-root folder can't fan out into a multi-minute scan.
        private const int ManualFolderScanMaxDepth = 4;

        private async Task BrowseForManualFolderAsync() {
            string? folder = PickManualFolder();
            if (folder == null) {
                return;
            }

            if (IsDriveRoot(folder)) {
                UserMessages.ShowError(
                    this,
                    "Pick a mod folder",
                    "You selected a drive root. Pick the mod folder (the one containing Config.ini) or a parent folder that holds it."
                );
                return;
            }

            // Probe off the UI thread — deep folder trees or slow disks would otherwise freeze input.
            (string? configPath, string? scanError, Dictionary<string, string>? config, string? readError) =
                await Task.Run(() => ProbeManualFolder(folder));

            if (scanError != null) {
                UserMessages.ShowError(this, "Invalid mod folder", scanError);
                return;
            }
            if (readError != null) {
                UserMessages.ShowError(this, "Invalid mod folder", readError);
                return;
            }

            if (!HasRequiredConfigKeys(config!, out string? keyError)) {
                UserMessages.ShowError(this, "Invalid mod folder", keyError!);
                return;
            }

            string modRoot = Path.GetDirectoryName(configPath!) ?? folder;
            await StartManualInstallAsync(modRoot, archive: null, config!);
        }

        private static (string? configPath, string? scanError, Dictionary<string, string>? config, string? readError)
            ProbeManualFolder(string folder) {
            (string? configPath, string? scanError) = LocateConfigInFolder(folder);
            if (scanError != null) {
                return (null, scanError, null, null);
            }
            try {
                Dictionary<string, string> config = IniFile.ReadValues(configPath!);
                return (configPath, null, config, null);
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to read Config.ini at '{configPath}'.", exception);
                return (configPath, null, null, $"Could not read Config.ini: {exception.Message}");
            }
        }

        private async Task BrowseForManualArchiveAsync() {
            string? archivePath = PickManualArchive();
            if (archivePath == null) {
                return;
            }

            // Peek into the zip off the UI thread — opening a large archive can stall repaint.
            (Dictionary<string, string>? config, string? peekError) =
                await Task.Run(() => ProbeManualArchive(archivePath));

            if (peekError != null) {
                UserMessages.ShowError(this, "Invalid mod archive", peekError);
                return;
            }

            if (!HasRequiredConfigKeys(config!, out string? keyError)) {
                UserMessages.ShowError(this, "Invalid mod archive", keyError!);
                return;
            }

            await StartManualInstallAsync(folder: null, archive: archivePath, config!);
        }

        private static (Dictionary<string, string>? config, string? error) ProbeManualArchive(string archivePath) {
            try {
                Dictionary<string, string> config = ReadConfigFromArchive(archivePath, out string? peekError);
                if (peekError != null) {
                    return ((Dictionary<string, string>?)null, peekError);
                }
                return (config, (string?)null);
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to peek manual archive '{archivePath}'.", exception);
                return ((Dictionary<string, string>?)null, $"Could not read the archive: {exception.Message}");
            }
        }

        private async Task StartManualInstallAsync(string? folder, string? archive, Dictionary<string, string> config) {
            // Safe to !-deref: HasRequiredConfigKeys guarantees ModGenVersion parses.
            int modGenVersion = TryParseInt(config, "ModGenVersion")!.Value;
            string? displayName = config.TryGetValue("Name", out string name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : null;

            var manualMetadata = new ModMetadata {
                ModType = ModTypes.Manual,
                GameVersion = modGenVersion,
                LocalSourceFolder = folder,
                LocalSourceArchive = archive,
                DisplayNameOverride = displayName,
            };
            await StartInstallAsync(manualMetadata, _chooseVersion);
        }

        private static (string? path, string? error) LocateConfigInFolder(string folder) {
            if (!Directory.Exists(folder)) {
                return (null, $"Folder does not exist:\n{folder}");
            }
            List<string> matches;
            try {
                matches = FindConfigFilesBounded(folder, ManualFolderScanMaxDepth);
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to scan manual mod folder '{folder}'.", exception);
                return (null, $"Could not scan the folder: {exception.Message}");
            }
            if (matches.Count == 0) {
                return (null, $"No Config.ini found within {ManualFolderScanMaxDepth} folders of the selected location.");
            }
            if (matches.Count > 1) {
                return (null, "Multiple Config.ini files found — pick a folder that contains exactly one mod.");
            }
            return (matches[0], null);
        }

        // Per-directory IO errors are skipped rather than aborting — a single locked subfolder
        // shouldn't fail the lookup.
        private static List<string> FindConfigFilesBounded(string root, int maxDepth) {
            var results = new List<string>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0) {
                (string current, int depth) = queue.Dequeue();
                try {
                    foreach (string file in Directory.GetFiles(current, "Config.ini")) {
                        results.Add(file);
                    }
                }
                catch {
                    // Permission/IO — skip this directory.
                }
                if (depth >= maxDepth) {
                    continue;
                }
                try {
                    foreach (string sub in Directory.GetDirectories(current)) {
                        queue.Enqueue((sub, depth + 1));
                    }
                }
                catch {
                    // Permission/IO — skip this branch.
                }
            }
            return results;
        }

        private static bool IsDriveRoot(string folder) {
            try {
                return new DirectoryInfo(folder).Parent == null;
            }
            catch {
                return false;
            }
        }

        private static Dictionary<string, string> ReadConfigFromArchive(string archivePath, out string? error) {
            byte[]? bytes;
            try {
                bytes = SafeArchiveExtractor.ReadEntryBytes(archivePath, "Config.ini");
            }
            catch (MultipleArchiveEntriesException) {
                error = "Archive contains multiple Config.ini files — only single-mod archives are supported.";
                return new Dictionary<string, string>();
            }

            if (bytes == null) {
                error = "Archive does not contain a Config.ini file.";
                return new Dictionary<string, string>();
            }

            error = null;
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream)) {
                return IniFile.ReadValues(reader);
            }
        }

        // ModGenVersion must parse: if we fell back to _chooseVersion silently, an incompatible mod
        // would slip past the version-mismatch gate and install as if it matched.
        private static bool HasRequiredConfigKeys(Dictionary<string, string> config, out string? error) {
            if (!config.ContainsKey("DeckFormatVersion") || !config.ContainsKey("Name")) {
                error = "Config.ini is missing required keys (DeckFormatVersion, Name).";
                return false;
            }
            if (TryParseInt(config, "ModGenVersion") == null) {
                error = "Config.ini is missing a valid ModGenVersion (required to verify WARNO compatibility).";
                return false;
            }
            error = null;
            return true;
        }

        private static int? TryParseInt(Dictionary<string, string> values, string key) {
            return values.TryGetValue(key, out string raw)
                && int.TryParse(
                    raw.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int parsed
                )
                ? parsed
                : (int?)null;
        }

        private string? PickManualFolder() {
            using (var dialog = new FolderBrowserDialog()) {
                dialog.Description = "Select the mod folder (or any parent that contains it).";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    return null;
                }
                return dialog.SelectedPath;
            }
        }

        private string? PickManualArchive() {
            using (var dialog = new OpenFileDialog()) {
                dialog.Title = "Select the mod archive";
                dialog.Filter = ModArchiveFormats.OpenFileDialogFilter + "|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    return null;
                }
                return dialog.FileName;
            }
        }
    }
}
