using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class InstallProgressBar : Control {
        private int _value;
        private const int HorizontalInset = 6;

        public InstallProgressBar() {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true
            );
            Height = 4;
            Dock = DockStyle.Top;
            Margin = Padding.Empty;
            MinimumSize = new Size(0, 4);
            MaximumSize = new Size(0, 4);
            BackColor = Color.FromArgb(26, 30, 41);
            Visible = false;
        }

        public int Value {
            get { return _value; }
            set {
                int normalized = Math.Max(0, Math.Min(100, value));
                if (_value == normalized) {
                    return;
                }

                _value = normalized;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int trackWidth = ClientSize.Width - HorizontalInset * 2 - 1;
            if (trackWidth <= 0) {
                return;
            }

            Rectangle trackRect = new Rectangle(
                HorizontalInset,
                0,
                trackWidth,
                ClientSize.Height - 1
            );
            using (
                GraphicsPath trackPath = RoundedControlRenderer.GetFigurePath(
                    trackRect,
                    ClientSize.Height / 2
                )
            ) {
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(85, 90, 108))) {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }
            }

            if (_value <= 0) {
                return;
            }

            int fillWidth = Math.Max(1, (int)Math.Round(trackRect.Width * (_value / 100.0)));
            Rectangle fillRect = new Rectangle(0, 0, fillWidth, trackRect.Height);

            using (
                GraphicsPath fillPath = RoundedControlRenderer.GetFigurePath(
                    fillRect,
                    ClientSize.Height / 2
                )
            ) {
                using (SolidBrush fillBrush = new SolidBrush(Theme.ButtonBackgroundHover)) {
                    e.Graphics.FillPath(fillBrush, fillPath);
                }
            }
        }
    }
}
