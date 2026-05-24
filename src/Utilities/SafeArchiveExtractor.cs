using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using SharpCompress.Archives;

namespace YSMInstaller {
    public sealed class ArchiveExtractionProgress {
        public ArchiveExtractionProgress(long bytesExtracted, long? totalBytes) {
            BytesExtracted = bytesExtracted;
            TotalBytes = totalBytes;
        }

        public long BytesExtracted { get; }
        public long? TotalBytes { get; }
    }

    // Thrown when ReadEntryBytes finds more than one matching entry; callers gate on the type,
    // not on exception message text. Derives from IOException (InvalidDataException is sealed on
    // .NET Framework 4.7.2) so any existing IOException-level handlers still catch it.
    public sealed class MultipleArchiveEntriesException : IOException {
        public MultipleArchiveEntriesException(string message) : base(message) { }
    }

    public static class SafeArchiveExtractor {
        private const int CopyBufferSize = 1024 * 1024;
        private const long ProgressReportThresholdBytes = 1024 * 1024;

        static SafeArchiveExtractor() {
            // Older zip/rar/7z entries can carry filenames in legacy code pages (cp866, cp1251)
            // that aren't available on .NET Framework by default; SharpCompress falls back to
            // these via the code pages provider.
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch (Exception exception) {
                // If the codepages assembly is missing entirely, SharpCompress would later crash
                // with a less obvious encoding error — log here so the root cause is visible.
                // Not rethrown: this runs in the type initializer, so a throw would convert every
                // call site into TypeInitializationException and take the app down for what is a
                // recoverable degradation (ASCII-only filenames still work).
                AppLogger.Critical("Failed to register code pages encoding provider.", exception);
            }
        }

        public static void ExtractToDirectory(
            string archivePath,
            string destinationDirectory,
            IProgress<int>? progress = null,
            IProgress<ArchiveExtractionProgress>? detailedProgress = null,
            CancellationToken cancellationToken = default
        ) {
            string fullDestinationPath = Path.GetFullPath(destinationDirectory);
            if (IsZip(archivePath)) {
                ExtractZip(archivePath, fullDestinationPath, progress, detailedProgress, cancellationToken);
            }
            else {
                ExtractWithSharpCompress(archivePath, fullDestinationPath, progress, detailedProgress, cancellationToken);
            }
        }

        // Reads only the archive's central directory / index, not the body — cheap (~10-100 ms
        // even for multi-GB archives). Used for pre-extraction disk-space checks.
        public static long MeasureUncompressedSize(string archivePath) {
            if (IsZip(archivePath)) {
                using (var archive = ZipFile.OpenRead(archivePath)) {
                    return archive.Entries
                        .Where(e => !string.IsNullOrEmpty(e.Name))
                        .Sum(e => e.Length);
                }
            }
            using (var archive = ArchiveFactory.Open(archivePath)) {
                return archive.Entries
                    .Where(e => !e.IsDirectory)
                    .Sum(e => Math.Max(0, e.Size));
            }
        }

        public static byte[]? ReadEntryBytes(string archivePath, string entryFileName) {
            return IsZip(archivePath)
                ? ReadEntryBytesZip(archivePath, entryFileName)
                : ReadEntryBytesSharpCompress(archivePath, entryFileName);
        }

        private static void ExtractZip(
            string archivePath,
            string fullDestinationPath,
            IProgress<int>? progress,
            IProgress<ArchiveExtractionProgress>? detailedProgress,
            CancellationToken cancellationToken
        ) {
            using (var archive = ZipFile.OpenRead(archivePath)) {
                long totalBytes = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Sum(e => e.Length);
                var tracker = new ProgressTracker(totalBytes, progress, detailedProgress);

                foreach (var entry in archive.Entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entryPath = ResolveSafeEntryPath(fullDestinationPath, entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name)) {
                        Directory.CreateDirectory(entryPath);
                        continue;
                    }

                    string entryDirectory =
                        Path.GetDirectoryName(entryPath)
                        ?? throw new InvalidDataException(
                            $"Archive entry has an invalid path: {entry.FullName}"
                        );
                    Directory.CreateDirectory(entryDirectory);

                    using (var source = entry.Open())
                    using (var destination = File.Create(entryPath)) {
                        CopyWithProgress(source, destination, tracker, cancellationToken);
                    }
                }

                tracker.Flush();
            }
        }

        private static byte[]? ReadEntryBytesZip(string archivePath, string entryFileName) {
            using (var archive = ZipFile.OpenRead(archivePath)) {
                var matches = archive.Entries
                    .Where(e => string.Equals(e.Name, entryFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 0) {
                    return null;
                }
                if (matches.Count > 1) {
                    throw new MultipleArchiveEntriesException(
                        $"Archive contains multiple '{entryFileName}' entries."
                    );
                }
                using (var stream = matches[0].Open())
                using (var memory = new MemoryStream()) {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        private static void ExtractWithSharpCompress(
            string archivePath,
            string fullDestinationPath,
            IProgress<int>? progress,
            IProgress<ArchiveExtractionProgress>? detailedProgress,
            CancellationToken cancellationToken
        ) {
            using (var archive = ArchiveFactory.Open(archivePath)) {
                long totalBytes = archive.Entries
                    .Where(e => !e.IsDirectory)
                    .Sum(e => Math.Max(0, e.Size));
                var tracker = new ProgressTracker(totalBytes, progress, detailedProgress);

                foreach (var entry in archive.Entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    string key = entry.Key
                        ?? throw new InvalidDataException("Archive entry has a null key.");
                    string entryPath = ResolveSafeEntryPath(fullDestinationPath, key);

                    if (entry.IsDirectory) {
                        Directory.CreateDirectory(entryPath);
                        continue;
                    }

                    string entryDirectory =
                        Path.GetDirectoryName(entryPath)
                        ?? throw new InvalidDataException(
                            $"Archive entry has an invalid path: {key}"
                        );
                    Directory.CreateDirectory(entryDirectory);

                    using (var source = entry.OpenEntryStream())
                    using (var destination = File.Create(entryPath)) {
                        CopyWithProgress(source, destination, tracker, cancellationToken);
                    }
                }

                tracker.Flush();
            }
        }

        private static byte[]? ReadEntryBytesSharpCompress(string archivePath, string entryFileName) {
            using (var archive = ArchiveFactory.Open(archivePath)) {
                var matches = archive.Entries
                    .Where(e =>
                        !e.IsDirectory
                        && e.Key != null
                        && string.Equals(
                            Path.GetFileName(e.Key.Replace('\\', '/')),
                            entryFileName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
                if (matches.Count == 0) {
                    return null;
                }
                if (matches.Count > 1) {
                    throw new MultipleArchiveEntriesException(
                        $"Archive contains multiple '{entryFileName}' entries."
                    );
                }
                using (var stream = matches[0].OpenEntryStream())
                using (var memory = new MemoryStream()) {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        // Copies source → destination in chunks so cancellation lands within ms even on
        // multi-gigabyte entries; Stream.CopyTo() ignores tokens, which would let a cancel
        // request sit until the entry finishes.
        private static void CopyWithProgress(
            Stream source,
            Stream destination,
            ProgressTracker tracker,
            CancellationToken cancellationToken
        ) {
            byte[] buffer = new byte[CopyBufferSize];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
                tracker.Add(read);
            }
        }

        private static string ResolveSafeEntryPath(string fullDestinationPath, string entryName) {
            string normalized = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string entryPath = Path.GetFullPath(
                Path.Combine(fullDestinationPath, normalized)
            );

            if (
                !entryPath.StartsWith(
                    fullDestinationPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    entryPath,
                    fullDestinationPath,
                    StringComparison.OrdinalIgnoreCase
                )
            ) {
                throw new InvalidDataException(
                    $"Archive entry points outside destination: {entryName}"
                );
            }

            return entryPath;
        }

        private static bool IsZip(string archivePath) {
            byte[] header = new byte[4];
            int read;
            using (var fs = File.OpenRead(archivePath)) {
                read = fs.Read(header, 0, header.Length);
            }
            return read >= 4
                && header[0] == 0x50
                && header[1] == 0x4B
                && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07);
        }

        private sealed class ProgressTracker {
            private readonly long _totalBytes;
            private readonly IProgress<int>? _percentProgress;
            private readonly IProgress<ArchiveExtractionProgress>? _detailedProgress;
            private long _bytesExtracted;
            private long _lastReportedBytes = -ProgressReportThresholdBytes;
            private int _lastReportedPercent = -1;

            public ProgressTracker(
                long totalBytes,
                IProgress<int>? percentProgress,
                IProgress<ArchiveExtractionProgress>? detailedProgress
            ) {
                _totalBytes = totalBytes;
                _percentProgress = percentProgress;
                _detailedProgress = detailedProgress;
            }

            public void Add(long delta) {
                _bytesExtracted += delta;
                if (_bytesExtracted - _lastReportedBytes < ProgressReportThresholdBytes) {
                    return;
                }
                _lastReportedBytes = _bytesExtracted;
                Report();
            }

            public void Flush() {
                Report();
            }

            private void Report() {
                _detailedProgress?.Report(
                    new ArchiveExtractionProgress(
                        _bytesExtracted,
                        _totalBytes > 0 ? _totalBytes : (long?)null
                    )
                );
                if (_percentProgress != null && _totalBytes > 0) {
                    int percent = (int)(_bytesExtracted * 100L / _totalBytes);
                    if (percent > 100) {
                        percent = 100;
                    }
                    if (percent != _lastReportedPercent) {
                        _percentProgress.Report(percent);
                        _lastReportedPercent = percent;
                    }
                }
            }
        }
    }
}
