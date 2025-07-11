using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller {
    static internal class r {

        [Obsolete("Создает артефакты при больших скруглениях")]
        [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        public static extern IntPtr roundRect(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);


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

        public static void round(Rectangle rectSurface, int borderSize, int cornerRadius, PaintEventArgs e, Control ctn) {
            Rectangle rectBorder = Rectangle.Inflate(rectSurface, -borderSize, -borderSize);
            int smoothSize = 2;

            using (GraphicsPath pathSurface = GetFigurePath(rectSurface, cornerRadius))
            using (GraphicsPath pathBorder = GetFigurePath(rectBorder, cornerRadius - borderSize))
            using (Pen penSurface = new Pen(ctn.Parent.BackColor, smoothSize))
            using (Pen penBorder = new Pen(Color.Transparent, borderSize)) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                ctn.Region = new Region(pathSurface);
                e.Graphics.DrawPath(penSurface, pathSurface);
                e.Graphics.DrawPath(penBorder, pathBorder);
            }
        }
        public static void roundBorder(Rectangle rectSurface, int borderSize, int cornerRadius, PaintEventArgs e, Control ctn, Color borderColor) {
            Rectangle rectBorder = Rectangle.Inflate(rectSurface, -borderSize, -borderSize);

            int
                smoothSize = 1;

            using (GraphicsPath pathSurface = GetFigurePath(rectSurface, cornerRadius))
            using (GraphicsPath pathBorder = GetFigurePath(rectBorder, cornerRadius - borderSize))
            using (Pen penSurface = new Pen(ctn.Parent.BackColor, smoothSize))
            using (Pen penBorder = new Pen(borderColor, borderSize)) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                ctn.Region = new Region(pathSurface);
                e.Graphics.DrawPath(penSurface, pathSurface);
                e.Graphics.DrawPath(penBorder, pathBorder);
            }
        }
    }

}
