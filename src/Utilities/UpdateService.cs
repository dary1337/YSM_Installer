using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller
{
    public static class UpdateService
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/dary1337/YSM_Installer/releases/latest";
        private const string GitHubApiAcceptHeader = "application/vnd.github+json";
        private const string InstallerAssetName = "YSMInstaller.exe";

        public static async Task<bool> CheckForUpdatesAsync(IWin32Window owner)
        {
            UpdateInfo updateInfo;

            try
            {
                AppLogger.Info("Checking for application updates.");
                updateInfo = await FetchLatestReleaseAsync();
            }
            catch (Exception exception)
            {
                AppLogger.Error("Failed to check for application updates.", exception);
                return false;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            if (updateInfo.Version <= currentVersion)
            {
                AppLogger.Info($"Application is up to date. Current: {currentVersion}, remote: {updateInfo.Version}.");
                return false;
            }

            var answer = MessageBox.Show(
                owner,
                $"A new YSM Installer version is available ({updateInfo.Version}). Install it now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (answer != DialogResult.Yes)
            {
                AppLogger.Info($"Application update skipped by user. Current: {currentVersion}, remote: {updateInfo.Version}.");
                return false;
            }

            try
            {
                AppLogger.Info($"Downloading application update {updateInfo.Version}.");
                await DownloadAndRestartAsync(updateInfo.DownloadUrl);
                return true;
            }
            catch (Exception exception)
            {
                AppLogger.Critical("Auto-update failed.", exception);
                MessageBox.Show(
                    owner,
                    $"Auto-update failed. Details were written to:\n{AppLogger.LogPath}",
                    "YSM Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

        private static async Task<UpdateInfo> FetchLatestReleaseAsync()
        {
            var json = await HttpService.GetStringAsync(LatestReleaseUrl, GitHubApiAcceptHeader);
            var release = JsonConvert.DeserializeObject<GitHubRelease>(json)
                ?? throw new InvalidDataException("GitHub release response is empty.");

            Version version = ParseTagVersion(release.TagName);
            var installerAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase));

            if (installerAsset == null || !IsSafeDownloadUrl(installerAsset.BrowserDownloadUrl))
            {
                throw new InvalidDataException($"Latest GitHub release must contain HTTPS asset '{InstallerAssetName}'.");
            }

            return new UpdateInfo(version, installerAsset.BrowserDownloadUrl);
        }

        private static Version ParseTagVersion(string tagName)
        {
            string normalizedTag = tagName.Trim();
            if (normalizedTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalizedTag = normalizedTag.Substring(1);
            }

            if (!Version.TryParse(normalizedTag, out var version))
            {
                throw new FormatException($"GitHub release tag '{tagName}' must use v1.0.0.2 format.");
            }

            return version;
        }

        private static bool IsSafeDownloadUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   uri.Scheme == Uri.UriSchemeHttps;
        }

        private static async Task DownloadAndRestartAsync(string downloadUrl)
        {
            var currentExePath = Application.ExecutablePath;
            var tempExePath = Path.Combine(Path.GetTempPath(), $"YSMInstaller_{Guid.NewGuid():N}.exe");
            var updaterPath = Path.Combine(Path.GetTempPath(), $"YSMInstaller_Update_{Guid.NewGuid():N}.cmd");

            await HttpService.DownloadFileAsync(downloadUrl, tempExePath);

            var script = BuildUpdaterScript(currentExePath, tempExePath, updaterPath);
            File.WriteAllText(updaterPath, script, Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Exit();
        }

        private static string BuildUpdaterScript(string currentExePath, string tempExePath, string updaterPath)
        {
            var currentProcessId = Process.GetCurrentProcess().Id;

            return
                "@echo off\r\n" +
                $"timeout /t 1 /nobreak >nul\r\n" +
                $":wait\r\n" +
                $"tasklist /fi \"PID eq {currentProcessId}\" | find \"{currentProcessId}\" >nul\r\n" +
                $"if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"copy /y \"{tempExePath}\" \"{currentExePath}\" >nul\r\n" +
                $"start \"\" \"{currentExePath}\"\r\n" +
                $"del \"{tempExePath}\" >nul 2>nul\r\n" +
                $"del \"{updaterPath}\" >nul 2>nul\r\n";
        }

        private sealed class UpdateInfo
        {
            public UpdateInfo(Version version, string downloadUrl)
            {
                Version = version;
                DownloadUrl = downloadUrl;
            }

            public Version Version { get; }
            public string DownloadUrl { get; }
        }

        private sealed class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}
