using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using YSMInstaller.SevenZip;

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

    // Zip goes through the BCL (non-solid, already fast, zero native dependency); everything else
    // (7z/rar/tar/gz/bz2/xz) goes through the bundled native 7z.dll, which decodes a solid block in
    // a single pass instead of re-decoding it per entry.
    public static class SafeArchiveExtractor {
        private const long ProgressReportThresholdBytes = 1024 * 1024;

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
                ExtractWithSevenZip(archivePath, fullDestinationPath, progress, detailedProgress, cancellationToken);
            }
        }

        // Reads only the archive's central directory / index, not the body — cheap even for
        // multi-GB archives. Used for pre-extraction disk-space checks.
        public static long MeasureUncompressedSize(
            string archivePath,
            CancellationToken cancellationToken = default
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsZip(archivePath)) {
                using (var archive = ZipFile.OpenRead(archivePath)) {
                    long total = 0;
                    foreach (var entry in archive.Entries) {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!string.IsNullOrEmpty(entry.Name)) {
                            total = AddOrThrow(total, entry.Length, entry.FullName);
                        }
                    }
                    return total;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using (var archive = SevenZipArchive.Open(archivePath)) {
                long total = 0;
                foreach (SevenZipEntry entry in archive.Entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory) {
                        total = AddOrThrow(total, Math.Max(0, entry.Size), entry.Path);
                    }
                }
                return total;
            }
        }

        // Checked addition so a malformed/malicious archive declaring huge per-entry sizes
        // can't overflow to negative and silently bypass the downstream disk-space check.
        private static long AddOrThrow(long total, long delta, string entryName) {
            try {
                return checked(total + delta);
            }
            catch (OverflowException) {
                throw new InvalidDataException(
                    $"Archive reports an impossibly large total size — file may be malformed or malicious (entry: {entryName})."
                );
            }
        }

        public static byte[]? ReadEntryBytes(string archivePath, string entryFileName) {
            return IsZip(archivePath)
                ? ReadEntryBytesZip(archivePath, entryFileName)
                : ReadEntryBytesSevenZip(archivePath, entryFileName);
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

        private static void ExtractWithSevenZip(
            string archivePath,
            string fullDestinationPath,
            IProgress<int>? progress,
            IProgress<ArchiveExtractionProgress>? detailedProgress,
            CancellationToken cancellationToken
        ) {
            // Single-stream formats (gz/xz/bz2) can report an empty entry name; fall back to the
            // archive's own base name so the decompressed payload still lands as a real file.
            string fallbackName = Path.GetFileNameWithoutExtension(archivePath);
            if (string.IsNullOrEmpty(fallbackName)) {
                fallbackName = "extracted";
            }

            using (var archive = SevenZipArchive.Open(archivePath)) {
                long totalBytes = archive.Entries
                    .Where(e => !e.IsDirectory)
                    .Sum(e => Math.Max(0, e.Size));
                var tracker = new ProgressTracker(totalBytes, progress, detailedProgress);

                archive.ExtractAll(
                    entry => {
                        cancellationToken.ThrowIfCancellationRequested();
                        string name = string.IsNullOrEmpty(entry.Path) ? fallbackName : entry.Path;
                        string entryPath = ResolveSafeEntryPath(fullDestinationPath, name);

                        if (entry.IsDirectory) {
                            Directory.CreateDirectory(entryPath);
                            return null;
                        }

                        string entryDirectory =
                            Path.GetDirectoryName(entryPath)
                            ?? throw new InvalidDataException(
                                $"Archive entry has an invalid path: {name}"
                            );
                        Directory.CreateDirectory(entryDirectory);
                        return File.Create(entryPath);
                    },
                    tracker.Add,
                    cancellationToken
                );

                tracker.Flush();
            }
        }

        private static byte[]? ReadEntryBytesSevenZip(string archivePath, string entryFileName) {
            using (var archive = SevenZipArchive.Open(archivePath)) {
                var matches = archive.Entries
                    .Where(e =>
                        !e.IsDirectory
                        && !string.IsNullOrEmpty(e.Path)
                        && string.Equals(
                            Path.GetFileName(e.Path.Replace('\\', '/')),
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
                return archive.ExtractEntryBytes(matches[0], CancellationToken.None);
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
            byte[] buffer = new byte[1024 * 1024];
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
            // Matches HttpService's download throttle: native 7z extraction emits byte updates far
            // faster than the network path did, so the detail label ("Extracting… X / Y MB") would
            // repaint hundreds of times a second and visibly flicker without a time gate. The percent
            // channel is already change-gated, so only the detail report needs throttling.
            private static readonly TimeSpan DetailedReportMinInterval = TimeSpan.FromMilliseconds(200);

            private readonly long _totalBytes;
            private readonly IProgress<int>? _percentProgress;
            private readonly IProgress<ArchiveExtractionProgress>? _detailedProgress;
            private long _bytesExtracted;
            private long _lastReportedBytes = -ProgressReportThresholdBytes;
            private int _lastReportedPercent = -1;
            private DateTime _lastDetailedReportAt = DateTime.MinValue;

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
                Report(force: false);
            }

            public void Flush() {
                Report(force: true);
            }

            private void Report(bool force) {
                if (_detailedProgress != null) {
                    DateTime now = DateTime.UtcNow;
                    if (force || now - _lastDetailedReportAt >= DetailedReportMinInterval) {
                        _lastDetailedReportAt = now;
                        _detailedProgress.Report(
                            new ArchiveExtractionProgress(
                                _bytesExtracted,
                                _totalBytes > 0 ? _totalBytes : (long?)null
                            )
                        );
                    }
                }
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
