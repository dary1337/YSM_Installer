using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Reusable Material 3 basic dialog: leading icon, title, supporting text and right-aligned text/
    /// filled actions on a rounded elevated surface (max-width 420, padding 24, M3 elevation level 3).
    /// </summary>
    public sealed class MaterialDialog : Form {
        private readonly List<MaterialButton> _actions = new List<MaterialButton>();
        private readonly List<(string text, string icon, Action onClick)> _links = new List<(string, string, Action)>();
        private string _iconGlyph = string.Empty;
        private Color _iconColor = MaterialPalette.Primary;
        private string _titleText = string.Empty;
        private string _bodyText = string.Empty;
        private MaterialButton? _defaultButton;

        private const int Pad = Sizes.DialogPadding;
        private const int Width420 = Sizes.DialogMaxWidth;
        // Expanded width for huge bodies (e.g. GitHub-sized release notes). Modest bump — wide
        // enough to relieve wrapping density, narrow enough to still read as a modal.
        private const int WidthExpanded = 560;

        public MaterialDialog() {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = MaterialPalette.SurfaceContainerHigh;
            ForeColor = MaterialPalette.OnSurface;
            Font = MaterialType.BodyMedium;
            Width = Width420;
            DoubleBuffered = true;
            KeyPreview = true;
            // Start invisible so the Material fade-in (in OnLoad) drives the open animation
            // instead of Windows' native fade. FormBorderStyle.None popups don't get a great
            // native animation anyway, so the M3 ease-out feels noticeably better here.
            Opacity = 0d;
            SetStyle(ControlStyles.ResizeRedraw, true);
            // Modal dialogs grab-anywhere — user can lift the dialog by clicking on the body
            // text, the icon, or any empty space (skips buttons / links via the helper).
            FormDragAnywhere.Enable(this);
        }

        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                // CS_DROPSHADOW — native drop shadow for borderless popup-style windows.
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        public string IconGlyph { get => _iconGlyph; set => _iconGlyph = value ?? string.Empty; }
        public Color IconColor { get => _iconColor; set => _iconColor = value; }
        public string TitleText { get => _titleText; set => _titleText = value ?? string.Empty; }
        public string BodyText { get => _bodyText; set => _bodyText = value ?? string.Empty; }

        /// <summary>Set by the action whose <c>tag</c> overload was clicked (e.g. the chosen mod variant).</summary>
        public object? ResultTag { get; private set; }

        /// <summary>Adds a non-dismissing inline link button (e.g. "Open log", "Report an issue").</summary>
        public void AddLink(string text, string icon, Action onClick) {
            _links.Add((text, icon, onClick));
        }

        /// <summary>Action that carries a payload tag instead of relying on a standard DialogResult.</summary>
        public void AddAction(string text, object tag, MaterialButtonVariant variant, Color accent = default, Color onAccent = default) {
            var button = new MaterialButton {
                Text = text,
                Variant = variant,
                AutoSize = false,
                Height = Sizes.ButtonHeight,
            };
            if (accent.A > 0) {
                button.SetAccent(accent, onAccent.A > 0 ? onAccent : MaterialPalette.OnPrimary);
            }
            using (Graphics g = CreateGraphics()) {
                SizeF size = g.MeasureString(text, button.Font);
                button.Width = (int)Math.Ceiling(size.Width) + 40;
            }
            button.Click += (s, e) => { ResultTag = tag; DialogResult = DialogResult.OK; Close(); };
            _actions.Add(button);
            if (variant == MaterialButtonVariant.Filled) {
                _defaultButton = button;
            }
        }

        public void AddAction(string text, DialogResult result, MaterialButtonVariant variant, Color accent = default, Color onAccent = default) {
            var button = new MaterialButton {
                Text = text,
                Variant = variant,
                DialogResult = result,
                AutoSize = false,
                Height = Sizes.ButtonHeight,
            };
            if (accent.A > 0) {
                button.SetAccent(accent, onAccent.A > 0 ? onAccent : MaterialPalette.OnPrimary);
            }
            using (Graphics g = CreateGraphics()) {
                SizeF size = g.MeasureString(text, button.Font);
                button.Width = (int)Math.Ceiling(size.Width) + 40;
            }
            button.Click += (s, e) => { DialogResult = result; Close(); };
            _actions.Add(button);
            if (variant == MaterialButtonVariant.Filled) {
                _defaultButton = button;
            }
        }

        protected override void OnLoad(EventArgs e) {
            // Resize + layout BEFORE Show so CenterParent positions the dialog using its
            // final dimensions — no visible jump from CenterParent then post-show recenter.
            AdjustWidthForBody();
            BuildLayout();
            ApplyRoundedRegion();
            // If width changed past CenterParent's calc, re-center against owner so the
            // open animation starts from the right anchor point.
            RecenterOnOwner();
            base.OnLoad(e);
            _ = FormAnimation.OpenAsync(this);
        }

        private void RecenterOnOwner() {
            Rectangle anchor = Owner != null
                ? Owner.Bounds
                : (Screen.FromControl(this) ?? Screen.PrimaryScreen)!.WorkingArea;
            Location = new Point(
                anchor.X + (anchor.Width - Width) / 2,
                anchor.Y + (anchor.Height - Height) / 2
            );
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if (_defaultButton != null) {
                AcceptButton = _defaultButton;
                _defaultButton.Focus();
            }
        }

        private bool _animatingClose;
        private DialogResult _pendingDialogResult;

        protected override void OnFormClosing(FormClosingEventArgs e) {
            base.OnFormClosing(e);
            if (e.Cancel || _animatingClose) {
                return;
            }
            if (e.CloseReason != CloseReason.UserClosing) {
                return;
            }
            // WinForms resets DialogResult to None when a Close() is cancelled (e.Cancel = true)
            // mid-flight, which would lose the button's result (Yes/OK/Cancel) across the fade
            // animation. Snapshot it now and restore before the real close.
            _pendingDialogResult = DialogResult;
            _animatingClose = true;
            e.Cancel = true;
            _ = FadeOutThenCloseAsync();
        }

        private async Task FadeOutThenCloseAsync() {
            await FormAnimation.CloseAsync(this);
            if (!IsDisposed) {
                DialogResult = _pendingDialogResult;
                Close();
            }
        }

        // For wall-of-text bodies (release notes), widen the dialog modestly so the column
        // isn't 372 px narrow and the text doesn't read like a tall ribbon. Heuristic: if the
        // body at narrow width would scroll more than ~1.5× the height cap, expand width.
        private void AdjustWidthForBody() {
            if (string.IsNullOrEmpty(_bodyText)) {
                return;
            }
            int narrowInner = Width420 - Pad * 2 - MaterialScrollPanel.TrackWidth;
            int narrowHeight = MeasureBodyHeight(_bodyText, narrowInner);
            int cap = ComputeMaxBodyHeight();
            if (narrowHeight > cap * 3 / 2) {
                Width = WidthExpanded;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Escape) {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildLayout() {
            Controls.Clear();
            int innerWidth = Width - Pad * 2;
            int y = Pad;

            if (!string.IsNullOrEmpty(_iconGlyph)) {
                var icon = new PictureBox {
                    BackColor = Color.Transparent,
                    Image = MaterialIconRenderer.Get(_iconGlyph, 26, _iconColor),
                    Location = new Point(Pad, y),
                    Size = new Size(26, 26),
                    SizeMode = PictureBoxSizeMode.Normal,
                };
                Controls.Add(icon);
                y += 38;
            }

            if (!string.IsNullOrEmpty(_titleText)) {
                var title = new SoftLabel {
                    AutoSize = false,
                    Font = MaterialType.TitleLarge,
                    ForeColor = MaterialPalette.OnSurface,
                    Text = _titleText,
                    Location = new Point(Pad, y),
                    Width = innerWidth,
                    Height = 28,
                    BackColor = Color.Transparent,
                };
                Controls.Add(title);
                y += 36;
            }

            if (!string.IsNullOrEmpty(_bodyText)) {
                // Measure at the narrow width first because that's what the body will use if
                // it ends up wrapped in a scroll panel. If the narrow-width height already fits
                // under the cap, use the full inner width (less wrap = tighter look).
                int narrowWidth = Math.Max(40, innerWidth - MaterialScrollPanel.TrackWidth);
                int narrowHeight = MeasureBodyHeight(_bodyText, narrowWidth);
                int bodyCap = ComputeMaxBodyHeight();

                if (narrowHeight <= bodyCap) {
                    int simpleHeight = MeasureBodyHeight(_bodyText, innerWidth);
                    var body = new SoftLabel {
                        AutoSize = false,
                        Font = MaterialType.BodyMedium,
                        ForeColor = MaterialPalette.OnSurfaceVariant,
                        Text = _bodyText,
                        Location = new Point(Pad, y),
                        Width = innerWidth,
                        Height = simpleHeight,
                        BackColor = Color.Transparent,
                    };
                    Controls.Add(body);
                    y += simpleHeight + Pad;
                }
                else {
                    var scroll = new MaterialScrollPanel {
                        BackColor = Color.Transparent,
                        Location = new Point(Pad, y),
                        Size = new Size(innerWidth, bodyCap),
                        Margin = Padding.Empty,
                        Padding = Padding.Empty,
                    };
                    var body = new SoftLabel {
                        AutoSize = false,
                        Font = MaterialType.BodyMedium,
                        ForeColor = MaterialPalette.OnSurfaceVariant,
                        Text = _bodyText,
                        Location = Point.Empty,
                        Width = narrowWidth,
                        Height = narrowHeight,
                        BackColor = Color.Transparent,
                    };
                    scroll.ContentPanel.Controls.Add(body);
                    Controls.Add(scroll);
                    y += bodyCap + Pad;
                }
            }
            else {
                y += Pad;
            }

            if (_links.Count > 0) {
                var linkRow = new FlowLayoutPanel {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = Color.Transparent,
                    FlowDirection = FlowDirection.LeftToRight,
                    Location = new Point(Pad, y),
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    WrapContents = true,
                    Width = Width - Pad * 2,
                };
                foreach ((string text, string icon, Action onClick) in _links) {
                    var link = new MaterialButton {
                        Variant = MaterialButtonVariant.Text,
                        Text = text,
                        IconGlyph = icon,
                        AutoSize = true,
                        Height = Tokens.BtnTextHeight,
                        Margin = new Padding(0, 0, Tokens.Space2, 0),
                    };
                    link.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
                    Action captured = onClick;
                    link.Click += (s, e) => captured();
                    linkRow.Controls.Add(link);
                }
                Controls.Add(linkRow);
                y += Tokens.BtnTextHeight + Tokens.Space2;
            }

            int buttonRowHeight = Sizes.ButtonHeight;
            int x = Width - Pad;
            for (int i = _actions.Count - 1; i >= 0; i--) {
                MaterialButton button = _actions[i];
                x -= button.Width;
                button.Location = new Point(x, y);
                x -= 8;
                Controls.Add(button);
            }
            y += buttonRowHeight + Pad;

            Height = y;
        }

        private int MeasureBodyHeight(string text, int width) {
            using (Graphics g = CreateGraphics()) {
                SizeF size = g.MeasureString(text, MaterialType.BodyMedium, width);
                return (int)Math.Ceiling(size.Height) + 4;
            }
        }

        // Caps the body region so a huge changelog doesn't push the dialog taller than the
        // screen. Scrollbar takes over when content exceeds this. No fixed upper bound — on
        // a 4K monitor a tall dialog is fine; we let the body grow up to (screen - chrome),
        // where chrome accounts for icon + title + actions + top/bottom screen margin.
        private int ComputeMaxBodyHeight() {
            Screen? screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
            int screenH = screen?.WorkingArea.Height ?? 800;
            const int reservedForChrome = 240;
            return Math.Max(200, screenH - reservedForChrome);
        }

        private void ApplyRoundedRegion() {
            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(
                new Rectangle(0, 0, Width, Height), Sizes.RadiusLarge)) {
                Region?.Dispose();
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(
                new Rectangle(0, 0, Width - 1, Height - 1), Sizes.RadiusLarge))
            using (var pen = new Pen(MaterialPalette.OutlineVariant, 1f)) {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}
