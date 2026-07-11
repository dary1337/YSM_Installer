using Material3.WinForms;
using Material3.WinForms.Controls;
using Material3.WinForms.Theming;
using Material3.WinForms.Typography;
using Material3.WinForms.Forms;
using MaterialIconRenderer = Material3.WinForms.Drawing.MaterialIconRenderer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
#if DEBUG
        private void OpenDevTestMenu() {
            using (var form = new BorderlessForm {
                Text = "Dev test menu",
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = MaterialColors.Surface,
                ForeColor = MaterialColors.OnSurface,
                Font = MaterialType.BodyMedium,
                Icon = Properties.Resources.logo,
                ClientSize = new Size(380, 640),
            }) {
            var scroll = new MaterialScrollPanel { Dock = DockStyle.Fill };
            Panel content = scroll.ContentPanel;

            const int leftPad = 16;
            const int itemWidth = 330;
            int y = 12;

            void Section(string title) {
                y += 8;
                content.Controls.Add(new SoftLabel {
                    AutoSize = false,
                    Font = MaterialType.Overline,
                    ForeColor = MaterialColors.OnSurfaceVariant,
                    Width = itemWidth,
                    Height = 22,
                    Location = new Point(leftPad, y),
                    Text = title.ToUpperInvariant(),
                });
                y += 22 + 4;
            }
            // Absolute layout: MaterialScrollPanel derives its scroll extent from the bottom-most
            // child, so each item is placed at a running offset rather than flowed.
            void Place(Control c, int height) {
                c.Location = new Point(leftPad, y);
                c.Width = itemWidth;
                c.Height = height;
                content.Controls.Add(c);
                y += height + 6;
            }
            void Add(string text, Action action) {
                var button = new MaterialButton {
                    Variant = MaterialButtonVariant.Tonal,
                    Text = text,
                    AutoSize = false,
                };
                button.Click += (s, e) => {
                    form.Close();
                    if (IsHandleCreated && !IsDisposed) {
                        BeginInvoke(new Action(action));
                    }
                };
                Place(button, 32);
            }

            Section("Switches");
            var mockButton = new MaterialButton {
                Variant = MaterialButtonVariant.Tonal,
                Text = $"Mock WARNO paths: {(DevService.IsMockWarnoPathsEnabled ? "ON" : "OFF — real install!")}",
                AutoSize = false,
                Width = 330,
                Height = 32,
                Margin = new Padding(0, 0, 0, 6),
            };
            mockButton.Click += (s, e) => {
                bool newValue = !DevService.IsMockWarnoPathsEnabled;
                form.Close();
                if (!IsHandleCreated || IsDisposed) {
                    return;
                }
                BeginInvoke(new Action(() => {
                    DevService.IsMockWarnoPathsEnabled = newValue;
                    AppLogger.Info($"Dev: MockWarnoPaths set to {newValue}.");
                    // Fire-and-forget — SafeFireDev internally try/catches and logs, so the Task
                    // is never faulted; discarding it avoids async-void semantics.
                    _ = SafeFireDev(ScanAsync, "Rescan after MockWarnoPaths toggle failed.");
                }));
            };
            Place(mockButton, 32);

            Section("Catalog override (raw mods-list URL)");
            var urlBox = new TextBox {
                Width = 330,
                Margin = new Padding(0, 0, 0, 6),
                Font = MaterialType.BodyMedium,
                BackColor = MaterialColors.SurfaceContainerHigh,
                ForeColor = MaterialColors.OnSurface,
                BorderStyle = BorderStyle.FixedSingle,
                Text = DevService.ModListUrlOverride ?? string.Empty,
            };
            Place(urlBox, urlBox.PreferredHeight);

            // Apply / Clear use a custom Click handler instead of Add() because we need to
            // capture TextBox state before form.Close() disposes it.
            var applyButton = new MaterialButton {
                Variant = MaterialButtonVariant.Filled,
                Text = "Apply override & rescan",
                AutoSize = false,
                Width = 330,
                Height = 32,
                Margin = new Padding(0, 0, 0, 6),
            };
            applyButton.Click += (s, e) => {
                string captured = urlBox.Text;
                form.Close();
                if (!IsHandleCreated || IsDisposed) {
                    return;
                }
                BeginInvoke(new Action(() => {
                    DevService.ModListUrlOverride =
                        string.IsNullOrWhiteSpace(captured) ? null : captured.Trim();
                    AppLogger.Info($"Dev catalog override set to: {DevService.ModListUrlOverride ?? "<none>"}");
                    _ = SafeFireDev(ScanAsync, "Rescan after catalog override failed.");
                }));
            };
            Place(applyButton, 32);

            var clearButton = new MaterialButton {
                Variant = MaterialButtonVariant.Tonal,
                Text = "Clear override (use official)",
                AutoSize = false,
                Width = 330,
                Height = 32,
                Margin = new Padding(0, 0, 0, 6),
            };
            clearButton.Click += (s, e) => {
                form.Close();
                if (!IsHandleCreated || IsDisposed) {
                    return;
                }
                BeginInvoke(new Action(() => {
                    DevService.ModListUrlOverride = null;
                    AppLogger.Info("Dev catalog override cleared.");
                    _ = SafeFireDev(ScanAsync, "Rescan after catalog override clear failed.");
                }));
            };
            Place(clearButton, 32);

            Section("Inject chunk failures (next multi-part download)");
            Add("Inject 1 chunk failure (retries, succeeds)",
                () => DevWarnoMocks.QueueChunkFailures(1, "single transient failure for retry test"));
            Add("Inject 3 chunk failures (exhausts retries, fails)",
                () => DevWarnoMocks.QueueChunkFailures(3, "all-attempts failure for fatal-path test"));
            Add("Clear injected chunk failures",
                () => DevWarnoMocks.QueueChunkFailures(0, string.Empty));

            Section("Dialogs");
            Add("Update available", DevShowUpdateDialog);
            Add("Update available (huge changelog)", DevShowLongUpdateDialog);
            Add("WARNO is running", DevShowWarnoRunningDialog);
            Add("Generic error", () => UserMessages.ShowError(this, "Something went wrong",
                $"A sample error message with details written to:\n{AppLogger.LogPath}"));
            Add("Selected Warno invalid", () => UserMessages.ShowSelectedWarnoInvalid(this));
            Add("Notice message", () => UserMessages.ShowNotice(this, "Heads up",
                "A sample informational notice (single OK action) — e.g. \"Switch canceled\"."));
            Add("Confirm: cancel installation", () => ConfirmCancelInstall());
            Add("Confirm: low disk space", () => ConfirmLowDiskSpaceAsync(
                new DiskSpaceWarning("C:", "install YSM x WiF", 800L * 1024 * 1024, 3L * 1024 * 1024 * 1024)));
            Add("Confirm: quit during install", () => ConfirmCloseDuringInstall());
            Add("Confirm: quit during auto-update", () => ConfirmCloseDuringAutoUpdate());
            Add("Confirm: quit during version switch", () => ConfirmCloseDuringSwitch());
            Add("Settings", () => { using (var f = new SettingsForm()) { f.ShowDialog(this); } });

            Section("States");
            Add("Scanning (skeleton)", RenderScanning);
            Add("Not found (normal)", () => { _includeSystemFolders = false; RenderNotFound(); });
            Add("Not found (all drives)", () => { _includeSystemFolders = true; RenderNotFound(); });
            Add("Catalog unavailable", () => RenderCatalogUnavailable(
                "Connected, but the mod list could not be loaded — the catalog response was invalid."));
            Add("Installs found", DevShowInstallsFound);
            Add("Choose a build", () => _ = SafeFireDev(DevShowChooseBuildAsync, "DevShowChooseBuild failed."));
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

            Section("Version switch");
            Add("Version switch plan", DevShowVersionSwitchPlan);

            Section("Manual install");
            Add("Pick mod folder", () => _ = SafeFireDev(BrowseForManualFolderAsync, "Dev manual-folder browse failed."));
            Add("Pick mod archive", () => _ = SafeFireDev(BrowseForManualArchiveAsync, "Dev manual-archive browse failed."));

            var titleBar = new MaterialTitleBar {
                TitleText = "Dev test menu",
                AppIcon = Properties.Resources.logo.ToBitmap(),
                ShowMinimize = false,
                ShowMaximize = false,
            };

            form.Controls.Add(scroll);
            form.Controls.Add(titleBar);
            form.ShowDialog(this);
            }
        }

        private void DevShowUpdateDialog() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Info;
                dialog.IconColor = MaterialColors.Primary;
                dialog.TitleText = "Update available";
                dialog.BodyText =
                    "YSM Installer 1.2.0 is available.\n\nWhat's new:\n"
                    + "• Catalog now supports YSM x WiF\n• Faster Steam library scan\n• Fixed rollback when WARNO crashes";
                dialog.AddAction("Later", DialogResult.No, MaterialButtonVariant.Text);
                dialog.AddAction("Install update", DialogResult.Yes, MaterialButtonVariant.Filled);
                dialog.ShowDialog(this);
            }
        }

        private void DevShowLongUpdateDialog() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Info;
                dialog.IconColor = MaterialColors.Primary;
                dialog.TitleText = "Update available";
                dialog.BodyText =
                    "YSM Installer 1.2.0 is available.\n\nWhat's new:\n"
                    + string.Join("\n", Enumerable.Range(1, 40).Select(i =>
                        $"• Bullet point {i} — describes a non-trivial change with enough text to wrap onto a second line in the dialog body, which is the whole point of stressing the scrollbar here. Lorem ipsum dolor sit amet."))
                    + "\n\nBreaking changes:\n"
                    + string.Join("\n", Enumerable.Range(1, 15).Select(i =>
                        $"  – BC{i:00}: catalog schema field renamed; behavior described in the migration notes."))
                    + "\n\nKnown issues:\n"
                    + "• None at release time.\n"
                    + "• Edge case: extremely fast networks (>500 MB/s) may briefly flash the progress text — cosmetic only.\n"
                    + "\n\nThank you to all the contributors listed in the GitHub release page. Full diff: see release notes link.";
                dialog.AddAction("Later", DialogResult.No, MaterialButtonVariant.Text);
                dialog.AddAction("Install update", DialogResult.Yes, MaterialButtonVariant.Filled);
                dialog.ShowDialog(this);
            }
        }

        private void DevShowWarnoRunningDialog() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialColors.Warning;
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

        private async Task DevShowChooseBuildAsync() {
            if (_entries == null || _entries.Count == 0) {
                return;
            }
            _selectedEntry = _entries.FirstOrDefault(e => BuildOptionsModel(e.Version).Any(o => o.Kind != BuildSwitchKind.Ready))
                ?? _entries[0];
            await RenderChooseBuild();
        }

        private static async Task SafeFireDev(Func<Task> work, string failureLog) {
            try {
                await work();
            }
            catch (Exception ex) {
                AppLogger.Critical(failureLog, ex);
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
            ModMetadata? sample = catalog.FirstOrDefault(m => m.ModType == ModTypes.Wto)
                ?? catalog.FirstOrDefault();
            if (sample == null) {
                return;
            }
            // Pretend the installed game build is newer than the catalog target.
            RenderVersionMismatch(sample, sample.GameVersion + 760);
        }

        private void DevShowVersionSwitchPlan() {
            // Seed a scenario with a non-Ready (up/downgrade) build option, then render its plan.
            // Clicking Start on a mock entry hits "Can't switch automatically" (no real Steam touch).
            if (_entries == null || _entries.Count == 0) {
                DevLoadScenario(DevWarnoMocks.MixedInstalls());
            }
            if (_entries == null || _entries.Count == 0) {
                return;
            }
            foreach (WarnoEntry entry in _entries) {
                BuildOption? option = BuildOptionsModel(entry.Version).FirstOrDefault(o => o.Kind != BuildSwitchKind.Ready);
                if (option != null) {
                    _selectedEntry = entry;
                    RenderVersionSwitchPlan(option);
                    return;
                }
            }
            AppLogger.Info("Dev: current scenario has no version-switch (up/downgrade) build option.");
        }

        private void DevRunInstall(bool fail) {
            // Self-bootstrap so the button works cold from app startup. Real-install path
            // doesn't honor SimulateInstallFailure (checked only in SimulateInstallAsync),
            // and an empty _entries silently aborts before any UI feedback — both prior
            // failure modes when a user clicks "Run install → failure" without first
            // toggling mocks + loading a scenario.
            if (!DevWarnoMocks.IsEnabled) {
                DevService.IsMockWarnoPathsEnabled = true;
                AppLogger.Info("Dev: auto-enabled Mock WARNO paths for Run install test.");
            }
            if (_entries == null || _entries.Count == 0) {
                DevLoadScenario(DevWarnoMocks.MixedInstalls());
                AppLogger.Info("Dev: auto-seeded mixed-installs scenario for Run install test.");
            }
            if (_entries!.Count == 0) {
                AppLogger.Info("Dev: Run install aborted — scenario seed returned no entries.");
                return;
            }
            _selectedEntry = _entries[0];
            List<BuildOption> options = BuildOptionsModel(_selectedEntry.Version);
            BuildOption? target = options.FirstOrDefault(o => o.Kind == BuildSwitchKind.Ready) ?? options.FirstOrDefault();
            if (target == null) {
                AppLogger.Info("Dev: Run install aborted — no build options after seed.");
                return;
            }
            DevWarnoMocks.SimulateInstallFailure = fail;
            int version = _selectedEntry.Version;
            _ = SafeFireDev(() => StartInstallAsync(target.Metadata, version), "Dev: Run install failed.");
        }
#endif
    }
}
