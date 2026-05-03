using System;
using System.Collections.Generic;
using System.IO;

namespace YSMInstaller {
    public static class IniFile {
        public static Dictionary<string, string> ReadValues(string path) {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(path)) {
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

            File.WriteAllLines(path, output);
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
