using System;
using System.Text.RegularExpressions;

namespace YSMInstaller {
    public static class GoogleDriveLinks {
        // /file/d/{ID}/..., /uc?id={ID}, /open?id={ID}, /d/{ID} all converge on the same ID.
        private static readonly Regex FileIdInPath = new Regex(
            @"^/(?:file/d|d|drive/folders|folders)/([A-Za-z0-9_-]+)",
            RegexOptions.Compiled
        );

        // drive.usercontent.google.com/download with confirm=t skips Google's "can't scan for
        // viruses" interstitial that otherwise breaks any non-browser GET on files >100 MB.
        public static string Normalize(string url) {
            if (string.IsNullOrWhiteSpace(url)) {
                return url;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) {
                return url;
            }
            if (!IsGoogleDriveHost(uri.Host)) {
                return url;
            }

            string fileId = ExtractFileId(uri);
            if (string.IsNullOrEmpty(fileId)) {
                return url;
            }

            return $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
        }

        private static bool IsGoogleDriveHost(string host) {
            return host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractFileId(Uri uri) {
            Match pathMatch = FileIdInPath.Match(uri.AbsolutePath);
            if (pathMatch.Success) {
                return pathMatch.Groups[1].Value;
            }
            return GetQueryParam(uri.Query, "id") ?? string.Empty;
        }

        private static string? GetQueryParam(string query, string name) {
            if (string.IsNullOrEmpty(query)) {
                return null;
            }
            string trimmed = query.TrimStart('?');
            foreach (string pair in trimmed.Split('&')) {
                int eq = pair.IndexOf('=');
                if (eq <= 0) {
                    continue;
                }
                string key = Uri.UnescapeDataString(pair.Substring(0, eq));
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) {
                    return Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            }
            return null;
        }
    }
}
