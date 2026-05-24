using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private async Task ScanAsync() {
            _isScanning = true;
            _state = AppState.Scanning;
            _showAllEntries = false;
            RenderScanning();

            try {
                ScanResult scanResult = await _scanCoordinator.ScanAsync(_includeSystemFolders);
                _lastScanResult = scanResult;
                _supportedVersions = scanResult.SupportedVersions;
                _entries = scanResult.Entries ?? new List<WarnoEntry>();
                _hasFoundWarnoExe = _entries.Count > 0;

                if (_supportedVersions.Count == 0) {
                    string reason = await Connectivity.DiagnoseCatalogAsync(scanResult.CatalogFallbackReason);
                    RenderCatalogUnavailable(reason);
                    return;
                }

                if (_hasFoundWarnoExe) {
                    RenderInstallsFound();
                }
                else {
                    RenderNotFound();
                }
            }
            catch (Exception exception) {
                // Avoid RenderNotFound: a scanner crash isn't a "not installed" answer, and the manual-browse
                // flow it leads to needs _supportedVersions which may have failed to load.
                AppLogger.Critical("Scan failed.", exception);
                _hasFoundWarnoExe = false;
                // Diagnose actively probes the network so an offline user gets "You appear to be
                // offline" instead of the wrapper's generic "One or more errors occurred".
                Exception root = UnwrapException(exception);
                string reason = await Connectivity.DiagnoseCatalogAsync(root.Message);
                RenderCatalogUnavailable(reason);
            }
            finally {
                _isScanning = false;
            }
        }

        // AggregateException.Message is the useless "One or more errors occurred"; the real cause
        // lives in InnerExceptions[0] (e.g. HttpRequestException → "No such host is known").
        private static Exception UnwrapException(Exception exception) {
            if (exception is AggregateException aggregate) {
                AggregateException flat = aggregate.Flatten();
                if (flat.InnerExceptions.Count > 0) {
                    return flat.InnerExceptions[0];
                }
            }
            return exception.GetBaseException();
        }

        // ---- Scanning ----
        private void RenderScanning() {
            _state = AppState.Scanning;
            SetHeader("Searching for Warno.exe…", "Scanning Steam libraries, registry, common folders");
            HideIsland();

            TableLayoutPanel stack = NewStack();
            AddToStack(stack, new SkeletonCard { Height = 72 }, Sizes.ContentGap);
            AddToStack(stack, new SkeletonCard { Height = 72 }, 0);
            SetContent(stack, fill: false);
        }

        // ---- Not found ----
        private void RenderNotFound() {
            _state = AppState.NotFound;
            bool fullScanDone = _includeSystemFolders;

            SetHeader(
                fullScanDone ? "Warno.exe not found" : "No game found",
                fullScanDone ? "Could not detect a WARNO installation" : "Warno.exe was not detected"
            );
            HideIsland();

            TableLayoutPanel stack = NewStack();

            MaterialCard card = BuildMessageCard(
                MaterialIcons.Search,
                MaterialPalette.OnErrorContainer,
                MaterialPalette.ErrorContainer,
                fullScanDone ? "No Warno installations detected" : "Warno installation not detected",
                fullScanDone
                    ? "Steam libraries, registry, and common folders were scanned."
                    : "Make sure Warno is installed via Steam, or locate the .exe manually."
            );
            AddToStack(stack, card, Sizes.ContentGap);

            MaterialButton browse = OutlinedButton(
                fullScanDone ? "Browse for Warno.exe" : "Browse manually",
                MaterialIcons.Folder
            );
            browse.Click += async (s, e) => {
                try {
                    await BrowseForWarnoAsync();
                }
                catch (Exception ex) {
                    AppLogger.Critical("Browse-for-Warno flow failed.", ex);
                }
            };

            MaterialButton rescan = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Re-scan",
                IconGlyph = MaterialIcons.Refresh,
            };
            rescan.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
            rescan.Click += async (s, e) => {
                try {
                    await ScanAsync();
                }
                catch (Exception ex) {
                    AppLogger.Critical("Re-scan failed.", ex);
                }
            };

            var buttons = new List<MaterialButton> { browse };
            if (!fullScanDone) {
                MaterialButton scanAll = TonalButton("Scan all drives", MaterialIcons.Drive);
                scanAll.Click += async (s, e) => {
                    try {
                        _includeSystemFolders = true;
                        await ScanAsync();
                    }
                    catch (Exception ex) {
                        AppLogger.Critical("Scan-all-drives failed.", ex);
                    }
                };
                buttons.Add(scanAll);
            }
            buttons.Add(rescan);

            // Auto-sized buttons in a left-aligned flow — match the mockup, no forced equal widths.
            var row = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false,
            };
            for (int i = 0; i < buttons.Count; i++) {
                MaterialButton b = buttons[i];
                b.AutoSize = true;
                b.Margin = new Padding(0, 0, i == buttons.Count - 1 ? 0 : Tokens.Space2, 0);
                row.Controls.Add(b);
            }

            AddToStack(stack, row, 0);
            SetContent(stack, fill: false);
        }

        // ---- Catalog unavailable / offline ----
        private void RenderCatalogUnavailable(string reason) {
            _state = AppState.CatalogUnavailable;
            SetHeader("Catalog unavailable", "Couldn't load the mod list");
            HideIsland();

            TableLayoutPanel stack = NewStack();
            MaterialCard card = BuildMessageCard(
                MaterialIcons.Cloud,
                MaterialPalette.OnErrorContainer,
                MaterialPalette.ErrorContainer,
                "Mod list unavailable",
                reason
            );
            AddToStack(stack, card, Sizes.ContentGap);

            MaterialButton retry = TonalButton("Retry", MaterialIcons.Refresh);
            retry.Click += async (s, e) => {
                try {
                    await ScanAsync();
                }
                catch (Exception ex) {
                    AppLogger.Critical("Retry scan failed.", ex);
                }
            };

            MaterialButton help = OutlinedButton("Get help", MaterialIcons.OpenInNew);
            help.Click += (s, e) => AppLinks.Open(AppLinks.Discord);

            AddToStack(stack, BuildButtonRow(retry, help), 0);
            SetContent(stack, fill: false);
        }

        private Task BrowseForWarnoAsync() {
            using (var dialog = new OpenFileDialog()) {
                dialog.Title = "Select Warno.exe";
                dialog.Filter = "WARNO executable (Warno.exe)|Warno.exe|Executable files (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    return Task.CompletedTask;
                }

                var executable = new WarnoExecutable(dialog.FileName, WarnoExecutableSources.Manual);
                List<WarnoEntry> entries = WarnoScanner.Scan(
                    new List<WarnoExecutable> { executable },
                    _supportedVersions
                );

                if (entries.Count == 0) {
                    UserMessages.ShowSelectedWarnoInvalid(this);
                    return Task.CompletedTask;
                }

                WarnoFinder.SaveLastWarnoExecutablePath(dialog.FileName);
                _entries = entries;
                _hasFoundWarnoExe = true;
                RenderInstallsFound();
            }

            return Task.CompletedTask;
        }

        // ---- Installs found ----
        private void RenderInstallsFound() {
            _state = AppState.InstallsFound;
            int count = _entries.Count;

            SetHeader(
                count > 1 ? "Multiple installs found" : "Warno install found",
                BuildScanSummary(count)
            );

            _cards.Clear();
            TableLayoutPanel stack = NewStack();

            var ordered = _entries.OrderByDescending(entry => entry.Version).ToList();
            bool collapsed = !_showAllEntries && ordered.Count > 1;
            var visible = collapsed ? ordered.Take(1).ToList() : ordered;

            foreach (WarnoEntry entry in visible) {
                var card = new MaterialRadioCard(entry) { Height = 72 };
                card.SelectedChanged += OnEntrySelected;
                _cards.Add(card);
                AddToStack(stack, card, Sizes.ContentGap);
            }

            if (collapsed) {
                int more = ordered.Count - 1;
                var showMore = new MaterialButton {
                    Variant = MaterialButtonVariant.Text,
                    Text = $"Show {more} more version{(more == 1 ? string.Empty : "s")}…",
                    IconGlyph = MaterialIcons.ChevronDown,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Dock = DockStyle.None,
                    Margin = new Padding(0, 2, 0, 0),
                };
                showMore.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
                showMore.Click += (s, e) => { _showAllEntries = true; RenderInstallsFound(); };
                stack.Controls.Add(showMore);
            }

            SetContent(stack, fill: false);

            MaterialRadioCard latest = _cards.FirstOrDefault();
            if (latest != null) {
                latest.SetSelected(true);
                _selectedEntry = latest.Entry;
                UpdateIslandForSelection();
            }
            else {
                HideIsland();
            }
        }

        private void OnEntrySelected(MaterialRadioCard selected) {
            foreach (MaterialRadioCard card in _cards) {
                if (!ReferenceEquals(card, selected)) {
                    card.SetSelected(false);
                }
            }
            _selectedEntry = selected.Entry;
            UpdateIslandForSelection();
        }

        private string BuildScanSummary(int count) {
            string summary = $"{count} Warno.exe";
            if (_lastScanResult == null) {
                return summary;
            }
            if (_lastScanResult.UsedCatalogFallback) {
                return $"{summary} · {_lastScanResult.CatalogSourceName} fallback";
            }
            if (!string.IsNullOrWhiteSpace(_lastScanResult.CatalogSourceName)) {
                return $"{summary} · {_lastScanResult.CatalogSourceName}";
            }
            return summary;
        }
    }
}
