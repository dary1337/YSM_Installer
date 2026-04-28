using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller {
    internal static class RoundedControlRenderer {

        [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        public static extern IntPtr CreateRoundRectRegion(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);


        public static GraphicsPath GetFigurePath(Rectangle rect, int radius) {
            GraphicsPath path = new GraphicsPath();
            float curveSize = radius * 2F;

            path.StartFigure();
            path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
            path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
            path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
            path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Rectangle rectSurface, int borderSize, int cornerRadius, PaintEventArgs e, Control control) {
            Rectangle rectBorder = Rectangle.Inflate(rectSurface, -borderSize, -borderSize);
            int smoothSize = 2;

            using (GraphicsPath pathSurface = GetFigurePath(rectSurface, cornerRadius))
            using (GraphicsPath pathBorder = GetFigurePath(rectBorder, cornerRadius - borderSize))
            using (Pen penSurface = new Pen(control.Parent?.BackColor ?? control.BackColor, smoothSize))
            using (Pen penBorder = new Pen(Color.Transparent, borderSize)) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                ReplaceRegion(control, pathSurface);
                e.Graphics.DrawPath(penSurface, pathSurface);
                e.Graphics.DrawPath(penBorder, pathBorder);
            }
        }

        public static void DrawRoundedBorder(Rectangle rectSurface, int borderSize, int cornerRadius, PaintEventArgs e, Control control, Color borderColor) {
            Rectangle rectBorder = Rectangle.Inflate(rectSurface, -borderSize, -borderSize);

            int smoothSize = 1;

            using (GraphicsPath pathSurface = GetFigurePath(rectSurface, cornerRadius))
            using (GraphicsPath pathBorder = GetFigurePath(rectBorder, cornerRadius - borderSize))
            using (Pen penSurface = new Pen(control.Parent?.BackColor ?? control.BackColor, smoothSize))
            using (Pen penBorder = new Pen(borderColor, borderSize)) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                ReplaceRegion(control, pathSurface);
                e.Graphics.DrawPath(penSurface, pathSurface);
                e.Graphics.DrawPath(penBorder, pathBorder);
            }
        }

        private static void ReplaceRegion(Control control, GraphicsPath path) {
            Region previousRegion = control.Region;
            control.Region = new Region(path);
            previousRegion?.Dispose();
        }
    }

}
