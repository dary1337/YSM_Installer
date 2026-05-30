using System;
using System.Collections.Generic;
using System.IO;

namespace YSMInstaller {
    public static class IniFile {
        public static Dictionary<string, string> ReadValues(string path) {
            using (var reader = new StreamReader(path)) {
                return ReadValues(reader);
            }
        }

        public static Dictionary<string, string> ReadValues(TextReader reader) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            }
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? line;
            while ((line = reader.ReadLine()) != null) {
                string trimmed = line.Trim();
                if (
                    string.IsNullOrEmpty(trimmed)
                    || trimmed.StartsWith(";")
                    || trimmed.StartsWith("[")
                ) {
                    continue;
                }

                int separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0) {
                    continue;
                }

                string key = trimmed.Substring(0, separatorIndex).Trim();
                string value = trimmed.Substring(separatorIndex + 1).Split(';')[0].Trim();
                values[key] = value;
            }

            return values;
        }

        public static void WriteValues(string path, Dictionary<string, string> values) {
            var lines = File.ReadAllLines(path);
            var writtenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].Trim();
                if (
                    string.IsNullOrEmpty(trimmed)
                    || trimmed.StartsWith(";")
                    || trimmed.StartsWith("[")
                ) {
                    continue;
                }

                int separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0) {
                    continue;
                }

                string key = trimmed.Substring(0, separatorIndex).Trim();
                if (!values.ContainsKey(key)) {
                    continue;
                }

                string comment = lines[i].Contains(";")
                    ? lines[i].Substring(lines[i].IndexOf(";"))
                    : "";
                lines[i] = $"{key} = {values[key]} {comment}";
                writtenKeys.Add(key);
            }

            var output = new List<string>(lines);
            foreach (var item in values) {
                if (!writtenKeys.Contains(item.Key)) {
                    output.Add($"{item.Key} = {item.Value}");
                }
            }

            // Atomic write via tmp + File.Replace. A direct WriteAllLines truncates the target
            // before writing — a crash, BSOD, or kill mid-flush would leave WARNO's Config.ini
            // empty or partial, wiping ActivatedMods and the rest of the per-user game settings.
            // The replace happens inside the install's "no-cancel" finalize block, so we can't
            // rely on rollback to recover. File.Replace is NTFS-journal-atomic on the same volume.
            string tempPath = path + ".tmp";
            try {
                File.WriteAllLines(tempPath, output);
                if (File.Exists(path)) {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                else {
                    File.Move(tempPath, path);
                }
            }
            catch {
                try {
                    if (File.Exists(tempPath)) {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupException) {
                    // Cleanup of the partial tmp is best-effort — the outer exception is the one we
                    // care about and will rethrow. Log this so an antivirus / file-lock blocking the
                    // delete is at least visible in the log rather than silently swallowed.
                    AppLogger.Error($"Failed to delete partial Config.ini tmp '{tempPath}'.", cleanupException);
                }
                throw;
            }
        }

        public static string GetRequiredValue(
            Dictionary<string, string> values,
            string key,
            string path
        ) {
            if (!values.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value)) {
                throw new InvalidDataException(
                    $"Required config value '{key}' is missing in {path}."
                );
            }

            return value;
        }
    }
}
