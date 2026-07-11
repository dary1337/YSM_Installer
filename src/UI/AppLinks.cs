using System;
using System.Diagnostics;

namespace YSMInstaller {
    public static class AppLinks {
        public const string Discord = "https://discord.gg/8AMXxnjngR";
        public const string Issues = "https://github.com/dary1337/YSM_Installer/issues";
        public const string Repository = "https://github.com/dary1337/YSM_Installer";

        // YSM Installer is superseded by Yuri's WARNO Toolkit — the successor that keeps YSM's one-click
        // install and adds a full battlegroup/profile editor. This points users at it (the last thing this
        // app ships is a bridge to the toolkit).
        public const string Toolkit = "https://github.com/dary1337/yuri-warno-toolkit";

        public static void Open(string url) {
            try {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception exception) {
                AppLogger.Critical($"Failed to open link: {url}", exception);
            }
        }
    }
}
