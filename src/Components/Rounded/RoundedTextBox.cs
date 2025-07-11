using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {

    public class RoundedTextBox : TextBox {

        public RoundedTextBox() {

            ContextMenu = new ContextMenu();
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            r.round(ClientRectangle, 0, 6, e, this);
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            Region = Region.FromHrgn(r.roundRect(1, 1, Width, Height, 6, 6));
        }
    }

}
