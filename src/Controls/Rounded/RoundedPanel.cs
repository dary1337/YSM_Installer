using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    public class RoundedPanel : Panel {
        private int _cornerRadius;
        private Color _outlineColor = Color.Transparent;

        public RoundedPanel(int cornerRadius = 5) {
            _cornerRadius = cornerRadius;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer,
                true
            );
        }

        public void SetCornerRadius(int cornerRadius) {
            _cornerRadius = cornerRadius;
        }

        public void SetOutline(Color outlineColor) {
            _outlineColor = outlineColor;
            Invalidate();
        }

        // UserPaint skips default background: fill here and sync Region to the path first, else padding/corner pixels show parent and
        // DrawRoundedBorder(..., mutateWindowRegion: false) would reset HWND shape every frame.
        protected override void OnPaintBackground(PaintEventArgs pevent) {
            if (BackColor.A <= 0) {
                Region? previous = Region;
                Region = null;
                previous?.Dispose();
                base.OnPaintBackground(pevent);
                return;
            }

            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) {
                return;
            }

            using (
                GraphicsPath path = RoundedControlRenderer.GetFigurePath(
                    ClientRectangle,
                    _cornerRadius
                )
            ) {
                Region? previous = Region;
                Region = new Region(path);
                previous?.Dispose();
            }

            using (SolidBrush brush = new SolidBrush(BackColor)) {
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (_outlineColor.A > 0) {
                RoundedControlRenderer.DrawRoundedBorder(
                    ClientRectangle,
                    1,
                    _cornerRadius,
                    e,
                    this,
                    _outlineColor,
                    clipToRoundedBounds: false,
                    mutateWindowRegion: false
                );
            }
            else {
                RoundedControlRenderer.ApplyRoundedRegion(
                    ClientRectangle,
                    0,
                    _cornerRadius,
                    e,
                    this,
                    clipToRoundedBounds: false,
                    mutateWindowRegion: false
                );
            }
        }
    }
}
