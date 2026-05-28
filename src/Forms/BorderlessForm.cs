using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Borderless form following the melak47/BorderlessWindow canonical pattern.
    ///
    /// Window styles: WS_POPUP | WS_THICKFRAME | WS_CAPTION. WS_POPUP suppresses the legacy
    /// frame-color artifact that appears with WS_THICKFRAME alone. WS_THICKFRAME enables
    /// native edge resize and Aero Snap. WS_CAPTION is kept invisible (WM_NCCALCSIZE strips
    /// the NC area) but its presence is required for DWM-managed window behavior — without
    /// it the native resize/drag loop misbehaves.
    ///
    /// Hit-testing: child controls that cover the form (e.g. contentWrap, titleBar) return
    /// HTTRANSPARENT in zones owned by the form (edges for resize, titlebar caption area for
    /// drag). Windows cascades the hit-test up to the form, which returns the appropriate HT
    /// code; DefWindowProc on the form then runs the native resize/drag loop via WS_THICKFRAME.
    /// Native open/close fade + Aero Snap animations come for free since we now look like a
    /// regular DWM-managed window from the OS perspective.
    /// </summary>
    public class BorderlessForm : Form {
        public bool EnableEdgeResize { get; protected set; } = true;
        protected bool EnableDragAnywhere { get; set; }

        // ---- Win32 constants ----
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int CS_DROPSHADOW = 0x00020000;

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;

        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_ENABLED = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public const int ResizeBorderThickness = 4;

        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NCCALCSIZE_PARAMS {
            public RECT rgrc0, rgrc1, rgrc2;
            public IntPtr lppos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONULL = 0;

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(ref int pfEnabled);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private bool _aeroEnabled;
        private int _cachedCaptionHeight = -1;

        public BorderlessForm() {
            FormBorderStyle = FormBorderStyle.None;
            // Defensive default — derived forms override this with their own surface variant.
            // Setting it on the base ensures the WNDCLASS-derived background brush is Surface
            // (not Control default) during the native open animation, so the titlebar doesn't
            // briefly flash black before our paint catches up to the fade-in frame.
            BackColor = MaterialPalette.Surface;
            ControlAdded += OnControlsChanged;
            ControlRemoved += OnControlsChanged;
        }

        private void OnControlsChanged(object? sender, ControlEventArgs e) {
            _cachedCaptionHeight = -1;
        }

        protected override CreateParams CreateParams {
            get {
                int enabled = 0;
                DwmIsCompositionEnabled(ref enabled);
                _aeroEnabled = enabled == 1;

                CreateParams cp = base.CreateParams;
                // Canonical aero-borderless style. KEEP WS_CAPTION even though it's invisible
                // (NC area stripped by WM_NCCALCSIZE): omitting it broke DWM-managed
                // resize/drag in an earlier attempt; melak47 keeps it for the same reason.
                cp.Style |= WS_POPUP | WS_THICKFRAME | WS_CAPTION;
                // WS_POPUP normally suppresses the taskbar button — without WS_EX_APPWINDOW
                // a programmatic minimize (e.g. after Launch WARNO) hides the window with no
                // way for the user to restore it, looking like the app closed.
                cp.ExStyle |= WS_EX_APPWINDOW;
                if (!_aeroEnabled) {
                    cp.ClassStyle |= CS_DROPSHADOW;
                }
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // One-shot DWM frame extension. {1,1,1,1} symmetric so the managed shadow attaches
            // on all sides. With WS_POPUP the 1-px frame zone composites as our content (not
            // legacy color), so no visible edge artifact.
            if (_aeroEnabled) {
                int ncPolicy = DWMNCRP_ENABLED;
                DwmSetWindowAttribute(Handle, DWMWA_NCRENDERING_POLICY, ref ncPolicy, sizeof(int));
                var margins = new MARGINS { leftWidth = 1, rightWidth = 1, topHeight = 1, bottomHeight = 1 };
                DwmExtendFrameIntoClientArea(Handle, ref margins);
                // Win11 rounded corners (silently ignored on Win10 — attribute unknown).
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
        }

        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
            if (EnableDragAnywhere) {
                FormDragAnywhere.Enable(this);
            }
        }

        protected override void WndProc(ref Message m) {
            switch (m.Msg) {
                case WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero) {
                        if (WindowState == FormWindowState.Maximized) {
                            // Clamp to monitor work area so the phantom WS_THICKFRAME border
                            // doesn't push the window past the screen edge when maximized.
                            var p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                            IntPtr hmon = MonitorFromWindow(Handle, MONITOR_DEFAULTTONULL);
                            if (hmon != IntPtr.Zero) {
                                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                                if (GetMonitorInfo(hmon, ref mi)) {
                                    p.rgrc0 = mi.rcWork;
                                    Marshal.StructureToPtr(p, m.LParam, false);
                                }
                            }
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case WM_NCHITTEST:
                    long lParam = m.LParam.ToInt64();
                    short sx = unchecked((short)(lParam & 0xFFFF));
                    short sy = unchecked((short)((lParam >> 16) & 0xFFFF));
                    Point clientPt = PointToClient(new Point(sx, sy));
                    int hit = ResolveHit(clientPt.X, clientPt.Y);
                    if (hit != HTCLIENT) {
                        m.Result = (IntPtr)hit;
                        return;
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Decides the HT code for a point in the form's client coordinates:
        /// — Edge zone → HT*{LEFT/RIGHT/TOP/BOTTOM/corners} (when EnableEdgeResize)
        /// — Top zone within caption height → HTCAPTION (drag)
        /// — Else → HTCLIENT
        /// </summary>
        public int ResolveHit(int x, int y) {
            if (EnableEdgeResize && WindowState == FormWindowState.Normal) {
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                int t = ResizeBorderThickness;
                bool onLeft = x >= 0 && x < t;
                bool onRight = x >= w - t && x < w;
                bool onTop = y >= 0 && y < t;
                bool onBottom = y >= h - t && y < h;
                if (onTop && onLeft) return HTTOPLEFT;
                if (onTop && onRight) return HTTOPRIGHT;
                if (onBottom && onLeft) return HTBOTTOMLEFT;
                if (onBottom && onRight) return HTBOTTOMRIGHT;
                if (onLeft) return HTLEFT;
                if (onRight) return HTRIGHT;
                if (onTop) return HTTOP;
                if (onBottom) return HTBOTTOM;
            }
            int captionH = GetCaptionHeight();
            if (captionH > 0 && y >= 0 && y < captionH) {
                return HTCAPTION;
            }
            return HTCLIENT;
        }

        private int GetCaptionHeight() {
            if (_cachedCaptionHeight >= 0) {
                return _cachedCaptionHeight;
            }
            int found = 0;
            foreach (Control c in Controls) {
                if (c is MaterialTitleBar bar) {
                    found = bar.Height;
                    break;
                }
            }
            _cachedCaptionHeight = found;
            return found;
        }

        /// <summary>
        /// Panel that returns HTTRANSPARENT for WM_NCHITTEST in zones owned by the form
        /// (edges, titlebar) — Windows then cascades the hit-test to the form, whose
        /// DefWindowProc runs native resize/drag via WS_THICKFRAME. Use this for any
        /// opaque child that covers the form's edge or caption area (typically contentWrap).
        /// </summary>
        public sealed class HitTestForwardingPanel : Panel {
            protected override void WndProc(ref Message m) {
                if (m.Msg == WM_NCHITTEST && FindForm() is BorderlessForm bf) {
                    long lParam = m.LParam.ToInt64();
                    short sx = unchecked((short)(lParam & 0xFFFF));
                    short sy = unchecked((short)((lParam >> 16) & 0xFFFF));
                    Point inForm = bf.PointToClient(new Point(sx, sy));
                    int hit = bf.ResolveHit(inForm.X, inForm.Y);
                    if (hit != HTCLIENT) {
                        m.Result = (IntPtr)HTTRANSPARENT;
                        return;
                    }
                }
                base.WndProc(ref m);
            }
        }
    }
}
