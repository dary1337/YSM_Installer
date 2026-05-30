using System;
using System.IO;
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
        // Dedicated probe client: short timeout, kept process-wide because per-call `new HttpClient` would
        // accumulate sockets in TIME_WAIT (~2 min each) and eventually exhaust the ephemeral port range if
        // diagnostics ever ran in a loop. HttpService's main Client has a 60s timeout — unsuitable here.
        private static readonly HttpClient ProbeClient = CreateProbeClient();

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
                if (completed != lookup) {
                    return false;
                }
                System.Net.IPAddress[] addresses = await lookup;
                return addresses.Length > 0;
            }
            catch (SocketException) {
                return false;
            }
            catch (ArgumentException) {
                return false;
            }
        }

        private static async Task<bool> CanReachAsync(string url) {
            try {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                using (var response = await ProbeClient.SendAsync(request, cts.Token)) {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (HttpRequestException) {
                return false;
            }
            catch (TaskCanceledException) {
                return false;
            }
            catch (IOException) {
                return false;
            }
            catch (SocketException) {
                return false;
            }
        }

        private static HttpClient CreateProbeClient() {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YSMInstaller/1.0");
            return client;
        }

        private static string Shorten(string value) {
            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 140 ? value.Substring(0, 140) + "…" : value;
        }
    }
}
