using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Enables click-and-drag on any non-interactive part of a form (typical for modal dialogs
    /// where the user expects to grab the whole window, not just the titlebar). Skips clicks
    /// on buttons, dropdowns, links, scrollers and our own title bar — those control their own
    /// click semantics. Installs an Application-level <see cref="IMessageFilter"/> that lives
    /// for the form's lifetime.
    /// </summary>
    public static class FormDragAnywhere {
        public static void Enable(Form form) {
            if (form == null) {
                return;
            }
            var filter = new DragFilter(form);
            // Subscribe to both HandleCreated and HandleDestroyed so the filter survives any
            // handle-recreation cycle (style changes, ShowInTaskbar toggles, etc.) — without
            // the pair, HandleDestroyed would remove the filter and it would never come back.
            form.HandleCreated += (s, e) => Application.AddMessageFilter(filter);
            form.HandleDestroyed += (s, e) => Application.RemoveMessageFilter(filter);
            if (form.IsHandleCreated) {
                Application.AddMessageFilter(filter);
            }
        }

        private sealed class DragFilter : IMessageFilter {
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_MOUSEMOVE = 0x0200;
            private const int WM_LBUTTONUP = 0x0202;

            private readonly Form _form;
            private bool _dragging;
            private Point _dragOriginScreen;
            private Point _formOriginLocation;

            public DragFilter(Form form) {
                _form = form;
            }

            public bool PreFilterMessage(ref Message m) {
                if (!_form.IsHandleCreated || !_form.Visible) {
                    return false;
                }

                switch (m.Msg) {
                    case WM_LBUTTONDOWN: {
                        // lParam packs (x, y) of the click in the target control's CLIENT
                        // coords, not screen. Convert via the hit-test target (msg.HWnd).
                        Point screen = MessageToScreenPoint(m);
                        Rectangle bounds = _form.RectangleToScreen(_form.ClientRectangle);
                        if (!bounds.Contains(screen)) {
                            return false;
                        }
                        Control? leaf = WalkToLeaf(_form, _form.PointToClient(screen));
                        if (leaf != null && IsInteractive(leaf)) {
                            return false;
                        }
                        _dragging = true;
                        _dragOriginScreen = screen;
                        _formOriginLocation = _form.Location;
                        // Don't swallow — labels / panels can still receive the click; nothing
                        // useful happens there, but suppressing could break unforeseen handlers.
                        return false;
                    }
                    case WM_MOUSEMOVE: {
                        if (!_dragging) {
                            return false;
                        }
                        Point cur = Cursor.Position;
                        _form.Location = new Point(
                            _formOriginLocation.X + cur.X - _dragOriginScreen.X,
                            _formOriginLocation.Y + cur.Y - _dragOriginScreen.Y
                        );
                        return false;
                    }
                    case WM_LBUTTONUP:
                        _dragging = false;
                        return false;
                }
                return false;
            }

            // lParam in mouse messages packs the cursor at message-generation time, in the
            // target window's client coords. Converting via Control.FromHandle keeps drag
            // attribution accurate even if the cursor moved between generation and dispatch.
            private static Point MessageToScreenPoint(Message m) {
                long lp = m.LParam.ToInt64();
                short x = unchecked((short)(lp & 0xFFFF));
                short y = unchecked((short)((lp >> 16) & 0xFFFF));
                Point inTarget = new Point(x, y);
                Control? target = Control.FromHandle(m.HWnd);
                return target != null ? target.PointToScreen(inTarget) : Cursor.Position;
            }

            private static Control? WalkToLeaf(Control root, Point pInRoot) {
                Control current = root;
                Point p = pInRoot;
                while (true) {
                    Control? child = current.GetChildAtPoint(p);
                    if (child == null) {
                        return current;
                    }
                    p = new Point(p.X - child.Left, p.Y - child.Top);
                    current = child;
                }
            }

            // Walks the parent chain too — a Label inside a MaterialButton should count as
            // interactive (it's a click target for the button, not a draggable surface).
            private static bool IsInteractive(Control c) {
                for (Control? p = c; p != null; p = p.Parent) {
                    if (p is ButtonBase
                        || p is TextBoxBase
                        || p is ComboBox
                        || p is LinkLabel
                        || p is DropdownSelect
                        || p is MaterialRadioCard
                        || p is MaterialOptionCard
                        || p is MaterialScrollPanel
                        || p is MaterialTitleBar) {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
