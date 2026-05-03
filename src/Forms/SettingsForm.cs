using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class SettingsForm : Form {
        private readonly DropdownSelect _sourceDropdown;
        private readonly ModCatalogSourceKind _initialSource;

        public SettingsForm() {
            _initialSource = ModCatalogSettings.SelectedSource;

            UiChrome.ApplyDialogChrome(this);
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(390, 320);

            var root = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(16),
                WrapContents = false,
            };

            const int innerWidth = 358;

            Label heading = UiChrome.CreateHeadingLabel("Mod catalog source");
            root.Controls.Add(heading);

            _sourceDropdown = new DropdownSelect {
                Margin = new Padding(0, 0, 0, 4),
                Width = innerWidth,
            };
            _sourceDropdown.AddItem(
                ModCatalogSources.ToDisplayName(ModCatalogSourceKind.OfficialModsList),
                ModCatalogSourceKind.OfficialModsList
            );
            _sourceDropdown.AddItem(
                ModCatalogSources.ToDisplayName(ModCatalogSourceKind.YokaisteGitHubReleases),
                ModCatalogSourceKind.YokaisteGitHubReleases
            );
            _sourceDropdown.SelectByTag(_initialSource);

            root.Controls.Add(_sourceDropdown);

            Label note = UiChrome.CreateMutedLabel(
                "Yokaiste releases fallback to official list if validation fails."
            );
            root.Controls.Add(note);

            var buttonRow = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 8, 0, 0),
                WrapContents = false,
                Width = innerWidth,
            };

            RoundedButton saveButton = UiChrome.CreateDialogButton("Save");
            saveButton.DialogResult = DialogResult.OK;
            saveButton.Click += (sender, args) => SaveSelectedSource();

            buttonRow.Controls.Add(saveButton);

            root.Controls.Add(buttonRow);

            Controls.Add(root);

            // AcceptButton only after Controls.Add — earlier assignment left an invalid parent chain and NRE'd on activate.
            AcceptButton = saveButton;
        }

        public bool SourceChanged => ModCatalogSettings.SelectedSource != _initialSource;

        private void SaveSelectedSource() {
            if (_sourceDropdown.SelectedTag is ModCatalogSourceKind kind) {
                ModCatalogSettings.SelectedSource = kind;
            }
        }
    }
}
