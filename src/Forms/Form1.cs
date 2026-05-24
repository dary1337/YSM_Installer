using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 : Form {
        private enum AppState {
            Scanning,
            NotFound,
            CatalogUnavailable,
            InstallsFound,
            ChooseBuild,
            VersionMismatch,
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
        private bool _hasFoundWarnoExe;
        private bool _showAllEntries;

        public Form1() {
            InitializeComponent();

            Text = "YSM Installer";
            Icon = Properties.Resources.logo;
            BackColor = MaterialPalette.Surface;
            ForeColor = MaterialPalette.OnSurface;
            Font = MaterialType.BodyMedium;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Tokens.WindowMinWidth, Tokens.WindowMinHeight);
            ClientSize = new Size(Tokens.WindowWidth, Tokens.WindowHeight);
            WindowChrome.ApplyDark(this);

            using (Graphics graphics = CreateGraphics()) {
                AutoScaleDimensions = new SizeF(graphics.DpiX, graphics.DpiY);
            }

            Activated += async (sender, args) => await ScanIfWarnoMissingAsync();
            BuildChrome();
        }

        async void Form1_Load(object sender, EventArgs e) {
            bool updateStarted;
            _isAutoUpdating = !DevWarnoMocks.IsEnabled;
            try {
                updateStarted = _isAutoUpdating && await UpdateService.CheckForUpdatesAsync(this);
            }
            finally {
                _isAutoUpdating = false;
            }

            if (!updateStarted && !IsDisposed) {
                await ScanAsync();
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
                }
            }

            base.OnFormClosing(e);
        }

        private bool ConfirmCloseDuringInstall() {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Warning;
                dialog.IconColor = MaterialPalette.Warning;
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
                dialog.IconColor = MaterialPalette.Warning;
                dialog.TitleText = "Update in progress";
                dialog.BodyText =
                    "The auto-update is still running. Quitting now will abort it. Continue?";
                dialog.AddAction("Keep updating", DialogResult.Cancel, MaterialButtonVariant.Text);
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

        private void OpenStepsForm() {
            using (var form = new StepsForm()) {
                form.ShowDialog(this);
            }
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
