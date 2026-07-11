using Material3.WinForms;
using Material3.WinForms.Controls;
using Material3.WinForms.Theming;
using Material3.WinForms.Typography;
using Material3.WinForms.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private readonly List<MaterialOptionCard> _buildCards = new List<MaterialOptionCard>();
        private BuildOption? _selectedBuild;
        private int _chooseVersion;
        private CancellationTokenSource? _switchCts;
        private bool _isSwitchingVersion;

        // Canonical display order for the standard build types.
        private static readonly string[] BuildTypeOrder =
            { ModTypes.Ysm, ModTypes.YsmWif, ModTypes.YsmWifWto, ModTypes.Wto };

        private enum BuildSwitchKind { Ready, Downgrade, Upgrade }

        // A single ChooseBuild card: which release we'd install, and whether reaching it costs a
        // game-version switch. Stored as the card's Payload.
        private sealed class BuildOption {
            public ModMetadata Metadata = null!;
            public BuildSwitchKind Kind;
            public int TargetVersion;
            public bool IsManual;
        }

        // One option per mod type: prefer a release that matches the installed build (zero cost);
        // otherwise take the type's latest release and flag whether reaching it means a downgrade
        // (game ahead — needs a Steam beta pin) or an upgrade (game behind — a normal Steam update).
        private List<BuildOption> BuildOptionsModel(int installedVersion) {
            var options = new List<BuildOption>();
            foreach (string type in BuildTypeOrder) {
                List<ModMetadata> releases =
                    _supportedVersions.Where(mod => mod.ModType == type).ToList();
                if (releases.Count == 0) {
                    continue;
                }

                ModMetadata? exact = releases.FirstOrDefault(mod => mod.GameVersion == installedVersion);
                if (exact != null) {
                    options.Add(new BuildOption {
                        Metadata = exact,
                        Kind = BuildSwitchKind.Ready,
                        TargetVersion = installedVersion,
                    });
                    continue;
                }

                ModMetadata latest = releases.OrderByDescending(mod => mod.GameVersion).First();
                options.Add(new BuildOption {
                    Metadata = latest,
                    Kind = latest.GameVersion < installedVersion
                        ? BuildSwitchKind.Downgrade
                        : BuildSwitchKind.Upgrade,
                    TargetVersion = latest.GameVersion,
                });
            }
            return options;
        }

        // ---- Island ----
        private void UpdateIslandForSelection() {
            if (_selectedEntry == null) {
                HideIsland();
                return;
            }

            MaterialButton choose = PrimaryButton("Continue", MaterialIcons.ArrowForward);
            choose.Click += async (s, e) => {
                try {
                    await RenderChooseBuild();
                }
                catch (Exception ex) {
                    AppLogger.Critical("RenderChooseBuild failed.", ex);
                }
            };
            SetIslandActions(choose);
        }

        // ---- Inline build selection ----
        private async Task RenderChooseBuild() {
            // Re-entering ChooseBuild (or any new render) supersedes any prior probe batch; cancel
            // it so two batches don't race against the cards we're about to rebuild.
            _chooseBuildCts?.Cancel();
            _chooseBuildCts?.Dispose();
            _chooseBuildCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _chooseBuildCts.Token;

            _state = AppState.ChooseBuild;
            _chooseVersion = _selectedEntry!.Version;
            _buildCards.Clear();
            _selectedBuild = null;

            SetHeader("Choose a build", "Pick a build to install, or bring your own mod folder.");

            List<BuildOption> options = BuildOptionsModel(_chooseVersion);
            List<BuildOption> ready = options.Where(o => o.Kind == BuildSwitchKind.Ready).ToList();
            List<BuildOption> needsSwitch = options.Where(o => o.Kind != BuildSwitchKind.Ready).ToList();

            TableLayoutPanel stack = NewStack();

            if (ready.Count > 0) {
                AddSectionHeader(stack, $"READY FOR WARNO v{_chooseVersion}");
                foreach (BuildOption option in ready) {
                    AddBuildCard(stack, option);
                }
            }
            else {
                // Nothing is built for the installed build yet (e.g. right after a WARNO patch).
                AddSectionLabel(stack, $"No build is made for your WARNO v{_chooseVersion} yet.");
            }

            if (needsSwitch.Count > 0) {
                AddSectionHeader(stack, "REQUIRES VERSION SWITCH");
                foreach (BuildOption option in needsSwitch) {
                    AddBuildCard(stack, option);
                }
            }

            // Manual lives in its own section so it never reads as part of the switch group; it's
            // always present so even a fully-unsupported build still has a path forward.
            AddSectionHeader(stack, "BRING YOUR OWN");
            MaterialOptionCard manualCard = AddManualCard(stack);

            SetContent(stack, fill: false);

            MaterialOptionCard def =
                _buildCards.FirstOrDefault(IsReadyYsm)
                ?? _buildCards.FirstOrDefault(c => !Option(c).IsManual)
                ?? manualCard;
            def.SetSelected(true);
            _selectedBuild = Option(def);
            UpdateChooseBuildIsland();

            await PopulateBuildSizesAsync(cancellationToken);
        }

        private static BuildOption Option(MaterialOptionCard card) => (BuildOption)card.Payload!;

        private static bool IsReadyYsm(MaterialOptionCard card) {
            BuildOption option = Option(card);
            return !option.IsManual
                && option.Kind == BuildSwitchKind.Ready
                && string.Equals(option.Metadata.ModType, ModTypes.Ysm, StringComparison.Ordinal);
        }

        private void AddBuildCard(TableLayoutPanel stack, BuildOption option) {
            string description = DescribeBuild(option.Metadata.ModType);
            string title = ModTypes.ToDisplayName(option.Metadata.ModType);

            var card = new MaterialOptionCard(title, description, accentSuffix: null, BuildIcons.ForBuild(option.Metadata.ModType)) {
                Height = 70,
                Payload = option,
            };
            // Switch cards flag the build they need as a Warning chip right after the title.
            if (option.Kind != BuildSwitchKind.Ready) {
                card.SetAccentChip($"needs v{option.TargetVersion}",
                    MaterialColors.WarningContainer, MaterialColors.OnWarningContainer, glyph: MaterialIcons.Warning);
            }

            card.SelectedChanged += OnBuildSelected;
            _buildCards.Add(card);
            AddToStack(stack, card, Sizes.ContentGap);
        }

        private MaterialOptionCard AddManualCard(TableLayoutPanel stack) {
            var manualMetadata = new ModMetadata {
                ModType = ModTypes.Manual,
                GameVersion = _chooseVersion,
            };
            string manualDescription = DescribeBuild(ModTypes.Manual);
            var manualCard = new MaterialOptionCard(
                ModTypes.ToDisplayName(ModTypes.Manual),
                manualDescription,
                accentSuffix: null,
                customIcon: null,
                fallbackGlyph: MaterialIcons.Game
            ) {
                Height = 70,
                Payload = new BuildOption {
                    Metadata = manualMetadata,
                    Kind = BuildSwitchKind.Ready,
                    TargetVersion = _chooseVersion,
                    IsManual = true,
                },
            };
            manualCard.SelectedChanged += OnBuildSelected;
            _buildCards.Add(manualCard);
            AddToStack(stack, manualCard, Sizes.ContentGap);
            return manualCard;
        }

        private void AddSectionHeader(TableLayoutPanel stack, string text) {
            var label = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.Overline,
                ForeColor = MaterialColors.OnSurfaceVariant,
                Margin = new Padding(2, 0, 0, 2),
                Text = text,
            };
            AddToStack(stack, label, Sizes.ContentGap);
        }

        private void AddSectionLabel(TableLayoutPanel stack, string text) {
            var label = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialColors.OnSurfaceVariant,
                Margin = new Padding(2, 0, 0, 2),
                Text = text,
            };
            AddToStack(stack, label, Sizes.ContentGap);
        }

        private void OnBuildSelected(MaterialOptionCard selected) {
            foreach (MaterialOptionCard card in _buildCards) {
                if (!ReferenceEquals(card, selected)) {
                    card.SetSelected(false);
                }
            }
            _selectedBuild = Option(selected);
            UpdateChooseBuildIsland();
        }

        private void UpdateChooseBuildIsland() {
            if (_selectedBuild == null) {
                HideIsland();
                return;
            }
            MaterialButton back = TonalButton("Back");
            back.Click += (s, e) => RenderInstallsFound();

            if (_selectedBuild.IsManual) {
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

            if (_selectedBuild.Kind == BuildSwitchKind.Ready) {
                string name = ModTypes.ToDisplayName(_selectedBuild.Metadata.ModType);
                MaterialButton install = PrimaryButton($"Install {name}", MaterialIcons.Download);
                ModMetadata target = _selectedBuild.Metadata;
                install.Click += async (s, e) => {
                    try {
                        await StartInstallAsync(target, _chooseVersion);
                    }
                    catch (Exception ex) {
                        AppLogger.Critical("StartInstall failed (build card).", ex);
                    }
                };
                SetIslandActions(back, install);
                return;
            }

            // Downgrade / Upgrade — route to a dedicated screen that spells out the switch before
            // anything happens.
            BuildOption switchOption = _selectedBuild;
            MaterialButton switchInstall = PrimaryButton("Switch & install", MaterialIcons.ArrowForward);
            switchInstall.Click += (s, e) => {
                try {
                    RenderVersionSwitchPlan(switchOption);
                }
                catch (Exception ex) {
                    AppLogger.Critical("Render version switch plan failed.", ex);
                }
            };
            SetIslandActions(back, switchInstall);
        }

        // The "what will happen" screen for a version switch — mirrors the installation layout
        // (warning card + step checklist). Start runs the real switch: close Steam → rewrite betakey /
        // StateFlags in appmanifest_<appid>.acf → relaunch Steam → poll the download → install.
        private void RenderVersionSwitchPlan(BuildOption option) {
            _state = AppState.VersionSwitchPlan;
            string name = ModTypes.ToDisplayName(option.Metadata.ModType);
            bool isDowngrade = option.Kind == BuildSwitchKind.Downgrade;
            SetHeader("Switch WARNO version", $"{name} needs WARNO v{option.TargetVersion}");

            TableLayoutPanel stack = NewStack();

            MaterialCard warn = BuildMessageCard(
                MaterialIcons.Warning,
                MaterialColors.OnWarningContainer,
                MaterialColors.WarningContainer,
                isDowngrade
                    ? $"WARNO will be rolled back to v{option.TargetVersion}"
                    : $"WARNO will be updated to v{option.TargetVersion}",
                "Steam performs the version change through its Beta system and downloads the matching build. "
                + "Other Steam activity pauses while it runs, and your current version is restored if anything fails."
            );
            AddToStack(stack, warn, Sizes.ContentGap);

            var planCard = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialColors.SurfaceContainer,
                Padding = new Padding(16),
            };
            var checklist = new StepChecklist { Dock = DockStyle.Top, Width = 320 };
            checklist.SetSteps(new[] {
                "Close Steam",
                $"Set WARNO to v{option.TargetVersion} (Steam Betas)",
                $"Steam downloads v{option.TargetVersion}",
                $"Install {name}",
            });
            checklist.ActiveIndex = -1;
            planCard.Controls.Add(checklist);
            AddToStack(stack, planCard, 0);

            SetContent(stack, fill: false);

            MaterialButton back = TonalButton("Back");
            back.Click += async (s, e) => {
                try {
                    await RenderChooseBuild();
                }
                catch (Exception ex) {
                    AppLogger.Critical("Back from version switch plan failed.", ex);
                }
            };
            MaterialButton start = PrimaryButton("Start", MaterialIcons.Download);
            start.Click += (s, e) => {
                try {
                    StartGameVersionSwitch(option);
                }
                catch (Exception ex) {
                    AppLogger.Critical("Start version switch failed.", ex);
                }
            };
            SetIslandActions(back, start);
        }

        private void StartGameVersionSwitch(BuildOption option) {
            if (_selectedEntry == null) {
                return;
            }
            bool isSteam = string.Equals(_selectedEntry.SourceLabel, WarnoExecutableSources.Steam, StringComparison.Ordinal);
            string? manifestPath = SteamVersionSwitcher.FindManifestPath(_selectedEntry.GamePath);
            if (!isSteam || manifestPath == null) {
                UserMessages.ShowNotice(this, "Can't switch automatically",
                    "Automatic version switching needs a Steam-managed WARNO install. Switch the build manually in Steam → WARNO → Properties → Betas.");
                return;
            }
            string? originalBranch = SteamVersionSwitcher.ReadCurrentBranch(manifestPath);
            if (originalBranch == null) {
                UserMessages.ShowNotice(this, "Can't switch automatically",
                    "Couldn't read your current WARNO build from Steam's manifest, so it can't be restored after the switch. Switch the build manually in Steam → WARNO → Properties → Betas.");
                return;
            }
            RenderSwitchingGameVersion(option, manifestPath, originalBranch);
        }

        private void RenderSwitchingGameVersion(BuildOption option, string manifestPath, string originalBranch) {
            _state = AppState.SwitchingGameVersion;
            _isSwitchingVersion = true;
            string name = ModTypes.ToDisplayName(option.Metadata.ModType);
            SetHeader("Switching WARNO version", $"Setting up v{option.TargetVersion} for {name}");
            HideIsland();

            var card = new MaterialCard(Sizes.RadiusMedium) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MaterialColors.SurfaceContainer,
                Padding = new Padding(20),
            };

            (TableLayoutPanel grid, SoftLabel statusLabel, SoftLabel percentLabel) = BuildProgressHeaderRow();

            var progressBar = new MaterialProgressBar {
                Dock = DockStyle.Top,
                Height = 8,
                Margin = new Padding(0, Tokens.Space5, 0, Tokens.Space2),
            };

            var checklist = new StepChecklist {
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                Width = 320,
            };
            checklist.SetSteps(new[] {
                "Close Steam",
                $"Set WARNO to v{option.TargetVersion}",
                $"Download v{option.TargetVersion}",
                $"Install {name}",
            });
            checklist.ActiveIndex = 0;

            MaterialButton cancel = BuildProgressCancelButton();
            cancel.Click += (s, e) => {
                cancel.Enabled = false;
                cancel.Text = "Canceling…";
                _switchCts?.Cancel();
            };

            // Docked Top stacks in reverse insertion order, so add bottom-most first.
            card.Controls.Add(cancel);
            card.Controls.Add(checklist);
            card.Controls.Add(progressBar);
            card.Controls.Add(grid);

            TableLayoutPanel stack = NewStack();
            AddToStack(stack, card, 0);
            SetContent(stack, fill: false);

            var progress = new Progress<SteamVersionSwitcher.SwitchProgress>(p => {
                if (_state != AppState.SwitchingGameVersion) {
                    return;
                }
                statusLabel.Text = SwitchPhaseText(p, option.TargetVersion);
                checklist.ActiveIndex = SwitchStepIndex(p.Phase);
                if (!p.Indeterminate && p.BytesToDownload > 0) {
                    int pct = (int)(100L * p.BytesDownloaded / p.BytesToDownload);
                    progressBar.Value = Math.Min(100, Math.Max(0, pct));
                    percentLabel.Text = progressBar.Value + "%";
                }
                else {
                    progressBar.Value = 0;
                    percentLabel.Text = string.Empty;
                }
            });

            _switchCts?.Dispose();
            _switchCts = new CancellationTokenSource();
            _ = RunSwitchAsync(option, manifestPath, originalBranch, progress, _switchCts.Token);
        }

        private static string SwitchPhaseText(SteamVersionSwitcher.SwitchProgress p, int target) {
            switch (p.Phase) {
                case SteamVersionSwitcher.SwitchPhase.ClosingSteam: return "Closing Steam…";
                case SteamVersionSwitcher.SwitchPhase.SettingVersion: return "Setting target version…";
                case SteamVersionSwitcher.SwitchPhase.StartingSteam: return "Starting Steam…";
                case SteamVersionSwitcher.SwitchPhase.Downloading:
                    return !p.Indeterminate && p.BytesToDownload > 0
                        ? $"Steam is downloading v{target} — {FormatSize(p.BytesDownloaded)} / {FormatSize(p.BytesToDownload)}"
                        : $"Steam is downloading v{target}…";
                case SteamVersionSwitcher.SwitchPhase.Done: return "Ready — starting install…";
                default: return "Working…";
            }
        }

        private static int SwitchStepIndex(SteamVersionSwitcher.SwitchPhase phase) {
            switch (phase) {
                case SteamVersionSwitcher.SwitchPhase.ClosingSteam: return 0;
                case SteamVersionSwitcher.SwitchPhase.SettingVersion:
                case SteamVersionSwitcher.SwitchPhase.StartingSteam: return 1;
                case SteamVersionSwitcher.SwitchPhase.Downloading: return 2;
                case SteamVersionSwitcher.SwitchPhase.Done: return 3;
                default: return 0;
            }
        }

        private async Task RunSwitchAsync(BuildOption option, string manifestPath, string originalBranch,
            IProgress<SteamVersionSwitcher.SwitchProgress> progress, CancellationToken cancellationToken) {
            var switcher = new SteamVersionSwitcher();
            try {
                await switcher.SwitchAndWaitAsync(
                    manifestPath, SteamVersionSwitcher.BranchForVersion(option.TargetVersion), progress, cancellationToken);
                _isSwitchingVersion = false;
                if (IsDisposed || Disposing) {
                    return;
                }
                await StartInstallAsync(option.Metadata, option.TargetVersion);
            }
            catch (OperationCanceledException) {
                _isSwitchingVersion = false;
                bool restored = await switcher.RestoreBranchAsync(manifestPath, originalBranch);
                if (IsDisposed || Disposing) {
                    return;
                }
                if (!restored) {
                    UserMessages.ShowNotice(this, "Switch canceled",
                        "Couldn't automatically restore your previous WARNO version. Re-select your build in Steam → WARNO → Properties → Betas.");
                }
                RenderVersionSwitchPlan(option);
            }
            catch (Exception ex) {
                _isSwitchingVersion = false;
                AppLogger.Critical("Game version switch failed.", ex);
                bool restored = await switcher.RestoreBranchAsync(manifestPath, originalBranch);
                if (IsDisposed || Disposing) {
                    return;
                }
                string restoreNote = restored
                    ? "Your previous version is being restored."
                    : "Couldn't auto-restore — re-select your build in Steam → WARNO → Properties → Betas.";
                UserMessages.ShowError(this, "Version switch failed",
                    $"Could not switch WARNO to v{option.TargetVersion}. {restoreNote}");
                RenderVersionSwitchPlan(option);
            }
        }

        // Shared progress-card pieces, used by the install (Form1.Installation) and version-switch
        // screens so the status/percent row and Cancel button stay identical.
        private static (TableLayoutPanel grid, SoftLabel status, SoftLabel percent) BuildProgressHeaderRow() {
            // Fixed-height, fully docked — an AutoSize TableLayoutPanel with a Percent column degenerates
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
            var status = new SoftLabel {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = MaterialType.BodyMedium,
                ForeColor = MaterialColors.OnSurface,
                Height = 22,
                Text = "Preparing…",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var percent = new SoftLabel {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = MaterialType.TitleMedium,
                ForeColor = MaterialColors.Primary,
                Height = 22,
                Text = "0%",
                TextAlign = ContentAlignment.MiddleRight,
                Width = 60,
            };
            grid.Controls.Add(status, 0, 0);
            grid.Controls.Add(percent, 1, 0);
            return (grid, status, percent);
        }

        private static MaterialButton BuildProgressCancelButton() {
            var cancel = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Cancel",
                IconGlyph = MaterialIcons.Cancel,
                Anchor = AnchorStyles.Right,
                Dock = DockStyle.Top,
                Height = Sizes.ButtonHeight,
                Margin = new Padding(0, Tokens.Space8, 0, 0),
            };
            cancel.SetAccent(MaterialColors.Error, MaterialColors.OnError);
            return cancel;
        }

        private async Task PopulateBuildSizesAsync(CancellationToken cancellationToken) {
            foreach (MaterialOptionCard card in _buildCards.ToList()) {
                if (cancellationToken.IsCancellationRequested || _state != AppState.ChooseBuild || card.IsDisposed) {
                    return;
                }
                ModMetadata variant = Option(card).Metadata;
                bool hasParts = variant.DownloadUrlParts != null && variant.DownloadUrlParts.Length > 0;
                // Manual install has no remote URL to probe — its card stays size-less.
                if (string.IsNullOrWhiteSpace(variant.DownloadUrl) && !hasParts) {
                    continue;
                }
                long? size;
                try {
                    size = hasParts
                        ? await HttpService.TryGetTotalSizeAsync(variant.DownloadUrlParts!, cancellationToken)
                        : await HttpService.TryGetRemoteFileSizeAsync(variant.DownloadUrl, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // Form closed / navigated away mid-probe. Exit silently — outer caller catches
                    // generic Exception and would log this as Critical otherwise.
                    return;
                }
                if (cancellationToken.IsCancellationRequested || _state != AppState.ChooseBuild || card.IsDisposed) {
                    return;
                }
                if (size.HasValue && size.Value > 0) {
                    card.DetailText = FormatSize(size.Value);
                }
                else if (string.Equals(variant.ModType, ModTypes.YsmWif, StringComparison.Ordinal)
                    || string.Equals(variant.ModType, ModTypes.YsmWifWto, StringComparison.Ordinal)) {
                    card.DetailText = "> 2 GB";
                }
            }
        }

        private static string FormatSize(long bytes) {
            double mb = bytes / 1024d / 1024d;
            return mb > 1000
                ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
                : $"{mb:0.0} MB";
        }

        private static string DescribeBuild(string modType) {
            if (modType == ModTypes.YsmWif) {
                return "Yokaiste's Sandbox Mod combined with A World in Flames.";
            }
            if (modType == ModTypes.YsmWifWto) {
                return "Yokaiste's Sandbox Mod combined with A World in Flames and WARNO Tactical Overhaul.";
            }
            if (modType == ModTypes.Wto) {
                return "WARNO Tactical Overhaul — Freedom Decks, Realistic LOS, Unit Speed, 2x Scale.";
            }
            if (modType == ModTypes.Manual) {
                return "Already downloaded a mod? Pick its folder or archive file.";
            }
            return "Yokaiste's Sandbox Mod — an open-source overhaul with deep customization.";
        }
    }
}
