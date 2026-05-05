using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    public class RoundedPanel : Panel {
        private int _cornerRadius;
        private Color _outlineColor = Color.Transparent;
        private Size _lastRegionSize = Size.Empty;
        private int _lastRegionRadius = -1;
        private bool _regionDirty = true;

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
            _regionDirty = true;
            Invalidate();
        }

        public void SetOutline(Color outlineColor) {
            _outlineColor = outlineColor;
            Invalidate();
        }

        // UserPaint skips default background: fill here and sync Region to the path first, else padding/corner pixels show parent and
        // DrawRoundedBorder(..., mutateWindowRegion: false) would reset HWND shape every frame.
        protected override void OnPaintBackground(PaintEventArgs pevent) {
            if (BackColor.A <= 0) {
                ClearRegionIfNeeded();
                base.OnPaintBackground(pevent);
                return;
            }

            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) {
                return;
            }

            EnsureRoundedRegion();

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
        }

        protected override void OnSizeChanged(EventArgs e) {
            base.OnSizeChanged(e);
            _regionDirty = true;
        }

        private void EnsureRoundedRegion() {
            if (
                !_regionDirty
                && _lastRegionSize == ClientSize
                && _lastRegionRadius == _cornerRadius
            ) {
                return;
            }

            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(ClientRectangle, _cornerRadius)) {
                Region? previous = Region;
                Region = new Region(path);
                previous?.Dispose();
            }

            _lastRegionSize = ClientSize;
            _lastRegionRadius = _cornerRadius;
            _regionDirty = false;
        }

        private void ClearRegionIfNeeded() {
            if (Region == null) {
                return;
            }

            Region? previous = Region;
            Region = null;
            previous?.Dispose();
            _lastRegionSize = Size.Empty;
            _lastRegionRadius = -1;
            _regionDirty = true;
        }
    }
}
