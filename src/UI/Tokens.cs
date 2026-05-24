using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Single source of truth for layout/sizing/type tokens (adapted from the project's WPF resource
    /// dictionary). Use these everywhere instead of magic numbers. Type sizes are in points
    /// (WPF px * 0.75) so WinForms <see cref="System.Drawing.Font"/> matches the intended pixel size.
    /// </summary>
    public static class Tokens {
        // Spacing
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space3_5 = 14;
        public const int Space4 = 16;
        public const int Space5 = 20;
        public const int Space6 = 24;
        public const int Space8 = 32;

        // Common paddings
        public static readonly Padding WindowBody = new Padding(20, 16, 20, 20);
        public static readonly Padding Card = new Padding(14);
        public static readonly Padding ListItem = new Padding(16, 12, 16, 12);
        public static readonly Padding Modal = new Padding(24);

        // Shape · corner radius
        public const int ShapeXs = 4;
        public const int ShapeSm = 8;
        public const int ShapeMd = 12;
        public const int ShapeLg = 16;
        public const int ShapeXl = 28;
        public const int ShapeFull = 9999;

        // Component sizes
        public const int BtnHeight = 40;
        public const int BtnHeightSm = 32;
        public const int BtnTextHeight = 36;
        public const int ChipHeight = 24;
        public const int ChipAssistHeight = 32;
        public const int ListItemMinHeight = 56;
        public const int DropdownHeight = 48;
        public const int ProgressHeight = 8;
        public const int ModalMaxWidth = 420;
        public const int TitleBarHeight = 40;

        // Window — fixed height (no per-state resizing); content is centered within it.
        public const int WindowWidth = 680;
        public const int WindowHeight = 470;
        public const int WindowMinWidth = 560;
        public const int WindowMinHeight = 430;

        // Icon sizes (pixels)
        public const int IconXs = 16;
        public const int IconSm = 18;
        public const int IconMd = 20;
        public const int IconLg = 24;

        // Type sizes (points = WPF DIP * 0.75)
        public const float TypeBodySmall = 8.25f;
        public const float TypeLabelMedium = 9f;
        public const float TypeBodyMedium = 9.75f;
        public const float TypeBodyLarge = 10.5f;
        public const float TypeTitleMedium = 12f;
        public const float TypeTitleLarge = 13.5f;
        public const float TypeDisplaySmall = 16.5f;
        public const float TypeOverline = 8f;
    }
}
