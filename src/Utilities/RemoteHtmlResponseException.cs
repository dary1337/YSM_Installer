using System.IO;

namespace YSMInstaller {
    // Download served HTML (interstitial / quota page) instead of the binary; the host condition clears in hours, so retrying is pointless.
    public sealed class RemoteHtmlResponseException : IOException {
        public RemoteHtmlResponseException(string message) : base(message) { }
    }
}
