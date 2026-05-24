using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Modal dialogs that survive the Material You redesign. Large flows — not-found, offline/catalog,
    /// install progress, completion, failure — are inline states owned by Form1.
    /// </summary>
    public static class UserMessages {
        /// <summary>Unexpected error: surfaces the log plus help links (issue tracker, Discord).</summary>
        public static void ShowError(IWin32Window owner, string title, string body) {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.ErrorBadge;
                dialog.IconColor = MaterialPalette.Error;
                dialog.TitleText = title;
                dialog.BodyText = body;
                dialog.AddLink("Open log", MaterialIcons.OpenInNew, OpenLog);
                dialog.AddLink("Report an issue", MaterialIcons.OpenInNew, () => AppLinks.Open(AppLinks.Issues));
                dialog.AddLink("Discord", MaterialIcons.OpenInNew, () => AppLinks.Open(AppLinks.Discord));
                dialog.AddAction("OK", DialogResult.OK, MaterialButtonVariant.Filled);
                dialog.ShowDialog(owner);
            }
        }

        /// <summary>Plain informational notice (no log / no help links).</summary>
        public static void ShowNotice(IWin32Window owner, string title, string body) {
            using (var dialog = new MaterialDialog()) {
                dialog.IconGlyph = MaterialIcons.Info;
                dialog.IconColor = MaterialPalette.Primary;
                dialog.TitleText = title;
                dialog.BodyText = body;
                dialog.AddAction("OK", DialogResult.OK, MaterialButtonVariant.Filled);
                dialog.ShowDialog(owner);
            }
        }

        public static void ShowSelectedWarnoInvalid(IWin32Window owner) {
            ShowNotice(owner, "Not a WARNO installation",
                "The selected file does not look like a valid WARNO installation. Pick the Warno.exe inside your WARNO game folder.");
        }

        private static void OpenLog() {
            try {
                Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
            }
            catch (Exception exception) {
                AppLogger.Critical("Failed to open log file.", exception);
            }
        }
    }
}
