using System;
using System.Runtime.InteropServices;

namespace YSMInstaller.SevenZip {
    // Minimal hand-rolled interop for 7z.dll's IInArchive COM surface. We implement only the
    // pieces extraction needs (open, enumerate, extract, single-file read) instead of taking a
    // managed wrapper dependency — keeps the bundle small and matches the project's existing
    // hand-written COM style (see TaskbarProgress). Interface vtables mirror 7-Zip's
    // CPP/7zip/Archive/IArchive.h and IStream.h exactly; the method order is load-bearing.

    internal static class SevenZipFormat {
        // Format CLSIDs are {23170F69-40C1-278A-1000-000110<id>0000}; <id> is the archive kind.
        public static Guid FromHandlerId(byte id) {
            return new Guid(0x23170F69, 0x40C1, 0x278A, 0x10, 0x00, 0x00, 0x01, 0x10, id, 0x00, 0x00);
        }

        public const byte Zip = 0x01;
        public const byte BZip2 = 0x02;
        public const byte Rar = 0x03;
        public const byte Xz = 0x0C;
        public const byte SevenZip = 0x07;
        public const byte Tar = 0xEE;
        public const byte GZip = 0xEF;
        public const byte Rar5 = 0xCC;
    }

    // 7-Zip PROPID values we read (subset of CPP/7zip/PropID.h).
    internal enum ItemPropId : uint {
        Path = 3,
        IsFolder = 6,
        Size = 7,
    }

    // NAskMode from IArchive.h.
    internal static class ExtractAskMode {
        public const int Extract = 0;
        public const int Test = 1;
        public const int Skip = 2;
    }

    // NOperationResult from IArchive.h (per-entry extraction outcome).
    internal static class OperationResult {
        public const int Ok = 0;
    }

    internal static class ExtractIndices {
        // Passed as numItems to Extract to mean "every entry" (indices pointer is then null).
        public const uint All = 0xFFFFFFFF;
    }

    internal static class HResult {
        public const int Ok = 0;
        public const int False = 1;
        public const int Abort = unchecked((int)0x80004004); // E_ABORT
        public const int Fail = unchecked((int)0x80004005);   // E_FAIL
    }

    // Reads a 16-byte native PROPVARIANT that we allocate and own. Passing a managed struct by-ref
    // to the COM property getter produced miscompiled/garbage reads under the optimizing JIT
    // (a property's value would come back as its own square, etc.); owning the buffer and reading
    // fields explicitly via Marshal makes the round-trip deterministic. Layout (x86/x64): vt at
    // offset 0, the value union at offset 8.
    internal sealed class PropVariantBuffer : IDisposable {
        private const ushort VtBStr = 8;
        private const ushort VtBool = 11;
        private const ushort VtUI4 = 19;
        private const ushort VtUI8 = 21;
        private const int PropVariantSize = 16;
        private const int ValueOffset = 8;

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(IntPtr pvar);

        public IntPtr Ptr { get; }

        public PropVariantBuffer() {
            Ptr = Marshal.AllocHGlobal(PropVariantSize);
            Zero();
        }

        private ushort VarType => (ushort)Marshal.ReadInt16(Ptr);

        public string? ReadString() {
            if (VarType != VtBStr) {
                return null;
            }
            IntPtr bstr = Marshal.ReadIntPtr(Ptr, ValueOffset);
            return bstr == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(bstr);
        }

        public ulong ReadUInt64() {
            switch (VarType) {
                case VtUI8: return (ulong)Marshal.ReadInt64(Ptr, ValueOffset);
                case VtUI4: return (uint)Marshal.ReadInt32(Ptr, ValueOffset);
                default: return 0;
            }
        }

        public bool ReadBool() {
            return VarType == VtBool && Marshal.ReadInt16(Ptr, ValueOffset) != 0;
        }

        // PropVariantClear frees whatever the getter allocated (BSTR and any other owned variant
        // type) and zeroes the struct, so the buffer is ready for the next property read. Hand-rolling
        // only a BSTR free would leak less-common allocating variant types.
        public void Reset() {
            PropVariantClear(Ptr);
        }

        private void Zero() {
            Marshal.WriteInt64(Ptr, 0, 0);
            Marshal.WriteInt64(Ptr, ValueOffset, 0);
        }

        public void Dispose() {
            Reset();
            Marshal.FreeHGlobal(Ptr);
        }
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300010000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISequentialInStream {
        // Returns S_OK and a short read (processedSize < size, including 0) at EOF — never an error.
        [PreserveSig] int Read(IntPtr data, uint size, IntPtr processedSize);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300030000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInStream {
        [PreserveSig] int Read(IntPtr data, uint size, IntPtr processedSize);
        [PreserveSig] int Seek(long offset, uint seekOrigin, IntPtr newPosition);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000300020000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISequentialOutStream {
        [PreserveSig] int Write(IntPtr data, uint size, IntPtr processedSize);
    }

    // Passing null for the open callback is allowed; declared so the vtable slot exists.
    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600100000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchiveOpenCallback {
        [PreserveSig] int SetTotal(IntPtr files, IntPtr bytes);
        [PreserveSig] int SetCompleted(IntPtr files, IntPtr bytes);
    }

    // IArchiveExtractCallback inherits IProgress; COM interop can't express that, so the two
    // IProgress methods (SetTotal/SetCompleted) are inlined first, in vtable order.
    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600200000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchiveExtractCallback {
        [PreserveSig] int SetTotal(ulong total);
        [PreserveSig] int SetCompleted(IntPtr completeValue);
        [PreserveSig] int GetStream(uint index, out ISequentialOutStream? outStream, int askExtractMode);
        [PreserveSig] int PrepareOperation(int askExtractMode);
        [PreserveSig] int SetOperationResult(int operationResult);
    }

    [ComImport]
    [Guid("23170F69-40C1-278A-0000-000600600000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInArchive {
        [PreserveSig] int Open(IInStream stream, [In] ref ulong maxCheckStartPosition, IArchiveOpenCallback? openCallback);
        [PreserveSig] int Close();
        [PreserveSig] int GetNumberOfItems(out uint numItems);
        [PreserveSig] int GetProperty(uint index, ItemPropId propId, IntPtr value);
        [PreserveSig] int Extract([MarshalAs(UnmanagedType.LPArray)] uint[]? indices, uint numItems, int testMode, IArchiveExtractCallback extractCallback);
        [PreserveSig] int GetArchiveProperty(ItemPropId propId, IntPtr value);
        [PreserveSig] int GetNumberOfProperties(out uint numProps);
        [PreserveSig] int GetPropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string name, out ItemPropId propId, out ushort varType);
        [PreserveSig] int GetNumberOfArchiveProperties(out uint numProps);
        [PreserveSig] int GetArchivePropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string name, out ItemPropId propId, out ushort varType);
    }
}
