using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private async Task ScanAsync() {
            _isScanning = true;
            ClearDynamicControls();

            try {
                label1.Text = "Searching for Warno.exe...";
                ScanResult scanResult = await _scanCoordinator.ScanAsync(_includeSystemFolders);
                _supportedVersions = scanResult.SupportedVersions;
                if (_supportedVersions.Count == 0) {
                    UserMessages.ShowSupportedVersionsLoadFailed(this);
                }

                var entries = scanResult.Entries;
                if (entries.Count == 0 && _includeSystemFolders) {
                    entries = SelectManualWarnoEntry();
                }

                AddEntryControls(entries);
                SelectLatestEntry(entries);
                AddShowMoreButtonIfNeeded(entries.Count);
                await FinalizeScanUiAsync(scanResult);
            }
            finally {
                _isScanning = false;
            }
        }

        private List<WarnoEntry> SelectManualWarnoEntry() {
            if (UserMessages.ConfirmSelectWarnoManually(this) != DialogResult.Yes) {
                return new List<WarnoEntry>();
            }

            using (var dialog = new OpenFileDialog()) {
                dialog.Title = "Select Warno.exe";
                dialog.Filter =
                    "WARNO executable (Warno.exe)|Warno.exe|Executable files (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    return new List<WarnoEntry>();
                }

                var executable = new WarnoExecutable(
                    dialog.FileName,
                    WarnoExecutableSources.Manual
                );
                var entries = WarnoScanner.Scan(
                    new List<WarnoExecutable> { executable },
                    _supportedVersions
                );
                if (entries.Count > 0) {
                    WarnoFinder.SaveLastWarnoExecutablePath(dialog.FileName);
                }
                else {
                    UserMessages.ShowSelectedWarnoInvalid(this);
                }

                return entries;
            }
        }

        private void AddEntryControls(List<WarnoEntry> entries) {
            foreach (var entry in entries) {
                var control = new WarnoEntryControl(entry) {
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, Sizes.PanelGap),
                    MinimumSize = new Size(0, Sizes.PanelHeight),
                    Visible = false,
                };

                control.VersionSelected += VersionSelected;
                control.HowToChangeVersionRequested += OpenStepsForm;
                _panels.Add(control);
                _entriesLayout.Controls.Add(control);
            }
        }

        private void SelectLatestEntry(List<WarnoEntry> entries) {
            var latest = entries.OrderByDescending(entry => entry.Version).FirstOrDefault();
            if (latest == null) {
                return;
            }

            VersionSelected(latest.Version);

            var selectedPanel = _panels.FirstOrDefault(panel =>
                panel.Entry.Version == latest.Version
            );
            if (selectedPanel != null) {
                selectedPanel.Visible = true;
            }

            RelayoutInstallButtons();
        }

        private void AddShowMoreButtonIfNeeded(int entryCount) {
            if (entryCount <= 1) {
                return;
            }

            _showMoreButton = new RoundedButton(14) {
                Text = $"Show {entryCount - 1} more versions...",
                BackColor = Theme.ButtonBackground,
                ForeColor = Theme.ButtonForeground,
                Margin = new Padding(0, Sizes.ButtonGap, 0, 0),
            };
            _showMoreButton.Click += (sender, args) => ShowAllEntries();

            _entriesLayout.Controls.Add(_showMoreButton);
        }

        private void ShowAllEntries() {
            foreach (var panel in _panels) {
                panel.Visible = true;
            }

            if (_showMoreButton != null) {
                _entriesLayout.Controls.Remove(_showMoreButton);
                _showMoreButton.Dispose();
            }
            _showMoreButton = null;

            RelayoutPanels();
            RelayoutInstallButtons();
            ResizeFormToFitContent();
        }

        private async Task FinalizeScanUiAsync(ScanResult scanResult) {
            label1.Text = BuildScanSummary(scanResult);
            _hasFoundWarnoExe = _panels.Count > 0;

            if (!_hasFoundWarnoExe) {
                await HandleNoWarnoFoundAsync();
                return;
            }

            ResizeFormToFitContent();
        }

        private string BuildScanSummary(ScanResult scanResult) {
            int visibleCount = _panels.Count(panel => panel.Visible);
            int displayedCount = visibleCount > 0 ? visibleCount : _panels.Count;
            string summary = $"Found {displayedCount} Warno.exe";
            if (scanResult.UsedCatalogFallback) {
                return $"{summary} ({scanResult.CatalogSourceName} fallback)";
            }

            if (!string.IsNullOrWhiteSpace(scanResult.CatalogSourceName)) {
                return $"{summary} ({scanResult.CatalogSourceName})";
            }

            return summary;
        }

        private async Task HandleNoWarnoFoundAsync() {
            if (_includeSystemFolders) {
                UserMessages.ShowWarnoNotFound(this);
                return;
            }

            if (UserMessages.ConfirmFullSystemScan(this) == DialogResult.Yes) {
                _includeSystemFolders = true;
                await ScanAsync();
            }
        }
    }
}
