using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace YSMInstaller.SevenZip {
    // Loads the bundled native 7z.dll on first use. The DLL ships gzip-compressed as an embedded
    // resource (~0.8 MB vs 1.8 MB raw — it's 80% of the bundle otherwise) and is inflated with the
    // BCL's GZipStream into a per-version %TEMP% cache, then loaded via LoadLibraryEx.
    internal static class SevenZipLibrary {
        // Integrity guard for the cached copy — must match the vendored src/Resources/native/7z.dll.gz
        // (7-Zip 26.01 x64). Update both together if the bundled DLL is ever replaced.
        private const long ExpectedRawSize = 1908736;
        private const string ExpectedSha256 =
            "9657871fdd714c96a3a21f59abf04abb14944516c2a10c8cb606c12a71a75957";
        // Length of the hash prefix used as the cache subfolder name — enough to be collision-free
        // across DLL versions without an unwieldy path.
        private const int CacheKeyLength = 16;
        // LoadLibraryEx flag: resolve the module's own dependencies from its directory, not the
        // process search path — avoids DLL-planting from a writable working directory.
        private const uint LoadWithAlteredSearchPath = 0x00000008;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateObjectDelegate(ref Guid classId, ref Guid interfaceId, out IntPtr outObject);

        private static readonly object InitLock = new object();
        private static CreateObjectDelegate? _createObject;
        private static Guid _iidInArchive = new Guid("23170F69-40C1-278A-0000-000600600000");

        public static IInArchive CreateInArchive(Guid formatClassId) {
            EnsureLoaded();
            Guid classId = formatClassId;
            int hr = _createObject!(ref classId, ref _iidInArchive, out IntPtr ptr);
            if (hr != HResult.Ok || ptr == IntPtr.Zero) {
                throw new IOException($"7z.dll CreateObject failed (HRESULT 0x{hr:X8}).");
            }
            try {
                return (IInArchive)Marshal.GetObjectForIUnknown(ptr);
            }
            finally {
                Marshal.Release(ptr);
            }
        }

        private static void EnsureLoaded() {
            if (_createObject != null) {
                return;
            }
            lock (InitLock) {
                if (_createObject != null) {
                    return;
                }
                try {
                    if (!Environment.Is64BitProcess) {
                        // We ship only the x64 7z.dll. WARNO is 64-bit-only, so its players run a
                        // 64-bit OS where this AnyCPU process is x64 — a 32-bit process here is a
                        // misconfiguration, not a supported path.
                        throw new PlatformNotSupportedException(
                            "Bundled 7z.dll is 64-bit; the installer must run as a 64-bit process."
                        );
                    }
                    string dllPath = EnsureExtracted();
                    IntPtr module = NativeMethods.LoadLibraryEx(dllPath, IntPtr.Zero, LoadWithAlteredSearchPath);
                    if (module == IntPtr.Zero) {
                        throw new IOException(
                            $"LoadLibrary failed for {dllPath} (Win32 {Marshal.GetLastWin32Error()})."
                        );
                    }
                    IntPtr proc = NativeMethods.GetProcAddress(module, "CreateObject");
                    if (proc == IntPtr.Zero) {
                        throw new IOException("7z.dll is missing the CreateObject export.");
                    }
                    _createObject = Marshal.GetDelegateForFunctionPointer<CreateObjectDelegate>(proc);
                }
                catch (Exception exception) {
                    // Surfaced at the top of every extraction path; log the root cause here since the
                    // rethrow loses the native-load detail by the time it reaches the install UI.
                    AppLogger.Critical("Failed to load bundled 7z.dll.", exception);
                    throw;
                }
            }
        }

        private static string EnsureExtracted() {
            string cacheDir = Path.Combine(
                Path.GetTempPath(),
                "YSMInstaller",
                "7z",
                ExpectedSha256.Substring(0, CacheKeyLength)
            );
            string dllPath = Path.Combine(cacheDir, "7z.dll");

            if (IsValidCachedCopy(dllPath)) {
                return dllPath;
            }

            Directory.CreateDirectory(cacheDir);
            // Inflate to a unique temp file then move into place so a second instance racing the same
            // cache never sees a half-written DLL.
            string stagingPath = Path.Combine(cacheDir, $"7z.{Guid.NewGuid():N}.tmp");
            try {
                using (Stream compressed = OpenResource())
                using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                using (var staging = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write)) {
                    gzip.CopyTo(staging);
                }
                if (!IsValidCopy(stagingPath)) {
                    throw new IOException("Inflated 7z.dll failed its integrity check.");
                }
                PromoteStaging(stagingPath, dllPath);
            }
            catch {
                TryDelete(stagingPath);
                throw;
            }
            return dllPath;
        }

        // File.Move won't overwrite on .NET Framework, so clear any stale/corrupt copy first. If a
        // racing instance produced a valid copy in the meantime, keep theirs and drop ours.
        private static void PromoteStaging(string stagingPath, string dllPath) {
            if (IsValidCachedCopy(dllPath)) {
                File.Delete(stagingPath);
                return;
            }
            TryDelete(dllPath);
            try {
                File.Move(stagingPath, dllPath);
            }
            catch (IOException) when (IsValidCachedCopy(dllPath)) {
                TryDelete(stagingPath);
            }
        }

        private static bool IsValidCachedCopy(string dllPath) {
            try {
                return File.Exists(dllPath) && IsValidCopy(dllPath);
            }
            catch (IOException) {
                return false;
            }
        }

        // Size check first as a cheap reject, then the full SHA-256 so a same-length corrupt copy
        // can't be loaded.
        private static bool IsValidCopy(string path) {
            if (new FileInfo(path).Length != ExpectedRawSize) {
                return false;
            }
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) {
                string actual = BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty);
                return string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static Stream OpenResource() {
            Assembly assembly = typeof(SevenZipLibrary).Assembly;
            string? name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("7z.dll.gz", StringComparison.OrdinalIgnoreCase));
            if (name == null) {
                throw new IOException("Embedded 7z.dll.gz resource is missing from the assembly.");
            }
            return assembly.GetManifestResourceStream(name)
                ?? throw new IOException($"Failed to open embedded resource '{name}'.");
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (IOException) {
                // Best-effort cleanup of a temp staging file; a leftover .tmp is harmless.
            }
        }

        private static class NativeMethods {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
            public static extern IntPtr GetProcAddress(IntPtr module, string procName);
        }
    }
}
