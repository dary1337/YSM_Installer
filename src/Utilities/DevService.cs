namespace YSMInstaller {
    /// <summary>
    /// Dev-build switches. Tied to the DEBUG build configuration (no more .env file): a Debug build is
    /// the dev build — it mocks WARNO scans and writes the log next to the executable.
    /// </summary>
    internal static class DevService {
        public static bool IsLogNextToExeEnabled =>
#if DEBUG
            true;
#else
            false;
#endif

        public static bool IsMockWarnoPathsEnabled =>
#if DEBUG
            true;
#else
            false;
#endif
    }
}
