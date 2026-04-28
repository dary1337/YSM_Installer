using System.Windows.Forms;

namespace YSMInstaller
{
    public class RoundedPanel : Panel
    {
        private int _cornerRadius;

        public RoundedPanel(int cornerRadius = 5)
        {
            _cornerRadius = cornerRadius;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetCornerRadius(int cornerRadius)
        {
            _cornerRadius = cornerRadius;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RoundedControlRenderer.ApplyRoundedRegion(ClientRectangle, 0, _cornerRadius, e, this);
        }
    }
}
