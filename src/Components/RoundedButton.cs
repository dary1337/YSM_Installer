using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public class RoundedButton : Button {
        private readonly int _cornerRadius;
        private readonly Color _borderColor;

        public void ChangeHoverColor(Color onHover) {
            FlatAppearance.MouseDownBackColor = onHover;
            FlatAppearance.MouseOverBackColor = onHover;
        }

        public RoundedButton(int cornerRadius = 6, Color onHover = default, Color borderColor = default) {
            _cornerRadius = cornerRadius;

            AutoSize = true;
            FlatAppearance.BorderColor = BackColor;
            FlatAppearance.BorderSize = 0;
            FlatStyle = FlatStyle.Flat;
            Cursor = SystemCursors.Pointer;
            ImageAlign = ContentAlignment.MiddleLeft;
            TextImageRelation = TextImageRelation.ImageBeforeText;
            UseCompatibleTextRendering = true;
            _borderColor = borderColor;

            ChangeHoverColor(onHover);
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            RoundedControlRenderer.DrawRoundedBorder(ClientRectangle, FlatAppearance.BorderSize, _cornerRadius, e, this, _borderColor);
        }
    }
}
