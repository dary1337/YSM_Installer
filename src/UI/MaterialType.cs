using System.Drawing;

namespace YSMInstaller {
    /// <summary>
    /// Material 3 type scale (Segoe UI), sized from <see cref="Tokens"/>. Rendered with grid-fitted
    /// ClearType everywhere (crisp at small sizes) via <see cref="SoftLabel"/> and the Material controls.
    /// </summary>
    public static class MaterialType {
        private const string Family = "Segoe UI";

        public static readonly Font HeadlineSmall = new Font(Family, Tokens.TypeDisplaySmall, FontStyle.Bold);
        public static readonly Font TitleLarge = new Font(Family, Tokens.TypeTitleLarge, FontStyle.Bold);
        public static readonly Font TitleMedium = new Font(Family, Tokens.TypeTitleMedium, FontStyle.Bold);
        public static readonly Font BodyLarge = new Font(Family, Tokens.TypeBodyLarge, FontStyle.Regular);
        public static readonly Font BodyMedium = new Font(Family, Tokens.TypeBodyMedium, FontStyle.Regular);
        public static readonly Font BodySmall = new Font(Family, Tokens.TypeBodySmall, FontStyle.Regular);
        public static readonly Font LabelLarge = new Font(Family, Tokens.TypeBodyMedium, FontStyle.Bold);
        public static readonly Font LabelMedium = new Font(Family, Tokens.TypeLabelMedium, FontStyle.Bold);

        /// <summary>Overline / eyebrow label (the all-caps "SEARCHING FOR WARNO.EXE…" headers).</summary>
        public static readonly Font Overline = new Font(Family, Tokens.TypeOverline, FontStyle.Bold);
    }
}
