using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class StepsForm : Form {
        public StepsForm() {
            Text = "Changing version";
            Size = new Size(600, 800);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScroll = true;
            Icon = Properties.Resources.logo;
            BackColor = Color.FromArgb(19, 22, 30);
            ForeColor = Color.White;

            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.RowStyles.Clear();

            AddStep(layout, "Open properties", Properties.Resources.Screenshot_2025_03_02_182641);
            AddStep(layout, "Click Betas", Properties.Resources.Screenshot_2025_03_02_182659);
            AddStep(layout, "Choose version", Properties.Resources.Screenshot_2025_03_02_182718);
            AddStep(layout, "Wait for Warno to load", Properties.Resources.Screenshot_2025_03_02_182724);

            Panel panel = new Panel {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            panel.Controls.Add(layout);

            Controls.Add(panel);
        }

        private void AddStep(TableLayoutPanel layout, string text, Image image) {
            Label label = new Label {
                Text = text,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true
            };

            PictureBox pictureBox = new PictureBox {
                Image = image,
                SizeMode = PictureBoxSizeMode.AutoSize,
                Dock = DockStyle.Top
            };

            layout.Controls.Add(label);
            layout.Controls.Add(pictureBox);
        }
    }
}
