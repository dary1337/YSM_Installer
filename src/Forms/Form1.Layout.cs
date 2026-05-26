using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private TableLayoutPanel _root = null!;
        private TableLayoutPanel _headerRow = null!;
        private FlowLayoutPanel _titleStack = null!;
        private Label _overlineLabel = null!;
        private Label _subLabel = null!;
        private MaterialButton _settingsButton = null!;
        private Panel _contentHost = null!;
        private Panel _island = null!;
        private FlowLayoutPanel _islandActions = null!;
        // _subLabel.Text is truncated in place on resize; without the original here we'd lose
        // characters every time the user grew/shrank the window.
        private string _headerSubFull = string.Empty;

        private void BuildChrome() {
            SuspendLayout();
            try {
                Controls.Clear();
                // Padding lives on the inner content wrapper instead of the form so the titlebar
                // can run edge-to-edge along the top.
                Padding = Padding.Empty;

                var titleBar = new MaterialTitleBar {
                    TitleText = "YSM Installer",
                    AppIcon = Properties.Resources.logo.ToBitmap(),
                };

                _root = new TableLayoutPanel {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                };
                _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                _root.Controls.Add(BuildHeader(), 0, 0);
                _root.Controls.Add(BuildContentHost(), 0, 1);
                _root.Controls.Add(BuildIsland(), 0, 2);

                // HitTestForwardingPanel returns HTTRANSPARENT in form-owned zones (edges +
                // titlebar) so Windows cascades WM_NCHITTEST up to the form, whose DefWindowProc
                // handles native resize/drag via WS_THICKFRAME.
                var contentWrap = new BorderlessForm.HitTestForwardingPanel {
                    BackColor = MaterialPalette.Surface,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = new Padding(Sizes.WindowPadding),
                };
                contentWrap.Controls.Add(_root);

                // Dock ordering: controls dock in reverse Z-order (last-added docks first).
                Controls.Add(contentWrap);
                Controls.Add(titleBar);
            }
            finally {
                ResumeLayout(performLayout: true);
            }
        }

        private Control BuildHeader() {
            _headerRow = new TableLayoutPanel {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Sizes.ContentGap),
                Padding = Padding.Empty,
            };
            _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var header = _headerRow;

            _titleStack = new FlowLayoutPanel {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false,
            };

            _overlineLabel = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.Overline,
                ForeColor = MaterialPalette.OnSurfaceVariant,
                Margin = new Padding(0, 0, 0, 2),
                Text = "STARTING…",
            };
            _subLabel = new SoftLabel {
                AutoSize = true,
                Font = MaterialType.TitleMedium,
                ForeColor = MaterialPalette.OnSurface,
                Margin = Padding.Empty,
                Text = "Preparing installer",
            };
            _titleStack.Controls.Add(_overlineLabel);
            _titleStack.Controls.Add(_subLabel);

            var rightActions = new FlowLayoutPanel {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 2, 0, 0),
                Padding = Padding.Empty,
                WrapContents = false,
            };

            _settingsButton = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                IconGlyph = MaterialIcons.Settings,
                Text = "Settings",
                Width = 110,
                Height = 36,
                Margin = Padding.Empty,
            };
            _settingsButton.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
            _settingsButton.Click += async (sender, args) => await OpenSettingsAsync();
            rightActions.Controls.Add(_settingsButton);

#if DEBUG
            var testButton = new MaterialButton {
                Variant = MaterialButtonVariant.Text,
                Text = "Test",
                Width = 70,
                Height = 36,
                Margin = new Padding(0, 0, 4, 0),
            };
            testButton.SetAccent(MaterialPalette.Tertiary, MaterialPalette.OnTertiaryContainer);
            testButton.Click += (sender, args) => OpenDevTestMenu();
            rightActions.Controls.Add(testButton);
#endif

            header.Controls.Add(_titleStack, 0, 0);
            header.Controls.Add(rightActions, 1, 0);
            return header;
        }

        private Control BuildContentHost() {
            _contentHost = new Panel {
                AutoScroll = false, // never show a scrollbar — the window grows to fit instead
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            return _contentHost;
        }

        private Control BuildIsland() {
            // No island chrome — just a transparent host that holds the bottom action row.
            _island = new Panel {
                BackColor = Color.Transparent,
                Dock = DockStyle.Top,
                Height = Sizes.ButtonHeight + 4,
                Margin = new Padding(0, Sizes.ContentGap, 0, 0),
                Padding = Padding.Empty,
                Visible = false,
            };

            _islandActions = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false,
            };
            _island.Controls.Add(_islandActions);
            _island.Resize += (s, e) => PositionIslandActions();
            return _island;
        }

        private void PositionIslandActions() {
            // Anchor=Right doesn't pin until the host has a measured width, so place manually on resize.
            _islandActions.Left = _island.ClientSize.Width - _islandActions.Width;
            _islandActions.Top = (_island.ClientSize.Height - _islandActions.Height) / 2;
        }

        // ---- Content helpers ----

        private void SetHeader(string overline, string sub) {
            _overlineLabel.Text = (overline ?? string.Empty).ToUpperInvariant();
            _headerSubFull = sub ?? string.Empty;
            bool hasSub = _headerSubFull.Length > 0;
            ApplyHeaderSubtitle();
            _subLabel.Visible = hasSub;
            // Anchor=Left (no Top) lets the TLP cell center the stack vertically, matching the Settings
            // button row when only the overline shows. With both labels, top-anchor keeps the original look.
            _titleStack.Anchor = hasSub
                ? AnchorStyles.Top | AnchorStyles.Left
                : AnchorStyles.Left;
        }

        // Manual ellipsis because AutoEllipsis only works with AutoSize=false, and the parent
        // FlowLayoutPanel needs AutoSize=true — without this the path silently clips off the edge.
        private void ApplyHeaderSubtitle() {
            if (_subLabel == null) {
                return;
            }
            int availablePx = GetHeaderSubAvailableWidth();
            _subLabel.Text = TruncateToWidth(_headerSubFull, _subLabel.Font, availablePx);
        }

        // Reads the actual width of the percent-100 title column from the laid-out header,
        // rather than approximating via ClientSize − magic constants. Previously the magic
        // constants under-reported the available width on wide windows and truncated the
        // subtitle even when there was plenty of room.
        private int GetHeaderSubAvailableWidth() {
            if (_headerRow != null && _headerRow.IsHandleCreated && _headerRow.Width > 0) {
                int[] columnWidths = _headerRow.GetColumnWidths();
                if (columnWidths.Length > 0 && columnWidths[0] > 0) {
                    // Leave a few px so the ellipsis doesn't kiss the Settings cluster.
                    return Math.Max(120, columnWidths[0] - 8);
                }
            }
            // Fallback for very early calls before layout has settled.
            return Math.Max(120, ClientSize.Width - 200);
        }

        // GDI+ MeasureString (matched to SoftLabel.OnPaint's Graphics.DrawString) — TextRenderer
        // is GDI and yields slightly different widths, which would clip the ellipsis a few px
        // off from where the label actually renders.
        private static string TruncateToWidth(string text, Font font, int maxPx) {
            if (string.IsNullOrEmpty(text) || maxPx <= 0) {
                return text ?? string.Empty;
            }
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp)) {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                if (MeasureWidth(g, text, font) <= maxPx) {
                    return text;
                }
                const string ellipsis = "…";
                int low = 0;
                int high = text.Length;
                while (low < high) {
                    int mid = (low + high + 1) / 2;
                    string candidate = text.Substring(0, mid) + ellipsis;
                    if (MeasureWidth(g, candidate, font) <= maxPx) {
                        low = mid;
                    }
                    else {
                        high = mid - 1;
                    }
                }
                return low > 0 ? text.Substring(0, low) + ellipsis : ellipsis;
            }
        }

        private static int MeasureWidth(Graphics g, string text, Font font) {
            return (int)Math.Ceiling(g.MeasureString(text, font).Width);
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            // OnResize fires during early form init before BuildChrome runs and before the
            // handle exists; ClientSize is unreliable until then, so skip — the first valid
            // SetHeader call after BuildChrome will fit correctly.
            if (!IsHandleCreated) {
                return;
            }
            // Other labels are AutoSize or content-bound; only the manually-ellipsized subtitle
            // needs to be re-fit when the window changes width.
            ApplyHeaderSubtitle();
        }

        // Window height is fixed (no jumping). Stacked content is top-aligned; only the dedicated
        // full states (e.g. Complete) center themselves via their own layout (fill = true).
        private void SetContent(Control container, bool fill) {
            _contentHost.SuspendLayout();
            for (int i = _contentHost.Controls.Count - 1; i >= 0; i--) {
                Control existing = _contentHost.Controls[i];
                _contentHost.Controls.RemoveAt(i);
                existing.Dispose();
            }
            if (fill) {
                // Filled layouts (e.g. centered Complete state) own their own internal sizing
                // and don't benefit from a scrollbar — content is bounded by viewport.
                container.Dock = DockStyle.Fill;
                _contentHost.Controls.Add(container);
            }
            else {
                // Stacked / Dock=Top layouts can overflow vertically (long lists, many build
                // cards, version-mismatch + guide stack). Wrap them in a scroll panel so
                // overflow becomes scroll instead of clipping.
                var scroll = new MaterialScrollPanel {
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                };
                container.Dock = DockStyle.Top;
                scroll.ContentPanel.Controls.Add(container);
                _contentHost.Controls.Add(scroll);
            }
            _contentHost.ResumeLayout(true);
        }

        /// <summary>Vertical full-width stack hosted in an AutoScroll panel.</summary>
        private static TableLayoutPanel NewStack() {
            var stack = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return stack;
        }

        private static void AddToStack(TableLayoutPanel stack, Control control, int bottomGap) {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, bottomGap);
            stack.Controls.Add(control);
        }

        // ---- Island helpers ----

        private static readonly bool IslandEnabled = true;

        private void HideIsland() {
            _island.Visible = false;
        }

        private void SetIslandActions(params Control[] actions) {
            if (!IslandEnabled) {
                _island.Visible = false;
                return;
            }
            _islandActions.SuspendLayout();
            for (int i = _islandActions.Controls.Count - 1; i >= 0; i--) {
                Control old = _islandActions.Controls[i];
                _islandActions.Controls.RemoveAt(i);
                old.Dispose();
            }
            // RightToLeft flow puts the first-added control at the far right; iterate in reverse so
            // the primary (last input) lands rightmost.
            for (int i = actions.Length - 1; i >= 0; i--) {
                Control action = actions[i];
                action.Dock = DockStyle.None;
                action.AutoSize = true;
                action.Margin = new Padding(i == 0 ? 0 : Tokens.Space2, 2, 0, 2);
                _islandActions.Controls.Add(action);
            }
            _islandActions.ResumeLayout(true);
            _island.Visible = actions.Length > 0;
            PositionIslandActions();
        }

        private Control BuildButtonRow(params MaterialButton[] buttons) {
            var grid = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = buttons.Length,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Height = Tokens.BtnHeight,
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.BtnHeight));
            for (int i = 0; i < buttons.Length; i++) {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
                buttons[i].AutoSize = false;
                buttons[i].Dock = DockStyle.Fill;
                buttons[i].Height = Tokens.BtnHeight;
                buttons[i].Margin = new Padding(
                    i == 0 ? 0 : Tokens.Space1,
                    0,
                    i == buttons.Length - 1 ? 0 : Tokens.Space1,
                    0
                );
                grid.Controls.Add(buttons[i], i, 0);
            }
            return grid;
        }

        private MaterialButton PrimaryButton(string text, string glyph = "") {
            return new MaterialButton {
                Variant = MaterialButtonVariant.Filled,
                Text = text,
                IconGlyph = glyph,
                Height = Sizes.ButtonHeight,
            };
        }

        private MaterialButton TonalButton(string text, string glyph = "") {
            return new MaterialButton {
                Variant = MaterialButtonVariant.Tonal,
                Text = text,
                IconGlyph = glyph,
                Height = Sizes.ButtonHeight,
            };
        }

        private MaterialButton OutlinedButton(string text, string glyph = "") {
            var button = new MaterialButton {
                Variant = MaterialButtonVariant.Outlined,
                Text = text,
                IconGlyph = glyph,
                Height = Sizes.ButtonHeight,
            };
            button.SetAccent(MaterialPalette.Primary, MaterialPalette.OnPrimary);
            return button;
        }
    }
}
