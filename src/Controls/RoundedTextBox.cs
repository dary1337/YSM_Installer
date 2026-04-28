using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public class RoundedTextBox : TextBox {
        public RoundedTextBox() {
            ContextMenu = new ContextMenu();
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            RoundedControlRenderer.ApplyRoundedRegion(ClientRectangle, 0, 6, e, this);
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            Region previousRegion = Region;
            Region = Region.FromHrgn(RoundedControlRenderer.CreateRoundRectRegion(1, 1, Width, Height, 6, 6));
            previousRegion?.Dispose();
        }
    }
}
