using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace YSMInstaller.SevenZip {
    internal sealed class SevenZipEntry {
        public SevenZipEntry(uint index, string path, bool isDirectory, long size) {
            Index = index;
            Path = path;
            IsDirectory = isDirectory;
            Size = size;
        }

        public uint Index { get; }
        public string Path { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
    }

    // Opens an archive through the native 7z.dll and exposes the few operations the installer needs.
    // Bulk extraction goes through a single Extract(all-indices) call so solid blocks (.7z/.rar) are
    // decoded once front-to-back rather than re-decoded per entry.
    internal sealed class SevenZipArchive : IDisposable {
        // Upper bound on how far into the file 7z scans for the archive signature — generous so a
        // self-extracting stub prepended to the real archive is still located.
        private const ulong SignatureScanLimit = 1 << 23;
        // Bytes sniffed up front to pick the format handler. 262 is the minimum to reach the tar
        // "ustar" magic at offset 257; 512 is one tar record and leaves margin.
        private const int HeaderProbeBytes = 512;
        private const int TarMagicOffset = 257;

        private readonly IInArchive _archive;
        private readonly InStreamWrapper _stream;
        private readonly List<SevenZipEntry> _entries;

        private SevenZipArchive(IInArchive archive, InStreamWrapper stream, List<SevenZipEntry> entries) {
            _archive = archive;
            _stream = stream;
            _entries = entries;
        }

        public IReadOnlyList<SevenZipEntry> Entries => _entries;

        public static SevenZipArchive Open(string archivePath) {
            Guid classId = SevenZipFormat.FromHandlerId(DetectHandlerId(archivePath));
            IInArchive archive = SevenZipLibrary.CreateInArchive(classId);
            InStreamWrapper? stream = null;
            try {
                stream = new InStreamWrapper(File.OpenRead(archivePath));
                ulong maxCheck = SignatureScanLimit;
                int hr = archive.Open(stream, ref maxCheck, null);
                if (hr != HResult.Ok) {
                    // Prefer the real source-read failure (e.g. file vanished) over the opaque HRESULT.
                    stream.FirstError?.Throw();
                    throw new InvalidDataException(
                        $"7z.dll could not open the archive (HRESULT 0x{hr:X8}): {archivePath}"
                    );
                }
                List<SevenZipEntry> entries = ReadEntries(archive);
                var result = new SevenZipArchive(archive, stream, entries);
                stream = null;
                return result;
            }
            catch {
                stream?.Dispose();
                SafeRelease(archive);
                throw;
            }
        }

        private static List<SevenZipEntry> ReadEntries(IInArchive archive) {
            int hr = archive.GetNumberOfItems(out uint count);
            if (hr != HResult.Ok) {
                throw new InvalidDataException($"7z.dll could not read the archive index (HRESULT 0x{hr:X8}).");
            }
            var entries = new List<SevenZipEntry>((int)Math.Min(count, int.MaxValue));
            // A throw mid-loop disposes the buffer (PropVariantClear + free) via the using, so a
            // failed read never leaves a leaked allocation or a half-built entry table.
            using (var prop = new PropVariantBuffer()) {
                for (uint i = 0; i < count; i++) {
                    RequireOk(archive.GetProperty(i, ItemPropId.Path, prop.Ptr), ItemPropId.Path, i);
                    string path = prop.ReadString() ?? string.Empty;
                    prop.Reset();

                    RequireOk(archive.GetProperty(i, ItemPropId.IsFolder, prop.Ptr), ItemPropId.IsFolder, i);
                    bool isDir = prop.ReadBool();
                    prop.Reset();

                    RequireOk(archive.GetProperty(i, ItemPropId.Size, prop.Ptr), ItemPropId.Size, i);
                    ulong rawSize = prop.ReadUInt64();
                    prop.Reset();
                    // Reject (don't wrap) sizes above long.MaxValue: a wrapped-negative size would be
                    // clamped to 0 by downstream Math.Max(0, ...) and bypass the disk-space check.
                    if (rawSize > long.MaxValue) {
                        throw new InvalidDataException(
                            $"Archive entry {i} reports an impossibly large size ({rawSize}) — file may be malformed or malicious."
                        );
                    }

                    entries.Add(new SevenZipEntry(i, path, isDir, (long)rawSize));
                }
            }
            return entries;
        }

        private static void RequireOk(int hr, ItemPropId propId, uint index) {
            if (hr != HResult.Ok) {
                throw new InvalidDataException(
                    $"7z.dll failed to read property {propId} of entry {index} (HRESULT 0x{hr:X8})."
                );
            }
        }

        // Streams every entry out in one decode pass. The callback resolves each index to a target
        // stream (or null to skip/create a directory), so path-safety stays under our control while
        // 7z keeps the solid block decoded only once.
        public void ExtractAll(
            Func<SevenZipEntry, Stream?> openTarget,
            Action<long> onBytesWritten,
            CancellationToken cancellationToken
        ) {
            using (var callback = new ArchiveExtractCallback(this, openTarget, onBytesWritten, cancellationToken)) {
                int hr = _archive.Extract(null, ExtractIndices.All, 0, callback);
                callback.ThrowIfFaulted(hr, cancellationToken, _stream.FirstError);
            }
        }

        // Even on a solid block, 7z only decodes up to the requested entry — no full-archive pass.
        public byte[] ExtractEntryBytes(SevenZipEntry entry, CancellationToken cancellationToken) {
            using (var memory = new MemoryStream(entry.Size > 0 && entry.Size < int.MaxValue ? (int)entry.Size : 0)) {
                Func<SevenZipEntry, Stream?> open = e => e.Index == entry.Index ? memory : null;
                using (var callback = new ArchiveExtractCallback(this, open, _ => { }, cancellationToken)) {
                    int hr = _archive.Extract(new[] { entry.Index }, 1, 0, callback);
                    callback.ThrowIfFaulted(hr, cancellationToken, _stream.FirstError);
                }
                return memory.ToArray();
            }
        }

        public void Dispose() {
            _archive.Close();
            SafeRelease(_archive);
            // Keep the input-stream CCW rooted until after Close: 7z still reads through it during
            // Extract, and the only managed reference is this field.
            GC.KeepAlive(_stream);
            _stream.Dispose();
        }

        private static void SafeRelease(IInArchive archive) {
            if (Marshal.IsComObject(archive)) {
                Marshal.ReleaseComObject(archive);
            }
        }

        private static byte DetectHandlerId(string archivePath) {
            byte[] header = new byte[HeaderProbeBytes];
            int read;
            using (var fs = File.OpenRead(archivePath)) {
                read = fs.Read(header, 0, header.Length);
            }

            if (StartsWith(header, read, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) return SevenZipFormat.SevenZip;
            if (StartsWith(header, read, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00)) return SevenZipFormat.Rar5;
            if (StartsWith(header, read, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00)) return SevenZipFormat.Rar;
            if (StartsWith(header, read, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00)) return SevenZipFormat.Xz;
            if (StartsWith(header, read, 0x42, 0x5A, 0x68)) return SevenZipFormat.BZip2;
            if (StartsWith(header, read, 0x1F, 0x8B)) return SevenZipFormat.GZip;
            if (HasTarMagic(header, read)) return SevenZipFormat.Tar;

            throw new InvalidDataException(
                $"Unrecognized archive format (not 7z/rar/xz/bzip2/gzip/tar): {archivePath}"
            );
        }

        private static bool HasTarMagic(byte[] header, int length) {
            // POSIX ustar magic "ustar" lives at offset 257 in the first tar record.
            if (length < TarMagicOffset + 5) {
                return false;
            }
            return header[TarMagicOffset] == 'u'
                && header[TarMagicOffset + 1] == 's'
                && header[TarMagicOffset + 2] == 't'
                && header[TarMagicOffset + 3] == 'a'
                && header[TarMagicOffset + 4] == 'r';
        }

        private static bool StartsWith(byte[] header, int length, params byte[] signature) {
            if (length < signature.Length) {
                return false;
            }
            for (int i = 0; i < signature.Length; i++) {
                if (header[i] != signature[i]) {
                    return false;
                }
            }
            return true;
        }
    }

    internal sealed class ArchiveExtractCallback : IArchiveExtractCallback, IDisposable {
        private readonly SevenZipArchive _archive;
        private readonly Func<SevenZipEntry, Stream?> _openTarget;
        private readonly Action<long> _onBytesWritten;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<uint, SevenZipEntry> _entriesByIndex;

        private Stream? _currentStream;
        // Captured (not rethrown inline) so the exception doesn't cross the COM boundary; replayed by
        // ThrowIfFaulted via ExceptionDispatchInfo, which preserves the original stack trace.
        private ExceptionDispatchInfo? _pendingError;
        private bool _cancelled;

        public ArchiveExtractCallback(
            SevenZipArchive archive,
            Func<SevenZipEntry, Stream?> openTarget,
            Action<long> onBytesWritten,
            CancellationToken cancellationToken
        ) {
            _archive = archive;
            _openTarget = openTarget;
            _onBytesWritten = onBytesWritten;
            _cancellationToken = cancellationToken;
            _entriesByIndex = new Dictionary<uint, SevenZipEntry>(archive.Entries.Count);
            foreach (SevenZipEntry entry in archive.Entries) {
                _entriesByIndex[entry.Index] = entry;
            }
        }

        public int SetTotal(ulong total) => HResult.Ok;

        public int SetCompleted(IntPtr completeValue) {
            return _cancellationToken.IsCancellationRequested ? Abort() : HResult.Ok;
        }

        public int GetStream(uint index, out ISequentialOutStream? outStream, int askExtractMode) {
            outStream = null;
            if (askExtractMode != ExtractAskMode.Extract) {
                return HResult.Ok;
            }
            if (_cancellationToken.IsCancellationRequested) {
                return Abort();
            }
            try {
                if (!_entriesByIndex.TryGetValue(index, out SevenZipEntry entry)) {
                    return HResult.Ok;
                }
                Stream? target = _openTarget(entry);
                if (target == null) {
                    return HResult.Ok;
                }
                _currentStream = target;
                outStream = new OutStreamWrapper(
                    target,
                    _onBytesWritten,
                    () => _cancellationToken.IsCancellationRequested,
                    exception => _pendingError ??= ExceptionDispatchInfo.Capture(exception)
                );
                return HResult.Ok;
            }
            catch (OperationCanceledException) {
                return Abort();
            }
            catch (Exception exception) {
                _pendingError ??= ExceptionDispatchInfo.Capture(exception);
                return HResult.Fail;
            }
        }

        public int PrepareOperation(int askExtractMode) => HResult.Ok;

        public int SetOperationResult(int operationResult) {
            _currentStream?.Dispose();
            _currentStream = null;
            if (operationResult != OperationResult.Ok) {
                _pendingError ??= ExceptionDispatchInfo.Capture(new IOException(
                    $"7z reported extraction failure for an entry (operation result {operationResult})."
                ));
                return HResult.Fail;
            }
            return HResult.Ok;
        }

        // transportError is the source-stream read/seek failure captured by InStreamWrapper, surfaced
        // ahead of the generic HRESULT but behind cancellation and per-entry write errors.
        public void ThrowIfFaulted(int extractHResult, CancellationToken cancellationToken, ExceptionDispatchInfo? transportError) {
            if (_cancelled || extractHResult == HResult.Abort) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            // .Throw() replays the captured exception with its original stack trace intact.
            _pendingError?.Throw();
            transportError?.Throw();
            if (extractHResult != HResult.Ok) {
                throw new IOException($"7z extraction failed (HRESULT 0x{extractHResult:X8}).");
            }
        }

        public void Dispose() {
            _currentStream?.Dispose();
            _currentStream = null;
        }

        private int Abort() {
            _cancelled = true;
            return HResult.Abort;
        }
    }
}
