using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller {
    /// <summary>
    /// Lightweight connectivity diagnosis so an offline / catalog failure tells the user what actually
    /// went wrong: no internet at all, GitHub unreachable, or the catalog parsed badly.
    /// </summary>
    public static class Connectivity {
        public static async Task<string> DiagnoseCatalogAsync(string? technicalReason = null) {
            bool google = await CanResolveAsync("www.google.com");
            bool github = await CanResolveAsync("github.com");

            if (!google && !github) {
                return "You appear to be offline. Check your internet connection and try again.";
            }

            if (!github || !await CanReachAsync("https://github.com")) {
                return "GitHub is unreachable right now. The mod catalog is hosted there, so try again in a bit.";
            }

            string detail = string.IsNullOrWhiteSpace(technicalReason)
                ? "the catalog response was invalid."
                : $"reading it failed: {Shorten(technicalReason!)}";
            return $"Connected, but the mod list could not be loaded — {detail}";
        }

        private static async Task<bool> CanResolveAsync(string host) {
            try {
                Task<System.Net.IPAddress[]> lookup = System.Net.Dns.GetHostAddressesAsync(host);
                Task completed = await Task.WhenAny(lookup, Task.Delay(3000));
                return completed == lookup && lookup.Result.Length > 0;
            }
            catch (SocketException) {
                return false;
            }
            catch (Exception) {
                return false;
            }
        }

        private static async Task<bool> CanReachAsync(string url) {
            try {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                using (var request = new HttpRequestMessage(HttpMethod.Head, url)) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("YSMInstaller/1.0");
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                    using (var response = await client.SendAsync(request, cts.Token)) {
                        // 4xx/5xx means the host is reachable but our probe failed — treat as unreachable
                        // so the caller surfaces a GitHub reachability message, not a "catalog format" one.
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception) {
                return false;
            }
        }

        private static string Shorten(string value) {
            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 140 ? value.Substring(0, 140) + "…" : value;
        }
    }
}
