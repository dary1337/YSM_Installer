using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Selectable Material 3 list item for one detected Warno.exe. Fully owner-drawn (no nested
    /// auto-size child controls) — icon, path, source/status chips and links are painted directly and
    /// links are hit-tested, which keeps the card a stable, full-width rectangle.
    /// </summary>
    public sealed class MaterialRadioCard : RoundedPanel {
        public event Action<MaterialRadioCard>? SelectedChanged;

        private readonly WarnoEntry _entry;
        private readonly Image? _exeIcon;
        private bool _selected;
        private bool _hovered;

        private Rectangle _issuesLinkRect;
        private HoveredLink _hoveredLink = HoveredLink.None;

        private enum HoveredLink { None, Issues }

        private const int IconBox = 36;
        private const int RadioArea = 44;
        private const int Pad = 14;
        private const int StatusRowTop = 38;
        private const int ChipHeight = 22;

        public WarnoEntry Entry => _entry;
        public bool IsSelected => _selected;

        public MaterialRadioCard(WarnoEntry entry)
            : base(Sizes.RadiusMedium) {
            _entry = entry;
            _exeIcon = TryLoadExeIcon(entry.ExePath);

            BackColor = MaterialPalette.SurfaceContainer;
            MinimumSize = new Size(0, Sizes.RadioCardMinHeight);
            Cursor = SystemCursors.Pointer;
            SetOutline(StatusOutline());
        }

        private bool HasKnownIssues =>
            _entry.VersionMetadata != null
            && !string.IsNullOrWhiteSpace(_entry.VersionMetadata.KnownIssuesUrl);

        private bool IsSteamEntry =>
            string.Equals(_entry.SourceLabel, WarnoExecutableSources.Steam, StringComparison.Ordinal);

        private bool NotSupported => _entry.VersionMetadata == null && _entry.LatestCompatibleModVersion <= 0;
        private bool UsesLatestMod => _entry.VersionMetadata == null && _entry.LatestCompatibleModVersion > 0;

        public void SelectCard() {
            if (_selected) {
                return;
            }
            _selected = true;
            UpdateSurface();
            SelectedChanged?.Invoke(this);
        }

        public void SetSelected(bool selected) {
            _selected = selected;
            UpdateSurface();
        }

        private void UpdateSurface() {
            BackColor = _selected
                ? MaterialPalette.SurfaceContainerHigh
                : _hovered
                    ? MaterialPalette.Overlay(MaterialPalette.SurfaceContainer, MaterialPalette.OnSurface, 0.05)
                    : MaterialPalette.SurfaceContainer;
            SetOutline(_selected ? MaterialPalette.Primary : StatusOutline());
            Invalidate();
        }

        private Color StatusOutline() {
            if (NotSupported) return MaterialPalette.Error;
            if (UsesLatestMod || HasKnownIssues) return MaterialPalette.Warning;
            return MaterialPalette.OutlineVariant;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; UpdateSurface(); }
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            _hovered = false;
            if (_hoveredLink != HoveredLink.None) {
                _hoveredLink = HoveredLink.None;
            }
            UpdateSurface();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            HoveredLink next = HasKnownIssues && _issuesLinkRect.Contains(e.Location)
                ? HoveredLink.Issues
                : HoveredLink.None;
            if (next != _hoveredLink) {
                _hoveredLink = next;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e);
            if (HasKnownIssues && _issuesLinkRect.Contains(e.Location)) {
                OpenKnownIssues();
                return;
            }
            SelectCard();
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawIcon(g);
            DrawPath(g);
            DrawStatusRow(g);
            DrawRadio(g);
        }

        private void DrawIcon(Graphics g) {
            var iconRect = new Rectangle(Pad, Pad, IconBox, IconBox);
            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(iconRect, Sizes.RadiusExtraSmall))
            using (var brush = new SolidBrush(MaterialPalette.SurfaceContainerHighest)) {
                g.FillPath(brush, path);
            }

            if (_exeIcon != null) {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_exeIcon, new Rectangle(iconRect.X + 5, iconRect.Y + 5, iconRect.Width - 10, iconRect.Height - 10));
            }
            else {
                const int s = 22;
                Bitmap img = MaterialIconRenderer.Get(MaterialIcons.Game, s, MaterialPalette.OnSurfaceVariant);
                g.DrawImageUnscaled(img, iconRect.X + (iconRect.Width - s) / 2, iconRect.Y + (iconRect.Height - s) / 2);
            }
        }

        private int TextLeft => Pad + IconBox + 12;

        private void DrawPath(Graphics g) {
            var pathRect = new RectangleF(TextLeft, Pad - 2, Width - TextLeft - RadioArea, 20);
            using (var brush = new SolidBrush(MaterialPalette.OnSurface))
            using (var fmt = new StringFormat(StringFormatFlags.NoWrap) {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisPath,
            }) {
                g.DrawString(_entry.ExePath, MaterialType.BodyLarge, brush, pathRect, fmt);
            }
        }

        private void DrawStatusRow(Graphics g) {
            float x = TextLeft;
            int midY = StatusRowTop + ChipHeight / 2;

            x = DrawText(g, $"v{_entry.Version}", MaterialType.BodySmall, MaterialPalette.OnSurfaceVariant, x, midY) + 8;

            if (NotSupported) {
                x = DrawChip(g, "not supported", string.Empty, MaterialPalette.ErrorContainer, MaterialPalette.OnErrorContainer, Color.Transparent, x) + 8;
            }
            else if (UsesLatestMod) {
                x = DrawChip(g, $"Game ahead — mod v{_entry.LatestCompatibleModVersion}", MaterialIcons.Warning,
                    MaterialPalette.WarningContainer, MaterialPalette.OnWarningContainer, Color.Transparent, x) + 8;
                _issuesLinkRect = Rectangle.Empty;
            }
            else if (HasKnownIssues) {
                _issuesLinkRect = DrawLink(g, "Has known issues — see why", MaterialIcons.Help,
                    iconOnLeft: true,
                    MaterialPalette.Warning, MaterialPalette.WarningContainer,
                    x, midY, _hoveredLink == HoveredLink.Issues);
                x = _issuesLinkRect.Right + 8;
            }
            else {
                _issuesLinkRect = Rectangle.Empty;
            }

            string sourceGlyph = IsSteamEntry ? MaterialIcons.Steam : MaterialIcons.Folder;
            Color sourceFill = IsSteamEntry ? Color.Transparent : MaterialPalette.SecondaryContainer;
            Color sourceContent = IsSteamEntry ? MaterialPalette.SteamBrand : MaterialPalette.OnSecondaryContainer;
            Color sourceOutline = IsSteamEntry ? MaterialPalette.SteamBrand : Color.Transparent;
            DrawChip(g, _entry.SourceLabel, sourceGlyph, sourceFill, sourceContent, sourceOutline, x);
        }

        private float DrawText(Graphics g, string text, Font font, Color color, float x, int midY) {
            SizeF size = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
            using (var brush = new SolidBrush(color)) {
                g.DrawString(text, font, brush, x, midY - size.Height / 2f, StringFormat.GenericTypographic);
            }
            return x + size.Width;
        }

        private float DrawChip(Graphics g, string text, string glyph, Color fill, Color content, Color outline, float x) {
            const int padX = 10;
            const int iconPx = 14;
            const int iconGap = 5;
            Font font = MaterialType.LabelMedium;
            SizeF textSize = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
            bool hasIcon = !string.IsNullOrEmpty(glyph);
            int chipWidth = (int)Math.Ceiling(textSize.Width) + padX * 2 + (hasIcon ? iconPx + iconGap : 0);
            var rect = new Rectangle((int)x, StatusRowTop, chipWidth, ChipHeight);

            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(rect, ChipHeight / 2)) {
                if (fill.A > 0) {
                    using (var brush = new SolidBrush(fill)) {
                        g.FillPath(brush, path);
                    }
                }
                if (outline.A > 0) {
                    using (var pen = new Pen(outline, 1f)) {
                        g.DrawPath(pen, path);
                    }
                }
            }

            float cx = rect.X + padX;
            int midY = rect.Y + rect.Height / 2;
            if (hasIcon) {
                Bitmap icon = MaterialIconRenderer.Get(glyph, iconPx, content);
                g.DrawImageUnscaled(icon, (int)cx, midY - iconPx / 2);
                cx += iconPx + iconGap;
            }
            using (var brush = new SolidBrush(content)) {
                g.DrawString(text, font, brush, cx, midY - textSize.Height / 2f, StringFormat.GenericTypographic);
            }
            return rect.Right;
        }

        // Two modes:
        //   - fillColor.A == 0 → text-button style (transparent rest, hover state layer of textColor).
        //   - fillColor.A  > 0 → tonal-chip style (always filled with fillColor, hover slightly brighter).
        // Icon can be placed before or after the label. Box height matches ChipHeight for row alignment.
        private Rectangle DrawLink(Graphics g, string text, string? iconKey, bool iconOnLeft,
                                   Color textColor, Color fillColor, float x, int midY, bool hovered) {
            const int iconPx = 12;
            const int iconGap = 5;
            const int padX = 10;
            SizeF size = g.MeasureString(text, MaterialType.LabelMedium, int.MaxValue, StringFormat.GenericTypographic);
            int textW = (int)Math.Ceiling(size.Width);
            int contentW = textW + (iconKey != null ? iconGap + iconPx : 0);
            int boxW = contentW + padX * 2;
            int boxH = ChipHeight;

            var box = new Rectangle((int)x, midY - boxH / 2, boxW, boxH);

            Color effectiveFill;
            if (fillColor.A > 0) {
                effectiveFill = hovered ? MaterialPalette.Overlay(fillColor, textColor, 0.10) : fillColor;
            }
            else {
                effectiveFill = hovered ? MaterialPalette.Overlay(BackColor, textColor, 0.12) : Color.Transparent;
            }
            if (effectiveFill.A > 0) {
                using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(box, boxH / 2))
                using (var brush = new SolidBrush(effectiveFill)) {
                    g.FillPath(brush, path);
                }
            }

            float contentX = box.X + padX;
            if (iconKey != null && iconOnLeft) {
                Bitmap icon = MaterialIconRenderer.Get(iconKey, iconPx, textColor);
                g.DrawImageUnscaled(icon, (int)contentX, midY - iconPx / 2);
                contentX += iconPx + iconGap;
            }
            using (var brush = new SolidBrush(textColor)) {
                g.DrawString(text, MaterialType.LabelMedium, brush, contentX, midY - size.Height / 2f, StringFormat.GenericTypographic);
            }
            if (iconKey != null && !iconOnLeft) {
                Bitmap icon = MaterialIconRenderer.Get(iconKey, iconPx, textColor);
                g.DrawImageUnscaled(icon, (int)contentX + textW + iconGap, midY - iconPx / 2);
            }

            return box;
        }

        private void DrawRadio(Graphics g) {
            int diameter = 20;
            int cx = Width - RadioArea / 2 - 2;
            int cy = Height / 2;
            var outer = new Rectangle(cx - diameter / 2, cy - diameter / 2, diameter, diameter);
            using (var pen = new Pen(_selected ? MaterialPalette.Primary : MaterialPalette.Outline, 2f)) {
                g.DrawEllipse(pen, outer);
            }
            if (_selected) {
                using (var brush = new SolidBrush(MaterialPalette.Primary)) {
                    g.FillEllipse(brush, new Rectangle(cx - 5, cy - 5, 10, 10));
                }
            }
        }

        private void OpenKnownIssues() {
            string? url = _entry.VersionMetadata?.KnownIssuesUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)) {
                return;
            }
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) {
                return;
            }
            try {
                Process.Start(new ProcessStartInfo { FileName = parsed.AbsoluteUri, UseShellExecute = true });
            }
            catch (Exception exception) {
                AppLogger.Critical("Failed to open known issues URL.", exception);
            }
        }

        private static Image? TryLoadExeIcon(string exePath) {
            try {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) {
                    return null;
                }
                using (Icon? raw = Icon.ExtractAssociatedIcon(exePath)) {
                    if (raw == null) {
                        return null;
                    }
                    using (var sized = new Icon(raw, 32, 32)) {
                        return sized.ToBitmap();
                    }
                }
            }
            catch (Exception exception) {
                AppLogger.Critical($"Failed to extract icon from: {exePath}", exception);
                return null;
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _exeIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
