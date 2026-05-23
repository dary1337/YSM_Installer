using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
#if DEBUG
        // Debug-only surface (compiled out of Release) to inspect every modal and every screen state —
        // including a simulated failed install — without a real game/network.
        private void OpenDevTestMenu() {
            var form = new Form {
                Text = "Dev test menu",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = MaterialPalette.Surface,
                ForeColor = MaterialPalette.OnSurface,
                Font = MaterialType.BodyMedium,
                Icon = Properties.Resources.logo,
                ClientSize = new Size(380, 620),
            };
            WindowChrome.ApplyDark(form);

            var flow = new FlowLayoutPanel {
                AutoScroll = true,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(16),
                WrapContents = false,
            };
            form.Controls.Add(flow);

            void Section(string title) {
                flow.Controls.Add(new SoftLabel {
                    AutoSize = false,
                    Font = MaterialType.Overline,
                    ForeColor = MaterialPalette.OnSurfaceVariant,
                    Height = 22,
                    Margin = new Padding(0, 8, 0, 4),
                    Text = title.ToUpperInvariant(),
                    Width = 330,
                });
            }
            void Add(string text, Action action) {
                var button = new MaterialButton {
                    Variant = MaterialButtonVariant.Tonal,
                    Text = text,
                    AutoSize = false,
                    Width = 330,
                    Height = 32,
                    Margin = new Padding(0, 0, 0, 6),
                };
                button.Click += (s, e) => { form.Close(); BeginInvoke(new Action(action)); };
                flow.Controls.Add(button);
            }

            Section("Dialogs");
            Add("Update available", DevShowUpdateDialog);
            Add("WARNO is running", DevShowWarnoRunningDialog);
            Add("Generic error", () => UserMessages.ShowError(this, "Something went wrong",
                $"A sample error message with details written to:\n{AppLogger.LogPath}"));
            Add("Selected Warno invalid", () => UserMessages.ShowSelectedWarnoInvalid(this));
            Add("Settings", () => { using (var f = new SettingsForm()) { f.ShowDialog(this); } });

            Section("States");
            Add("Scanning (skeleton)", RenderScanning);
            Add("Not found (normal)", () => { _includeSystemFolders = false; RenderNotFound(); });
            Add("Not found (all drives)", () => { _includeSystemFolders = true; RenderNotFound(); });
            Add("Catalog unavailable", () => RenderCatalogUnavailable(
                "Connected, but the mod list could not be loaded — the catalog response was invalid."));
            Add("Installs found", DevShowInstallsFound);
            Add("Choose a build", DevShowChooseBuild);
            Add("Installing (static)", DevShowInstalling);
            Add("Installation complete", () => RenderComplete("YSM", _entries.FirstOrDefault()));
            Add("Version mismatch", DevShowVersionMismatch);
            Add("Installation failed", () => { _currentStepIndex = 5; RenderFailed(); });

            Section("Mock data (version statuses)");
            Add("4 mixed installs", () => DevLoadScenario(DevWarnoMocks.MixedInstalls()));
            Add("Single supported", () => DevLoadScenario(DevWarnoMocks.SingleSupported()));
            Add("All unsupported", () => DevLoadScenario(DevWarnoMocks.AllUnsupported()));
            Add("Has known issues", () => DevLoadScenario(DevWarnoMocks.KnownIssues()));
            Add("Future versions (latest mod)", () => DevLoadScenario(DevWarnoMocks.FutureVersions()));
            Add("No installs", () => DevLoadScenario(new List<WarnoEntry>()));

            Section("Install flow (mock)");
            Add("Run install → success", () => DevRunInstall(fail: false));
            Add("Run install → failure", () => DevRunInstall(fail: true));

            form.ShowDialog(this);
        }

        private void DevShowUpdateDialog() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Info;
                dialog.IconColor = MaterialPalette.Primary;
                dialog.TitleText = "Update available";
                dialog.BodyText =
                    "A new YSM Installer version is available (1.2.0).\n\nWhat is new:\n"
                    + "• Catalog now supports YSM x WiF\n• Faster Steam library scan\n• Fixed rollback when WARNO crashes";
                dialog.AddAction("Later", DialogResult.No, MaterialButtonVariant.Text);
                dialog.AddAction("Install update", DialogResult.Yes, MaterialButtonVariant.Filled);
                dialog.ShowDialog(this);
            }
        }

        private void DevShowWarnoRunningDialog() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialPalette.Warning;
                dialog.TitleText = "WARNO is running";
                dialog.BodyText = "WARNO will be closed to install the mod. All other mods are disabled for compatibility.";
                dialog.AddAction("Cancel", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Close & install", DialogResult.OK, MaterialButtonVariant.Filled);
                dialog.ShowDialog(this);
            }
        }

        private void DevShowInstallsFound() {
            if (_entries == null || _entries.Count == 0) {
                return;
            }
            _showAllEntries = true;
            RenderInstallsFound();
        }

        private async void DevShowChooseBuild() {
            if (_entries == null || _entries.Count == 0) {
                return;
            }
            _selectedEntry = _entries.FirstOrDefault(e => GetVariantsForVersion(e.Version).Count > 1) ?? _entries[0];
            var variants = GetVariantsForVersion(_selectedEntry.Version);
            if (variants.Count > 0) {
                try {
                    await RenderChooseBuild(variants);
                }
                catch (Exception ex) {
                    AppLogger.Critical("DevShowChooseBuild failed.", ex);
                }
            }
        }

        private void DevShowInstalling() {
            _selectedEntry = _entries.FirstOrDefault();
            RenderInstalling("YSM x WiF", _selectedEntry?.GamePath ?? @"D:\SteamLibrary\steamapps\common\WARNO");
            if (_progressBar != null) {
                _progressBar.Value = 60;
            }
            if (_percentLabel != null) {
                _percentLabel.Text = "60%";
            }
            if (_detailLabel != null) {
                _detailLabel.Text = "Reading config...";
            }
            if (_stepChecklist != null) {
                _stepChecklist.ActiveIndex = 3;
            }
            _currentStepIndex = 3;
        }

        private void DevLoadScenario(List<WarnoEntry> entries) {
            _supportedVersions = DevWarnoMocks.Catalog();
            _entries = entries;
            _hasFoundWarnoExe = entries.Count > 0;
            _showAllEntries = true;
            if (entries.Count > 0) {
                RenderInstallsFound();
            }
            else {
                _includeSystemFolders = false;
                RenderNotFound();
            }
        }

        private void DevShowVersionMismatch() {
            var catalog = DevWarnoMocks.Catalog();
            ModMetadata? sample = catalog.FirstOrDefault(m => m.ModType == ModTypes.YsmWto)
                ?? catalog.FirstOrDefault();
            if (sample == null) {
                return;
            }
            // Pretend the installed game build is newer than the catalog target.
            RenderVersionMismatch(sample, sample.GameVersion + 760);
        }

        private void DevRunInstall(bool fail) {
            if (_entries == null || _entries.Count == 0) {
                return;
            }
            _selectedEntry = _entries[0];
            var variants = GetVariantsForVersion(_selectedEntry.Version);
            if (variants.Count == 0) {
                return;
            }
            DevWarnoMocks.SimulateInstallFailure = fail;
            _ = StartInstallAsync(variants[0], _selectedEntry.Version);
        }
#endif
    }
}
