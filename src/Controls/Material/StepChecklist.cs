using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Vertical checklist of install stages. Steps before <see cref="ActiveIndex"/> render as completed
    /// (green check), the active step shows a spinning arc, later steps render as muted pending circles.
    /// Active→done transitions are animated: arc finishes its sweep, blends Primary→Success, fill grows,
    /// check glyph scale-pops in.
    /// </summary>
    public sealed class StepChecklist : Control {
        private string[] _steps = Array.Empty<string>();
        private int _activeIndex;
        private int _errorIndex = -1;
        private float _spinnerAngle;
        private float[] _completionAnims = Array.Empty<float>();
        private readonly Timer _spinner;
        private readonly Timer _morphTimer;

        private const int RowHeight = 26;
        private const int Indicator = 18;
        private const float MorphStep = 16f / 150f; // ~150ms total per step morph

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
            _morphTimer = new Timer { Interval = 16 };
            _morphTimer.Tick += OnMorphTick;
        }

        public void SetSteps(string[] steps) {
            _steps = steps ?? Array.Empty<string>();
            _errorIndex = -1;
            _activeIndex = 0;
            _completionAnims = new float[_steps.Length];
            _spinner.Stop();
            _morphTimer.Stop();
            Height = _steps.Length * RowHeight;
            Invalidate();
        }

        public void SetError(int index) {
            _errorIndex = index;
            _spinner.Stop();
            _morphTimer.Stop();
            Invalidate();
        }

        public int ActiveIndex {
            get => _activeIndex;
            set {
                int prev = _activeIndex;
                _activeIndex = value;
                bool active = value >= 0 && value < _steps.Length;
                if (active && !_spinner.Enabled) _spinner.Start();
                if (!active && _spinner.Enabled) _spinner.Stop();
                for (int i = Math.Max(0, prev); i < value && i < _completionAnims.Length; i++) {
                    if (_completionAnims[i] <= 0f) {
                        _completionAnims[i] = float.Epsilon;
                    }
                }
                if (HasActiveMorph() && !_morphTimer.Enabled) {
                    _morphTimer.Start();
                }
                Invalidate();
            }
        }

        public void Complete() {
            int prev = _activeIndex;
            _activeIndex = _steps.Length;
            _spinner.Stop();
            for (int i = Math.Max(0, prev); i < _steps.Length; i++) {
                if (i < _completionAnims.Length && _completionAnims[i] <= 0f) {
                    _completionAnims[i] = float.Epsilon;
                }
            }
            if (HasActiveMorph() && !_morphTimer.Enabled) {
                _morphTimer.Start();
            }
            Invalidate();
        }

        private bool HasActiveMorph() {
            for (int i = 0; i < _completionAnims.Length; i++) {
                if (_completionAnims[i] > 0f && _completionAnims[i] < 1f) {
                    return true;
                }
            }
            return false;
        }

        private void OnMorphTick(object? sender, EventArgs e) {
            bool any = false;
            for (int i = 0; i < _completionAnims.Length; i++) {
                if (_completionAnims[i] > 0f && _completionAnims[i] < 1f) {
                    _completionAnims[i] = Math.Min(1f, _completionAnims[i] + MorphStep);
                    any = true;
                }
            }
            if (!any) {
                _morphTimer.Stop();
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _spinner.Stop();
                _spinner.Dispose();
                _morphTimer.Stop();
                _morphTimer.Dispose();
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
                        float morph = i < _completionAnims.Length ? _completionAnims[i] : 1f;
                        if (morph >= 1f) {
                            DrawDone(g, indicatorRect);
                        }
                        else {
                            DrawDoneMorph(g, indicatorRect, morph);
                        }
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
            DrawCheckGlyph(g, rect, MaterialPalette.OnSuccess, 1f);
        }

        // Three overlapping phases driven by a single 0→1 progress (~300ms total):
        //   [0.0, 0.55] arc completes the remaining sweep — feels like the spinner "lands"
        //   [0.0, 1.0]  arc color shifts Primary → Success
        //   [0.55, 1.0] interior fills with Success (alpha 0 → 1)
        //   [0.55, 1.0] check glyph scale-pops from 0 to 1 with mild overshoot
        private void DrawDoneMorph(Graphics g, Rectangle rect, float progress) {
            float arcPhase = Math.Min(1f, progress / 0.55f);
            float fillPhase = Math.Max(0f, (progress - 0.55f) / 0.45f);
            float checkPhase = fillPhase;

            // Arc that finishes the loop. Start from where the spinner left off; sweep to 360.
            float startAngle = _spinnerAngle;
            float sweep = 100f + (260f * arcPhase); // 100° baseline + extends to a full ring
            Color arcColor = MaterialPalette.Overlay(MaterialPalette.Primary, MaterialPalette.Success, progress);

            using (var arc = new Pen(arcColor, 2.4f)) {
                g.DrawArc(arc, rect, startAngle, Math.Min(360f, sweep));
            }

            // Interior fill grows from transparent toward full Success.
            if (fillPhase > 0f) {
                int alpha = (int)(255f * fillPhase);
                using (var brush = new SolidBrush(Color.FromArgb(alpha, MaterialPalette.Success))) {
                    g.FillEllipse(brush, rect);
                }
            }

            // Scale-pop check: ease-out cubic with mild overshoot.
            if (checkPhase > 0f) {
                float t = checkPhase;
                float scale = 1f - (float)Math.Pow(1f - t, 3f); // easeOutCubic
                int alpha = (int)(255f * Math.Min(1f, checkPhase * 1.5f));
                DrawCheckGlyph(g, rect, Color.FromArgb(alpha, MaterialPalette.OnSuccess), scale);
            }
        }

        private void DrawError(Graphics g, Rectangle rect) {
            using (var brush = new SolidBrush(MaterialPalette.Error)) {
                g.FillEllipse(brush, rect);
            }
            DrawCrossGlyph(g, rect, MaterialPalette.OnError);
        }

        // Hand-drawn glyphs guarantee true visual centering inside the indicator circle (SVG Material
        // Symbols have asymmetric bboxes that make a mathematically centered bitmap look off).
        private static void DrawCheckGlyph(Graphics g, Rectangle rect, Color color, float scale) {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float s = rect.Width * 0.30f * scale;
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
