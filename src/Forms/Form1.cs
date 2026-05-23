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
            bool updateStarted =
                !DevWarnoMocks.IsEnabled && await UpdateService.CheckForUpdatesAsync(this);

            if (!updateStarted && !IsDisposed) {
                await ScanAsync();
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
                    if (form.ShowDialog(this) == DialogResult.OK && form.SourceChanged) {
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
