using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    public enum MaterialButtonVariant {
        Filled,
        Tonal,
        Outlined,
        Text,
    }

    /// <summary>
    /// Material 3 button: pill-shaped, owner-drawn, with hover/press state layers and an optional
    /// leading Segoe MDL2 icon glyph. Inherits <see cref="Button"/> so DialogResult / AcceptButton work.
    /// </summary>
    public sealed class MaterialButton : Button {
        private MaterialButtonVariant _variant = MaterialButtonVariant.Filled;
        private string _iconGlyph = string.Empty;
        private Color _accent = MaterialPalette.Primary;
        private Color _onAccent = MaterialPalette.OnPrimary;
        private bool _hovered;
        private bool _pressed;
        private int _cornerRadius = Sizes.RadiusFull;

        public MaterialButton() {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                true
            );
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            Cursor = SystemCursors.Pointer;
            Font = MaterialType.LabelLarge;
            AutoSize = false;
            Height = Sizes.ButtonHeight;
            BackColor = Color.Transparent;
            ApplyVariantColors();
        }

        public MaterialButtonVariant Variant {
            get => _variant;
            set {
                _variant = value;
                ApplyVariantColors();
                Invalidate();
            }
        }

        /// <summary>Overrides the container/fill accent (e.g. Error or Tertiary tinted buttons).</summary>
        public void SetAccent(Color accent, Color onAccent) {
            _accent = accent;
            _onAccent = onAccent;
            ApplyVariantColors();
            Invalidate();
        }

        public string IconGlyph {
            get => _iconGlyph;
            set {
                _iconGlyph = value ?? string.Empty;
                Invalidate();
            }
        }

        public int CornerRadius {
            get => _cornerRadius;
            set { _cornerRadius = value; Invalidate(); }
        }

        private Color FillColor {
            get {
                switch (_variant) {
                    case MaterialButtonVariant.Filled: return _accent;
                    case MaterialButtonVariant.Tonal: return MaterialPalette.SecondaryContainer;
                    default: return Color.Transparent;
                }
            }
        }

        private Color ContentColor {
            get {
                switch (_variant) {
                    case MaterialButtonVariant.Filled: return _onAccent;
                    case MaterialButtonVariant.Tonal: return MaterialPalette.OnSecondaryContainer;
                    default: return _accent;
                }
            }
        }

        private void ApplyVariantColors() {
            ForeColor = ContentColor;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        private int EffectiveRadius => Math.Max(0, Math.Min(_cornerRadius, Math.Min(Width, Height) / 2));

        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 0 || Height <= 0) {
                return;
            }

            // Render off-screen onto a buffer pre-cleared with the parent color, so FillPath's AA edges
            // blend the pill against the actual parent color (smooth, no ring artifact, no white corners).
            using (var buffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(buffer)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                Color parent = ResolveParentColor();
                g.Clear(parent);

                int radius = EffectiveRadius;
                Color fill = FillColor;
                if (!Enabled) {
                    fill = _variant == MaterialButtonVariant.Filled || _variant == MaterialButtonVariant.Tonal
                        ? MaterialPalette.Overlay(parent, MaterialPalette.OnSurface, 0.12)
                        : Color.Transparent;
                }

                Rectangle rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(rect, radius)) {
                    if (fill.A > 0) {
                        using (var brush = new SolidBrush(fill)) {
                            g.FillPath(brush, path);
                        }
                    }

                    double layerOpacity = _pressed ? 0.12 : _hovered ? 0.08 : 0;
                    if (Enabled && layerOpacity > 0) {
                        Color layerBase = fill.A > 0 ? fill : parent;
                        Color layer = MaterialPalette.Overlay(layerBase, ContentColor, layerOpacity);
                        using (var brush = new SolidBrush(layer)) {
                            g.FillPath(brush, path);
                        }
                    }

                    if (_variant == MaterialButtonVariant.Outlined) {
                        // Inset by 1px and shift the transform by +0.5 so the 1px pen (centered on the
                        // path) sits fully inside the buffer — otherwise the top/left edges are clipped
                        // by half a pixel and the border looks "half-cut" along those sides.
                        GraphicsState saved = g.Save();
                        g.TranslateTransform(0.5f, 0.5f);
                        Rectangle outlineRect = new Rectangle(
                            0, 0,
                            Math.Max(0, Width - 2),
                            Math.Max(0, Height - 2)
                        );
                        using (GraphicsPath outlinePath = RoundedControlRenderer.GetFigurePath(outlineRect, Math.Max(0, radius - 1)))
                        using (var pen = new Pen(Enabled ? MaterialPalette.Outline : MaterialPalette.OutlineVariant, 1f)) {
                            g.DrawPath(pen, outlinePath);
                        }
                        g.Restore(saved);
                    }
                }

                DrawContent(g, ClientRectangle);

                e.Graphics.DrawImageUnscaled(buffer, 0, 0);
            }
        }

        private void DrawContent(Graphics g, Rectangle rect) {
            Color content = Enabled ? ContentColor : MaterialPalette.OnSurfaceMuted;
            bool hasIcon = !string.IsNullOrEmpty(_iconGlyph);
            string text = Text ?? string.Empty;

            const int iconGap = 8;
            const int iconPx = 18;
            SizeF textSize = string.IsNullOrEmpty(text)
                ? SizeF.Empty
                : g.MeasureString(text, Font, int.MaxValue, StringFormat.GenericTypographic);

            float totalWidth = textSize.Width + (hasIcon ? iconPx + (string.IsNullOrEmpty(text) ? 0 : iconGap) : 0);
            float startX = rect.X + (rect.Width - totalWidth) / 2f;
            float midY = rect.Y + rect.Height / 2f;

            if (hasIcon) {
                Bitmap icon = MaterialIconRenderer.Get(_iconGlyph, iconPx, content);
                g.DrawImage(icon, (int)Math.Round(startX), (int)Math.Round(midY - iconPx / 2f));
                startX += iconPx + (string.IsNullOrEmpty(text) ? 0 : iconGap);
            }

            if (!string.IsNullOrEmpty(text)) {
                using (var brush = new SolidBrush(content)) {
                    var fmt = new StringFormat(StringFormat.GenericTypographic) {
                        LineAlignment = StringAlignment.Center,
                        Alignment = StringAlignment.Near,
                    };
                    g.DrawString(text, Font, brush, new RectangleF(startX, midY - textSize.Height / 2f, textSize.Width + 2, textSize.Height), fmt);
                }
            }
        }

        public override Size GetPreferredSize(Size proposedSize) {
            // Filled/Tonal/Outlined have a visible fill, so they get the full pill padding.
            // Text buttons have no fill — a tighter padding keeps inline link buttons compact enough
            // to share a row inside a 420-wide dialog.
            int horizontalPadding = _variant == MaterialButtonVariant.Text ? 16 : 34;
            const int iconPx = 18;
            const int iconGap = 8;
            string text = Text ?? string.Empty;

            using (Graphics g = CreateGraphics()) {
                int textWidth = string.IsNullOrEmpty(text)
                    ? 0
                    : (int)Math.Ceiling(g.MeasureString(text, Font, int.MaxValue, StringFormat.GenericTypographic).Width);
                int iconWidth = string.IsNullOrEmpty(_iconGlyph)
                    ? 0
                    : iconPx + (string.IsNullOrEmpty(text) ? 0 : iconGap);
                int width = horizontalPadding + iconWidth + textWidth;
                int height = Math.Max(Sizes.ButtonHeight, Font.Height + 16);
                return new Size(width, height);
            }
        }

        private Color ResolveParentColor() {
            for (Control? p = Parent; p != null; p = p.Parent) {
                if (p.BackColor.A > 0) {
                    return p.BackColor;
                }
            }
            return MaterialPalette.Surface;
        }
    }
}
