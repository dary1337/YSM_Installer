using System.IO;

namespace YSMInstaller {
    // Thrown when a download endpoint serves HTML (interstitial / quota page / error page)
    // instead of the expected binary payload. Deterministic per URL — retrying within seconds
    // is pointless because the underlying host condition (rate limit, quota cooldown) clears
    // in hours, not seconds.
    public sealed class RemoteHtmlResponseException : IOException {
        public RemoteHtmlResponseException(string message) : base(message) { }
    }
}
