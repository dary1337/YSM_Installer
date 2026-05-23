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
            var format = new StringFormat();
            if (AutoEllipsis) {
                format.FormatFlags = StringFormatFlags.NoWrap;
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
