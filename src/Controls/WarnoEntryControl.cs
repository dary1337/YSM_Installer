using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public class WarnoEntryControl : RoundedPanel {
        public event Action<int>? VersionSelected;
        public event Action? HowToChangeVersionRequested;

        private readonly RoundedButton _howToButton;
        private readonly RoundedBorderButton? _knownIssuesButton;
        private readonly FlowLayoutPanel _actionsPanel;
        private readonly Label _label;
        private readonly WarnoEntry _entry;
        private const int ActionButtonsGap = 8;
        private readonly bool _showHowToButton;

        public WarnoEntry Entry => _entry;

        public WarnoEntryControl(WarnoEntry entry) {
            _entry = entry;
            _showHowToButton = string.Equals(
                _entry.SourceLabel,
                WarnoExecutableSources.Steam,
                StringComparison.Ordinal
            );

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(0, Sizes.PanelHeight);
            Padding = new Padding(10);
            BackColor = Theme.PanelBackground;
            SetCornerRadius(16);
            SetOutline(Theme.EntryPanelBorder);

            _label = new Label {
                Text = GetLabelText(),
                AutoSize = true,
                ForeColor = GetLabelColor(),
                Dock = DockStyle.Top,
                Margin = new Padding(0),
            };

            _actionsPanel = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0, 14, 0, 0),
                BackColor = Theme.PanelBackground,
            };

            _howToButton = new RoundedButton(16, Theme.ButtonBackgroundHover) {
                Text = "How to change Warno version",
                Cursor = SystemCursors.Pointer,
                ForeColor = Theme.ButtonForeground,
                BackColor = Theme.ButtonBackground,
                Margin = new Padding(0, 0, ActionButtonsGap, 0),
                AutoSize = false,
                Height = 34,
            };
            _howToButton.Click += OpenHowToChangeVersion;
            if (_showHowToButton) {
                _actionsPanel.Controls.Add(_howToButton);
            }

            if (HasKnownIssuesUrl) {
                _knownIssuesButton = new RoundedBorderButton(
                    Color.OrangeRed,
                    16,
                    Color.FromArgb(255, 115, 85)
                ) {
                    Text = "Click to find out why",
                    Cursor = SystemCursors.Pointer,
                    ForeColor = Theme.ButtonForeground,
                    AutoSize = false,
                    Height = 34,
                    Margin = new Padding(0),
                };
                _knownIssuesButton.Click += OpenKnownIssues;
                _actionsPanel.Controls.Add(_knownIssuesButton);
            }

            Controls.Add(_actionsPanel);
            Controls.Add(_label);

            AttachEvents(this);
            AttachEvents(_label);
            if (_showHowToButton) {
                AttachEvents(_howToButton, selectOnClick: false);
            }
            if (_knownIssuesButton != null) {
                AttachEvents(_knownIssuesButton, selectOnClick: false);
            }

            SizeChanged += OnSizeChanged;
            UpdateActionButtonsLayout();
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
            _actionsPanel.BackColor = BackColor;
        }

        private void OnHoverLeave(object sender, EventArgs e) {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                return;

            if (IsSelected)
                return;

            BackColor = Theme.PanelBackground;
            _actionsPanel.BackColor = BackColor;
        }

        private bool IsSelected { get; set; }

        public void SetSelected(bool isSelected) {
            IsSelected = isSelected;
            BackColor = isSelected ? Theme.PanelBackgroundHover : Theme.PanelBackground;
            _actionsPanel.BackColor = BackColor;
        }

        private void OnSizeChanged(object sender, EventArgs e) {
            UpdateActionButtonsLayout();
        }

        private void UpdateActionButtonsLayout() {
            if (_actionsPanel.Width <= 0) {
                return;
            }

            int availableWidth = _actionsPanel.ClientSize.Width;
            if (availableWidth <= 0) {
                return;
            }

            int actionButtonsCount = (_showHowToButton ? 1 : 0) + (_knownIssuesButton != null ? 1 : 0);
            if (actionButtonsCount == 0) {
                return;
            }

            if (actionButtonsCount == 1) {
                if (_showHowToButton) {
                    _howToButton.Width = availableWidth;
                    _howToButton.Margin = new Padding(0);
                }

                if (_knownIssuesButton != null) {
                    _knownIssuesButton.Width = availableWidth;
                    _knownIssuesButton.Margin = new Padding(0);
                }

                return;
            }

            int halfWidth = (availableWidth - ActionButtonsGap) / 2;
            if (_showHowToButton) {
                _howToButton.Width = halfWidth;
                _howToButton.Margin = new Padding(0, 0, ActionButtonsGap, 0);
            }

            if (_knownIssuesButton != null) {
                _knownIssuesButton.Width = availableWidth - halfWidth - ActionButtonsGap;
                _knownIssuesButton.Margin = new Padding(0);
            }
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

        private void OpenHowToChangeVersion(object sender, EventArgs e) {
            HowToChangeVersionRequested?.Invoke();
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
                if (_showHowToButton) {
                    DetachEvents(_howToButton, selectOnClick: false);
                }
                _howToButton.Click -= OpenHowToChangeVersion;
                if (_knownIssuesButton != null) {
                    DetachEvents(_knownIssuesButton, selectOnClick: false);
                    _knownIssuesButton.Click -= OpenKnownIssues;
                }
                SizeChanged -= OnSizeChanged;
            }

            base.Dispose(disposing);
        }
    }
}
