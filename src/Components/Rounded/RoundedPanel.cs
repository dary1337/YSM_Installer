using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {

    public class RoundedPanel : Panel {

        private int cornerRadius;

        public RoundedPanel(int cornerRadius = 5) {
            this.cornerRadius = cornerRadius;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void setCornerRadius(int cornerRadius) {
            this.cornerRadius = cornerRadius;
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            r.round(ClientRectangle, 0, cornerRadius, e, this);
        }
    }
}
