using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Selectable Material 3 option card for the inline "Choose a build" step: leading icon, title with
    /// an optional "· recommended" accent, a description line, the package size, and a trailing radio.
    /// </summary>
    public sealed class MaterialOptionCard : RoundedPanel {
        public event Action<MaterialOptionCard>? SelectedChanged;

        private static readonly Image LogoImage = Properties.Resources.logo.ToBitmap();

        private readonly string _title;
        private readonly string _description;
        private readonly bool _recommended;
        private readonly Image? _customIcon;
        private string _sizeText = string.Empty;
        private bool _selected;
        private bool _hovered;
        private float _selectionProgress;
        private readonly Timer _selectionTween;

        private const int IconBox = 40;
        private const int RadioArea = 44;
        private const int Pad = 14;

        public object? Tag2 { get; set; }

        public MaterialOptionCard(string title, string description, bool recommended, Image? customIcon = null)
            : base(Sizes.RadiusMedium) {
            _title = title;
            _description = description;
            _recommended = recommended;
            _customIcon = customIcon;

            BackColor = MaterialPalette.SurfaceContainer;
            MinimumSize = new Size(0, 68);
            Cursor = SystemCursors.Pointer;
            SetOutline(MaterialPalette.OutlineVariant);

            _selectionTween = new Timer { Interval = 16 };
            _selectionTween.Tick += OnSelectionTweenTick;

            Click += (s, e) => SelectCard();
        }

        private void OnSelectionTweenTick(object? sender, EventArgs e) {
            float target = _selected ? 1f : 0f;
            float delta = target - _selectionProgress;
            if (Math.Abs(delta) < 0.01f) {
                _selectionProgress = target;
                _selectionTween.Stop();
            }
            else {
                _selectionProgress += delta * 0.22f;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _selectionTween.Stop();
                _selectionTween.Dispose();
            }
            base.Dispose(disposing);
        }

        public bool IsSelected => _selected;

        public string SizeText {
            get => _sizeText;
            set { _sizeText = value ?? string.Empty; Invalidate(); }
        }

        public void SelectCard() {
            if (_selected) {
                return;
            }
            _selected = true;
            _selectionTween.Start();
            UpdateSurface();
            SelectedChanged?.Invoke(this);
        }

        public void SetSelected(bool selected) {
            if (_selected == selected) {
                return;
            }
            _selected = selected;
            _selectionTween.Start();
            UpdateSurface();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; UpdateSurface(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; UpdateSurface(); }

        private void UpdateSurface() {
            BackColor = _selected
                ? MaterialPalette.SurfaceContainerHigh
                : _hovered
                    ? MaterialPalette.Overlay(MaterialPalette.SurfaceContainer, MaterialPalette.OnSurface, 0.05)
                    : MaterialPalette.SurfaceContainer;
            SetOutline(_selected ? MaterialPalette.Primary : MaterialPalette.OutlineVariant);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var iconRect = new Rectangle(Pad, (Height - IconBox) / 2, IconBox, IconBox);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(iconRect, Sizes.RadiusExtraSmall)) {
                if (_customIcon != null) {
                    // Full-bleed colorful art clipped to the rounded square (no tile background).
                    using (Region previousClip = g.Clip) {
                        g.SetClip(path);
                        g.DrawImage(_customIcon, iconRect);
                        g.Clip = previousClip;
                    }
                }
                else {
                    using (var brush = new SolidBrush(_selected ? MaterialPalette.PrimaryContainer : MaterialPalette.SurfaceContainerHighest)) {
                        g.FillPath(brush, path);
                    }
                    var logoRect = new Rectangle(iconRect.X + 6, iconRect.Y + 6, iconRect.Width - 12, iconRect.Height - 12);
                    g.DrawImage(LogoImage, logoRect);
                }
            }

            int textLeft = Pad + IconBox + 14;
            int textRight = Width - RadioArea - 8;
            using (var nearFmt = new StringFormat(StringFormat.GenericTypographic) {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
            }) {
                int sizeWidth = 0;
                if (!string.IsNullOrEmpty(_sizeText)) {
                    SizeF measured = g.MeasureString(_sizeText, MaterialType.BodySmall);
                    sizeWidth = (int)Math.Ceiling(measured.Width) + 10;
                    using (var brush = new SolidBrush(MaterialPalette.OnSurfaceVariant))
                    using (var rightFmt = new StringFormat(StringFormat.GenericTypographic) {
                        Alignment = StringAlignment.Far,
                        LineAlignment = StringAlignment.Center,
                    }) {
                        g.DrawString(_sizeText, MaterialType.BodySmall, brush,
                            new RectangleF(textRight - sizeWidth, 0, sizeWidth, Height), rightFmt);
                    }
                }

                float titleY = 13;
                SizeF titleSize = g.MeasureString(_title, MaterialType.TitleMedium, int.MaxValue, StringFormat.GenericTypographic);
                using (var brush = new SolidBrush(MaterialPalette.OnSurface)) {
                    g.DrawString(_title, MaterialType.TitleMedium, brush,
                        new RectangleF(textLeft, titleY, textRight - sizeWidth - textLeft, 22), nearFmt);
                }
                if (_recommended) {
                    using (var brush = new SolidBrush(MaterialPalette.Primary)) {
                        g.DrawString(" · recommended", MaterialType.LabelMedium, brush,
                            new RectangleF(textLeft + titleSize.Width, titleY + 1, 150, 20), nearFmt);
                    }
                }

                using (var descFmt = new StringFormat(StringFormat.GenericTypographic) {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                })
                using (var brush = new SolidBrush(MaterialPalette.OnSurfaceVariant)) {
                    g.DrawString(_description, MaterialType.BodySmall, brush,
                        new RectangleF(textLeft, 35, textRight - textLeft, 20), descFmt);
                }
            }

            DrawRadio(g);
        }

        private void DrawRadio(Graphics g) {
            int diameter = 20;
            int cx = Width - RadioArea / 2 - 2;
            int cy = Height / 2;
            var outer = new Rectangle(cx - diameter / 2, cy - diameter / 2, diameter, diameter);
            Color ringColor = MaterialPalette.Overlay(MaterialPalette.Outline, MaterialPalette.Primary, _selectionProgress);
            using (var pen = new Pen(ringColor, 2f)) {
                g.DrawEllipse(pen, outer);
            }
            if (_selectionProgress > 0.01f) {
                float innerDiameter = 10f * _selectionProgress;
                var inner = new RectangleF(cx - innerDiameter / 2f, cy - innerDiameter / 2f, innerDiameter, innerDiameter);
                using (var brush = new SolidBrush(MaterialPalette.Primary)) {
                    g.FillEllipse(brush, inner);
                }
            }
        }
    }
}
