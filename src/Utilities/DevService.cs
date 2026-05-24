namespace YSMInstaller {
    /// <summary>
    /// Dev-build switches owned by the Test menu. Debug builds default to "dev mode" (mocks on,
    /// log next to exe); the Test menu can flip mutable ones at runtime. Release builds compile
    /// out the menu, so defaults below are the only thing end-users ever see.
    /// </summary>
    internal static class DevService {
        // One-shot at startup (AppLogger.Initialize captures it); no point in a runtime toggle.
        public static bool IsLogNextToExeEnabled =>
#if DEBUG
            true;
#else
            false;
#endif

        // Default OFF in both configs so DEBUG runs the real scan/install path out of the box
        // (matches what end-users experience). Test menu flips it on when you want UI states
        // without a real game.
        public static bool IsMockWarnoPathsEnabled { get; set; }

        // Optional override for the official mods-list URL. Set via the Test menu to point at
        // a local fork / staging file (e.g. raw.githubusercontent.com on a feature branch)
        // without rebuilding.
        public static string? ModListUrlOverride { get; set; }
    }
}
