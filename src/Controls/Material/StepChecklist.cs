using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Vertical checklist of install stages. Steps before <see cref="ActiveIndex"/> render as completed
    /// (green check), the active step shows a spinning arc, later steps render as muted pending circles.
    /// </summary>
    public sealed class StepChecklist : Control {
        private string[] _steps = Array.Empty<string>();
        private int _activeIndex;
        private int _errorIndex = -1;
        private float _spinnerAngle;
        private readonly Timer _spinner;

        private const int RowHeight = 26;
        private const int Indicator = 18;

        public StepChecklist() {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true
            );
            BackColor = Color.Transparent;
            _spinner = new Timer { Interval = 40 };
            _spinner.Tick += (s, e) => { _spinnerAngle = (_spinnerAngle + 18) % 360; Invalidate(); };
        }

        public void SetSteps(string[] steps) {
            _steps = steps ?? Array.Empty<string>();
            _errorIndex = -1;
            _activeIndex = 0;
            _spinner.Stop();
            Height = _steps.Length * RowHeight;
            Invalidate();
        }

        public void SetError(int index) {
            _errorIndex = index;
            _spinner.Stop();
            Invalidate();
        }

        public int ActiveIndex {
            get => _activeIndex;
            set {
                _activeIndex = value;
                bool active = value >= 0 && value < _steps.Length;
                if (active && !_spinner.Enabled) _spinner.Start();
                if (!active && _spinner.Enabled) _spinner.Stop();
                Invalidate();
            }
        }

        public void Complete() {
            _activeIndex = _steps.Length;
            _spinner.Stop();
            Invalidate();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _spinner.Stop();
                _spinner.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(BackColor)) {
                g.FillRectangle(bg, ClientRectangle);
            }

            using (var fmt = new StringFormat(StringFormat.GenericTypographic) {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
            }) {
                for (int i = 0; i < _steps.Length; i++) {
                    int y = i * RowHeight;
                    var indicatorRect = new Rectangle(0, y + (RowHeight - Indicator) / 2, Indicator, Indicator);

                    if (i == _errorIndex) {
                        DrawError(g, indicatorRect);
                    }
                    else if (i < _activeIndex) {
                        DrawDone(g, indicatorRect);
                    }
                    else if (i == _activeIndex && _errorIndex < 0) {
                        DrawActive(g, indicatorRect);
                    }
                    else {
                        DrawPending(g, indicatorRect);
                    }

                    Color textColor = i == _errorIndex
                        ? MaterialPalette.Error
                        : i <= _activeIndex ? MaterialPalette.OnSurface : MaterialPalette.OnSurfaceMuted;
                    var textRect = new RectangleF(Indicator + 12, y, Width - Indicator - 12, RowHeight);
                    using (var brush = new SolidBrush(textColor)) {
                        g.DrawString(_steps[i], MaterialType.BodyMedium, brush, textRect, fmt);
                    }
                }
            }
        }

        private void DrawDone(Graphics g, Rectangle rect) {
            using (var brush = new SolidBrush(MaterialPalette.Success)) {
                g.FillEllipse(brush, rect);
            }
            DrawCheckGlyph(g, rect, MaterialPalette.OnSuccess);
        }

        private void DrawError(Graphics g, Rectangle rect) {
            using (var brush = new SolidBrush(MaterialPalette.Error)) {
                g.FillEllipse(brush, rect);
            }
            DrawCrossGlyph(g, rect, MaterialPalette.OnError);
        }

        // Hand-drawn glyphs guarantee true visual centering inside the indicator circle (SVG Material
        // Symbols have asymmetric bboxes that make a mathematically centered bitmap look off).
        private static void DrawCheckGlyph(Graphics g, Rectangle rect, Color color) {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float s = rect.Width * 0.30f;
            using (var pen = new Pen(color, Math.Max(1.6f, rect.Width / 10f))) {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[] {
                    new PointF(cx - s * 0.95f, cy + s * 0.05f),
                    new PointF(cx - s * 0.20f, cy + s * 0.60f),
                    new PointF(cx + s * 0.95f, cy - s * 0.55f),
                });
            }
        }

        private static void DrawCrossGlyph(Graphics g, Rectangle rect, Color color) {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float s = rect.Width * 0.26f;
            using (var pen = new Pen(color, Math.Max(1.6f, rect.Width / 10f))) {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
            }
        }

        private void DrawActive(Graphics g, Rectangle rect) {
            using (var track = new Pen(MaterialPalette.SurfaceContainerHighest, 2.4f)) {
                g.DrawEllipse(track, rect);
            }
            using (var arc = new Pen(MaterialPalette.Primary, 2.4f)) {
                g.DrawArc(arc, rect, _spinnerAngle, 100);
            }
        }

        private void DrawPending(Graphics g, Rectangle rect) {
            using (var pen = new Pen(MaterialPalette.OutlineVariant, 2f)) {
                g.DrawEllipse(pen, rect);
            }
        }
    }
}
