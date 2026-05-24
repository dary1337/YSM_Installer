namespace YSMInstaller {
    /// <summary>Semantic aliases over <see cref="Tokens"/>. Keep names domain-flavored (ButtonHeight,
    /// RadioCardMinHeight) so callsites read intent instead of raw token names.</summary>
    static class Sizes {
        public const int WindowPadding = Tokens.Space4;
        public const int ContentGap = Tokens.Space3;

        public const int RadiusExtraSmall = Tokens.ShapeSm;
        public const int RadiusSmall = Tokens.ShapeMd;
        public const int RadiusMedium = Tokens.ShapeLg;
        public const int RadiusLarge = 20;
        public const int RadiusFull = Tokens.ShapeFull;

        public const int ButtonHeight = Tokens.BtnHeight;
        public const int RadioCardMinHeight = Tokens.ListItemMinHeight;

        public const int DialogMaxWidth = Tokens.ModalMaxWidth;
        public const int DialogPadding = Tokens.Space6;
    }
}
