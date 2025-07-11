using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 : Form {

        List<Control> controls = new List<Control>();
        List<RoundedButton> installButtons = new List<RoundedButton>();
        List<WarnoEntryControl> panels = new List<WarnoEntryControl>();
        FlowLayoutPanel installControlPanel;
        RoundedButton showMoreButton;

        public Form1() {
            InitializeComponent();

            Icon = Properties.Resources.logo;
            BackColor = Color.FromArgb(19, 22, 30);
            Opacity = 0.95;
            Graphics g = CreateGraphics();
            AutoScaleDimensions = new SizeF(g.DpiX, g.DpiY);
            label1.AutoSize = true;

            linkLabel1.Click += (s, a) => new StepsForm().ShowDialog();

            this.SizeChanged += (s, e) => {
                foreach (var panel in panels)
                    panel.Width = this.ClientSize.Width - 20;
            };
        }

        void Form1_Load(object sender, EventArgs e) {
            Scan();
            WarnoEntryControl.VersionSelected += VersionSelected;
        }

        void VersionSelected(int version) {

            foreach (var c in installButtons) {
                c.Dispose();
            }
            installButtons.Clear();

            var foundVersions = WarnoSupportedVersions
                .Get()
                .Where(x => x.GameVersion == version)
                .ToList();

            if (foundVersions.Count == 0)
                return;

            bool isInstalling = false;

            async void Install(RoundedButton button, ModMetadata metadata) {
                if (isInstalling)
                    return;

                isInstalling = true;
                button.Text = "Installing...";

                bool success = await ModInstallerService.PromptAndInstallAsync(metadata, new Progress<int>((x) => {
                    button.Text = $"Installing ({x}%)...";
                }));

                var meta = (string)button.Tag;

                button.Text = success
                    ? $"Reinstall ({meta.Replace("Install ", "")})"
                    : meta;

                isInstalling = false;
            }


            var buttonPanel = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };

            installControlPanel = buttonPanel;

            RelayoutInstallButtons();

            Controls.Add(buttonPanel);

            void AddInstallButton(string text, ModMetadata metadata) {
                var button = new RoundedButton(14) {
                    Text = text,
                    Tag = text,
                    AutoSize = true,
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(38, 44, 60),
                    Margin = new Padding(0, 0, 10, 0),
                };

                button.Click += (s, e) => Install(button, metadata);
                buttonPanel.Controls.Add(button);
                installButtons.Add(button);
            }

            var modYsm = foundVersions.FirstOrDefault(x => x.ModType == "ysm");
            var modWif = foundVersions.FirstOrDefault(x => x.ModType == "ysm_wif");

            if (modYsm != null)
                AddInstallButton("Install YSM", modYsm);

            if (modWif != null)
                AddInstallButton("Install YSM x WiF", modWif);
        }

        async Task Scan() {
            foreach (Control c in controls)
                c.Dispose();
            controls.Clear();

            foreach (Control c in panels)
                c.Dispose();
            panels.Clear();

            foreach (Control c in installButtons)
                c.Dispose();
            installButtons.Clear();

            label1.Text = "Loading supported versions...";
            var supported = await WarnoSupportedVersions.Update();

            label1.Text = "Searching for Warno.exe...";
            var warnoPaths = await Warno.Find();
            var entries = await WarnoScanner.ScanAsync(warnoPaths, supported);

            int y = 70;
            const int gap = 6;

            foreach (var entry in entries) {
                var control = new WarnoEntryControl(entry) {
                    Location = new Point(10, y),
                    Visible = false
                };
                panels.Add(control);
                Controls.Add(control);
                y += control.Height + gap;
            }

            var latest = entries.OrderByDescending(x => x.Version).FirstOrDefault();
            if (latest != null) {
                Warno.selectedVersion = latest.Version;
                WarnoEntryControl.RaiseVersionSelected(latest.Version);

                var selectedPanel = panels.FirstOrDefault(x => x._entry.Version == latest.Version);
                if (selectedPanel != null) {
                    selectedPanel.Visible = true;
                }
            }

            if (entries.Count > 1) {
                showMoreButton = new RoundedButton(14) {
                    Text = $"Show {entries.Count - 1} more versions...",
                    Location = new Point(10, panels.First(x => x.Visible).Bottom + 10),
                    BackColor = Color.FromArgb(38, 44, 60),
                    ForeColor = Color.White,
                };
                showMoreButton.Click += (s, e) => {
                    foreach (var panel in panels)
                        panel.Visible = true;

                    showMoreButton.Dispose();
                    showMoreButton = null;
                    RelayoutPanels();
                    RelayoutInstallButtons();
                    ResizeFormToFitContent();
                };
                Controls.Add(showMoreButton);
                controls.Add(showMoreButton);
            }

            AddRescanButton();
        }

        void RelayoutPanels() {
            int y = 70;
            const int gap = 6;
            foreach (var panel in panels) {
                if (panel.Visible) {
                    panel.Location = new Point(10, y);
                    y += panel.Height + gap;
                }
            }
        }

        void RelayoutInstallButtons() {

            if (installControlPanel == null)
                return;

            var visiblePanels = panels.Where(p => p.Visible);
            var lastVisiblePanel = visiblePanels.LastOrDefault();
            var installPanelY = lastVisiblePanel != null ? lastVisiblePanel.Bottom + (visiblePanels.Count() > 1 ? 20 : 70) : 240;

            installControlPanel.Location = new Point(10, installPanelY);
        }

        void ResizeFormToFitContent() {
            this.Width = Math.Max(400, controls.Max(x => x.Right) + 30);
            this.Height = Math.Max(300, panels.Last().Bottom + 200);
            this.MinimumSize = new Size(Width, Height);
        }

        void AddRescanButton() {
            RoundedButton rescanBtn = new RoundedButton(14) {
                Dock = DockStyle.Bottom,
                Text = "Rescan",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(38, 44, 60),
            };
            rescanBtn.Click += (sender, e) => Scan();
            controls.Add(rescanBtn);
            Controls.Add(rescanBtn);

            label1.Text = $"Found {panels.Count} Warno.exe";

            if (panels.Count == 0) {
                if (Warno.searchInSystemFolders) {
                    MessageBox.Show("WARNO executable not found.");
                    return;
                }

                if (MessageBox.Show(
                    "WARNO executable not found. Make sure it's not installed in C:\\Windows or C:\\Users.\n\nDo you want to scan all directories anyway?",
                    "WARNO Not Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                ) == DialogResult.Yes) {
                    Warno.searchInSystemFolders = true;
                    Scan();
                }

                return;
            }

            ResizeFormToFitContent();
        }
    }
}
