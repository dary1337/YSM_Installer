using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public class WarnoEntryControl : RoundedPanel {
        public event Action<int>? VersionSelected;

        private readonly RoundedBorderButton? _button;
        private readonly Label _label;
        private readonly WarnoEntry _entry;

        public WarnoEntry Entry => _entry;

        public WarnoEntryControl(WarnoEntry entry) {
            _entry = entry;

            Height = Sizes.PanelHeight;
            Padding = new Padding(10);
            BackColor = Theme.PanelBackground;
            SetCornerRadius(16);
            SetOutline(Theme.EntryPanelBorder);

            _label = new Label {
                Text = GetLabelText(),
                AutoSize = true,
                ForeColor = GetLabelColor(),
                Dock = DockStyle.Top,
                Cursor = HasKnownIssuesUrl ? Cursors.Hand : Cursors.Default,
            };

            if (HasKnownIssuesUrl) {
                _button = new RoundedBorderButton(Color.OrangeRed, 16, Color.FromArgb(255, 115, 85)) {
                    Text = "Click to find out why",
                    Cursor = SystemCursors.Pointer,
                    Dock = DockStyle.Bottom,
                    ForeColor = Color.White,
                    Margin = new Padding(0, 6, 0, 0),
                };
                _button.Click += OpenKnownIssues;
                Controls.Add(_button);
            }

            Controls.Add(_label);

            AttachEvents(this);
            AttachEvents(_label);
            if (_button != null)
                AttachEvents(_button, selectOnClick: false);
        }

        private bool HasKnownIssuesUrl =>
            _entry.VersionMetadata != null
            && !string.IsNullOrWhiteSpace(_entry.VersionMetadata.KnownIssuesUrl);

        private string GetLabelText() {
            string sourceText = string.IsNullOrWhiteSpace(_entry.SourceLabel)
                ? string.Empty
                : $", {_entry.SourceLabel}";

            if (_entry.VersionMetadata == null)
                return _entry.LatestCompatibleModVersion > 0
                    ? $"{_entry.ExePath} (v{_entry.Version}{sourceText}, latest mod v{_entry.LatestCompatibleModVersion} available)"
                    : $"{_entry.ExePath} (v{_entry.Version}{sourceText} Not supported)";
            if (HasKnownIssuesUrl)
                return $"{_entry.ExePath} (v{_entry.Version}{sourceText} has issues)";
            return $"{_entry.ExePath} (v{_entry.Version}{sourceText})";
        }

        private Color GetLabelColor() {
            if (_entry.VersionMetadata == null)
                return _entry.LatestCompatibleModVersion > 0 ? Color.Orange : Color.Red;
            if (HasKnownIssuesUrl)
                return Color.OrangeRed;
            return Theme.RecommendedBackground;
        }

        private void AttachEvents(Control control, bool selectOnClick = true) {
            control.Cursor = SystemCursors.Pointer;

            if (selectOnClick)
                control.Click += OnControlClick;
            control.MouseEnter += OnHoverEnter;
            control.MouseLeave += OnHoverLeave;
        }

        private void DetachEvents(Control control, bool selectOnClick = true) {
            if (selectOnClick)
                control.Click -= OnControlClick;
            control.MouseEnter -= OnHoverEnter;
            control.MouseLeave -= OnHoverLeave;
        }

        private void OnControlClick(object sender, EventArgs e) {
            VersionSelected?.Invoke(_entry.Version);
        }

        private void OnHoverEnter(object sender, EventArgs e) {
            BackColor = Theme.PanelBackgroundHover;
        }

        private void OnHoverLeave(object sender, EventArgs e) {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                return;

            if (IsSelected)
                return;

            BackColor = Theme.PanelBackground;
        }

        private bool IsSelected { get; set; }

        public void SetSelected(bool isSelected) {
            IsSelected = isSelected;
            BackColor = isSelected ? Theme.PanelBackgroundHover : Theme.PanelBackground;
        }

        private void OpenKnownIssues(object sender, EventArgs e) {
            var url = _entry.VersionMetadata?.KnownIssuesUrl;
            if (!TryCreateBrowserUrl(url, out Uri browserUrl))
                return;

            try {
                Process.Start(
                    new ProcessStartInfo {
                        FileName = browserUrl.AbsoluteUri,
                        UseShellExecute = true,
                    }
                );
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to open known issues URL.", exception);
            }
        }

        private static bool TryCreateBrowserUrl(string? value, out Uri uri) {
            uri = null!;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri parsedUri)) {
                return false;
            }

            if (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) {
                return false;
            }

            uri = parsedUri;
            return true;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                DetachEvents(this);
                DetachEvents(_label);
                if (_button != null) {
                    DetachEvents(_button, selectOnClick: false);
                    _button.Click -= OpenKnownIssues;
                }
            }

            base.Dispose(disposing);
        }
    }
}
