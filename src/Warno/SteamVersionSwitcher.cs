using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace YSMInstaller {
    // Switches WARNO's installed build by driving Steam's own beta system: rewrite the betakey in
    // appmanifest_<appid>.acf and let Steam download the matching build — no Steam login, no manifest
    // ids by hand. The branch naming is `compat_<gameVersion>` (Steam → WARNO → Betas).
    internal sealed class SteamVersionSwitcher {
        public const string WarnoAppId = "1611600";

        // 0x402 = StateUpdateStarted (0x400) | StateUpdateRequired (0x2): on relaunch Steam sees the
        // app must (re)download to match the new betakey and queues it.
        private const string StateFlagsNeedsUpdate = "1026";
        private const string StateFlagsFullyInstalled = "4";

        private const int SteamShutdownTimeoutSeconds = 40;

        // If nothing in the manifest changes for this long after we launch Steam, assume Steam won't
        // download the branch (bad/absent branch, offline, auto-update-on-launch off) and bail.
        private const int StallTimeoutSeconds = 180;

        // Right after the branch flip Steam first syncs a tiny depot manifest (~1 MB) and briefly
        // reports BytesDownloaded == BytesToDownload (100%) before it computes the real content
        // delta. Treat anything below this as the "preparing" phase, not real download progress.
        private const long MinRealDownloadBytes = 50L * 1024 * 1024;
        private static readonly string[] SteamProcessNames = { "steam", "steamwebhelper", "steamservice" };

        public enum SwitchPhase { ClosingSteam, SettingVersion, StartingSteam, Downloading, Done }

        public sealed class SwitchProgress {
            public SwitchPhase Phase;
            public long BytesDownloaded;
            public long BytesToDownload;
            public bool Indeterminate;
        }

        // Public ("Default Public Version") is the empty betakey; every other build is compat_<ver>.
        public static string BranchForVersion(int gameVersion) {
            return "compat_" + gameVersion.ToString();
        }

        // From ...\steamapps\common\WARNO to ...\steamapps\appmanifest_<appid>.acf.
        public static string? FindManifestPath(string gamePath) {
            try {
                DirectoryInfo? common = Directory.GetParent(gamePath);
                DirectoryInfo? steamapps = common?.Parent;
                if (steamapps == null) {
                    return null;
                }
                string path = Path.Combine(steamapps.FullName, "appmanifest_" + WarnoAppId + ".acf");
                return File.Exists(path) ? path : null;
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to resolve Steam app manifest path.", exception);
                return null;
            }
        }

        // "" == public.
        public static string ReadCurrentBranch(string manifestPath) {
            try {
                AcfKeyValues manifest = AcfKeyValues.Parse(File.ReadAllText(manifestPath));
                return manifest.GetValue("UserConfig", "betakey") ?? string.Empty;
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to read current Steam branch.", exception);
                return string.Empty;
            }
        }

        public async Task SwitchAndWaitAsync(string manifestPath, string targetBranch,
            IProgress<SwitchProgress> progress, CancellationToken cancellationToken) {
            progress.Report(new SwitchProgress { Phase = SwitchPhase.ClosingSteam, Indeterminate = true });
            await CloseSteamAsync(cancellationToken);

            progress.Report(new SwitchProgress { Phase = SwitchPhase.SettingVersion, Indeterminate = true });
            WriteBranch(manifestPath, targetBranch, StateFlagsNeedsUpdate);

            progress.Report(new SwitchProgress { Phase = SwitchPhase.StartingSteam, Indeterminate = true });
            LaunchSteam();

            await WaitForBranchInstalledAsync(manifestPath, targetBranch, progress, cancellationToken);
        }

        // Best-effort rollback: close Steam, restore the original betakey, relaunch. Returns false if
        // it couldn't unwind cleanly (e.g. Steam refused to close), so the caller can be honest with
        // the user instead of claiming the version was restored. Runs uncancelled.
        public async Task<bool> RestoreBranchAsync(string manifestPath, string originalBranch) {
            try {
                await CloseSteamAsync(CancellationToken.None);
                // If the original build is still mounted (we cancelled before anything downloaded),
                // restoring the betakey needs no update — keep StateFlags=installed so Steam doesn't
                // re-validate a build the user never actually left.
                string mounted = ReadMountedBranch(manifestPath);
                string stateFlags = string.Equals(mounted, originalBranch, StringComparison.OrdinalIgnoreCase)
                    ? StateFlagsFullyInstalled
                    : StateFlagsNeedsUpdate;
                WriteBranch(manifestPath, originalBranch, stateFlags);
                LaunchSteam();
                return true;
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to restore the original Steam branch.", exception);
                return false;
            }
        }

        private static string ReadMountedBranch(string manifestPath) {
            try {
                return AcfKeyValues.Parse(File.ReadAllText(manifestPath)).GetValue("MountedConfig", "betakey") ?? string.Empty;
            }
            catch (Exception exception) {
                // Conservative fallback: an empty branch won't match originalBranch, so the caller
                // forces a re-validate rather than trusting a build we couldn't confirm is mounted.
                AppLogger.Error("Failed to read the mounted Steam branch.", exception);
                return string.Empty;
            }
        }

        private void WriteBranch(string manifestPath, string branch, string stateFlags) {
            AcfKeyValues manifest = AcfKeyValues.Parse(File.ReadAllText(manifestPath));
            if (string.IsNullOrEmpty(branch)) {
                manifest.Remove("UserConfig", "betakey");
            }
            else {
                manifest.SetValue(branch, "UserConfig", "betakey");
            }
            manifest.SetValue(stateFlags, "StateFlags");

            // Atomic replace so a crash mid-write can't truncate the manifest Steam depends on.
            string tempPath = manifestPath + ".tmp";
            File.WriteAllText(tempPath, manifest.Serialize());
            if (File.Exists(manifestPath)) {
                File.Replace(tempPath, manifestPath, null);
            }
            else {
                File.Move(tempPath, manifestPath);
            }
        }

        private async Task CloseSteamAsync(CancellationToken cancellationToken) {
            if (!AnySteamProcessRunning()) {
                return;
            }
            // Ask Steam to exit cleanly. Killing it would let it flush its in-memory manifest on the
            // way out and clobber our edit.
            try {
                using (Process.Start(new ProcessStartInfo("steam://exit") { UseShellExecute = true })) { }
            }
            catch (Exception exception) {
                AppLogger.Error("steam://exit failed; polling for shutdown anyway.", exception);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(SteamShutdownTimeoutSeconds);
            while (DateTime.UtcNow < deadline) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AnySteamProcessRunning()) {
                    return;
                }
                await Task.Delay(500, cancellationToken);
            }
            throw new TimeoutException("Steam did not shut down within " + SteamShutdownTimeoutSeconds + " s.");
        }

        private static bool AnySteamProcessRunning() {
            foreach (string name in SteamProcessNames) {
                Process[] processes = Process.GetProcessesByName(name);
                bool any = processes.Length > 0;
                foreach (Process process in processes) {
                    process.Dispose();
                }
                if (any) {
                    return true;
                }
            }
            return false;
        }

        private void LaunchSteam() {
            string? steamExe = FindSteamExe();
            try {
                ProcessStartInfo info = steamExe != null
                    ? new ProcessStartInfo(steamExe)
                    : new ProcessStartInfo("steam://open/main");
                info.UseShellExecute = true;
                using (Process.Start(info)) { }
            }
            catch (Exception exception) {
                AppLogger.Error("Failed to launch Steam.", exception);
            }
        }

        private async Task WaitForBranchInstalledAsync(string manifestPath, string targetBranch,
            IProgress<SwitchProgress> progress, CancellationToken cancellationToken) {
            string lastSnapshot = string.Empty;
            DateTime lastChange = DateTime.UtcNow;

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                AcfKeyValues? manifest = null;
                try {
                    manifest = AcfKeyValues.Parse(File.ReadAllText(manifestPath));
                }
                catch (Exception exception) when (exception is IOException || exception is FormatException) {
                    // Steam is mid-write (file locked) or the manifest is half-flushed — retry next
                    // tick. Non-transient faults (ACL, missing path) propagate to the caller, which
                    // logs and rolls back instead of spinning until the stall timeout.
                }

                string snapshot = string.Empty;
                if (manifest != null) {
                    string stateFlags = manifest.GetValue("StateFlags") ?? string.Empty;
                    string mounted = manifest.GetValue("MountedConfig", "betakey") ?? string.Empty;
                    long.TryParse(manifest.GetValue("BytesDownloaded"), out long downloaded);
                    long.TryParse(manifest.GetValue("BytesToDownload"), out long total);

                    bool onTarget = string.Equals(mounted, targetBranch, StringComparison.OrdinalIgnoreCase);
                    if (stateFlags == StateFlagsFullyInstalled && onTarget) {
                        progress.Report(new SwitchProgress {
                            Phase = SwitchPhase.Done,
                            BytesDownloaded = total,
                            BytesToDownload = total,
                        });
                        return;
                    }

                    progress.Report(new SwitchProgress {
                        Phase = SwitchPhase.Downloading,
                        BytesDownloaded = downloaded,
                        BytesToDownload = total,
                        Indeterminate = total < MinRealDownloadBytes,
                    });
                    snapshot = stateFlags + "|" + mounted + "|" + downloaded + "|" + total;
                }

                // Stall guard: a successfully-progressing download changes BytesDownloaded every tick,
                // so lastChange keeps resetting; only a genuine stall (Steam never started, or stuck)
                // trips the timeout. An unreadable manifest counts as no-progress too (empty snapshot).
                if (snapshot != lastSnapshot) {
                    lastSnapshot = snapshot;
                    lastChange = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastChange > TimeSpan.FromSeconds(StallTimeoutSeconds)) {
                    throw new TimeoutException(
                        "Steam isn't downloading the new WARNO version (no progress for " + StallTimeoutSeconds + " s).");
                }

                await Task.Delay(1000, cancellationToken);
            }
        }

        private static string? FindSteamExe() {
            string[] subKeys = { @"SOFTWARE\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" };
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                foreach (string subKey in subKeys) {
                    try {
                        using (RegistryKey? key = root.OpenSubKey(subKey)) {
                            if (key == null) {
                                continue;
                            }
                            string? path = key.GetValue("SteamExe") as string;
                            if (string.IsNullOrWhiteSpace(path)) {
                                string? steamPath = key.GetValue("SteamPath") as string;
                                if (!string.IsNullOrWhiteSpace(steamPath)) {
                                    path = Path.Combine(steamPath!.Replace('/', '\\'), "steam.exe");
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(path)) {
                                path = path!.Replace('/', '\\');
                                if (File.Exists(path)) {
                                    return path;
                                }
                            }
                        }
                    }
                    catch (Exception exception) {
                        AppLogger.Error("Failed to read Steam path from registry.", exception);
                    }
                }
            }
            return null;
        }
    }
}
