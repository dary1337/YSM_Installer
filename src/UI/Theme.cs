using System.Drawing;

namespace YSMInstaller {
    /// <summary>
    /// Semantic theme surface mapped onto <see cref="MaterialPalette"/>. Legacy field names are kept as
    /// aliases so pre-redesign controls keep compiling while the UI migrates to Material 3 roles.
    /// </summary>
    public static class Theme {
        // ---- Material 3 semantic roles (preferred) ----
        public static readonly Color Surface = MaterialPalette.Surface;
        public static readonly Color SurfaceLowest = MaterialPalette.SurfaceContainerLowest;
        public static readonly Color SurfaceLow = MaterialPalette.SurfaceContainerLow;
        public static readonly Color SurfaceCard = MaterialPalette.SurfaceContainer;
        public static readonly Color SurfaceHigh = MaterialPalette.SurfaceContainerHigh;
        public static readonly Color SurfaceHighest = MaterialPalette.SurfaceContainerHighest;

        public static readonly Color OnSurface = MaterialPalette.OnSurface;
        public static readonly Color OnSurfaceVariant = MaterialPalette.OnSurfaceVariant;
        public static readonly Color OnSurfaceMuted = MaterialPalette.OnSurfaceMuted;
        public static readonly Color Outline = MaterialPalette.Outline;
        public static readonly Color OutlineVariant = MaterialPalette.OutlineVariant;

        public static readonly Color Primary = MaterialPalette.Primary;
        public static readonly Color OnPrimary = MaterialPalette.OnPrimary;

        // ---- Legacy aliases (do not use in new code) ----
        public static readonly Color Background = MaterialPalette.Surface;
        public static readonly Color PanelBackground = MaterialPalette.SurfaceContainer;
        public static readonly Color PanelBackgroundHover = MaterialPalette.SurfaceContainerHigh;
        public static readonly Color ButtonBackground = MaterialPalette.SecondaryContainer;
        public static readonly Color ButtonBackgroundHover = MaterialPalette.SurfaceContainerHighest;
        public static readonly Color ButtonForeground = MaterialPalette.OnSurface;
        public static readonly Color EntryPanelBorder = MaterialPalette.OutlineVariant;
        public static readonly Color RecommendedBackground = MaterialPalette.Primary;
        public static readonly Color TextPrimary = MaterialPalette.OnSurface;
        public static readonly Color TextMuted = MaterialPalette.OnSurfaceVariant;
        public static readonly Color DropdownListBackground = MaterialPalette.SurfaceContainerHigh;
        public static readonly Color DropdownRowHover = MaterialPalette.SurfaceContainerHighest;
    }
}
