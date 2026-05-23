namespace YSMInstaller {
    /// <summary>
    /// Thin semantic aliases over <see cref="Tokens"/> kept for existing call sites. New code should
    /// prefer <see cref="Tokens"/> directly.
    /// </summary>
    static class Sizes {
        // ---- Legacy layout constants (still referenced by older code paths) ----
        public const int FormPadding = 10;
        public const int PanelHeight = 80;
        public const int PanelPadding = 10;
        public const int PanelGap = 10;
        public const int HeaderToEntriesGap = 24;
        public const int SingleEntryInstallGap = 70;
        public const int MultipleEntriesInstallGap = 20;
        public const int ButtonGap = Tokens.Space2;
        public const int MinimumFormWidth = Tokens.WindowMinWidth;
        public const int MinimumFormHeight = Tokens.WindowMinHeight;
        public const int ContentBottomPadding = 120;

        // ---- Material 3 tokens (aliases) ----
        public const int WindowPadding = Tokens.Space4;
        public const int ContentGap = Tokens.Space3;
        public const int SectionGap = Tokens.Space5;

        public const int RadiusExtraSmall = Tokens.ShapeSm;   // 8
        public const int RadiusSmall = Tokens.ShapeMd;        // 12
        public const int RadiusMedium = Tokens.ShapeLg;       // 16
        public const int RadiusLarge = 20;
        public const int RadiusFull = Tokens.ShapeFull;

        public const int ButtonHeight = Tokens.BtnHeight;
        public const int ChipHeight = Tokens.ChipHeight;
        public const int RadioCardMinHeight = Tokens.ListItemMinHeight;
        public const int IslandHeight = 56;
        public const int TitleBarHeight = Tokens.TitleBarHeight;

        public const int DialogMaxWidth = Tokens.ModalMaxWidth;
        public const int DialogPadding = Tokens.Space6;
    }
}
