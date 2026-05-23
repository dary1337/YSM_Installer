using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Placeholder card with a left-to-right shimmer sweep, shown while scanning for installs.
    /// Mirrors the layout of <see cref="MaterialRadioCard"/> so the transition feels in-place.
    /// </summary>
    public sealed class SkeletonCard : RoundedPanel {
        private static readonly Timer SharedTimer = new Timer { Interval = 33 };
        private static event Action? Tick;
        private float _phase;

        static SkeletonCard() {
            // Single fixed handler — re-subscribing on every restart would leak handlers as cards come and go.
            SharedTimer.Tick += (s, a) => Tick?.Invoke();
        }

        public SkeletonCard()
            : base(Sizes.RadiusMedium) {
            BackColor = MaterialPalette.SurfaceContainer;
            SetOutline(MaterialPalette.OutlineVariant);
            MinimumSize = new Size(0, Sizes.RadioCardMinHeight);
            Height = Sizes.RadioCardMinHeight;
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Tick += OnTick;
            if (!SharedTimer.Enabled) {
                SharedTimer.Start();
            }
        }

        protected override void OnHandleDestroyed(EventArgs e) {
            Tick -= OnTick;
            if (Tick == null) {
                SharedTimer.Stop();
            }
            base.OnHandleDestroyed(e);
        }

        private void OnTick() {
            _phase += 0.04f;
            if (_phase > 1.4f) {
                _phase = -0.4f;
            }
            if (IsHandleCreated && !IsDisposed) {
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int pad = 14;
            DrawBlock(g, new Rectangle(pad, pad, 36, 36), Sizes.RadiusExtraSmall);
            int headerWidth = Math.Max(0, Math.Min(Width - pad * 2 - 60, 360));
            if (headerWidth > 0) {
                DrawBlock(g, new Rectangle(pad + 48, pad, headerWidth, 14), 7);
            }
            DrawBlock(g, new Rectangle(pad + 48, pad + 24, 70, 16), 8);
            DrawBlock(g, new Rectangle(pad + 48 + 78, pad + 24, 56, 16), 8);
        }

        private void DrawBlock(Graphics g, Rectangle rect, int radius) {
            if (rect.Width <= 0 || rect.Height <= 0) {
                return;
            }
            using (GraphicsPath path = RoundedControlRenderer.GetFigurePath(rect, radius)) {
                using (var baseBrush = new SolidBrush(MaterialPalette.SurfaceContainerHigh)) {
                    g.FillPath(baseBrush, path);
                }

                float center = rect.Left + rect.Width * _phase;
                float bandWidth = Math.Max(40, rect.Width * 0.5f);
                var bandRect = new RectangleF(center - bandWidth / 2f, rect.Top, bandWidth, rect.Height);
                if (bandRect.Width <= 0) {
                    return;
                }

                using (Region prevClip = g.Clip) {
                    g.SetClip(path);
                    using (var gradient = new LinearGradientBrush(
                        bandRect,
                        Color.FromArgb(0, MaterialPalette.OnSurface),
                        Color.FromArgb(0, MaterialPalette.OnSurface),
                        LinearGradientMode.Horizontal)) {
                        var blend = new ColorBlend(3) {
                            Colors = new[] {
                                Color.FromArgb(0, MaterialPalette.OnSurface),
                                Color.FromArgb(28, MaterialPalette.OnSurface),
                                Color.FromArgb(0, MaterialPalette.OnSurface),
                            },
                            Positions = new[] { 0f, 0.5f, 1f },
                        };
                        gradient.InterpolationColors = blend;
                        g.FillRectangle(gradient, bandRect);
                    }
                    g.Clip = prevClip;
                }
            }
        }
    }
}
