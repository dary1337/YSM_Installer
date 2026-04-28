using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller
{
    public partial class Form1
    {
        private void VersionSelected(int version)
        {
            foreach (var panel in _panels)
            {
                panel.SetSelected(panel.Entry.Version == version);
            }

            ClearInstallControls();
            ShowInstallButtonsForVersion(version);
        }

        private void ShowInstallButtonsForVersion(int version)
        {
            var foundVersions = _supportedVersions
                .Where(mod => mod.GameVersion == version)
                .ToList();

            if (foundVersions.Count == 0)
            {
                foundVersions = GetLatestCompatibleMods(version);
                if (foundVersions.Count == 0)
                {
                    return;
                }
            }

            AddInstallButtonIfAvailable(foundVersions.FirstOrDefault(mod => mod.ModType == ModTypes.Ysm), "Install YSM", version);
            AddInstallButtonIfAvailable(foundVersions.FirstOrDefault(mod => mod.ModType == ModTypes.YsmWif), "Install YSM x WiF", version);
            RelayoutInstallButtons();
        }

        private List<ModMetadata> GetLatestCompatibleMods(int gameVersion)
        {
            int latestSupportedVersion = _supportedVersions
                .Select(mod => mod.GameVersion)
                .DefaultIfEmpty(0)
                .Max();

            if (latestSupportedVersion == 0 || gameVersion <= latestSupportedVersion)
            {
                return new List<ModMetadata>();
            }

            return _supportedVersions
                .Where(mod => mod.GameVersion == latestSupportedVersion)
                .ToList();
        }

        private void AddInstallButtonIfAvailable(ModMetadata? metadata, string text, int selectedGameVersion)
        {
            if (metadata == null)
            {
                return;
            }

            if (selectedGameVersion > metadata.GameVersion)
            {
                text = $"{text} (latest v{metadata.GameVersion})";
            }

            var button = new RoundedButton(14)
            {
                Text = text,
                Tag = text,
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(38, 44, 60),
                Margin = new Padding(0, 0, Sizes.ButtonGap, 0),
            };

            button.Click += async (sender, args) => await InstallModFromButtonAsync(button, metadata, selectedGameVersion);
            _installControlPanel.Controls.Add(button);
            _installButtons.Add(button);
        }

        private async Task InstallModFromButtonAsync(RoundedButton button, ModMetadata metadata, int selectedGameVersion)
        {
            if (_isInstallButtonBusy)
            {
                return;
            }

            _isInstallButtonBusy = true;
            var originalText = (string)button.Tag;
            button.Text = "Installing...";

            try
            {
                if (UserMessages.ConfirmInstall(this, selectedGameVersion, metadata) != DialogResult.OK)
                {
                    button.Text = originalText;
                    return;
                }

                InstallModResult installResult = await WarnoInstaller.InstallAsync(metadata, new Progress<int>(progress =>
                {
                    button.Text = $"Installing ({progress}%)...";
                }));

                if (installResult == InstallModResult.AlreadyRunning)
                {
                    UserMessages.ShowInstallAlreadyRunning(this);
                    button.Text = originalText;
                    return;
                }

                UserMessages.ShowInstallCompleted(this);
                button.Text = $"Reinstall ({originalText.Replace("Install ", "")})";
            }
            catch (Exception exception)
            {
                AppLogger.Critical("Mod installation failed.", exception);
                UserMessages.ShowInstallFailed(this);
                button.Text = originalText;
            }
            finally
            {
                _isInstallButtonBusy = false;
            }
        }
    }
}
