using Material3.WinForms;
using Material3.WinForms.Controls;
using Material3.WinForms.Theming;
using Material3.WinForms.Typography;
using Material3.WinForms.Forms;
using MaterialIconRenderer = Material3.WinForms.Drawing.MaterialIconRenderer;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace YSMInstaller {
    public static class UserMessages {
        public static void ShowError(IWin32Window owner, string title, string body) {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.ErrorBadge;
                dialog.IconColor = MaterialColors.Error;
                dialog.TitleText = title;
                dialog.BodyText = body;
                dialog.AddLink("Open log", MaterialIcons.OpenInNew, OpenLog);
                dialog.AddLink("Report an issue", MaterialIcons.OpenInNew, () => AppLinks.Open(AppLinks.Issues));
                dialog.AddLink("Discord", MaterialIcons.OpenInNew, () => AppLinks.Open(AppLinks.Discord));
                dialog.AddAction("OK", DialogResult.OK, MaterialButtonVariant.Filled);
                dialog.ShowDialog(owner);
            }
        }

        public static void ShowNotice(IWin32Window owner, string title, string body) =>
            MaterialMessageBox.Info(owner, title, body);

        public static void ShowSelectedWarnoInvalid(IWin32Window owner) {
            ShowNotice(owner, "Not a WARNO installation",
                "The selected file does not look like a valid WARNO installation. Pick Warno.exe inside your WARNO game folder.");
        }

        public static void OpenLog() {
            try {
                Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
            }
            catch (Exception exception) {
                AppLogger.Critical("Failed to open log file.", exception);
            }
        }
    }
}
