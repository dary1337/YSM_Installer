using System;
using System.IO;

namespace YSMInstaller {
    internal static class DevService {
        private const string LogNextToExeKey = "LOG_NEXT_TO_EXE";
        private const string MockWarnoPathsKey = "MOCK_WARNO_PATHS";

        public static bool IsLogNextToExeEnabled {
            get { return GetBooleanFlag(LogNextToExeKey); }
        }

        public static bool IsMockWarnoPathsEnabled {
            get { return GetBooleanFlag(MockWarnoPathsKey); }
        }

        private static bool GetBooleanFlag(string key) {
            string? value = ReadEnvValue(key);
            return bool.TryParse(value, out bool result) && result;
        }

        private static string? ReadEnvValue(string key) {
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath)) {
                return null;
            }

            try {
                foreach (string line in File.ReadLines(envPath)) {
                    string? value = TryReadEnvValue(line, key);
                    if (value != null) {
                        return value;
                    }
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to read dev .env file: {envPath}", exception);
            }

            return null;
        }

        private static string? TryReadEnvValue(string line, string key) {
            string trimmed = line.Trim();
            if (
                string.IsNullOrWhiteSpace(trimmed)
                || trimmed.StartsWith("#", StringComparison.Ordinal)
            ) {
                return null;
            }

            int separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) {
                return null;
            }

            string envKey = trimmed.Substring(0, separatorIndex).Trim();
            if (!string.Equals(envKey, key, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            return trimmed.Substring(separatorIndex + 1).Trim().Trim('"', '\'');
        }
    }
}
