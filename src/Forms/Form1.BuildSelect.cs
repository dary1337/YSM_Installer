using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
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
                    title += $" (latest v{variant.GameVersion})";
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
                bool hasParts = variant.DownloadUrlParts != null && variant.DownloadUrlParts.Length > 0;
                // Manual install has no remote URL to probe — its card stays size-less.
                if (string.IsNullOrWhiteSpace(variant.DownloadUrl) && !hasParts) {
                    continue;
                }
                long? size = hasParts
                    ? await HttpService.TryGetTotalSizeAsync(variant.DownloadUrlParts!)
                    : await HttpService.TryGetRemoteFileSizeAsync(variant.DownloadUrl);
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
                return ("Yokaiste's Sandbox Mod combined with A World in Flames.", false);
            }
            if (modType == ModTypes.YsmWifWto) {
                return ("Yokaiste's Sandbox Mod combined with A World in Flames and WARNO Tactical Overhaul.", false);
            }
            if (modType == ModTypes.Wto) {
                return ("WARNO Tactical Overhaul — Freedom Decks, Realistic LOS, Unit Speed, 2x Scale.", false);
            }
            if (modType == ModTypes.Manual) {
                return ("Install from a local folder or an archive you already have.", false);
            }
            return ("Yokaiste's Sandbox Mod — an open-source overhaul with deep customization.", false);
        }
    }
}
