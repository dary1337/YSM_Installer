using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class StepsForm : Form {
        private readonly List<(string title, Image image)> _steps;
        private int _index;

        private Label _overlineLabel = null!;
        private Label _titleLabel = null!;
        private Panel _screenshotHost = null!;
        private PictureBox _screenshot = null!;
        private MaterialButton _backButton = null!;
        private MaterialButton _nextButton = null!;

        public StepsForm() {
            InitializeComponent();

            Text = "Changing version";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = Properties.Resources.logo;
            BackColor = MaterialPalette.Surface;
            ForeColor = MaterialPalette.OnSurface;
            Font = MaterialType.BodyMedium;
            ClientSize = new Size(720, 560);
            Padding = new Padding(Sizes.WindowPadding);
            WindowChrome.ApplyDark(this);

            _steps = new List<(string, Image)> {
                ("Open properties", Properties.Resources.Screenshot_2025_03_02_182641),
                ("Click Betas", Properties.Resources.Screenshot_2025_03_02_182659),
                ("Choose version", Properties.Resources.Screenshot_2025_03_02_182718),
                ("Wait for Warno to load", Properties.Resources.Screenshot_2025_03_02_182724),
            };

            BuildLayout();
            RenderStep();
        }

        private void BuildLayout() {
            var root = new TableLayoutPanel {
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                RowCount = 3,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0, 0, 0, Sizes.ContentGap),
                Padding = Padding.Empty,
                WrapContents = false,
            };
            _overlineLabel = new Label {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.Overline,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Margin = new Padding(0, 0, 0, 2),
            };
            _titleLabel = new Label {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = MaterialType.TitleLarge,
                ForeColor = MaterialPalette.OnSurface,
                Margin = Padding.Empty,
            };
            header.Controls.Add(_overlineLabel);
            header.Controls.Add(_titleLabel);

            _screenshotHost = new Panel {
                AutoScroll = true,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _screenshot = new PictureBox {
                BackColor = Color.Transparent,
                Location = new Point(0, 0),
                SizeMode = PictureBoxSizeMode.AutoSize,
            };
            _screenshotHost.Controls.Add(_screenshot);

            var buttonRow = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, Sizes.ContentGap, 0, 0),
                Padding = Padding.Empty,
                RowCount = 1,
            };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonRow.RowStyles.Add(new RowStyle(SizeType.Absolute, Sizes.ButtonHeight + 4));

            _backButton = new MaterialButton {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = Sizes.ButtonHeight,
                Margin = new Padding(0, 2, 4, 2),
                Text = "Back",
                Variant = MaterialButtonVariant.Tonal,
            };
            _backButton.Click += (s, e) => GoBack();

            _nextButton = new MaterialButton {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = Sizes.ButtonHeight,
                Margin = new Padding(4, 2, 0, 2),
                Text = "Next",
                Variant = MaterialButtonVariant.Filled,
            };
            _nextButton.Click += (s, e) => GoNext();

            buttonRow.Controls.Add(_backButton, 0, 0);
            buttonRow.Controls.Add(_nextButton, 1, 0);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_screenshotHost, 0, 1);
            root.Controls.Add(buttonRow, 0, 2);

            Controls.Add(root);

            AcceptButton = _nextButton;
        }

        private void GoBack() {
            if (_index == 0) {
                return;
            }
            _index--;
            RenderStep();
        }

        private void GoNext() {
            if (_index >= _steps.Count - 1) {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _index++;
            RenderStep();
        }

        private void RenderStep() {
            (string title, Image image) step = _steps[_index];
            _overlineLabel.Text = $"STEP {_index + 1} OF {_steps.Count}";
            _titleLabel.Text = step.title;
            _screenshot.Image = step.image;
            _screenshotHost.AutoScrollPosition = new Point(0, 0);

            _backButton.Enabled = _index > 0;
            _nextButton.Text = _index >= _steps.Count - 1 ? "Done" : "Next";
        }
    }
}
