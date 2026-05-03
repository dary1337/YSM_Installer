using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller
{
    public partial class Form1
    {
        private async Task ScanAsync()
        {
            ClearDynamicControls();

            label1.Text = "Searching for Warno.exe...";
            ScanResult scanResult = await _scanCoordinator.ScanAsync(_includeSystemFolders);
            _supportedVersions = scanResult.SupportedVersions;
            if (_supportedVersions.Count == 0)
            {
                UserMessages.ShowSupportedVersionsLoadFailed(this);
            }

            var entries = scanResult.Entries;
            if (entries.Count == 0 && _includeSystemFolders)
            {
                entries = SelectManualWarnoEntry();
            }

            AddEntryControls(entries);
            SelectLatestEntry(entries);
            AddShowMoreButtonIfNeeded(entries.Count);
            await AddRescanButtonAsync();
        }

        private List<WarnoEntry> SelectManualWarnoEntry()
        {
            if (UserMessages.ConfirmSelectWarnoManually(this) != DialogResult.Yes)
            {
                return new List<WarnoEntry>();
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select Warno.exe";
                dialog.Filter = "WARNO executable (Warno.exe)|Warno.exe|Executable files (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return new List<WarnoEntry>();
                }

                var executable = new WarnoExecutable(dialog.FileName, WarnoExecutableSources.Manual);
                var entries = WarnoScanner.Scan(new List<WarnoExecutable> { executable }, _supportedVersions);
                if (entries.Count > 0)
                {
                    WarnoFinder.SaveLastWarnoExecutablePath(dialog.FileName);
                }
                else
                {
                    UserMessages.ShowSelectedWarnoInvalid(this);
                }

                return entries;
            }
        }

        private void AddEntryControls(List<WarnoEntry> entries)
        {
            foreach (var entry in entries)
            {
                var control = new WarnoEntryControl(entry)
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, Sizes.PanelGap),
                    Visible = false
                };

                control.VersionSelected += VersionSelected;
                _panels.Add(control);
                _entriesLayout.Controls.Add(control);
            }
        }

        private void SelectLatestEntry(List<WarnoEntry> entries)
        {
            var latest = entries.OrderByDescending(entry => entry.Version).FirstOrDefault();
            if (latest == null)
            {
                return;
            }

            VersionSelected(latest.Version);

            var selectedPanel = _panels.FirstOrDefault(panel => panel.Entry.Version == latest.Version);
            if (selectedPanel != null)
            {
                selectedPanel.Visible = true;
            }

            RelayoutInstallButtons();
        }

        private void AddShowMoreButtonIfNeeded(int entryCount)
        {
            if (entryCount <= 1)
            {
                return;
            }

            _showMoreButton = new RoundedButton(14)
            {
                Text = $"Show {entryCount - 1} more versions...",
                BackColor = Color.FromArgb(38, 44, 60),
                ForeColor = Color.White,
                Margin = new Padding(0, Sizes.ButtonGap, 0, 0),
            };
            _showMoreButton.Click += (sender, args) => ShowAllEntries();

            _entriesLayout.Controls.Add(_showMoreButton);
        }

        private void ShowAllEntries()
        {
            foreach (var panel in _panels)
            {
                panel.Visible = true;
            }

            if (_showMoreButton != null)
            {
                _entriesLayout.Controls.Remove(_showMoreButton);
                _showMoreButton.Dispose();
            }
            _showMoreButton = null;

            RelayoutPanels();
            RelayoutInstallButtons();
            ResizeFormToFitContent();
        }

        private async Task AddRescanButtonAsync()
        {
            _rescanButton = new RoundedButton(14)
            {
                Dock = DockStyle.Bottom,
                Text = "Rescan",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(38, 44, 60),
            };
            _rescanButton.Click += async (sender, args) => await ScanAsync();
            _rootLayout.Controls.Add(_rescanButton, 0, 4);

            label1.Text = $"Found {_panels.Count} Warno.exe";

            if (_panels.Count == 0)
            {
                await HandleNoWarnoFoundAsync();
                return;
            }

            ResizeFormToFitContent();
        }

        private async Task HandleNoWarnoFoundAsync()
        {
            if (_includeSystemFolders)
            {
                UserMessages.ShowWarnoNotFound(this);
                return;
            }

            if (UserMessages.ConfirmFullSystemScan(this) == DialogResult.Yes)
            {
                _includeSystemFolders = true;
                await ScanAsync();
            }
        }
    }
}
