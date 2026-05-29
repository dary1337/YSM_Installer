using System.Drawing;

namespace YSMInstaller {
    /// <summary>
    /// Material You (Material 3) dark color roles — "Platinum" monochrome theme. Primary and
    /// the accent families are neutral greys so the only chromatic events on screen are the
    /// red Y logo and the shared semantic colors (Success/Error/Warning) plus the Steam blue.
    /// Surfaces are unchanged from the base dark scale. Roles not in the source palette
    /// (OnSecondary, OnTertiaryContainer, OnWarning*, scrim) are derived to complete the set.
    /// </summary>
    public static class MaterialPalette {
        private static Color Hex(string hex) {
            return ColorTranslator.FromHtml(hex);
        }

        // Primary
        public static readonly Color Primary = Hex("#D5D2DA");
        public static readonly Color OnPrimary = Hex("#1B1B22");
        public static readonly Color PrimaryContainer = Hex("#45454F");
        public static readonly Color OnPrimaryContainer = Hex("#F2F0F4");

        // Secondary
        public static readonly Color SecondaryContainer = Hex("#3A3A42");
        public static readonly Color OnSecondaryContainer = Hex("#E1DFE6");

        // Tertiary
        public static readonly Color Tertiary = Hex("#C7C5D0");
        public static readonly Color TertiaryContainer = Hex("#3E3D46");
        public static readonly Color OnTertiaryContainer = Hex("#E1DFE6"); // derived light tone

        // Error
        public static readonly Color Error = Hex("#FFB4AB");
        public static readonly Color OnError = Hex("#690005");
        public static readonly Color ErrorContainer = Hex("#93000A");
        public static readonly Color OnErrorContainer = Hex("#FFDAD6");

        // Success (custom)
        public static readonly Color Success = Hex("#7FD68A");
        public static readonly Color OnSuccess = Hex("#00390C");
        public static readonly Color SuccessContainer = Hex("#0A5217");
        public static readonly Color OnSuccessContainer = Hex("#9CF3A6");

        // Warning (custom)
        public static readonly Color Warning = Hex("#F2C66B");
        public static readonly Color WarningContainer = Hex("#5A4216");
        public static readonly Color OnWarningContainer = Hex("#FFDEA6"); // derived light tone

        // Surfaces
        public static readonly Color SurfaceContainerLowest = Hex("#0D0E13");
        public static readonly Color Surface = Hex("#131318");
        public static readonly Color SurfaceContainerLow = Hex("#1B1B22");
        public static readonly Color SurfaceContainer = Hex("#1F1F27");
        public static readonly Color SurfaceContainerHigh = Hex("#292932");
        public static readonly Color SurfaceContainerHighest = Hex("#34343D");

        // On-surface
        public static readonly Color OnSurface = Hex("#E5E1E9");
        public static readonly Color OnSurfaceVariant = Hex("#C7C5D0");
        public static readonly Color OnSurfaceMuted = Hex("#8E8C97");
        public static readonly Color Outline = Hex("#918F9A");
        public static readonly Color OutlineVariant = Hex("#47464F");

        // Platform / brand
        public static readonly Color SteamBrand = Hex("#66C0F4");
        public static readonly Color BrandCyan = Hex("#00B8D4");
        public static readonly Color BrandMagenta = Hex("#E91E63");
        public static readonly Color BrandYellow = Hex("#FFEB3B");

        // Derived utility tones
        public static readonly Color Scrim = Color.FromArgb(168, 0, 0, 0);
        public static readonly Color OnSecondary = Hex("#1B1B22"); // derived dark for filled secondary

        /// <summary>Composites a translucent state-layer color over a solid base (M3 hover/press overlays).</summary>
        public static Color Overlay(Color baseColor, Color layer, double opacity) {
            double a = opacity < 0 ? 0 : opacity > 1 ? 1 : opacity;
            int r = (int)(baseColor.R + (layer.R - baseColor.R) * a);
            int g = (int)(baseColor.G + (layer.G - baseColor.G) * a);
            int b = (int)(baseColor.B + (layer.B - baseColor.B) * a);
            return Color.FromArgb(255, r, g, b);
        }
    }
}
