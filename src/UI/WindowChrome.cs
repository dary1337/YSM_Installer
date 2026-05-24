using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Themes the native window caption dark via DWM attributes (Windows 10 build 19041+). Keeps native
    /// resize/drag/snap behavior while matching the Material You surface palette of the client area.
    /// </summary>
    public static class WindowChrome {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void ApplyDark(Form form) {
            if (form == null) {
                return;
            }
            if (form.IsHandleCreated) {
                Apply(form.Handle);
            }
            else {
                form.HandleCreated += (s, e) => Apply(form.Handle);
            }
        }

        private static void Apply(IntPtr hwnd) {
            try {
                int useDark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

                int caption = ToColorRef(MaterialPalette.Surface);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                int border = ToColorRef(MaterialPalette.OutlineVariant);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

                int text = ToColorRef(MaterialPalette.OnSurface);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch {
                // Older builds without these attributes simply keep the default chrome.
            }
        }

        private static int ToColorRef(Color color) {
            return color.R | (color.G << 8) | (color.B << 16);
        }
    }
}
