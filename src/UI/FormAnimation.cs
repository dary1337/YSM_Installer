using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Material 3 motion for modal dialogs. Subtle top-down slide combined with opacity fade:
    /// dialog starts slightly above its final position and settles down. Pure Location +
    /// Opacity — no Region or rasterization tricks; small slide (~10 px) is fast enough that
    /// per-frame SetWindowPos doesn't visibly jerk.
    ///
    /// Easing: Material 3 emphasized cubic-bezier curves (same algorithm CSS engines use).
    /// </summary>
    public static class FormAnimation {
        public const int OpenDurationMs = 200;
        public const int CloseDurationMs = 140;
        private const int FrameDelayMs = 8;
        private const int SlideOffsetPx = 10;

        public static async Task OpenAsync(Form form) {
            if (form == null || form.IsDisposed) {
                return;
            }
            Point target = form.Location;
            Point start = new Point(target.X, target.Y - SlideOffsetPx);
            form.Location = start;
            form.Opacity = 0d;

            var sw = Stopwatch.StartNew();
            while (true) {
                if (form.IsDisposed) {
                    return;
                }
                double t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)OpenDurationMs);
                double eased = EmphasizedDecelerate(t);
                form.Opacity = eased;
                form.Location = new Point(
                    target.X,
                    start.Y + (int)Math.Round((target.Y - start.Y) * eased)
                );
                if (t >= 1.0) {
                    break;
                }
                await Task.Delay(FrameDelayMs);
            }
            if (!form.IsDisposed) {
                form.Opacity = 1d;
                form.Location = target;
            }
        }

        public static async Task CloseAsync(Form form) {
            if (form == null || form.IsDisposed) {
                return;
            }
            Point start = form.Location;
            Point target = new Point(start.X, start.Y + SlideOffsetPx);
            double startOpacity = form.Opacity;

            var sw = Stopwatch.StartNew();
            while (true) {
                if (form.IsDisposed) {
                    return;
                }
                double t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)CloseDurationMs);
                double eased = EmphasizedAccelerate(t);
                form.Opacity = startOpacity * (1d - eased);
                form.Location = new Point(
                    start.X,
                    start.Y + (int)Math.Round((target.Y - start.Y) * eased)
                );
                if (t >= 1.0) {
                    break;
                }
                await Task.Delay(FrameDelayMs);
            }
            if (!form.IsDisposed) {
                form.Opacity = 0d;
            }
        }

        // cubic-bezier(0.05, 0.7, 0.1, 1.0) — Material 3 emphasized decelerate.
        private static double EmphasizedDecelerate(double t) =>
            CubicBezier(t, 0.05, 0.7, 0.1, 1.0);

        // cubic-bezier(0.3, 0.0, 0.8, 0.15) — Material 3 emphasized accelerate.
        private static double EmphasizedAccelerate(double t) =>
            CubicBezier(t, 0.3, 0.0, 0.8, 0.15);

        // Cubic-bezier evaluator with Newton-Raphson — same algorithm CSS engines use for
        // their cubic-bezier() timing functions.
        private static double CubicBezier(double t, double p1x, double p1y, double p2x, double p2y) {
            double s = t;
            for (int i = 0; i < 6; i++) {
                double x = BezierAxis(s, p1x, p2x);
                double dx = BezierAxisDerivative(s, p1x, p2x);
                if (Math.Abs(dx) < 1e-6) break;
                s -= (x - t) / dx;
                if (s < 0) s = 0;
                else if (s > 1) s = 1;
            }
            return BezierAxis(s, p1y, p2y);
        }

        private static double BezierAxis(double t, double p1, double p2) {
            double mt = 1 - t;
            return 3 * mt * mt * t * p1 + 3 * mt * t * t * p2 + t * t * t;
        }

        private static double BezierAxisDerivative(double t, double p1, double p2) {
            double mt = 1 - t;
            return 3 * mt * mt * p1 + 6 * mt * t * (p2 - p1) + 3 * t * t * (1 - p2);
        }
    }
}
