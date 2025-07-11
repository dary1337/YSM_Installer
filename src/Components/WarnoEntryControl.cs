using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public class WarnoEntryControl : RoundedPanel {
        public static event Action<int> VersionSelected;

        private readonly RoundedBorderButton _button;
        private readonly Label _label;
        public readonly WarnoEntry _entry;

        public WarnoEntryControl(WarnoEntry entry) {
            _entry = entry;

            Height = Sizes.PanelHeight;
            Padding = new Padding(8);
            BackColor = Theme.PanelBackground;
            setCornerRadius(14);

            _label = new Label {
                Text = GetLabelText(),
                AutoSize = true,
                ForeColor = GetLabelColor(),
                Dock = DockStyle.Top,
                Cursor = HasKnownIssuesUrl ? Cursors.Hand : Cursors.Default,
            };

            if (HasKnownIssuesUrl) {
                _button = new RoundedBorderButton(Color.OrangeRed, 16) {
                    Text = "Click to find out why",
                    Cursor = SystemCursors.Pointer,
                    Dock = DockStyle.Bottom,
                    ForeColor = Color.White,
                };
                _button.Click += OpenKnownIssues;
                Controls.Add(_button);
            }

            Controls.Add(_label);

            AttachEvents(this);
            AttachEvents(_label);
            if (_button != null)
                AttachEvents(_button);

            VersionSelected += UpdateBackgroundState;
            UpdateBackgroundState(_entry.Version);
        }

        private bool HasKnownIssuesUrl =>
            _entry.VersionMetadata != null &&
            !string.IsNullOrWhiteSpace(_entry.VersionMetadata.KnownIssuesUrl);

        private string GetLabelText() {
            if (_entry.VersionMetadata == null)
                return $"{_entry.ExePath} (v{_entry.Version} Not supported)";
            if (HasKnownIssuesUrl)
                return $"{_entry.ExePath} (v{_entry.Version} has issues)";
            return $"{_entry.ExePath} (v{_entry.Version})";
        }

        private Color GetLabelColor() {
            if (_entry.VersionMetadata == null)
                return Color.Red;
            if (HasKnownIssuesUrl)
                return Color.OrangeRed;
            return Theme.RecommendedBackground;
        }

        private void AttachEvents(Control control) {
            control.Cursor = SystemCursors.Pointer;

            control.Click += OnControlClick;
            control.MouseEnter += OnHoverEnter;
            control.MouseLeave += OnHoverLeave;
        }

        private void DetachEvents(Control control) {
            control.Click -= OnControlClick;
            control.MouseEnter -= OnHoverEnter;
            control.MouseLeave -= OnHoverLeave;
        }

        private void OnControlClick(object sender, EventArgs e) {
            if (Warno.selectedVersion == _entry.Version)
                return;

            Warno.selectedVersion = _entry.Version;
            VersionSelected?.Invoke(_entry.Version);
        }

        private void OnHoverEnter(object sender, EventArgs e) {
            BackColor = Theme.PanelBackgroundHover;
        }

        private void OnHoverLeave(object sender, EventArgs e) {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                return;

            if (Warno.selectedVersion == _entry.Version)
                return;

            BackColor = Theme.PanelBackground;
        }

        private void UpdateBackgroundState(int version) {
            BackColor = Warno.selectedVersion == _entry.Version
                ? Theme.PanelBackgroundHover
                : Theme.PanelBackground;
        }

        private void OpenKnownIssues(object sender, EventArgs e) {
            var url = _entry.VersionMetadata?.KnownIssuesUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            Task.Run(() => {
                try {
                    Process.Start(new ProcessStartInfo {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            });
        }

        public static void RaiseVersionSelected(int version) {
            VersionSelected?.Invoke(version);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                VersionSelected -= UpdateBackgroundState;

                DetachEvents(this);
                DetachEvents(_label);
                if (_button != null)
                    DetachEvents(_button);

                _label.Click -= OpenKnownIssues;
            }

            base.Dispose(disposing);
        }
    }
}
