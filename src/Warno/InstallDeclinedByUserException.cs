using System;

namespace YSMInstaller {
    // Distinct from OperationCanceledException so the workflow can map this user-initiated
    // soft-abort to Cancelled without needing a specific CancellationToken to be signalled.
    public sealed class InstallDeclinedByUserException : Exception {
        public InstallDeclinedByUserException(string message) : base(message) { }
    }
}
