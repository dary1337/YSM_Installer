using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                    ConfirmLowDiskSpaceAsync,
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
                    UserMessages.ShowError(this, "Installation in progress",
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

        // Task-returning so WarnoInstaller can await us from its background extraction thread,
        // even though the dialog itself is synchronous.
        private Task<bool> ConfirmLowDiskSpaceAsync(DiskSpaceWarning warning) {
            // Form may have started disposing while extraction was mid-flight (e.g. user closed
            // the window). Invoke against a dead handle would throw — default to "don't proceed"
            // so the install fails closed via InstallDeclinedByUserException.
            if (IsDisposed || !IsHandleCreated) {
                return Task.FromResult(false);
            }
            if (InvokeRequired) {
                // Race: form disposed between the IsDisposed/IsHandleCreated check above and
                // Invoke firing. Treat as "declined" so InstallDeclinedByUserException routes
                // through Cancelled instead of bubbling up as a Failed install.
                // InvalidOperationException covers ObjectDisposedException (its subclass) too —
                // both fire when Invoke targets a handle that's gone away mid-flight.
                try {
                    return (Task<bool>)Invoke(new Func<Task<bool>>(() => ConfirmLowDiskSpaceAsync(warning)));
                }
                catch (InvalidOperationException) {
                    return Task.FromResult(false);
                }
            }
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialPalette.Warning;
                dialog.TitleText = "Low disk space";
                dialog.BodyText =
                    $"{warning.Message}\n\nContinue anyway?";
                dialog.AddAction("Cancel install", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Continue anyway", DialogResult.OK, MaterialButtonVariant.Filled);
                bool proceed = dialog.ShowDialog(this) == DialogResult.OK;
                return Task.FromResult(proceed);
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
                dialog.BodyText = "WARNO will be closed and all other mods will be disabled for compatibility.";
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
            // Progress<int> callbacks marshal asynchronously, so a final report can land after
            // the install finished and RenderComplete already cleared the taskbar. Ignoring
            // them once we've left the Installing state prevents a late SetProgressValue from
            // re-lighting the taskbar green (the API auto-switches state to Normal on SetValue).
            if (_state != AppState.Installing || _progressBar == null) {
                return;
            }
            // Bytes-based progress arrives from multiple phases (download, then extraction);
            // each phase starts at 0%, so without this guard the bar would drop back to 0 when
            // extraction kicks in after a finished download.
            if (percent < _progressBar.Value) {
                return;
            }
            _progressBar.Value = percent;
            TaskbarProgress.SetValue(this, percent);
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
            SetHeader("Version mismatch", "Mod targets a different WARNO build");

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
                $"{name} targets WARNO v{metadata.GameVersion}",
                $"You have v{selectedGameVersion}. The mod may load but is not guaranteed to work."
            );
            AddToStack(stack, warn, isSteamEntry ? Sizes.ContentGap : 0);

            if (isSteamEntry) {
                MaterialCard guide = BuildGuideCard(
                    "How to switch WARNO versions",
                    "Steam → right-click WARNO → Betas",
                    OpenStepsForm
                );
                AddToStack(stack, guide, 0);
            }

            SetContent(stack, fill: false);

            MaterialButton back = TonalButton("Back");
            back.Click += async (s, e) => {
                try {
                    List<ModMetadata> variants = GetVariantsForVersion(selectedGameVersion);
                    await RenderChooseBuild(variants);
                }
                catch (Exception ex) {
                    AppLogger.Critical("Back from version mismatch failed.", ex);
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
            SetIslandActions(back, install);
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
            // A prior backgrounded-complete may have armed the clear-on-activate beacon; a new
            // install supersedes it so it can't wipe this run's fresh progress on refocus.
            _clearTaskbarOnActivate = false;
            SetHeader("Installing", $"{modName} → {PathFormatting.Shorten(gamePath, 60)}");
            HideIsland();
            TaskbarProgress.SetState(this, TaskbarProgress.State.Normal);
            TaskbarProgress.SetValue(this, 0);

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
            // If the user is looking at us, drop the taskbar progress right away. If we're in
            // the background (they tabbed out — likely playing a game), freeze it green at 100%
            // as a "done, come back" beacon and clear it only when they refocus the window.
            if (Form.ActiveForm == this) {
                TaskbarProgress.Clear(this);
            }
            else {
                TaskbarProgress.SetState(this, TaskbarProgress.State.Normal);
                TaskbarProgress.SetValue(this, 100);
                _clearTaskbarOnActivate = true;
            }
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
            // PictureBox doesn't own its Image — without this each Complete render leaks a
            // fresh 64x64 Bitmap (and its GDI handle) until the process exits.
            check.Disposed += (s, e) => check.Image?.Dispose();
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
            const int folderCardWidth = 500;
            var folderCard = new MaterialCard(Sizes.RadiusSmall) {
                Anchor = AnchorStyles.None,
                BackColor = MaterialPalette.SurfaceContainerHigh,
                Margin = Padding.Empty,
                Size = new Size(folderCardWidth, 48),
            };
            var folderLabel = new SoftLabel {
                AutoEllipsis = true,
                AutoSize = false,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(14, 14),
                Size = new Size(390, 20),
                Text = PathFormatting.Shorten(modsPath, 60),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var openButton = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Open",
                IconGlyph = MaterialIcons.OpenFolder,
                Location = new Point(folderCardWidth - 92, 4),
                Size = new Size(84, 40),
            };
            openButton.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
            openButton.Click += (s, e) => ShellOpen.RevealInExplorer(modsPath);
            folderCard.Controls.Add(openButton);
            folderCard.Controls.Add(folderLabel);
            stack.Controls.Add(folderCard);

            center.Controls.Add(stack, 0, 1);
            SetContent(center, fill: true);

            MaterialButton again = TonalButton("Switch build", MaterialIcons.Refresh);
            again.Click += (s, e) => RenderInstallsFound();
            MaterialButton launch = PrimaryButton("Launch WARNO", MaterialIcons.Play);
            launch.Click += (s, e) => LaunchWarno(entry);
            SetIslandActions(again, launch);
        }

        // ---- Failed state ----
        private void RenderFailed() {
            // Briefly flash the taskbar red so the user notices on a backgrounded window.
            // Cleared next time the user enters any other state.
            TaskbarProgress.SetState(this, TaskbarProgress.State.Error);
            TaskbarProgress.SetValue(this, 100);
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
            openLog.Click += (s, e) => UserMessages.OpenLog();

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

        private static string ModKey(ModMetadata metadata, int version) {
            return $"{metadata.ModType}:{version}";
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
