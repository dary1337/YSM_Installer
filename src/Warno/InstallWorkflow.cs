using System;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller {
    public sealed class InstallWorkflow {
        public async Task<InstallWorkflowResult> InstallAsync(
            ModMetadata metadata,
            IProgress<int>? progress = null,
            IProgress<string>? stageProgress = null,
            Func<DiskSpaceWarning, Task<bool>>? lowDiskSpaceConfirm = null,
            CancellationToken cancellationToken = default
        ) {
            try {
                InstallModResult installResult = await WarnoInstaller.InstallAsync(
                    metadata,
                    progress,
                    stageProgress,
                    lowDiskSpaceConfirm,
                    cancellationToken
                );
                return installResult == InstallModResult.AlreadyRunning
                    ? InstallWorkflowResult.AlreadyRunning
                    : InstallWorkflowResult.Installed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Internal timeout OCEs would otherwise be reported as user cancellation.
                AppLogger.Info("Mod installation cancelled by user.");
                return InstallWorkflowResult.Cancelled;
            }
            catch (InstallDeclinedByUserException exception) {
                AppLogger.Info($"Mod installation declined by user: {exception.Message}");
                return InstallWorkflowResult.Cancelled;
            }
            catch (Exception exception) {
                AppLogger.Critical("Mod installation failed.", exception);
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
