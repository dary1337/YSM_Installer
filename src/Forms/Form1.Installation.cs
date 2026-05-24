using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private static readonly string[] AutoInstallSteps = {
            "Preparing",
            "Closing WARNO",
            "Downloading",
            "Extracting",
            "Reading mod settings",
            "Backing up your mods",
            "Installing",
            "Finalizing",
        };

        private static readonly string[] ManualFolderInstallSteps = {
            "Preparing",
            "Closing WARNO",
            "Copying mod",
            "Reading mod settings",
            "Backing up your mods",
            "Installing",
            "Finalizing",
        };

        private static readonly string[] ManualArchiveInstallSteps = {
            "Preparing",
            "Closing WARNO",
            "Extracting",
            "Reading mod settings",
            "Backing up your mods",
            "Installing",
            "Finalizing",
        };

        private string[] _currentInstallSteps = AutoInstallSteps;

        private int _currentStepIndex;

        private CancellationTokenSource? _installCts;
        private MaterialProgressBar? _progressBar;
        private StepChecklist? _stepChecklist;
        private Label? _percentLabel;
        private Label? _detailLabel;
        private Label? _etaLabel;
        private DateTime _installStartUtc;

        private ModMetadata? _lastInstallMetadata;
        private int _lastInstallVersion;

        private readonly List<MaterialOptionCard> _buildCards = new List<MaterialOptionCard>();
        private ModMetadata? _selectedBuild;
        private int _chooseVersion;

        // ---- Variant resolution (ported from the original install-button logic) ----
        private List<ModMetadata> GetVariantsForVersion(int version) {
            List<ModMetadata> found = _supportedVersions.Where(mod => mod.GameVersion == version).ToList();
            if (found.Count == 0) {
                found = GetLatestCompatibleMods(version);
            }

            var ordered = new List<ModMetadata>();
            foreach (string type in new[] { ModTypes.Ysm, ModTypes.YsmWif, ModTypes.YsmWifWto, ModTypes.Wto }) {
                ModMetadata? match = found.FirstOrDefault(mod => mod.ModType == type);
                if (match != null) {
                    ordered.Add(match);
                }
            }
            return ordered;
        }

        private List<ModMetadata> GetLatestCompatibleMods(int gameVersion) {
            int latestSupported = _supportedVersions.Select(mod => mod.GameVersion).DefaultIfEmpty(0).Max();
            if (latestSupported == 0 || gameVersion <= latestSupported) {
                return new List<ModMetadata>();
            }
            return _supportedVersions.Where(mod => mod.GameVersion == latestSupported).ToList();
        }

        // ---- Island ----
        private void UpdateIslandForSelection() {
            if (_selectedEntry == null) {
                HideIsland();
                return;
            }

            // Always route through ChooseBuild — even with 0 or 1 standard variant. Manual install is
            // appended there, so user always has at least one path forward (including "not supported"
            // versions where only the manual option exists).
            List<ModMetadata> variants = GetVariantsForVersion(_selectedEntry.Version);
            MaterialButton choose = PrimaryButton("Continue", MaterialIcons.ArrowForward);
            choose.Click += async (s, e) => {
                try {
                    await RenderChooseBuild(variants);
                }
                catch (Exception ex) {
                    AppLogger.Critical("RenderChooseBuild failed.", ex);
                }
            };
            SetIslandActions(choose);
        }

        // ---- Inline build selection ----
        private async Task RenderChooseBuild(List<ModMetadata> variants) {
            _state = AppState.ChooseBuild;
            _chooseVersion = _selectedEntry!.Version;
            _buildCards.Clear();
            _selectedBuild = null;

            SetHeader("Choose a build", "Pick a build to install, or bring your own mod folder.");

            TableLayoutPanel stack = NewStack();
            foreach (ModMetadata variant in variants) {
                (string description, bool recommended) = DescribeBuild(variant.ModType);
                string title = ModTypes.ToDisplayName(variant.ModType);
                if (_chooseVersion > variant.GameVersion) {
                    title += $"  (latest v{variant.GameVersion})";
                }
                var card = new MaterialOptionCard(title, description, recommended, BuildIcons.ForBuild(variant.ModType)) {
                    Height = 70,
                    Tag2 = variant,
                };
                card.SelectedChanged += OnBuildSelected;
                _buildCards.Add(card);
                AddToStack(stack, card, Sizes.ContentGap);
            }

            // Manual is always appended — even on "not supported" versions where no standard variants
            // exist, so the user still has a path forward.
            var manualMetadata = new ModMetadata {
                ModType = ModTypes.Manual,
                GameVersion = _chooseVersion,
            };
            (string manualDescription, _) = DescribeBuild(ModTypes.Manual);
            var manualCard = new MaterialOptionCard(
                ModTypes.ToDisplayName(ModTypes.Manual),
                manualDescription,
                recommended: false,
                customIcon: null,
                fallbackGlyph: MaterialIcons.Game
            ) {
                Height = 70,
                Tag2 = manualMetadata,
            };
            manualCard.SelectedChanged += OnBuildSelected;
            _buildCards.Add(manualCard);
            AddToStack(stack, manualCard, Sizes.ContentGap);

            SetContent(stack, fill: false);

            MaterialOptionCard def =
                _buildCards.FirstOrDefault(c => ((ModMetadata)c.Tag2!).ModType == ModTypes.Ysm)
                ?? _buildCards.FirstOrDefault(c => ((ModMetadata)c.Tag2!).ModType != ModTypes.Manual)
                ?? manualCard;
            def.SetSelected(true);
            _selectedBuild = (ModMetadata)def.Tag2!;
            UpdateChooseBuildIsland();

            await PopulateBuildSizesAsync(variants);
        }

        private void OnBuildSelected(MaterialOptionCard selected) {
            foreach (MaterialOptionCard card in _buildCards) {
                if (!ReferenceEquals(card, selected)) {
                    card.SetSelected(false);
                }
            }
            _selectedBuild = (ModMetadata)selected.Tag2!;
            UpdateChooseBuildIsland();
        }

        private void UpdateChooseBuildIsland() {
            if (_selectedBuild == null) {
                HideIsland();
                return;
            }
            MaterialButton back = TonalButton("Back");
            back.Click += (s, e) => RenderInstallsFound();

            bool isManual = string.Equals(_selectedBuild.ModType, ModTypes.Manual, StringComparison.Ordinal);
            if (isManual) {
                MaterialButton browseFolder = TonalButton("Browse folder", MaterialIcons.Folder);
                browseFolder.Click += async (s, e) => {
                    try {
                        await BrowseForManualFolderAsync();
                    }
                    catch (Exception ex) {
                        AppLogger.Critical("Manual folder browse flow failed.", ex);
                    }
                };

                MaterialButton browseArchive = PrimaryButton("Browse archive", MaterialIcons.Document);
                browseArchive.Click += async (s, e) => {
                    try {
                        await BrowseForManualArchiveAsync();
                    }
                    catch (Exception ex) {
                        AppLogger.Critical("Manual archive browse flow failed.", ex);
                    }
                };

                SetIslandActions(back, browseFolder, browseArchive);
                return;
            }

            string name = ModTypes.ToDisplayName(_selectedBuild.ModType);
            MaterialButton install = PrimaryButton($"Install {name}", MaterialIcons.Download);
            ModMetadata target = _selectedBuild;
            install.Click += async (s, e) => {
                try {
                    await StartInstallAsync(target, _chooseVersion);
                }
                catch (Exception ex) {
                    AppLogger.Critical("StartInstall failed (build card).", ex);
                }
            };

            SetIslandActions(back, install);
        }

        private async Task PopulateBuildSizesAsync(List<ModMetadata> variants) {
            foreach (MaterialOptionCard card in _buildCards.ToList()) {
                if (_state != AppState.ChooseBuild || card.IsDisposed) {
                    return;
                }
                var variant = (ModMetadata)card.Tag2!;
                long? size = await HttpService.TryGetRemoteFileSizeAsync(variant.DownloadUrl);
                if (_state != AppState.ChooseBuild || card.IsDisposed) {
                    return;
                }
                if (size.HasValue && size.Value > 0) {
                    card.SizeText = $"{size.Value / 1024d / 1024d:0.0} MB";
                }
                else if (string.Equals(variant.ModType, ModTypes.YsmWif, StringComparison.Ordinal)
                    || string.Equals(variant.ModType, ModTypes.YsmWifWto, StringComparison.Ordinal)) {
                    card.SizeText = "> 2 GB";
                }
            }
        }

        private static (string description, bool recommended) DescribeBuild(string modType) {
            if (modType == ModTypes.YsmWif) {
                return ("Combined version of Yokaiste's Sandbox Mod and A World in Flames.", false);
            }
            if (modType == ModTypes.YsmWifWto) {
                return ("YSM combined with both A World in Flames and WARNO Tactical Overhaul.", false);
            }
            if (modType == ModTypes.Wto) {
                return ("WARNO Tactical Overhaul — Freedom Decks, Realistic LOS, Unit Speed, 2x Scale.", false);
            }
            if (modType == ModTypes.Manual) {
                return ("Install from a local folder or archive (.zip, .7z, .rar, .tar) you already have.", false);
            }
            return ("Yokaiste's Sandbox Mod — an advanced open-source project for unlimited experience.", false);
        }

        // ---- Install flow ----
        private async Task StartInstallAsync(ModMetadata metadata, int version) {
            if (_isInstalling) {
                return;
            }

            // Both catalog mismatch (game > catalog target) and manual mismatch (Config.ini's
            // ModGenVersion ≠ running WARNO) funnel through the same warning so the user can bail.
            if (version != metadata.GameVersion) {
                RenderVersionMismatch(metadata, version);
                return;
            }

            await ProceedWithInstallAsync(metadata, version);
        }

        private static string GetEffectiveDisplayName(ModMetadata metadata) {
            return !string.IsNullOrWhiteSpace(metadata.DisplayNameOverride)
                ? metadata.DisplayNameOverride!
                : ModTypes.ToDisplayName(metadata.ModType);
        }

        private static string[] SelectInstallSteps(ModMetadata metadata) {
            if (!string.IsNullOrEmpty(metadata.LocalSourceFolder)) return ManualFolderInstallSteps;
            if (!string.IsNullOrEmpty(metadata.LocalSourceArchive)) return ManualArchiveInstallSteps;
            return AutoInstallSteps;
        }

        private async Task ProceedWithInstallAsync(ModMetadata metadata, int version) {
            if (_isInstalling) {
                return;
            }

            if (!ConfirmWarnoCloseIfRunning()) {
                return;
            }

            _isInstalling = true;
            _lastInstallMetadata = metadata;
            _lastInstallVersion = version;
            _installCts = new CancellationTokenSource();
            _currentInstallSteps = SelectInstallSteps(metadata);
            string name = GetEffectiveDisplayName(metadata);
            RenderInstalling(name, _selectedEntry?.GamePath ?? string.Empty);
            _installStartUtc = DateTime.UtcNow;

            InstallWorkflowResult result;
            try {
                var workflow = new InstallWorkflow();
                result = await workflow.InstallAsync(
                    metadata,
                    new Progress<int>(OnInstallPercent),
                    new Progress<string>(OnInstallStage),
                    _installCts.Token
                );
            }
            finally {
                _isInstalling = false;
                _installCts?.Dispose();
                _installCts = null;
            }

            switch (result) {
                case InstallWorkflowResult.Installed:
                    _installedKeys.Add(ModKey(metadata, version));
                    RenderComplete(name, _selectedEntry);
                    break;
                case InstallWorkflowResult.Cancelled:
                    RenderInstallsFound();
                    break;
                case InstallWorkflowResult.AlreadyRunning:
                    UserMessages.ShowError(this, "Installer busy",
                        "Another installation is already running. Please wait for it to finish.");
                    RenderInstallsFound();
                    break;
                default:
                    RenderFailed();
                    break;
            }
        }

        private async Task BrowseForManualFolderAsync() {
            string? folder = PickManualFolder();
            if (folder == null) {
                return;
            }

            if (IsDriveRoot(folder)) {
                UserMessages.ShowError(
                    this,
                    "Pick a mod folder",
                    "You selected a drive root. Pick the mod folder itself (the one that contains Config.ini), or a parent folder that holds the mod folder."
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

        // Capped so a Browse on a near-root folder can't fan out into a multi-minute scan.
        private const int ManualFolderScanMaxDepth = 4;

        private static (string? path, string? error) LocateConfigInFolder(string folder) {
            if (!Directory.Exists(folder)) {
                return (null, $"Folder does not exist:\n{folder}");
            }
            List<string> matches;
            try {
                matches = FindConfigFilesBounded(folder, ManualFolderScanMaxDepth);
            }
            catch (Exception exception) {
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
                dialog.Filter = "Mod archive|*.zip;*.7z;*.rar;*.tar;*.tar.gz;*.tgz;*.tar.bz2;*.tbz2";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    return null;
                }
                return dialog.FileName;
            }
        }

        private bool ConfirmCancelInstall() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialPalette.Warning;
                dialog.TitleText = "Cancel installation?";
                dialog.BodyText = "Changes already made will be rolled back. Your previous mods and game config will be restored.";
                dialog.AddAction("Keep installing", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Cancel install", DialogResult.OK, MaterialButtonVariant.Filled);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private bool ConfirmWarnoCloseIfRunning() {
            if (!WarnoInstallWarning.IsGameRunning) {
                return true;
            }

            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialPalette.Warning;
                dialog.TitleText = "WARNO is running";
                dialog.BodyText = "WARNO will be closed to install the mod. All other mods are disabled for compatibility.";
                dialog.AddAction("Cancel", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Close & install", DialogResult.OK, MaterialButtonVariant.Filled);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private async Task ShowSizeOnButtonAsync(MaterialButton button, ModMetadata metadata, string baseText) {
            long? size = await HttpService.TryGetRemoteFileSizeAsync(metadata.DownloadUrl);
            if (button.IsDisposed || button.Parent == null) {
                return;
            }
            if (size.HasValue && size.Value > 0) {
                button.Text = $"{baseText} · {size.Value / 1024d / 1024d:0.0} MB";
            }
        }

        private void OnInstallPercent(int percent) {
            if (_progressBar == null) {
                return;
            }
            // Bytes-based progress arrives from multiple phases (download, then extraction);
            // each phase starts at 0%, so without this guard the bar would drop back to 0 when
            // extraction kicks in after a finished download.
            if (percent < _progressBar.Value) {
                return;
            }
            _progressBar.Value = percent;
            if (_percentLabel != null) {
                _percentLabel.Text = $"{percent}%";
            }
            UpdateEta(percent);
        }

        private void OnInstallStage(string stage) {
            if (_detailLabel != null) {
                _detailLabel.Text = stage;
            }

            int index = StageToStepIndex(stage);
            if (index >= 0) {
                _currentStepIndex = index;
                if (_stepChecklist != null) {
                    _stepChecklist.ActiveIndex = index;
                }
            }
            // Bar/percent/ETA are owned by WarnoInstaller's overall-progress channel now —
            // stages only drive the checklist.
            if (stage.StartsWith("Finalizing", StringComparison.Ordinal)) {
                _currentStepIndex = _currentInstallSteps.Length;
                _stepChecklist?.Complete();
            }
        }

        private int StageToStepIndex(string stage) {
            string? prefix = MatchStagePrefix(stage);
            if (prefix == null) {
                return -1;
            }
            for (int i = 0; i < _currentInstallSteps.Length; i++) {
                if (_currentInstallSteps[i].StartsWith(prefix, StringComparison.Ordinal)) {
                    return i;
                }
            }
            return -1;
        }

        private static string? MatchStagePrefix(string stage) {
            if (stage.StartsWith("Preparing", StringComparison.Ordinal)) return "Preparing";
            if (stage.StartsWith("Closing", StringComparison.Ordinal)) return "Closing";
            if (stage.StartsWith("Downloading", StringComparison.Ordinal)) return "Downloading";
            if (stage.StartsWith("Copying", StringComparison.Ordinal)) return "Copying";
            if (stage.StartsWith("Extracting", StringComparison.Ordinal)) return "Extracting";
            if (stage.StartsWith("Reading", StringComparison.Ordinal)) return "Reading";
            if (stage.StartsWith("Backing up", StringComparison.Ordinal)) return "Backing up";
            if (stage.StartsWith("Installing", StringComparison.Ordinal)) return "Installing";
            if (stage.StartsWith("Finalizing", StringComparison.Ordinal)) return "Finalizing";
            return null;
        }

        private void UpdateEta(int percent) {
            if (_etaLabel == null) {
                return;
            }
            if (percent <= 1 || percent >= 100) {
                _etaLabel.Text = percent >= 100 ? "Almost done" : string.Empty;
                return;
            }
            double elapsed = (DateTime.UtcNow - _installStartUtc).TotalSeconds;
            double estimatedTotal = elapsed / (percent / 100.0);
            int remaining = (int)Math.Max(1, Math.Round(estimatedTotal - elapsed));
            _etaLabel.Text = remaining >= 60
                ? $"~{remaining / 60} min left"
                : $"~{remaining} s left";
        }

        // ---- Version mismatch state ----
        private void RenderVersionMismatch(ModMetadata metadata, int selectedGameVersion) {
            _state = AppState.VersionMismatch;
            _lastInstallMetadata = metadata;
            _lastInstallVersion = selectedGameVersion;
            string name = GetEffectiveDisplayName(metadata);
            SetHeader("Version mismatch", "Mod targets a different Warno build");

            TableLayoutPanel stack = NewStack();

            bool isSteamEntry = string.Equals(
                _selectedEntry?.SourceLabel,
                WarnoExecutableSources.Steam,
                StringComparison.Ordinal
            );

            MaterialCard warn = BuildMessageCard(
                MaterialIcons.Warning,
                MaterialPalette.OnWarningContainer,
                MaterialPalette.WarningContainer,
                $"{name} targets Warno v{metadata.GameVersion}",
                $"You have v{selectedGameVersion}. The mod may load but is not guaranteed to work."
            );
            AddToStack(stack, warn, isSteamEntry ? Sizes.ContentGap : 0);

            if (isSteamEntry) {
                MaterialCard guide = BuildGuideCard(
                    "How to switch Warno versions",
                    "Steam → right-click Warno → Betas",
                    OpenStepsForm
                );
                AddToStack(stack, guide, 0);
            }

            SetContent(stack, fill: false);

            MaterialButton cancel = TonalButton("Cancel");
            cancel.Click += async (s, e) => {
                try {
                    List<ModMetadata> variants = GetVariantsForVersion(selectedGameVersion);
                    await RenderChooseBuild(variants);
                }
                catch (Exception ex) {
                    AppLogger.Critical("Cancel/back from version mismatch failed.", ex);
                }
            };
            MaterialButton install = PrimaryButton("Install anyway", MaterialIcons.Download);
            install.Click += async (s, e) => {
                try {
                    await ProceedWithInstallAsync(metadata, selectedGameVersion);
                }
                catch (Exception ex) {
                    AppLogger.Critical("Install-anyway (version mismatch) failed.", ex);
                }
            };
            SetIslandActions(cancel, install);
        }

        private MaterialCard BuildGuideCard(string title, string subtitle, Action onGuide) {
            var card = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialPalette.SurfaceContainer,
                Padding = new Padding(16),
            };

            var grid = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var textStack = new FlowLayoutPanel {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                WrapContents = false,
            };
            textStack.Controls.Add(new Label {
                AutoSize = true,
                Font = MaterialType.TitleMedium,
                ForeColor = MaterialPalette.OnSurface,
                Margin = new Padding(0, 0, 0, 2),
                Text = title,
            });
            textStack.Controls.Add(new Label {
                AutoSize = true,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Margin = Padding.Empty,
                Text = subtitle,
            });

            MaterialButton guideBtn = OutlinedButton("Guide", MaterialIcons.OpenInNew);
            guideBtn.AutoSize = true;
            guideBtn.Anchor = AnchorStyles.None;
            guideBtn.Margin = new Padding(Tokens.Space3, 0, 0, 0);
            Action capturedAction = onGuide;
            guideBtn.Click += (s, e) => capturedAction();

            grid.Controls.Add(textStack, 0, 0);
            grid.Controls.Add(guideBtn, 1, 0);
            card.Controls.Add(grid);
            return card;
        }

        // ---- Installing state ----
        private void RenderInstalling(string modName, string gamePath) {
            _state = AppState.Installing;
            SetHeader("Installing", $"{modName} → {ShortenPath(gamePath, 60)}");
            HideIsland();

            var card = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialPalette.SurfaceContainer,
                Padding = new Padding(20),
            };

            // Fixed-height, fully docked grid — AutoSize TableLayoutPanel with a Percent column degenerates
            // under Dock=Top and pushes the labels to the wrong edges relative to the progress bar.
            var grid = new TableLayoutPanel {
                AutoSize = false,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Height = 24,
                Margin = Padding.Empty,
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));

            _detailLabel = new SoftLabel {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurface,
                Height = 22,
                Text = "Preparing…",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _percentLabel = new SoftLabel {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = MaterialType.TitleMedium,
                ForeColor = MaterialPalette.Primary,
                Height = 22,
                Text = "0%",
                TextAlign = ContentAlignment.MiddleRight,
                Width = 60,
            };
            grid.Controls.Add(_detailLabel, 0, 0);
            grid.Controls.Add(_percentLabel, 1, 0);

            _progressBar = new MaterialProgressBar {
                Dock = DockStyle.Top,
                Height = 8,
                Margin = new Padding(0, Tokens.Space5, 0, Tokens.Space2),
            };

            _etaLabel = new SoftLabel {
                AutoSize = false,
                Dock = DockStyle.Top,
                Font = MaterialType.BodySmall,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Height = 18,
                Text = string.Empty,
                TextAlign = ContentAlignment.MiddleRight,
            };

            _stepChecklist = new StepChecklist {
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                Width = 320,
            };
            _stepChecklist.SetSteps(_currentInstallSteps);
            _stepChecklist.ActiveIndex = 0;
            _currentStepIndex = 0;

            var cancel = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Cancel",
                IconGlyph = MaterialIcons.Cancel,
                Anchor = AnchorStyles.Right,
                Dock = DockStyle.Top,
                Height = Sizes.ButtonHeight,
                Margin = new Padding(0, Tokens.Space8, 0, 0),
            };
            cancel.SetAccent(MaterialPalette.Error, MaterialPalette.OnError);
            cancel.Click += (s, e) => {
                if (!ConfirmCancelInstall()) {
                    return;
                }
                cancel.Enabled = false;
                cancel.Text = "Canceling…";
                _installCts?.Cancel();
            };

            // Docked Top stacks in reverse insertion order, so add bottom-most first.
            card.Controls.Add(cancel);
            card.Controls.Add(_stepChecklist);
            card.Controls.Add(_etaLabel);
            card.Controls.Add(_progressBar);
            card.Controls.Add(grid);

            TableLayoutPanel stack = NewStack();
            AddToStack(stack, card, 0);
            SetContent(stack, fill: false);
        }

        // ---- Complete state ----
        private void RenderComplete(string modName, WarnoEntry? entry) {
            _state = AppState.Complete;
            SetHeader("Done", "Installation complete");

            var center = new TableLayoutPanel {
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                RowCount = 3,
            };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Inner column auto-sizes to its widest child (the 440px folder card); every row is
            // Anchor=None so it centers horizontally within that column, and the column centers in the cell.
            var stack = new TableLayoutPanel {
                Anchor = AnchorStyles.None,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = Padding.Empty,
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var check = new PictureBox {
                Anchor = AnchorStyles.None,
                BackColor = Color.Transparent,
                Image = BuildSuccessBadge(64),
                Margin = new Padding(0, 0, 0, 10),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Normal,
            };
            stack.Controls.Add(check);

            var title = new Label {
                Anchor = AnchorStyles.None,
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.HeadlineSmall,
                ForeColor = MaterialPalette.OnSurface,
                Margin = new Padding(0, 0, 0, 8),
                Text = "Installation complete",
            };
            stack.Controls.Add(title);

            var subtitle = new Label {
                Anchor = AnchorStyles.None,
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                MaximumSize = new Size(440, 0),
                Text = $"{modName} is installed and activated. Launch WARNO or install another mod.",
                TextAlign = ContentAlignment.TopCenter,
                Margin = new Padding(0, 0, 0, 16),
            };
            stack.Controls.Add(subtitle);

            // entry.ModsPath ({GamePath}\Mods) is the Steam install probe / Workshop mirror, not
            // where WARNO loads mods from — the real load path is Saved Games.
            string modsPath = WarnoPaths.ModFolder;
            var folderCard = new MaterialCard(Sizes.RadiusSmall) {
                Anchor = AnchorStyles.None,
                BackColor = MaterialPalette.SurfaceContainerHigh,
                Margin = Padding.Empty,
                Size = new Size(440, 48),
            };
            var folderLabel = new SoftLabel {
                AutoEllipsis = true,
                AutoSize = false,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(14, 14),
                Size = new Size(330, 20),
                Text = ShortenPath(modsPath, 60),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var openButton = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Open",
                IconGlyph = MaterialIcons.OpenFolder,
                Location = new Point(440 - 92, 4),
                Size = new Size(84, 40),
            };
            openButton.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
            openButton.Click += (s, e) => OpenInExplorer(modsPath);
            folderCard.Controls.Add(openButton);
            folderCard.Controls.Add(folderLabel);
            stack.Controls.Add(folderCard);

            center.Controls.Add(stack, 0, 1);
            SetContent(center, fill: true);

            MaterialButton again = TonalButton("Install something else", MaterialIcons.Refresh);
            again.Click += (s, e) => RenderInstallsFound();
            MaterialButton launch = PrimaryButton("Launch Warno", MaterialIcons.Play);
            launch.Click += (s, e) => LaunchWarno(entry);
            SetIslandActions(again, launch);
        }

        // ---- Failed state ----
        private void RenderFailed() {
            _state = AppState.Failed;
            SetHeader("Installation failed", "Changes were rolled back");
            HideIsland();

            TableLayoutPanel stack = NewStack();
            MaterialCard card = BuildMessageCard(
                MaterialIcons.ErrorBadge,
                MaterialPalette.OnErrorContainer,
                MaterialPalette.ErrorContainer,
                "Installation failed",
                $"Changes have been rolled back. Details written to:\n{AppLogger.LogPath}"
            );
            AddToStack(stack, card, Sizes.ContentGap);

            // Show the full step list with the failed step marked, so it's clear where it broke.
            var stepsCard = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialPalette.SurfaceContainer,
                Padding = new Padding(16),
            };
            var failChecklist = new StepChecklist { Dock = DockStyle.Top, Width = 320 };
            failChecklist.SetSteps(_currentInstallSteps);
            int failIdx = Math.Min(Math.Max(0, _currentStepIndex), _currentInstallSteps.Length - 1);
            failChecklist.ActiveIndex = failIdx;
            failChecklist.SetError(failIdx);
            stepsCard.Controls.Add(failChecklist);
            AddToStack(stack, stepsCard, Sizes.ContentGap);

            MaterialButton openLog = OutlinedButton("Open log", MaterialIcons.Document);
            openLog.Click += (s, e) => OpenInExplorer(AppLogger.LogPath);

            MaterialButton tryAgain = TonalButton("Try again", MaterialIcons.Refresh);
            tryAgain.Click += async (s, e) => {
                try {
                    if (_lastInstallMetadata != null) {
                        await StartInstallAsync(_lastInstallMetadata, _lastInstallVersion);
                    }
                    else {
                        RenderInstallsFound();
                    }
                }
                catch (Exception ex) {
                    AppLogger.Critical("Try-again install failed.", ex);
                }
            };

            AddToStack(stack, BuildButtonRow(openLog, tryAgain), 0);
            SetContent(stack, fill: false);
        }

        // ---- Shared building blocks ----
        private MaterialCard BuildMessageCard(string glyph, Color glyphColor, Color glyphBg, string title, string body) {
            var card = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialPalette.SurfaceContainer,
                Padding = new Padding(16),
            };

            var grid = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var badge = new RoundedPanel(Sizes.RadiusExtraSmall) {
                Anchor = AnchorStyles.None, // vertically center the badge in the (possibly taller) text row
                BackColor = glyphBg,
                Margin = new Padding(0, 0, 12, 0),
                Size = new Size(40, 40),
            };
            var badgeIcon = new PictureBox {
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Image = MaterialIconRenderer.Get(glyph, 20, glyphColor),
                SizeMode = PictureBoxSizeMode.CenterImage,
            };
            badge.Controls.Add(badgeIcon);

            var textStack = new FlowLayoutPanel {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                WrapContents = false,
            };
            textStack.Controls.Add(new Label {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.TitleMedium,
                ForeColor = MaterialPalette.OnSurface,
                Margin = new Padding(0, 0, 0, 4),
                Text = title,
            });
            textStack.Controls.Add(new Label {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                MaximumSize = new Size(440, 0),
                Margin = Padding.Empty,
                Text = body,
            });

            grid.Controls.Add(badge, 0, 0);
            grid.Controls.Add(textStack, 1, 0);
            card.Controls.Add(grid);
            return card;
        }

        private static Bitmap BuildSuccessBadge(int size) {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(MaterialPalette.Success)) {
                    g.FillEllipse(brush, 0, 0, size - 1, size - 1);
                }
                int cs = (int)(size * 0.5);
                Bitmap check = MaterialIconRenderer.Get(MaterialIcons.Check, cs, MaterialPalette.OnSuccess);
                g.DrawImage(check, (size - cs) / 2, (size - cs) / 2, cs, cs);
            }
            return bmp;
        }

        private static string ShortenPath(string path, int maxLength = 44) {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength) {
                return path;
            }
            string root = Path.GetPathRoot(path) ?? string.Empty;
            string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            return $"{root.TrimEnd('\\')}\\…\\{parent}\\{leaf}";
        }

        private static string ModKey(ModMetadata metadata, int version) {
            return $"{metadata.ModType}:{version}";
        }

        private static void OpenInExplorer(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    return;
                }
                if (File.Exists(path)) {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                else if (Directory.Exists(path)) {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else {
                    string parent = Path.GetDirectoryName(path);
                    if (Directory.Exists(parent)) {
                        Process.Start(new ProcessStartInfo(parent) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception exception) {
                AppLogger.Critical($"Failed to open path: {path}", exception);
            }
        }

        private void LaunchWarno(WarnoEntry? entry) {
            try {
                if (entry != null && File.Exists(entry.ExePath)) {
                    Process.Start(new ProcessStartInfo(entry.ExePath) { UseShellExecute = true });
                    WindowState = FormWindowState.Minimized;
                }
            }
            catch (Exception exception) {
                AppLogger.Critical("Failed to launch WARNO.", exception);
                UserMessages.ShowError(this, "Could not launch WARNO", exception.Message);
            }
        }
    }
}
