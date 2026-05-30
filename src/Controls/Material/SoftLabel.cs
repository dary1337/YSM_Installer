using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Drop-in <see cref="Label"/> replacement that owner-draws its text via GDI+ with a controlled
    /// <see cref="TextRenderingHint"/>, so its rendering stays consistent with the owner-drawn Material
    /// controls (and we never get the Label fallbacks at small sizes). Honors Font, ForeColor, TextAlign,
    /// AutoSize and word wrap (when AutoSize is off).
    /// </summary>
    public class SoftLabel : Label {
        public SoftLabel() {
            SetStyle(
                ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.SupportsTransparentBackColor,
                true
            );
            BackColor = Color.Transparent;
        }

        // Label.AutoSize sizes via TextRenderer (GDI), but OnPaint draws via Graphics.DrawString
        // (GDI+) — and GDI+ measurements run a few px wider. The mismatch makes DrawString think
        // the text doesn't fit and wraps to a second invisible line under the fixed row height,
        // dropping everything after the first wrap-break. Take the wider of the two for Width so
        // our own render always fits. Height stays from the base (TextRenderer) — GDI+ MeasureString
        // includes the full em-box and would puff multi-label layouts vertically.
        public override Size GetPreferredSize(Size proposedSize) {
            Size baseSize = base.GetPreferredSize(proposedSize);
            if (string.IsNullOrEmpty(Text)) {
                return baseSize;
            }
            using (var bitmap = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var format = BuildFormat()) {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                SizeF layout = proposedSize.Width > 0 && proposedSize.Width < int.MaxValue
                    ? new SizeF(proposedSize.Width, int.MaxValue)
                    : new SizeF(float.MaxValue, float.MaxValue);
                int gdiPlusWidth = (int)Math.Ceiling(g.MeasureString(Text, Font, layout, format).Width);
                return new Size(Math.Max(baseSize.Width, gdiPlusWidth + 1), baseSize.Height);
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(BackColor)) {
                g.FillRectangle(bg, ClientRectangle);
            }

            if (string.IsNullOrEmpty(Text)) {
                return;
            }

            using (var format = BuildFormat())
            using (var brush = new SolidBrush(Enabled ? ForeColor : MaterialPalette.OnSurfaceMuted)) {
                RectangleF rect = ClientRectangle;
                g.DrawString(Text, Font, brush, rect, format);
            }
        }

        private StringFormat BuildFormat() {
            // NoClip lets the trailing glyph's sub-pixel ClearType extent render even when it would
            // graze the rect edge — without it, GDI+ trims the last character or two at the exact
            // width we draw into. The control's own clip still caps real overflow at ClientRectangle.
            // MeasureTrailingSpaces keeps trailing whitespace in the layout so right/center alignment
            // and AutoSize width stay correct.
            var format = new StringFormat {
                FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces,
            };
            if (AutoEllipsis) {
                format.FormatFlags |= StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisPath;
            }
            else {
                format.Trimming = StringTrimming.None;
            }

            switch (TextAlign) {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    format.Alignment = StringAlignment.Near;
                    break;
                case ContentAlignment.TopCenter:
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.BottomCenter:
                    format.Alignment = StringAlignment.Center;
                    break;
                default:
                    format.Alignment = StringAlignment.Far;
                    break;
            }

            switch (TextAlign) {
                case ContentAlignment.TopLeft:
                case ContentAlignment.TopCenter:
                case ContentAlignment.TopRight:
                    format.LineAlignment = StringAlignment.Near;
                    break;
                case ContentAlignment.BottomLeft:
                case ContentAlignment.BottomCenter:
                case ContentAlignment.BottomRight:
                    format.LineAlignment = StringAlignment.Far;
                    break;
                default:
                    format.LineAlignment = StringAlignment.Center;
                    break;
            }

            return format;
        }
    }
}
