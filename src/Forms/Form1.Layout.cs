using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private void BuildLayout() {
            Controls.Remove(label1);
            Controls.Remove(linkLabel1);

            Padding = new Padding(Sizes.FormPadding);
            MinimumSize = new Size(Sizes.MinimumFormWidth, Sizes.MinimumFormHeight);

            _rootLayout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _rootLayout.Controls.Add(CreateHeaderLayout(), 0, 0);
            _rootLayout.Controls.Add(CreateEntriesLayout(), 0, 1);
            _rootLayout.Controls.Add(CreateInstallButtonsPanel(), 0, 3);

            Controls.Add(_rootLayout);
        }

        private Control CreateHeaderLayout() {
            var headerLayout = new TableLayoutPanel {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            label1.Margin = Padding.Empty;
            label1.Dock = DockStyle.Fill;
            label1.TextAlign = ContentAlignment.MiddleLeft;
            _settingsButton = CreateSettingsButton();

            headerLayout.Controls.Add(label1, 0, 0);
            headerLayout.Controls.Add(_settingsButton, 1, 0);
            return headerLayout;
        }

        private RoundedButton CreateSettingsButton() {
            var button = new RoundedButton(14) {
                AutoSize = true,
                BackColor = Theme.ButtonBackground,
                ForeColor = Theme.ButtonForeground,
                Margin = Padding.Empty,
                Text = "Settings",
            };

            button.Click += async (sender, args) => await OpenSettingsAsync();
            return button;
        }

        private async Task OpenSettingsAsync() {
            try {
                using (var form = new SettingsForm()) {
                    if (form.ShowDialog(this) == DialogResult.OK && form.SourceChanged) {
                        await ScanAsync();
                    }
                }
            }
            catch (Exception ex) {
                AppLogger.Critical("Settings dialog failed.", ex);
                MessageBox.Show(
                    $"{ex.GetType().Name}: {ex.Message}\n\n{(ex.StackTrace ?? string.Empty)}",
                    "YSM Installer — Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private Control CreateEntriesLayout() {
            _entriesLayout = new TableLayoutPanel {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, Sizes.HeaderToEntriesGap, 0, 0),
                Padding = Padding.Empty,
            };
            _entriesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return _entriesLayout;
        }

        private Control CreateInstallButtonsPanel() {
            _installIslandPanel = new RoundedPanel(16) {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, Sizes.ButtonGap, 0, 0),
                Padding = new Padding(6),
                BackColor = Color.FromArgb(26, 30, 41),
                Visible = false,
            };
            _installIslandPanel.SetOutline(Theme.EntryPanelBorder);
            _installIslandPanel.SizeChanged += (sender, args) => UpdateInstallButtonsLayout();

            _installProgressBar = new InstallProgressBar {
                Visible = false,
            };

            _installControlPanel = new FlowLayoutPanel {
                AutoSize = false,
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 4, 0, 0),
                Padding = Padding.Empty,
                Visible = false,
                WrapContents = false,
            };
            _installIslandPanel.Controls.Add(_installControlPanel);
            _installIslandPanel.Controls.Add(_installProgressBar);

            return _installIslandPanel;
        }

        private void ClearDynamicControls() {
            ClearInstallControls();

            if (_showMoreButton != null) {
                _entriesLayout.Controls.Remove(_showMoreButton);
                _showMoreButton.Dispose();
                _showMoreButton = null;
            }

            foreach (var panel in _panels) {
                panel.VersionSelected -= VersionSelected;
                panel.HowToChangeVersionRequested -= OpenStepsForm;
                _entriesLayout.Controls.Remove(panel);
                panel.Dispose();
            }
            _panels.Clear();

        }

        private void ClearInstallControls() {
            foreach (Control control in _installButtons) {
                control.Dispose();
            }
            _installButtons.Clear();
            _lastInstallButtonsLayoutWidth = 0;
            _lastInstallButtonsCount = 0;

            _installControlPanel.Controls.Clear();
            _installControlPanel.Visible = false;
            _installControlPanel.Margin = Padding.Empty;
            _installProgressBar.Value = 0;
            _installProgressBar.Visible = false;
            _installIslandPanel.Visible = false;
        }

        private void RelayoutPanels() {
            _entriesLayout.PerformLayout();
        }

        private void RelayoutInstallButtons() {
            bool hasButtons = _installControlPanel.Controls.Count > 0;
            _installControlPanel.Visible = hasButtons;
            _installIslandPanel.Visible = hasButtons;
            UpdateInstallButtonsLayout();
        }

        private void UpdateInstallButtonsLayout() {
            if (_installButtons.Count == 0) {
                return;
            }

            int availableWidth = _installControlPanel.ClientSize.Width;
            if (availableWidth <= 0) {
                return;
            }
            if (
                _lastInstallButtonsLayoutWidth == availableWidth
                && _lastInstallButtonsCount == _installButtons.Count
            ) {
                return;
            }

            int gap = Sizes.ButtonGap;
            int buttonCount = _installButtons.Count;
            int totalGaps = gap * (buttonCount - 1);
            int targetWidth = Math.Max(80, (availableWidth - totalGaps) / buttonCount);
            int targetHeight = 0;

            foreach (RoundedButton button in _installButtons) {
                targetHeight = Math.Max(targetHeight, Math.Max(34, button.PreferredSize.Height));
            }

            for (int index = 0; index < _installButtons.Count; index++) {
                RoundedButton button = _installButtons[index];
                button.AutoSize = false;
                button.Width = index == _installButtons.Count - 1
                    ? availableWidth - (targetWidth * (buttonCount - 1)) - totalGaps
                    : targetWidth;
                button.Height = targetHeight;
                button.Margin = index == _installButtons.Count - 1
                    ? new Padding(0)
                    : new Padding(0, 0, gap, 0);
            }

            _installControlPanel.Height = targetHeight;
            _lastInstallButtonsLayoutWidth = availableWidth;
            _lastInstallButtonsCount = _installButtons.Count;
        }

        private void ResizeFormToFitContent() {
            Width = Math.Max(Sizes.MinimumFormWidth, Width);
            Height = Math.Max(
                Sizes.MinimumFormHeight,
                _rootLayout.PreferredSize.Height + Padding.Vertical + Sizes.ContentBottomPadding
            );
            MinimumSize = new Size(Width, Height);
        }
    }
}
