using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class SettingsForm : BorderlessForm {
        private readonly DropdownSelect _sourceDropdown;
        private readonly ModCatalogSourceKind _initialSource;

        public SettingsForm() {
            _initialSource = ModCatalogSettings.SelectedSource;

            Text = "Settings";
            // Fixed-size secondary window — no edge resize; drag from anywhere on the body.
            // Uses native Windows open/close animation (Material 3 fade reserved for actual
            // dialog modals — MaterialDialog).
            EnableEdgeResize = false;
            EnableDragAnywhere = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            // Total height = original content (300) + titlebar (40) so the visible body matches
            // the previous SettingsForm layout exactly.
            ClientSize = new Size(420, 300 + Tokens.TitleBarHeight);
            BackColor = MaterialPalette.Surface;
            ForeColor = MaterialPalette.OnSurface;
            Font = MaterialType.BodyMedium;
            Icon = Properties.Resources.logo;

            const int margin = 24;
            int innerWidth = ClientSize.Width - margin * 2;
            int top = Tokens.TitleBarHeight + margin;

            var overline = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.Overline,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(margin, top),
                Text = "MOD LIST",
            };
            Controls.Add(overline);

            var fieldLabel = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.BodySmall,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Location = new Point(margin, top + 26),
                Text = "Source",
            };
            Controls.Add(fieldLabel);

            _sourceDropdown = new DropdownSelect {
                Location = new Point(margin, top + 46),
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

            int noteTop = top + 46 + Tokens.DropdownHeight + Tokens.Space4;
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

            var titleBar = new MaterialTitleBar {
                TitleText = "Settings",
                AppIcon = Properties.Resources.logo.ToBitmap(),
                ShowMinimize = false,
            };
            Controls.Add(titleBar);

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
