using Material3.WinForms;
using Material3.WinForms.Controls;
using Material3.WinForms.Theming;
using Material3.WinForms.Typography;
using Material3.WinForms.Forms;
using MaterialIconRenderer = Material3.WinForms.Drawing.MaterialIconRenderer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 : BorderlessForm {
        private enum AppState {
            Scanning,
            NotFound,
            CatalogUnavailable,
            InstallsFound,
            ChooseBuild,
            VersionMismatch,
            VersionSwitchPlan,
            SwitchingGameVersion,
            Installing,
            Complete,
            Failed,
        }

        private readonly ScanCoordinator _scanCoordinator = new ScanCoordinator();
        private readonly List<MaterialRadioCard> _cards = new List<MaterialRadioCard>();
        private readonly HashSet<string> _installedKeys = new HashSet<string>();

        private List<ModMetadata> _supportedVersions = new List<ModMetadata>();
        private List<WarnoEntry> _entries = new List<WarnoEntry>();
        private WarnoEntry? _selectedEntry;
        private ScanResult? _lastScanResult;

        private AppState _state = AppState.Scanning;
        private bool _includeSystemFolders;
        private bool _isScanning;
        private bool _isInstalling;
        private bool _isAutoUpdating;
        private CancellationTokenSource? _autoUpdateCts;
        // Scoped to the ChooseBuild screen — cancels in-flight HEAD probes for build sizes when
        // the user navigates away or closes the form, so we don't keep sockets open against the
        // CDN for the full HttpClient timeout after the UI is already gone.
        private CancellationTokenSource? _chooseBuildCts;
        private bool _hasFoundWarnoExe;
        private bool _showAllEntries;
        // Set when an install completes while the window is backgrounded — the taskbar stays
        // green at 100% until the user refocuses, then this triggers the clear.
        private bool _clearTaskbarOnActivate;

        public Form1() {
            InitializeComponent();

            Text = "YSM Installer";
            Icon = Properties.Resources.logo;
            BackColor = MaterialColors.Surface;
            ForeColor = MaterialColors.OnSurface;
            Font = MaterialType.BodyMedium;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Tokens.WindowMinWidth, Tokens.WindowMinHeight);
            ClientSize = new Size(Tokens.WindowWidth, Tokens.WindowHeight);

            using (Graphics graphics = CreateGraphics()) {
                AutoScaleDimensions = new SizeF(graphics.DpiX, graphics.DpiY);
            }

            Activated += async (sender, args) => {
                // Backgrounded-install completion left the taskbar green at 100% as a beacon;
                // the user just refocused, so clear it now.
                if (_clearTaskbarOnActivate) {
                    _clearTaskbarOnActivate = false;
                    TaskbarProgress.Clear(this);
                }
                try {
                    await ScanIfWarnoMissingAsync();
                }
                catch (Exception exception) {
                    AppLogger.Critical("Unhandled exception during activation scan.", exception);
                }
            };
            BuildChrome();
        }

        async void Form1_Load(object sender, EventArgs e) {
            bool updateStarted = false;
            bool autoUpdateCancelled = false;
            _isAutoUpdating = !DevWarnoMocks.IsEnabled;
            if (_isAutoUpdating) {
                _autoUpdateCts = new CancellationTokenSource();
            }
            try {
                if (_isAutoUpdating) {
                    updateStarted = await UpdateService.CheckForUpdatesAsync(this, _autoUpdateCts!.Token);
                }
            }
            // async void escapes go straight to the UI thread's unhandled bucket and crash the
            // process. UpdateService rethrows OCE on cancellation; treat that as a normal close.
            catch (OperationCanceledException) {
                autoUpdateCancelled = true;
                AppLogger.Info("Auto-update aborted by form close.");
            }
            catch (Exception exception) {
                AppLogger.Critical("Auto-update path failed.", exception);
            }
            finally {
                _isAutoUpdating = false;
                // UpdateService doesn't always throw on cancel — its download leg swallows
                // OCE and returns false. Pick up token state here so the scan guard below
                // sees a close-triggered cancel either way.
                autoUpdateCancelled = autoUpdateCancelled
                    || (_autoUpdateCts?.IsCancellationRequested ?? false);
                _autoUpdateCts?.Dispose();
                _autoUpdateCts = null;
            }

            // OnFormClosing-triggered cancel leaves IsDisposed==false until after this event
            // returns, so check the local flag + Disposing to avoid kicking off ScanAsync into
            // a form that's about to tear down.
            if (!updateStarted && !autoUpdateCancelled && !IsDisposed && !Disposing) {
                try {
                    await ScanAsync();
                }
                catch (Exception exception) {
                    AppLogger.Critical("Initial scan failed.", exception);
                }
                MaybeShowToolkitPrompt();
            }
        }

        // Final-release nudge toward the successor (Yuri's WARNO Toolkit). Shown once the initial scan
        // has settled — never mid auto-update (that path restarts the app).
        private void MaybeShowToolkitPrompt() {
            if (IsDisposed || Disposing || _isInstalling || _isScanning) {
                return;
            }

            DialogResult result;
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.ArrowForward;
                dialog.IconColor = MaterialColors.Primary;
                dialog.TitleText = "Meet Yuri's WARNO Toolkit";
                dialog.BodyText =
                    "This is the final YSM Installer — it won't be updated anymore.\n\n"
                    + "Its successor, Yuri's WARNO Toolkit, keeps the one-click YSM install and adds a full "
                    + "battlegroup editor: build and edit decks straight on your profile — even the ones WARNO "
                    + "hides or won't let you import — switch game builds, and back up your whole profile.";
                dialog.AddAction("Not now", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Get the Toolkit", DialogResult.Yes, MaterialButtonVariant.Filled);
                dialog.Load += (sender, args) => SetActionIcon(dialog, "Get the Toolkit", MaterialIcons.OpenInNew);
                result = dialog.ShowDialog(this);
            }

            if (result == DialogResult.Yes) {
                AppLinks.Open(AppLinks.Toolkit);
            }
        }

        // MaterialDialog has no icon-bearing AddAction and only builds its action buttons in OnLoad,
        // so the glyph is grafted on afterwards — widening the pill and re-packing the right-aligned row.
        private static void SetActionIcon(Form dialog, string actionText, string glyph) {
            MaterialButton? target = null;
            foreach (Control control in dialog.Controls) {
                if (control is MaterialButton button && string.Equals(button.Text, actionText, StringComparison.Ordinal)) {
                    target = button;
                    break;
                }
            }
            if (target == null) {
                return;
            }

            const int IconWidth = 18;
            const int IconGap = 8;
            int extra = (int)Math.Round((IconWidth + IconGap) * dialog.DeviceDpi / 96.0);
            int rowTop = target.Top;
            int anchorLeft = target.Left;

            target.IconGlyph = glyph;
            target.Width += extra;
            foreach (Control control in dialog.Controls) {
                if (control is MaterialButton button && button.Top == rowTop && button.Left <= anchorLeft) {
                    button.Left -= extra;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            // Forced close (Windows shutdown, task manager) — can't safely prompt; skip warning.
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing) {
                if (_isInstalling) {
                    if (!ConfirmCloseDuringInstall()) {
                        e.Cancel = true;
                    }
                    else {
                        // Best-effort rollback before the app dies; nothing we can await here.
                        _installCts?.Cancel();
                    }
                }
                else if (_isAutoUpdating) {
                    if (!ConfirmCloseDuringAutoUpdate()) {
                        e.Cancel = true;
                    }
                    else {
                        // Aborts the in-flight HEAD/GET so the runtime actually releases the
                        // download (otherwise we just hide the UI while the network keeps going).
                        _autoUpdateCts?.Cancel();
                    }
                }
                else if (_isSwitchingVersion) {
                    if (!ConfirmCloseDuringSwitch()) {
                        e.Cancel = true;
                    }
                    else {
                        // Best-effort rollback before the app dies; nothing we can await here.
                        _switchCts?.Cancel();
                    }
                }
            }

            // Always abort ChooseBuild size probes on close — they're best-effort UI decoration,
            // no prompt needed, and we don't want them holding sockets after teardown.
            if (!e.Cancel) {
                _chooseBuildCts?.Cancel();
                _chooseBuildCts?.Dispose();
                _chooseBuildCts = null;
                _switchCts?.Cancel();
                _switchCts?.Dispose();
                _switchCts = null;
            }

            base.OnFormClosing(e);
        }

        private bool ConfirmCloseDuringInstall() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialColors.Warning;
                dialog.TitleText = "Installation in progress";
                dialog.BodyText =
                    "Quitting now will cancel the install and roll back any partial changes. Continue?";
                dialog.AddAction("Keep installing", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Cancel & quit", DialogResult.OK, MaterialButtonVariant.Filled);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private bool ConfirmCloseDuringAutoUpdate() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialColors.Warning;
                dialog.TitleText = "Update in progress";
                dialog.BodyText =
                    "The auto-update is still running. Quitting now will abort it. Continue?";
                dialog.AddAction("Keep updating", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Quit anyway", DialogResult.OK, MaterialButtonVariant.Filled);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private bool ConfirmCloseDuringSwitch() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialColors.Warning;
                dialog.TitleText = "Version switch in progress";
                dialog.BodyText =
                    "WARNO's version is still being switched through Steam. Quitting now stops it and tries to restore your previous version. Continue?";
                dialog.AddAction("Keep switching", DialogResult.Cancel, MaterialButtonVariant.Text);
                dialog.AddAction("Quit anyway", DialogResult.OK, MaterialButtonVariant.Filled);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private async Task ScanIfWarnoMissingAsync() {
            bool busyState = _state == AppState.ChooseBuild
                || _state == AppState.VersionMismatch
                || _state == AppState.Installing
                || _state == AppState.Complete
                || _state == AppState.Failed;
            if (_isScanning || _isInstalling || _hasFoundWarnoExe || busyState || IsDisposed || !Visible) {
                return;
            }

            await ScanAsync();
        }

        private async Task OpenSettingsAsync() {
            try {
                using (var form = new SettingsForm()) {
                    // Rescan during install would replace the live progress UI with disposed controls.
                    if (form.ShowDialog(this) == DialogResult.OK && form.SourceChanged && !_isInstalling) {
                        await ScanAsync();
                    }
                }
            }
            catch (Exception ex) {
                AppLogger.Critical("Settings dialog failed.", ex);
                UserMessages.ShowError(this, "Settings error", $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
