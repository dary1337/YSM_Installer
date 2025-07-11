using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public class RoundedPictureBox : PictureBox {

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            r.round(ClientRectangle, 0, 6, e, this);
        }
    }
}
