using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {

    public class RoundedButton : Button {
        private int cornerRadius;
        private Color borderColor;

        public void changeHoverColor(Color onHover) {
            FlatAppearance.MouseDownBackColor = onHover;
            FlatAppearance.MouseOverBackColor = onHover;
        }

        public RoundedButton(int cornerRadius = 6, Color onHover = default, Color borderColor = default) {

            this.cornerRadius = cornerRadius;

            AutoSize = true;
            FlatAppearance.BorderColor = BackColor;
            FlatAppearance.BorderSize = 0;
            FlatStyle = FlatStyle.Flat;
            Cursor = SystemCursors.Pointer;
            ImageAlign = ContentAlignment.MiddleLeft;
            TextImageRelation = TextImageRelation.ImageBeforeText;
            UseCompatibleTextRendering = true;
            this.borderColor = borderColor;

            this.changeHoverColor(onHover);
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            r.roundBorder(ClientRectangle, FlatAppearance.BorderSize, cornerRadius, e, this, borderColor);
        }

    }
}
