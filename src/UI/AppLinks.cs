using System;
using System.Diagnostics;

namespace YSMInstaller {
    /// <summary>External help/support links surfaced from error dialogs and the offline panel.</summary>
    public static class AppLinks {
        public const string Discord = "https://discord.gg/8AMXxnjngR";
        public const string Issues = "https://github.com/dary1337/YSM_Installer/issues";
        public const string Repository = "https://github.com/dary1337/YSM_Installer";

        public static void Open(string url) {
            try {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to open link: {url}", exception);
            }
        }
    }
}
