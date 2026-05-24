using System;
using System.IO;

namespace YSMInstaller {
    public sealed class DiskSpaceWarning {
        public DiskSpaceWarning(string drive, string action, long availableBytes, long requiredBytes) {
            Drive = drive;
            Action = action;
            AvailableBytes = availableBytes;
            RequiredBytes = requiredBytes;
        }

        public string Drive { get; }
        public string Action { get; }
        public long AvailableBytes { get; }
        public long RequiredBytes { get; }

        public string Message =>
            $"Low disk space on {Drive} for {Action}: " +
            $"{FormatGb(AvailableBytes)} free, {FormatGb(RequiredBytes)} estimated. " +
            "Installation may fail mid-way if other apps consume the remaining space.";

        private static string FormatGb(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }

    public static class DiskSpace {
        // Headroom so an install that exactly fits doesn't wedge the OS — Windows starts
        // misbehaving (page file, temp writes by other apps) when the system drive crosses ~95%.
        public const long SafetyMarginBytes = 200L * 1024 * 1024;

        // Real ratio for our 7z mods is ~1.15x; padded heavily to cover parallel disk activity
        // (other downloads, OS updates) and prefer warning over crashing mid-extract.
        public const double EstimatedExtractedRatio = 2.5;

        // Smaller cushion for cases where we already know the precise extracted size (manual
        // archive / folder copy) — covers background disk activity without the formula's slack.
        public const double KnownSizeHeadroomRatio = 1.5;

        // Peak = compressed archive + uncompressed extracted tree on the same drive simultaneously.
        public static long EstimatePeakBytesForDownloadAndExtract(long compressedBytes) {
            if (compressedBytes <= 0) {
                return 0;
            }
            return compressedBytes + (long)(compressedBytes * EstimatedExtractedRatio);
        }

        public static long ApplyKnownSizeHeadroom(long actualBytes) {
            if (actualBytes <= 0) {
                return 0;
            }
            return (long)(actualBytes * KnownSizeHeadroomRatio);
        }

        public static long GetAvailableFreeSpace(string path) {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) {
                throw new ArgumentException(
                    $"Could not determine drive root for path: {path}",
                    nameof(path)
                );
            }
            return new DriveInfo(root).AvailableFreeSpace;
        }

        // Soft-check: a yanked removable drive shouldn't pre-emptively fail — real I/O will
        // surface the underlying error if it actually matters.
        public static DiskSpaceWarning? CheckAvailableSpace(string path, long requiredBytes, string action) {
            if (requiredBytes <= 0) {
                return null;
            }
            long needed = requiredBytes + SafetyMarginBytes;
            long available;
            try {
                available = GetAvailableFreeSpace(path);
            }
            catch (Exception exception) {
                AppLogger.Critical(
                    $"Could not check free space for '{path}' before {action}.",
                    exception
                );
                return null;
            }
            if (available >= needed) {
                return null;
            }
            string drive = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            return new DiskSpaceWarning(drive, action, available, needed);
        }
    }
}
