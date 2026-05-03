using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class InstallWorkflow {
        private readonly IWin32Window _owner;

        public InstallWorkflow(IWin32Window owner) {
            _owner = owner;
        }

        public async Task<InstallWorkflowResult> InstallAsync(
            ModMetadata metadata,
            int selectedGameVersion,
            IProgress<int>? progress = null
        ) {
            try {
                if (
                    UserMessages.ConfirmInstall(_owner, selectedGameVersion, metadata)
                    != DialogResult.OK
                ) {
                    return InstallWorkflowResult.Cancelled;
                }

                InstallModResult installResult = await WarnoInstaller.InstallAsync(
                    metadata,
                    progress
                );
                if (installResult == InstallModResult.AlreadyRunning) {
                    UserMessages.ShowInstallAlreadyRunning(_owner);
                    return InstallWorkflowResult.AlreadyRunning;
                }

                UserMessages.ShowInstallCompleted(_owner);
                return InstallWorkflowResult.Installed;
            }
            catch (Exception exception) {
                AppLogger.Critical("Mod installation failed.", exception);
                UserMessages.ShowInstallFailed(_owner);
                return InstallWorkflowResult.Failed;
            }
        }
    }

    public enum InstallWorkflowResult {
        Cancelled,
        AlreadyRunning,
        Installed,
        Failed,
    }
}
