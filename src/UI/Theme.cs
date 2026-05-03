using System.Drawing;

namespace YSMInstaller {
    public static class Theme {
        public static readonly Color Background = Color.FromArgb(19, 22, 30);

        public static readonly Color PanelBackground = Color.FromArgb(14, 14, 22);
        public static readonly Color PanelBackgroundHover = Color.FromArgb(30, 30, 40);

        public static readonly Color ButtonBackground = Color.FromArgb(38, 44, 60);

        public static readonly Color ButtonBackgroundHover = Color.FromArgb(52, 58, 82);

        public static readonly Color ButtonForeground = Color.White;

        public static readonly Color EntryPanelBorder = Color.FromArgb(48, 54, 72);

        public static readonly Color RecommendedBackground = Color.FromArgb(124, 141, 180);

        public static readonly Color TextPrimary = Color.White;

        public static readonly Color TextMuted = Color.FromArgb(180, 184, 198);

        public static readonly Color DropdownListBackground = Color.FromArgb(24, 27, 38);

        public static readonly Color DropdownRowHover = PanelBackgroundHover;
    }
}
