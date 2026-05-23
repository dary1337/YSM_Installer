using System;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public partial class Form1 {
        private TableLayoutPanel _root = null!;
        private Label _overlineLabel = null!;
        private Label _subLabel = null!;
        private MaterialButton _settingsButton = null!;
        private Panel _contentHost = null!;
        private Panel _island = null!;
        private FlowLayoutPanel _islandActions = null!;

        private void BuildChrome() {
            Controls.Clear();
            Padding = new Padding(Sizes.WindowPadding);

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

            Controls.Add(_root);
        }

        private Control BuildHeader() {
            var header = new TableLayoutPanel {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Sizes.ContentGap),
                Padding = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleStack = new FlowLayoutPanel {
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
            titleStack.Controls.Add(_overlineLabel);
            titleStack.Controls.Add(_subLabel);

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

            header.Controls.Add(titleStack, 0, 0);
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
            _subLabel.Text = sub ?? string.Empty;
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
            container.Dock = fill ? DockStyle.Fill : DockStyle.Top;
            _contentHost.Controls.Add(container);
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
                _islandActions.Controls.RemoveAt(i);
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
