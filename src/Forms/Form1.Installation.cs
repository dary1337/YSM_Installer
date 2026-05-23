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
        private static readonly string[] InstallSteps = {
            "Preparing",
            "Closing WARNO",
            "Downloading",
            "Extracting",
            "Reading mod settings",
            "Backing up your mods",
            "Installing",
            "Finalizing",
        };

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
            foreach (string type in new[] { ModTypes.Ysm, ModTypes.YsmWif, ModTypes.Wto }) {
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

            List<ModMetadata> variants = GetVariantsForVersion(_selectedEntry.Version);
            if (variants.Count == 0) {
                MaterialButton disabled = TonalButton("No compatible mod for this version");
                disabled.Enabled = false;
                SetIslandActions(disabled);
                return;
            }

            if (variants.Count == 1) {
                ModMetadata metadata = variants[0];
                string name = ModTypes.ToDisplayName(metadata.ModType);
                bool installed = _installedKeys.Contains(ModKey(metadata, _selectedEntry.Version));
                string baseText = installed ? $"Reinstall {name}" : $"Install {name}";
                MaterialButton button = PrimaryButton(baseText, MaterialIcons.Download);
                int version = _selectedEntry.Version;
                button.Click += async (s, e) => await StartInstallAsync(metadata, version);
                SetIslandActions(button);
                _ = ShowSizeOnButtonAsync(button, metadata, baseText);
                return;
            }

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

            SetHeader("Choose a build", "Each build is a separate collaboration. Pick the one you want to play.");

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
            SetContent(stack, fill: false);

            MaterialOptionCard def =
                _buildCards.FirstOrDefault(c => ((ModMetadata)c.Tag2!).ModType == ModTypes.Ysm)
                ?? _buildCards.First();
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

            string name = ModTypes.ToDisplayName(_selectedBuild.ModType);
            MaterialButton install = PrimaryButton($"Install {name}", MaterialIcons.Download);
            ModMetadata target = _selectedBuild;
            install.Click += async (s, e) => await StartInstallAsync(target, _chooseVersion);

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
                else if (string.Equals(variant.ModType, ModTypes.YsmWif, StringComparison.Ordinal)) {
                    card.SizeText = "> 2 GB";
                }
            }
        }

        private static (string description, bool recommended) DescribeBuild(string modType) {
            if (modType == ModTypes.YsmWif) {
                return ("Combined version of  Yokaiste's Sandbox Mod and A World in Flames.", false);
            }
            if (modType == ModTypes.Wto) {
                return ("WARNO Tactical Overhaul — Freedom Decks, Realistic LOS, Unit Speed, 2x Scale.", false);
            }
            return ("Yokaiste's Sandbox Mod — an advanced open-source project for unlimited experience.", false);
        }

        // ---- Install flow ----
        private async Task StartInstallAsync(ModMetadata metadata, int version) {
            if (_isInstalling) {
                return;
            }

            // Mod was selected via the "latest compatible" fallback (game build is newer than the mod
            // target). Show the mismatch step before installing so the user can switch Warno versions.
            if (version != metadata.GameVersion) {
                RenderVersionMismatch(metadata, version);
                return;
            }

            await ProceedWithInstallAsync(metadata, version);
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
            string name = ModTypes.ToDisplayName(metadata.ModType);
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
            if (stage.StartsWith("Finalizing", StringComparison.Ordinal)) {
                _currentStepIndex = InstallSteps.Length;
                _stepChecklist?.Complete();
            }
        }

        private static int StageToStepIndex(string stage) {
            if (stage.StartsWith("Preparing", StringComparison.Ordinal)) return 0;
            if (stage.StartsWith("Closing", StringComparison.Ordinal)) return 1;
            if (stage.StartsWith("Downloading", StringComparison.Ordinal)) return 2;
            if (stage.StartsWith("Extracting", StringComparison.Ordinal)) return 3;
            if (stage.StartsWith("Reading", StringComparison.Ordinal)) return 4;
            if (stage.StartsWith("Backing up", StringComparison.Ordinal)) return 5;
            if (stage.StartsWith("Installing", StringComparison.Ordinal)) return 6;
            if (stage.StartsWith("Finalizing", StringComparison.Ordinal)) return 7;
            return -1;
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
            string name = ModTypes.ToDisplayName(metadata.ModType);
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
                    if (variants.Count > 1) {
                        await RenderChooseBuild(variants);
                    }
                    else {
                        RenderInstallsFound();
                    }
                }
                catch (Exception ex) {
                    AppLogger.Critical("Cancel/back from version mismatch failed.", ex);
                }
            };
            MaterialButton install = PrimaryButton("Install anyway", MaterialIcons.Download);
            install.Click += async (s, e) => await ProceedWithInstallAsync(metadata, selectedGameVersion);
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
            _stepChecklist.SetSteps(InstallSteps);
            _stepChecklist.ActiveIndex = 0;
            _currentStepIndex = 0;

            var cancel = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Cancel",
                IconGlyph = MaterialIcons.Cancel,
                Anchor = AnchorStyles.Right,
                Dock = DockStyle.Top,
                Height = Sizes.ButtonHeight,
                Margin = new Padding(0, Tokens.Space6, 0, 0),
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

            string modsPath = entry?.ModsPath ?? string.Empty;
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
            failChecklist.SetSteps(InstallSteps);
            int failIdx = Math.Min(Math.Max(0, _currentStepIndex), InstallSteps.Length - 1);
            failChecklist.ActiveIndex = failIdx;
            failChecklist.SetError(failIdx);
            stepsCard.Controls.Add(failChecklist);
            AddToStack(stack, stepsCard, Sizes.ContentGap);

            MaterialButton openLog = OutlinedButton("Open log", MaterialIcons.Document);
            openLog.Click += (s, e) => OpenInExplorer(AppLogger.LogPath);

            MaterialButton tryAgain = TonalButton("Try again", MaterialIcons.Refresh);
            tryAgain.Click += async (s, e) => {
                if (_lastInstallMetadata != null) {
                    await StartInstallAsync(_lastInstallMetadata, _lastInstallVersion);
                }
                else {
                    RenderInstallsFound();
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
