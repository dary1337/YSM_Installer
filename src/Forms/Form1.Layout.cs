using System;
using System.Drawing;
using System.Linq;
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
                RowCount = 5,
                BackColor = Color.Transparent,
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _rootLayout.Controls.Add(CreateHeaderLayout(), 0, 0);
            _rootLayout.Controls.Add(CreateEntriesLayout(), 0, 1);
            _rootLayout.Controls.Add(CreateInstallButtonsPanel(), 0, 2);

            Controls.Add(_rootLayout);
        }

        private Control CreateHeaderLayout() {
            var headerLayout = new TableLayoutPanel {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            linkLabel1.Margin = Padding.Empty;
            label1.Margin = new Padding(Sizes.ButtonGap, 0, 0, 0);
            label1.TextAlign = ContentAlignment.MiddleLeft;
            _settingsButton = CreateSettingsButton();

            headerLayout.Controls.Add(linkLabel1, 0, 0);
            headerLayout.Controls.Add(label1, 1, 0);
            headerLayout.Controls.Add(_settingsButton, 2, 0);
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

        private async System.Threading.Tasks.Task OpenSettingsAsync() {
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
            _installControlPanel = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Visible = false,
                WrapContents = false,
            };

            return _installControlPanel;
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
                _entriesLayout.Controls.Remove(panel);
                panel.Dispose();
            }
            _panels.Clear();

            if (_rescanButton != null) {
                _rootLayout.Controls.Remove(_rescanButton);
                _rescanButton.Dispose();
                _rescanButton = null;
            }
        }

        private void ClearInstallControls() {
            foreach (Control control in _installButtons) {
                control.Dispose();
            }
            _installButtons.Clear();

            _installControlPanel.Controls.Clear();
            _installControlPanel.Visible = false;
        }

        private void RelayoutPanels() {
            _entriesLayout.PerformLayout();
        }

        private void RelayoutInstallButtons() {
            int visiblePanelCount = _panels.Count(panel => panel.Visible);
            int topMargin =
                visiblePanelCount > 1
                    ? Sizes.MultipleEntriesInstallGap
                    : Sizes.SingleEntryInstallGap;

            _installControlPanel.Margin = new Padding(0, topMargin, 0, 0);
            _installControlPanel.Visible = _installControlPanel.Controls.Count > 0;
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
