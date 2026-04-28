using System.Windows.Forms;

namespace YSMInstaller
{
    public class RoundedLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RoundedControlRenderer.ApplyRoundedRegion(ClientRectangle, 0, 6, e, this);
        }
    }
}
