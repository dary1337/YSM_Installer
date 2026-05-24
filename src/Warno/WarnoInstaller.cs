using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class WarnoInstaller {
        // Single overall progress budget — phases with byte-level reports (download, extract) map
        // their 0..100 into their slice; phases without (folder copy, post-extract steps) just emit
        // the slice start at the stage transition so the bar stays monotonic and ETA stays sane.
        private const int PercentPreparing = 0;
        private const int PercentClosing = 2;
        private const int PercentDownloadStart = 5;
        private const int PercentDownloadEnd = 50;
        private const int PercentExtractAutoStart = 50;
        private const int PercentExtractManualStart = 5;
        private const int PercentExtractEnd = 85;
        private const int PercentCopyStart = 5;
        private const int PercentReading = 85;
        private const int PercentBackingUp = 88;
        private const int PercentInstalling = 93;
        private const int PercentFinalizing = 97;
        private const int PercentDone = 100;

        private static bool _isInstalling;

        public static async Task<InstallModResult> InstallAsync(
            ModMetadata modMetadata,
            IProgress<int>? progress = null,
            IProgress<string>? stageProgress = null,
            Func<DiskSpaceWarning, Task<bool>>? lowDiskSpaceConfirm = null,
            CancellationToken cancellationToken = default
        ) {
            if (DevWarnoMocks.IsEnabled) {
                return await SimulateInstallAsync(modMetadata, progress, stageProgress, cancellationToken);
            }

            if (_isInstalling) {
                AppLogger.Info(
                    "Install request ignored because another installation is already running."
                );
                return InstallModResult.AlreadyRunning;
            }

            _isInstalling = true;

            // Manual install has no awaited I/O until late, so without this yield the install screen
            // wouldn't paint before the sync file work blocks the UI thread.
            await Task.Yield();

            string warnoSavedGames = WarnoPaths.SavedGamesRoot;
            string modFolder = WarnoPaths.ModFolder;
            string lockFile = Path.Combine(warnoSavedGames, "EugGame.lock");
            string gameConfig = Path.Combine(modFolder, "Config.ini");
            string modArchivePath = Path.Combine(
                modFolder,
                $"YSM_{modMetadata.GameVersion}{GetArchiveExtension(modMetadata.DownloadUrl)}"
            );
            // Inside modFolder on purpose: keeps the final Directory.Move a same-volume rename
            // instead of a cross-volume copy when %TEMP% lives on a different drive.
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
            bool isManualFolder = !string.IsNullOrEmpty(modMetadata.LocalSourceFolder);
            bool isManualArchive = !string.IsNullOrEmpty(modMetadata.LocalSourceArchive);
            bool isAutoDownload = !isManualFolder && !isManualArchive;

            try {
                AppLogger.Info(
                    $"Starting mod installation. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );
                // Manual cancel checkpoints — Directory.Move/File.Copy don't honor CancellationToken,
                // so without these a mid-install cancel would silently complete instead of rolling back.
                cancellationToken.ThrowIfCancellationRequested();
                ReportStage(stageProgress, "Preparing...");
                progress?.Report(PercentPreparing);
                Directory.CreateDirectory(modFolder);

                cancellationToken.ThrowIfCancellationRequested();
                AppLogger.Info("Closing WARNO if running.");
                ReportStage(stageProgress, "Closing WARNO...");
                progress?.Report(PercentClosing);
                CloseRunningGame();
                DeleteFileIfExists(lockFile);

                if (isManualFolder && isManualArchive) {
                    throw new InvalidOperationException(
                        "Specify either LocalSourceFolder or LocalSourceArchive, not both."
                    );
                }
                string extractedModPath;
                if (isManualFolder) {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppLogger.Info($"Manual install: staging from folder {modMetadata.LocalSourceFolder}");
                    ReportStage(stageProgress, "Copying mod...");
                    progress?.Report(PercentCopyStart);
                    if (!Directory.Exists(modMetadata.LocalSourceFolder)) {
                        throw new DirectoryNotFoundException(
                            $"Manual install source folder is missing: {modMetadata.LocalSourceFolder}"
                        );
                    }
                    // Off-thread: huge mod trees can take seconds to enumerate, and we're still on
                    // the install-async chain that started on the UI sync context.
                    long folderBytes = await Task.Run(
                        () => MeasureFolderSize(modMetadata.LocalSourceFolder!, cancellationToken),
                        cancellationToken
                    );
                    await ConfirmDiskSpaceAsync(
                        tempModPath,
                        DiskSpace.ApplyKnownSizeHeadroom(folderBytes),
                        "copying the mod folder",
                        lowDiskSpaceConfirm
                    );
                    // Stage into temp so we can rewrite Config.ini without touching the user's folder.
                    Directory.CreateDirectory(tempModPath);
                    await Task.Run(
                        () => CopyDirectoryRecursive(modMetadata.LocalSourceFolder!, tempModPath, cancellationToken),
                        cancellationToken
                    );
                    extractedModPath = ResolveExtractedModPath(tempModPath);
                    AppLogger.Info($"Staged manual mod at: {extractedModPath}");
                }
                else if (isManualArchive) {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppLogger.Info($"Manual install: extracting archive {modMetadata.LocalSourceArchive}");
                    if (!File.Exists(modMetadata.LocalSourceArchive)) {
                        throw new FileNotFoundException(
                            "Manual install archive is missing.",
                            modMetadata.LocalSourceArchive
                        );
                    }
                    ReportStage(stageProgress, "Extracting...");
                    progress?.Report(PercentExtractManualStart);
                    // No pre-download estimate covered this flow — archive lives on the user's
                    // disk already; check destination drive against the real uncompressed size.
                    long extractedBytes = await Task.Run(
                        () => SafeArchiveExtractor.MeasureUncompressedSize(
                            modMetadata.LocalSourceArchive!,
                            cancellationToken
                        ),
                        cancellationToken
                    );
                    await ConfirmDiskSpaceAsync(
                        tempModPath,
                        DiskSpace.ApplyKnownSizeHeadroom(extractedBytes),
                        "extracting the archive",
                        lowDiskSpaceConfirm
                    );
                    Directory.CreateDirectory(tempModPath);
                    await Task.Run(
                        () => SafeArchiveExtractor.ExtractToDirectory(
                            modMetadata.LocalSourceArchive!,
                            tempModPath,
                            RangedProgress(progress, PercentExtractManualStart, PercentExtractEnd),
                            new Progress<ArchiveExtractionProgress>(extractProgress => {
                                ReportStage(stageProgress, BuildExtractStage(extractProgress));
                            }),
                            cancellationToken
                        ),
                        cancellationToken
                    );
                    extractedModPath = ResolveExtractedModPath(tempModPath);
                    AppLogger.Info($"Extracted manual archive to: {extractedModPath}");
                }
                else {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppLogger.Info("Downloading mod archive.");
                    ReportStage(stageProgress, "Downloading...");
                    progress?.Report(PercentDownloadStart);
                    long? remoteSize = await HttpService.TryGetRemoteFileSizeAsync(
                        modMetadata.DownloadUrl,
                        cancellationToken
                    );
                    if (remoteSize.HasValue) {
                        // Footprint = compressed archive + extracted tree on the same drive at
                        // peak, so checking just remoteSize would let hopeless installs through.
                        await ConfirmDiskSpaceAsync(
                            modArchivePath,
                            DiskSpace.EstimatePeakBytesForDownloadAndExtract(remoteSize.Value),
                            "downloading and extracting the mod",
                            lowDiskSpaceConfirm
                        );
                    }
                    await HttpService.DownloadFileAsync(
                        modMetadata.DownloadUrl,
                        modArchivePath,
                        RangedProgress(progress, PercentDownloadStart, PercentDownloadEnd),
                        new Progress<HttpService.DownloadProgressInfo>(downloadProgress => {
                            ReportStage(stageProgress, BuildDownloadStage(downloadProgress));
                        }),
                        cancellationToken
                    );

                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(tempModPath);
                    AppLogger.Info("Extracting mod archive.");
                    ReportStage(stageProgress, "Extracting...");
                    progress?.Report(PercentExtractAutoStart);
                    await Task.Run(
                        () => SafeArchiveExtractor.ExtractToDirectory(
                            modArchivePath,
                            tempModPath,
                            RangedProgress(progress, PercentExtractAutoStart, PercentExtractEnd),
                            new Progress<ArchiveExtractionProgress>(extractProgress => {
                                ReportStage(stageProgress, BuildExtractStage(extractProgress));
                            }),
                            cancellationToken
                        ),
                        cancellationToken
                    );
                    extractedModPath = ResolveExtractedModPath(tempModPath);
                    AppLogger.Info($"Resolved extracted mod root: {extractedModPath}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                ReportStage(stageProgress, "Reading mod settings...");
                progress?.Report(PercentReading);
                EnsureGameModConfigExists(gameConfig);

                string modConfig = Path.Combine(extractedModPath, "Config.ini");
                Dictionary<string, string> ysmConfig = IniFile.ReadValues(modConfig);
                Dictionary<string, string> gameConfigData = IniFile.ReadValues(gameConfig);

                string rawDeckFormatVersion = IniFile.GetRequiredValue(
                    ysmConfig,
                    "DeckFormatVersion",
                    modConfig
                );
                // DeckFormatVersion is interpolated into a folder path — a crafted value like
                // "../../Windows" would otherwise let Path.Combine escape modFolder.
                string deckFormatVersion = SanitizeForFolderName(rawDeckFormatVersion);
                if (string.IsNullOrEmpty(deckFormatVersion)) {
                    throw new InvalidDataException(
                        $"DeckFormatVersion in {modConfig} contains only invalid characters."
                    );
                }
                string rawBaseName = !string.IsNullOrWhiteSpace(modMetadata.DisplayNameOverride)
                    ? modMetadata.DisplayNameOverride!
                    : ModTypes.ToDisplayName(modMetadata.ModType);
                // baseName ends up in a folder path, so strip path-invalid chars (mods named e.g.
                // "WiF/WTO" would otherwise crash Path.Combine). Fallback if sanitization eats it all.
                string baseName = SanitizeForFolderName(rawBaseName);
                if (string.IsNullOrEmpty(baseName)) {
                    baseName = ModTypes.ToDisplayName(modMetadata.ModType);
                }
                string manualVersion = $"{baseName} (v{deckFormatVersion}) (Installer)";
                finalModPath = Path.Combine(modFolder, manualVersion);
                // Defense in depth — even after sanitization, refuse to proceed if the final path
                // somehow escapes modFolder (symlinks, normalized traversal we missed).
                if (!IsPathInside(finalModPath, modFolder)) {
                    throw new InvalidDataException(
                        $"Resolved install path '{finalModPath}' escapes the mod folder."
                    );
                }

                ysmConfig["ID"] = manualVersion;
                ysmConfig["Name"] = manualVersion;
                IniFile.WriteValues(modConfig, ysmConfig);

                cancellationToken.ThrowIfCancellationRequested();
                AppLogger.Info("Backing up current WARNO mod configuration.");
                ReportStage(stageProgress, "Backing up your mods...");
                progress?.Report(PercentBackingUp);
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

                cancellationToken.ThrowIfCancellationRequested();
                ReportStage(stageProgress, "Installing...");
                progress?.Report(PercentInstalling);
                await Task.Run(
                    () => Directory.Move(extractedModPath, finalModPath),
                    cancellationToken
                );
                finalModCreated = true;

                // Past this point cancel is ignored: a half-written ActivatedMods corrupts game config.
                ReportStage(stageProgress, "Finalizing...");
                progress?.Report(PercentFinalizing);
                // Breathing room so fast installs don't flicker past the Finalizing morph. No token —
                // Finalizing is the no-cancel zone, OCE here would corrupt half-written game config.
                await Task.Delay(1000);
                gameConfigData["ActivatedMods"] = $"{manualVersion}|";
                IniFile.WriteValues(gameConfig, gameConfigData);
                progress?.Report(PercentDone);

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
                // Only auto-download writes modArchivePath; for manual flows the file was never
                // created here, and a same-named file in modFolder belongs to the user.
                if (isAutoDownload) {
                    DeleteFileIfExists(modArchivePath);
                }
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
            IProgress<int>? progress,
            IProgress<string>? stageProgress,
            CancellationToken cancellationToken
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

                ReportStage(stageProgress, "Downloading...");
                await ReportMockProgress(progress, PercentDownloadStart, PercentDownloadEnd, 60, cancellationToken);
                ReportStage(stageProgress, "Extracting...");
                await ReportMockProgress(progress, PercentExtractAutoStart, PercentExtractEnd, 60, cancellationToken);
                ReportStage(stageProgress, "Backing up your mods...");
                await ReportMockProgress(progress, PercentBackingUp, PercentInstalling, 60, cancellationToken);
                ReportStage(stageProgress, "Installing...");
                await ReportMockProgress(progress, PercentInstalling, PercentFinalizing, 60, cancellationToken);
                if (DevWarnoMocks.SimulateInstallFailure) {
                    DevWarnoMocks.SimulateInstallFailure = false;
                    throw new InvalidOperationException("Simulated install failure (dev test).");
                }
                ReportStage(stageProgress, "Finalizing...");
                await ReportMockProgress(progress, PercentFinalizing, PercentDone, 75, cancellationToken);

                AppLogger.Info(
                    $"Mock mod installation completed. Type: {modMetadata.ModType}, game version: {modMetadata.GameVersion}."
                );
                return InstallModResult.Installed;
            }
            finally {
                _isInstalling = false;
            }
        }

        private static void ReportStage(IProgress<string>? stageProgress, string stage) {
            stageProgress?.Report(stage);
        }

        // No callback = silently proceed (CLI/test contexts); UI layer is expected to wire the
        // confirmation dialog so end-users actually see the warning.
        private static async Task ConfirmDiskSpaceAsync(
            string path,
            long requiredBytes,
            string action,
            Func<DiskSpaceWarning, Task<bool>>? lowDiskSpaceConfirm
        ) {
            DiskSpaceWarning? warning = DiskSpace.CheckAvailableSpace(path, requiredBytes, action);
            if (warning == null) {
                return;
            }
            AppLogger.Info(warning.Message);
            if (lowDiskSpaceConfirm == null) {
                return;
            }
            bool proceed = await lowDiskSpaceConfirm(warning);
            if (!proceed) {
                throw new InstallDeclinedByUserException(warning.Message);
            }
        }

        // Lets each phase emit its native 0..100 byte-fraction without knowing the overall
        // percent budget — the wrapper maps it into the phase's slice on the shared channel.
        private static IProgress<int>? RangedProgress(IProgress<int>? target, int rangeStart, int rangeEnd) {
            if (target == null) {
                return null;
            }
            int span = rangeEnd - rangeStart;
            return new Progress<int>(value => {
                int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
                target.Report(rangeStart + (clamped * span) / 100);
            });
        }

        private static string BuildExtractStage(ArchiveExtractionProgress progress) {
            string extracted = ToMegabytesLabel(progress.BytesExtracted);
            if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0) {
                string total = ToMegabytesLabel(progress.TotalBytes.Value);
                return $"Extracting... {extracted} / {total}";
            }
            return $"Extracting... {extracted}";
        }

        private static string BuildDownloadStage(HttpService.DownloadProgressInfo progress) {
            string downloaded = ToMegabytesLabel(progress.BytesReceived);
            if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0) {
                string total = ToMegabytesLabel(progress.TotalBytes.Value);
                return $"Downloading... {downloaded} / {total}";
            }

            return $"Downloading... {downloaded}";
        }

        private static string ToMegabytesLabel(long bytes) {
            double megabytes = bytes / 1024d / 1024d;
            return $"{megabytes:0.0} MB";
        }

        private static async Task ReportMockProgress(
            IProgress<int>? progress,
            int fromInclusive,
            int toInclusive,
            int delayMs,
            CancellationToken cancellationToken
        ) {
            for (int value = fromInclusive; value <= toInclusive; value++) {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(value);
                await Task.Delay(delayMs, cancellationToken);
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

        private static bool IsPathInside(string candidate, string root) {
            string fullCandidate = Path.GetFullPath(candidate);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        // The extractor sniffs by magic bytes, so the cached filename's extension is cosmetic —
        // but matching the URL keeps the file recognizable on disk and avoids confusing leftovers.
        private static string GetArchiveExtension(string downloadUrl) {
            if (string.IsNullOrWhiteSpace(downloadUrl)) {
                return ".zip";
            }
            try {
                string path = new Uri(downloadUrl).AbsolutePath;
                if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return ".tar.gz";
                if (path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)) return ".tar.bz2";
                string ext = Path.GetExtension(path);
                return string.IsNullOrEmpty(ext) ? ".zip" : ext.ToLowerInvariant();
            }
            catch (Exception exception) {
                AppLogger.Critical($"GetArchiveExtension failed for URL: {downloadUrl}", exception);
                return ".zip";
            }
        }

        private static string SanitizeForFolderName(string value) {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value) {
                if (Array.IndexOf(invalid, c) < 0) {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        // Pre-flight sum so a multi-gigabyte copy doesn't die on disk-full halfway.
        // Per-file token check keeps cancel-mid-scan latency in the ms range on huge trees.
        private static long MeasureFolderSize(string folder, CancellationToken cancellationToken) {
            long total = 0;
            var stack = new System.Collections.Generic.Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                string current = stack.Pop();
                // Silently skip per-file/per-dir FS noise (permissions, locked subfolders); the
                // real copy below hits any structural issue with a clearer error.
                try {
                    foreach (string file in Directory.GetFiles(current)) {
                        cancellationToken.ThrowIfCancellationRequested();
                        try {
                            total += new FileInfo(file).Length;
                        }
                        // IOException covers PathTooLongException (its subclass) too.
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (SecurityException) { }
                    }
                    foreach (string sub in Directory.GetDirectories(current)) {
                        stack.Push(sub);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
            return total;
        }

        // CancellationToken is checked per-file so cancel during a 2GB mod copy stops within ms
        // instead of waiting for the whole tree — Task.Run(action, token) only honors the token
        // before action starts, not while it runs.
        private static void CopyDirectoryRecursive(string sourceDir, string destDir, CancellationToken cancellationToken) {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir)) {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir)) {
                cancellationToken.ThrowIfCancellationRequested();
                CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), cancellationToken);
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
