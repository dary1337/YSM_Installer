using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class SettingsForm : Form {
        private readonly DropdownSelect _sourceDropdown;
        private readonly ModCatalogSourceKind _initialSource;

        public SettingsForm() {
            _initialSource = ModCatalogSettings.SelectedSource;

            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 300);
            BackColor = MaterialPalette.Surface;
            ForeColor = MaterialPalette.OnSurface;
            Font = MaterialType.BodyMedium;
            Icon = Properties.Resources.logo;
            WindowChrome.ApplyDark(this);

            const int margin = 24;
            int innerWidth = ClientSize.Width - margin * 2;

            var overline = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.Overline,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(margin, margin),
                Text = "MOD LIST",
            };
            Controls.Add(overline);

            var fieldLabel = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.BodySmall,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(margin, margin + 26),
                Text = "Source",
            };
            Controls.Add(fieldLabel);

            _sourceDropdown = new DropdownSelect {
                Location = new Point(margin, margin + 46),
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
            Controls.Add(_sourceDropdown);

            int noteTop = margin + 46 + Tokens.DropdownHeight + Tokens.Space4;
            var noteIcon = new PictureBox {
                BackColor = Color.Transparent,
                Image = MaterialIconRenderer.Get(MaterialIcons.Info, Tokens.IconXs, MaterialPalette.OnSurfaceMuted),
                Location = new Point(margin, noteTop),
                Size = new Size(Tokens.IconXs, Tokens.IconXs),
                SizeMode = PictureBoxSizeMode.Normal,
            };
            Controls.Add(noteIcon);

            var note = new SoftLabel {
                AutoSize = false,
                Font = MaterialType.BodySmall,
                ForeColor = MaterialPalette.OnSurfaceMuted,
                Location = new Point(margin + 22, noteTop - 1),
                Size = new Size(innerWidth - 22, 40),
                Text = "Loads releases straight from the GitHub repo. Falls back to the official mod list if unreachable.",
            };
            Controls.Add(note);

            var saveButton = new MaterialButton {
                Text = "Save",
                Variant = MaterialButtonVariant.Filled,
                Width = 92,
                Location = new Point(ClientSize.Width - margin - 92, ClientSize.Height - margin - Sizes.ButtonHeight),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK,
            };
            saveButton.Click += (sender, args) => { SaveSelectedSource(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(saveButton);

            var cancelButton = new MaterialButton {
                Text = "Cancel",
                Variant = MaterialButtonVariant.Text,
                Width = 88,
                Location = new Point(ClientSize.Width - margin - 92 - 96, ClientSize.Height - margin - Sizes.ButtonHeight),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel,
            };
            cancelButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public bool SourceChanged => ModCatalogSettings.SelectedSource != _initialSource;

        private void SaveSelectedSource() {
            if (_sourceDropdown.SelectedTag is ModCatalogSourceKind kind) {
                ModCatalogSettings.SelectedSource = kind;
            }
        }
    }
}
