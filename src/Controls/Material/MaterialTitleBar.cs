using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Custom Material titlebar replacing the native window caption. Left side hosts the app icon
    /// and title (paint-only, so the whole strip is one big drag target); right side hosts compact
    /// caption buttons (Material 3 <see cref="MaterialButton"/>s with the project's cursor /
    /// hover / accent system).
    ///
    /// Drag is handled by the parent <see cref="BorderlessForm"/>: this control returns
    /// HTTRANSPARENT for WM_NCHITTEST, Windows cascades the hit-test to the form, the form
    /// returns HTCAPTION for the caption zone, and DefWindowProc runs the native drag loop
    /// (with Aero Snap, shake-to-minimize, monitor-aware drag, etc.).
    /// </summary>
    public sealed class MaterialTitleBar : Control {
        public const int BarHeight = Tokens.TitleBarHeight;

        private const int ButtonWidth = 44;
        private const int ButtonHeight = Tokens.BtnHeightSm;
        private const int ButtonGap = 4;
        private const int EdgePadding = 6;
        private const int IconSize = Tokens.IconXs;
        private const int LeftPadding = 12;
        private const int IconTextGap = 8;

        private readonly MaterialButton _minimize;
        private readonly MaterialButton _close;
        private string _titleText = "YSM Installer";
        private Bitmap? _appIcon;
        private bool _showMinimize = true;

        public string TitleText {
            get => _titleText;
            set { _titleText = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>
        /// Takes ownership of the supplied Bitmap — the previous icon (and the icon set here)
        /// is disposed by the title bar. Callers can pass <c>Properties.Resources.logo.ToBitmap()</c>
        /// inline without tracking the Bitmap for disposal themselves.
        /// </summary>
        public Bitmap? AppIcon {
            get => _appIcon;
            set {
                if (ReferenceEquals(_appIcon, value)) {
                    return;
                }
                _appIcon?.Dispose();
                _appIcon = value;
                Invalidate();
            }
        }

        /// <summary>Modal dialogs set this false — they can't be minimized while parent is up.</summary>
        public bool ShowMinimize {
            get => _showMinimize;
            set {
                if (_showMinimize == value) return;
                _showMinimize = value;
                _minimize.Visible = value;
                PerformLayout();
            }
        }

        public MaterialTitleBar() {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                true
            );
            BackColor = MaterialPalette.Surface;
            ForeColor = MaterialPalette.OnSurface;

            // MaterialButton brings the project's cursor (SystemCursors.Pointer — modern OS hand),
            // hover/press state layers, and the same accent system as the cancel-install button —
            // no need to roll our own caption-button look. Same construction order matters:
            // buttons must exist before Dock/Height assignments because docking fires OnResize.
            _minimize = BuildCaptionButton(MaterialIcons.Minimize, MaterialButtonVariant.Text);
            _minimize.SetAccent(MaterialPalette.OnSurfaceVariant, MaterialPalette.OnSurface);
            _minimize.Click += OnMinimizeClick;

            // Close: stays Text-variant always (no fill, no border). Only the glyph swaps —
            // grey idle, Error red on hover. The Material 3 hover state-layer (~8 % overlay)
            // tints the bg slightly red but no solid fill / border.
            _close = BuildCaptionButton(MaterialIcons.Close, MaterialButtonVariant.Text);
            _close.SetAccent(MaterialPalette.OnSurfaceVariant, MaterialPalette.OnSurface);
            _close.MouseEnter += OnCloseHoverEnter;
            _close.MouseLeave += OnCloseHoverLeave;
            _close.Click += OnCloseClick;

            Controls.Add(_minimize);
            Controls.Add(_close);

            Dock = DockStyle.Top;
            Height = BarHeight;
        }

        private static MaterialButton BuildCaptionButton(string iconGlyph, MaterialButtonVariant variant) {
            return new MaterialButton {
                AutoSize = false,
                IconGlyph = iconGlyph,
                Variant = variant,
                Width = ButtonWidth,
                Height = ButtonHeight,
                Text = string.Empty,
                Margin = Padding.Empty,
                TabStop = false,
            };
        }

        private void OnMinimizeClick(object? sender, EventArgs e) {
            Form? form = FindForm();
            if (form != null) {
                form.WindowState = FormWindowState.Minimized;
            }
        }

        private void OnCloseClick(object? sender, EventArgs e) {
            FindForm()?.Close();
        }

        private void OnCloseHoverEnter(object? sender, EventArgs e) {
            _close.SetAccent(MaterialPalette.Error, MaterialPalette.OnError);
        }

        private void OnCloseHoverLeave(object? sender, EventArgs e) {
            _close.SetAccent(MaterialPalette.OnSurfaceVariant, MaterialPalette.OnSurface);
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            int y = (Height - ButtonHeight) / 2;
            _close.SetBounds(Width - ButtonWidth - EdgePadding, y, ButtonWidth, ButtonHeight);
            if (_showMinimize) {
                _minimize.SetBounds(_close.Left - ButtonWidth - ButtonGap, y, ButtonWidth, ButtonHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int x = LeftPadding;
            if (_appIcon != null) {
                int iconY = (Height - IconSize) / 2;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_appIcon, new Rectangle(x, iconY, IconSize, IconSize));
                x += IconSize + IconTextGap;
            }

            if (!string.IsNullOrEmpty(_titleText)) {
                int rightReserved = ButtonWidth * 2 + ButtonGap + EdgePadding + 4;
                var textRect = new Rectangle(x, 0, Math.Max(0, Width - x - rightReserved), Height);
                // TextRenderer (GDI) — pixel-grid aligned, accurate VerticalCenter. Graphics.DrawString
                // centers the measured glyph box including font leading, which drifts a few px off-center.
                TextRenderer.DrawText(
                    g,
                    _titleText,
                    MaterialType.LabelLarge,
                    textRect,
                    MaterialPalette.OnSurface,
                    TextFormatFlags.Left
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding
                        | TextFormatFlags.NoPrefix
                        | TextFormatFlags.EndEllipsis
                );
            }
        }

        // Cascade hit-tests to the form via HTTRANSPARENT. Form's WM_NCHITTEST then decides
        // (HTCAPTION for drag, HT*{TOP,TOPLEFT,TOPRIGHT} for the top-edge resize zone).
        // DefWindowProc on the form runs the native drag/resize loop.
        protected override void WndProc(ref Message m) {
            const int WM_NCHITTEST = 0x0084;
            if (m.Msg == WM_NCHITTEST) {
                m.Result = (IntPtr)BorderlessForm.HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _appIcon?.Dispose();
                _appIcon = null;
            }
            base.Dispose(disposing);
        }
    }
}
