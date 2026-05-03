using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace YSMInstaller {
    public static class AppLogger {
        private static readonly object Sync = new object();
        private const string LogFileName = "ysm_installer_log.txt";
        private static string _logPath = Path.Combine(Path.GetTempPath(), LogFileName);
        private static bool _isAvailable;

        public static string LogPath => _logPath;

        public static void Initialize() {
            lock (Sync) {
                string logDirectory = DevService.IsLogNextToExeEnabled
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : Path.GetTempPath();

                _isAvailable = TryInitializeAt(Path.Combine(logDirectory, LogFileName));
            }
        }

        public static void Critical(string message, Exception? exception = null) {
            Write("CRITICAL", message, exception);
        }

        public static void Error(string message, Exception? exception = null) {
            Write("ERROR", message, exception);
        }

        public static void Info(string message) {
            Write("INFO", message, null);
        }

        private static void Write(string level, string message, Exception? exception) {
            try {
                lock (Sync) {
                    if (!_isAvailable) {
                        return;
                    }

                    var builder = new StringBuilder();
                    builder.AppendLine();
                    builder.AppendLine($"[{DateTimeOffset.Now:O}] {level}: {Sanitize(message)}");

                    if (exception != null) {
                        builder.AppendLine(Sanitize(exception.ToString()));
                    }

                    File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch {
            }
        }

        private static bool TryInitializeAt(string path) {
            try {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) {
                    Directory.CreateDirectory(directory);
                }

                _logPath = path;
                File.WriteAllText(_logPath, BuildHeader(), Encoding.UTF8);
                return true;
            }
            catch {
                return false;
            }
        }

        private static string BuildHeader() {
            var version =
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var builder = new StringBuilder();

            builder.AppendLine("YSM Installer log");
            builder.AppendLine($"Started: {DateTimeOffset.Now:O}");
            builder.AppendLine($"Application version: {version}");
            builder.AppendLine($"OS version: {GetFriendlyOsVersion()}");
            builder.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            builder.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
            builder.AppendLine($"CLR version: {Environment.Version}");
            builder.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name}");
            builder.AppendLine($"UI culture: {CultureInfo.CurrentUICulture.Name}");
            builder.AppendLine($"Working set MB: {Environment.WorkingSet / 1024 / 1024}");
            builder.AppendLine($"Log path: {Sanitize(_logPath)}");

            return builder.ToString();
        }

        private static string GetFriendlyOsVersion() {
            try {
                using (
                    RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"
                    )
                ) {
                    if (key != null) {
                        string productName =
                            Convert.ToString(key.GetValue("ProductName")) ?? "Windows";
                        string? displayVersion =
                            Convert.ToString(key.GetValue("DisplayVersion"))
                            ?? Convert.ToString(key.GetValue("ReleaseId"));
                        string? currentBuild =
                            Convert.ToString(key.GetValue("CurrentBuildNumber"))
                            ?? Convert.ToString(key.GetValue("CurrentBuild"));
                        string? ubr = Convert.ToString(key.GetValue("UBR"));

                        if (
                            int.TryParse(currentBuild, out int buildNumber)
                            && buildNumber >= 22000
                            && productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase)
                                >= 0
                        ) {
                            productName = productName.Replace("Windows 10", "Windows 11");
                        }

                        var builder = new StringBuilder(productName);

                        if (!string.IsNullOrWhiteSpace(displayVersion)) {
                            builder.Append(' ');
                            builder.Append(displayVersion);
                        }

                        if (!string.IsNullOrWhiteSpace(currentBuild)) {
                            builder.Append(" (build ");
                            builder.Append(currentBuild);

                            if (!string.IsNullOrWhiteSpace(ubr)) {
                                builder.Append('.');
                                builder.Append(ubr);
                            }

                            builder.Append(')');
                        }

                        return builder.ToString();
                    }
                }
            }
            catch {
            }

            Version version = Environment.OSVersion.Version;
            return $"Windows {version.Major}.{version.Minor}.{version.Build}";
        }

        private static string Sanitize(string value) {
            if (string.IsNullOrEmpty(value)) {
                return value;
            }

            string result = value;
            result = ReplaceIfNotEmpty(
                result,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "%USERPROFILE%"
            );
            result = ReplaceIfNotEmpty(
                result,
                Path.GetTempPath()
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                "%TEMP%"
            );
            result = ReplaceIfNotEmpty(result, Environment.UserName, "%USERNAME%");
            return result;
        }

        private static string ReplaceIfNotEmpty(string value, string oldValue, string newValue) {
            if (string.IsNullOrWhiteSpace(oldValue)) {
                return value;
            }

            return value.Replace(oldValue, newValue);
        }
    }
}
