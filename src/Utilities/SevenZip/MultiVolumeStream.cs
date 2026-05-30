using System;
using System.Collections.Generic;
using System.IO;

namespace YSMInstaller.SevenZip {
    // Presents a sequence of `.001`, `.002`, ... files as a single seekable read-only stream so the
    // native 7z.dll can open byte-split archives (e.g. `.7z.001 + .7z.002 + ...`) without first
    // concatenating them to a temp file. Reads/seeks transparently cross part boundaries by opening
    // the part that contains the requested offset; only one FileStream is held at a time.
    internal sealed class MultiVolumeStream : Stream {
        private readonly string[] _paths;
        private readonly long[] _starts;   // cumulative absolute offset where each part begins
        private readonly long[] _sizes;
        private readonly long _length;
        private int _currentIndex = -1;
        private FileStream? _currentStream;
        private long _position;
        private bool _disposed;

        public MultiVolumeStream(IReadOnlyList<string> partPaths) {
            if (partPaths == null || partPaths.Count == 0) {
                throw new ArgumentException("At least one part is required.", nameof(partPaths));
            }
            _paths = new string[partPaths.Count];
            _starts = new long[partPaths.Count];
            _sizes = new long[partPaths.Count];
            long running = 0;
            for (int i = 0; i < partPaths.Count; i++) {
                _paths[i] = partPaths[i];
                _starts[i] = running;
                long size = new FileInfo(partPaths[i]).Length;
                _sizes[i] = size;
                running += size;
            }
            _length = running;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) {
            ThrowIfDisposed();
            if (count <= 0 || _position >= _length) {
                return 0;
            }
            EnsurePartForPosition();
            // Cap to whatever is left in the current part. 7z bridges already loop until they have
            // what they need, so a short read here is fine and avoids reopening mid-call.
            long remainingInPart = _sizes[_currentIndex] - (_position - _starts[_currentIndex]);
            int toRead = (int)Math.Min(count, remainingInPart);
            int read = _currentStream!.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) {
            ThrowIfDisposed();
            long target = origin switch {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0) {
                throw new IOException("Seek before start of stream.");
            }
            // Clamp seeks past the end to Length: 7z probes EOF by seeking to absurd offsets. The BCL
            // FileStream allows seeks past end but a read from there returns 0; we match that.
            _position = target;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void EnsurePartForPosition() {
            int target = FindPartIndex(_position);
            if (target != _currentIndex) {
                _currentStream?.Dispose();
                _currentStream = new FileStream(
                    _paths[target],
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                _currentIndex = target;
            }
            long relative = _position - _starts[target];
            if (_currentStream!.Position != relative) {
                _currentStream.Seek(relative, SeekOrigin.Begin);
            }
        }

        private int FindPartIndex(long absolute) {
            // Small N (typically 2–10 parts), so linear is faster than binary search overhead.
            for (int i = 0; i < _paths.Length; i++) {
                if (absolute < _starts[i] + _sizes[i]) {
                    return i;
                }
            }
            return _paths.Length - 1;
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(MultiVolumeStream));
            }
        }

        protected override void Dispose(bool disposing) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            if (disposing) {
                _currentStream?.Dispose();
                _currentStream = null;
            }
            base.Dispose(disposing);
        }

        // Resolves the sibling list for a first-volume path. Walks `.001`, `.002`, ... in the same
        // directory until the next number is missing; returns the absolute paths in order.
        public static List<string> ResolveSiblings(string firstVolumePath) {
            string directory = Path.GetDirectoryName(firstVolumePath)
                ?? throw new ArgumentException("First volume path has no directory.", nameof(firstVolumePath));
            string firstName = Path.GetFileName(firstVolumePath);
            int dotIndex = firstName.LastIndexOf('.');
            if (dotIndex < 0) {
                throw new ArgumentException("First volume path has no numeric suffix.", nameof(firstVolumePath));
            }
            string baseName = firstName.Substring(0, dotIndex + 1); // includes trailing dot
            string firstSuffix = firstName.Substring(dotIndex + 1);
            if (firstSuffix.Length < 2 || !int.TryParse(firstSuffix, out int firstNumber) || firstNumber != 1) {
                throw new ArgumentException($"First volume must end in '.001' (got '{firstSuffix}').", nameof(firstVolumePath));
            }
            int width = firstSuffix.Length; // preserve zero-padding width (3 for `.001`)

            var parts = new List<string>();
            int next = firstNumber;
            while (true) {
                string candidate = Path.Combine(directory, baseName + next.ToString(new string('0', width)));
                if (!File.Exists(candidate)) {
                    break;
                }
                parts.Add(candidate);
                next++;
            }
            if (parts.Count == 0) {
                throw new FileNotFoundException("First volume not found.", firstVolumePath);
            }
            return parts;
        }
    }
}
