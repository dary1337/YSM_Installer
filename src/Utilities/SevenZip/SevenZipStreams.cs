using System;
using System.IO;
using System.Runtime.InteropServices;

namespace YSMInstaller.SevenZip {
    internal static class SevenZipStreamBuffer {
        // Fixed copy-chunk size for the native<->managed stream bridges. Read/Write report a partial
        // count when 7z asks for more, so the buffer never has to grow to an attacker-controlled size.
        public const int Size = 1 << 20;
    }

    // Feeds the archive file to 7z.dll during Open/Extract. 7z drives this synchronously on the
    // extraction thread, so no locking is needed. Implements ISequentialInStream as well as
    // IInStream because some handlers QueryInterface for the sequential base — returning
    // E_NOINTERFACE there caused intermittent open failures.
    internal sealed class InStreamWrapper : IInStream, ISequentialInStream, IDisposable {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[SevenZipStreamBuffer.Size];

        public InStreamWrapper(Stream stream) {
            _stream = stream;
        }

        public int Read(IntPtr data, uint size, IntPtr processedSize) {
            int toRead = (int)Math.Min(size, (uint)_buffer.Length);
            int read = _stream.Read(_buffer, 0, toRead);
            if (read > 0) {
                Marshal.Copy(_buffer, 0, data, read);
            }
            if (processedSize != IntPtr.Zero) {
                Marshal.WriteInt32(processedSize, read);
            }
            return HResult.Ok;
        }

        public int Seek(long offset, uint seekOrigin, IntPtr newPosition) {
            long pos = _stream.Seek(offset, (SeekOrigin)seekOrigin);
            if (newPosition != IntPtr.Zero) {
                Marshal.WriteInt64(newPosition, pos);
            }
            return HResult.Ok;
        }

        public void Dispose() {
            _stream.Dispose();
        }
    }

    // Receives one entry's bytes from 7z. Aborts mid-write (E_ABORT) when cancellation is requested
    // so a cancel during a multi-GB entry stops within a buffer, not at the entry boundary.
    internal sealed class OutStreamWrapper : ISequentialOutStream {
        private readonly Stream _stream;
        private readonly Action<long> _onWrote;
        private readonly Func<bool> _isCancellationRequested;
        private readonly Action<Exception> _reportError;
        private readonly byte[] _buffer = new byte[SevenZipStreamBuffer.Size];

        public OutStreamWrapper(
            Stream stream,
            Action<long> onWrote,
            Func<bool> isCancellationRequested,
            Action<Exception> reportError
        ) {
            _stream = stream;
            _onWrote = onWrote;
            _isCancellationRequested = isCancellationRequested;
            _reportError = reportError;
        }

        public int Write(IntPtr data, uint size, IntPtr processedSize) {
            if (_isCancellationRequested()) {
                return HResult.Abort;
            }
            try {
                // Consume at most one buffer; reporting a short processedSize tells 7z to call again
                // for the remainder, so a single huge request can't force an oversized allocation.
                int toWrite = (int)Math.Min(size, (uint)_buffer.Length);
                Marshal.Copy(data, _buffer, 0, toWrite);
                _stream.Write(_buffer, 0, toWrite);
                _onWrote(toWrite);
                if (processedSize != IntPtr.Zero) {
                    Marshal.WriteInt32(processedSize, toWrite);
                }
                return HResult.Ok;
            }
            catch (Exception exception) {
                // A write failure (e.g. disk full) must not escape across the COM boundary, where it
                // would collapse into a generic 7z HRESULT. Hand it to the callback so ThrowIfFaulted
                // can surface the real cause; report no processedSize on failure.
                _reportError(exception);
                return HResult.Fail;
            }
        }
    }
}
