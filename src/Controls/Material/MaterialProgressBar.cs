using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Material 3 linear determinate progress indicator: rounded track + primary indicator with the
    /// signature trailing stop dot. Set <see cref="Value"/> in [0,100]; the painted fill smoothly tweens
    /// toward the target instead of jumping, so noisy progress reports look fluid.
    /// </summary>
    public sealed class MaterialProgressBar : Control {
        private int _value;
        private float _displayValue;
        private readonly Timer _tween;

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
            _tween = new Timer { Interval = 16 };
            _tween.Tick += OnTweenTick;
        }

        public int Value {
            get => _value;
            set {
                int normalized = Math.Max(0, Math.Min(100, value));
                if (normalized == _value) {
                    return;
                }
                _value = normalized;
                if (!_tween.Enabled) {
                    _tween.Start();
                }
            }
        }

        private void OnTweenTick(object? sender, EventArgs e) {
            // Exponential approach: each frame closes ~22% of remaining distance — settles in ~250ms.
            float delta = _value - _displayValue;
            if (Math.Abs(delta) < 0.1f) {
                _displayValue = _value;
                _tween.Stop();
            }
            else {
                _displayValue += delta * 0.22f;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _tween.Stop();
                _tween.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(BackColor)) {
                g.FillRectangle(bg, ClientRectangle);
            }

            int h = Math.Min(Height, 8);
            int y = (Height - h) / 2;
            int radius = h / 2;
            int width = Width - 1;
            if (width <= 0) {
                return;
            }

            float fraction = _displayValue / 100f;
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
