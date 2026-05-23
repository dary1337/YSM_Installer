using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Material 3 linear determinate progress indicator: rounded track + primary indicator with the
    /// signature trailing stop dot. Set <see cref="Value"/> in [0,100].
    /// </summary>
    public sealed class MaterialProgressBar : Control {
        private int _value;

        public MaterialProgressBar() {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true
            );
            Height = 8;
            BackColor = Color.Transparent;
        }

        public int Value {
            get => _value;
            set {
                int normalized = Math.Max(0, Math.Min(100, value));
                if (normalized == _value) {
                    return;
                }
                _value = normalized;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int h = Math.Min(Height, 8);
            int y = (Height - h) / 2;
            int radius = h / 2;
            int width = Width - 1;
            if (width <= 0) {
                return;
            }

            float fraction = _value / 100f;
            int indicatorWidth = (int)Math.Round((width - 6) * fraction);
            int gap = indicatorWidth > 0 && indicatorWidth < width - 6 ? 4 : 0;

            var trackRect = new Rectangle(indicatorWidth + gap, y, Math.Max(0, width - indicatorWidth - gap), h);
            if (trackRect.Width > 0) {
                using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(trackRect, radius))
                using (var brush = new SolidBrush(MaterialPalette.SurfaceContainerHighest)) {
                    g.FillPath(brush, path);
                }
            }

            if (indicatorWidth > 0) {
                var fillRect = new Rectangle(0, y, indicatorWidth, h);
                using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(fillRect, radius))
                using (var brush = new SolidBrush(MaterialPalette.Primary)) {
                    g.FillPath(brush, path);
                }
            }
        }
    }
}
