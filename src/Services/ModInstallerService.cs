using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public static class ModInstallerService {

        public static bool IsGameRunning => Process.GetProcessesByName("WARNO").Any();

        public static async Task<bool> PromptAndInstallAsync(ModMetadata info, IProgress<int> progress = null) {
            string msg = "YSM Installer will disable all other mods for compatibility.\r\n\r\n";

            if (IsGameRunning) {
                msg = "WARNO is currently running and will be closed.\r\n\r\n" + msg;
            }

            if (MessageBox.Show(msg + "Continue?", "Install",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK
            )
                return false;

            await Warno.InstallMod(info, progress);
            MessageBox.Show("Done, you can launch the game.");
            return true;
        }
    }
}
