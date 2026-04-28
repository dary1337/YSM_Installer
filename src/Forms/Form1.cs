using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 : Form {
        private readonly List<RoundedButton> _installButtons = new List<RoundedButton>();
        private readonly List<WarnoEntryControl> _panels = new List<WarnoEntryControl>();
        private List<ModMetadata> _supportedVersions = new List<ModMetadata>();

        private TableLayoutPanel _rootLayout = null!;
        private TableLayoutPanel _entriesLayout = null!;
        private FlowLayoutPanel _installControlPanel = null!;
        private RoundedButton? _rescanButton;
        private RoundedButton? _showMoreButton;
        private bool _includeSystemFolders;
        private bool _isInstallButtonBusy;

        public Form1() {
            InitializeComponent();

            label1.Text = "Starting...";
            Icon = Properties.Resources.logo;
            BackColor = Color.FromArgb(19, 22, 30);
            Opacity = 0.95;
            using (Graphics graphics = CreateGraphics()) {
                AutoScaleDimensions = new SizeF(graphics.DpiX, graphics.DpiY);
            }
            label1.AutoSize = true;

            linkLabel1.Click += (sender, args) => OpenStepsForm();
            BuildLayout();
        }

        private void OpenStepsForm() {
            using (var form = new StepsForm()) {
                form.ShowDialog(this);
            }
        }

        async void Form1_Load(object sender, EventArgs e) {
            bool updateStarted = await UpdateService.CheckForUpdatesAsync(this);

            if (!updateStarted && !IsDisposed) {
                await ScanAsync();
            }
        }
    }
}
