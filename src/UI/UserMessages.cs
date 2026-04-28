using System.Windows.Forms;

namespace YSMInstaller
{
    public static class UserMessages
    {
        public static DialogResult ConfirmInstall(IWin32Window owner, int selectedGameVersion, ModMetadata modMetadata)
        {
            string message = WarnoInstallWarning.BuildMessage();

            if (selectedGameVersion > modMetadata.GameVersion)
            {
                message +=
                    $"Your WARNO version is newer than the latest available {ModTypes.ToDisplayName(modMetadata.ModType)} package.\n" +
                    $"The installer can use the latest mod package for game version {modMetadata.GameVersion}.\n\n" +
                    "This can still work: sometimes game developers do not change the mod file structure, " +
                    "so the existing mod does not need an update and works as intended.\n\n";
            }

            return MessageBox.Show(
                owner,
                message + "Continue?",
                "Install",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question
            );
        }

        public static void ShowInstallCompleted(IWin32Window owner)
        {
            MessageBox.Show(owner, "Done, you can launch the game.");
        }

        public static void ShowInstallAlreadyRunning(IWin32Window owner)
        {
            MessageBox.Show(
                owner,
                "Another installation is already running. Please wait for it to finish.",
                "Install",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        public static void ShowInstallFailed(IWin32Window owner)
        {
            MessageBox.Show(
                owner,
                $"Can't install the mod. Details were written to:\n{AppLogger.LogPath}",
                "Install failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }

        public static void ShowSupportedVersionsLoadFailed(IWin32Window owner)
        {
            MessageBox.Show(
                owner,
                "Can't load supported versions. Check your internet connection.",
                "YSM Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        public static void ShowWarnoNotFound(IWin32Window owner)
        {
            MessageBox.Show(owner, "WARNO executable not found.");
        }

        public static DialogResult ConfirmFullSystemScan(IWin32Window owner)
        {
            return MessageBox.Show(
                owner,
                "WARNO executable not found. Make sure it's not installed in C:\\Windows or C:\\Users.\n\nDo you want to scan all directories anyway?",
                "WARNO Not Found",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
        }
    }
}
