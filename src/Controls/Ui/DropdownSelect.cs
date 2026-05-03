using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    public sealed class DropdownSelect : UserControl, IMessageFilter {
        private readonly RoundedPanel _header;
        private readonly Label _selectedLabel;
        private readonly Label _chevronLabel;
        private readonly RoundedPanel _listHost;
        private readonly FlowLayoutPanel _listFlow;

        private readonly List<DropdownItem> _items = new List<DropdownItem>();
        private readonly List<RoundedButton> _optionButtons = new List<RoundedButton>();

        private bool _isOpen;
        private bool _messageFilterActive;
        private int _selectedIndex = -1;
        private int _listChromeHeight;
        private bool _layoutSafe;

        private const int HeaderHeight = 40;

        public DropdownSelect() {
            BackColor = Theme.Background;
            Margin = new Padding(0, 0, 0, 8);

            _header = new RoundedPanel(12) {
                BackColor = Theme.PanelBackground,
                Cursor = SystemCursors.Pointer,
                Dock = DockStyle.None,
                Height = HeaderHeight,
                Padding = new Padding(12, 8, 12, 8),
            };

            _header.SetOutline(Theme.EntryPanelBorder);

            var headerLayout = new Panel {
                Cursor = SystemCursors.Pointer,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            _selectedLabel = new Label {
                AutoEllipsis = true,
                AutoSize = false,
                Cursor = SystemCursors.Pointer,
                ForeColor = Theme.TextPrimary,
                Text = string.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _chevronLabel = new Label {
                AutoSize = false,
                Cursor = SystemCursors.Pointer,
                ForeColor = Theme.TextMuted,
                Padding = new Padding(0, 2, 0, 0),
                Text = "▼",
                TextAlign = ContentAlignment.MiddleRight,
                UseCompatibleTextRendering = false,
            };

            headerLayout.Controls.Add(_selectedLabel);
            headerLayout.Controls.Add(_chevronLabel);
            headerLayout.Layout += OnHeaderLayout;
            _header.Controls.Add(headerLayout);

            _listHost = new RoundedPanel(12) {
                BackColor = Theme.DropdownListBackground,
                Dock = DockStyle.None,
                Padding = new Padding(6, 6, 6, 6),
                Visible = false,
            };

            _listHost.SetOutline(Theme.EntryPanelBorder);

            _listFlow = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false,
            };

            _listHost.Controls.Add(_listFlow);

            Controls.Add(_listHost);
            Controls.Add(_header);
            _header.BringToFront();

            _header.MouseClick += OnHeaderClicked;
            headerLayout.MouseClick += OnHeaderClicked;
            _selectedLabel.MouseClick += OnHeaderClicked;
            _chevronLabel.MouseClick += OnHeaderClicked;

            Disposed += OnDisposed;

            _layoutSafe = true;
            Width = 320;
            Height = HeaderHeight;
        }

        public event EventHandler? SelectedIndexChanged;

        public int SelectedIndex => _selectedIndex;

        public object? SelectedTag =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count
                ? _items[_selectedIndex].Tag
                : null;

        public void ClearItems() {
            foreach (RoundedButton button in _optionButtons) {
                button.Dispose();
            }

            _optionButtons.Clear();
            _items.Clear();
            _listFlow.Controls.Clear();
            _selectedIndex = -1;
            _selectedLabel.Text = string.Empty;
            Collapse();
            UpdateLayoutMetrics();
        }

        public void AddItem(string displayText, object tag) {
            var item = new DropdownItem(displayText, tag);
            _items.Add(item);

            var button = new RoundedButton(10, Theme.DropdownRowHover) {
                AutoSize = false,
                BackColor = Theme.DropdownListBackground,
                Cursor = SystemCursors.Pointer,
                FlatAppearance = { BorderSize = 0 },
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(12, 8, 12, 8),
                Tag = _items.Count - 1,
                Text = displayText,
                TextAlign = ContentAlignment.MiddleLeft,
                UseCompatibleTextRendering = false,
            };

            button.MouseEnter += (sender, args) => {
                button.BackColor = Theme.DropdownRowHover;
            };

            button.MouseLeave += (sender, args) => {
                button.BackColor = Theme.DropdownListBackground;
            };

            button.Click += OnOptionClicked;

            _optionButtons.Add(button);
            _listFlow.Controls.Add(button);

            UpdateOptionWidths();
            UpdateLayoutMetrics();

            if (_selectedIndex < 0) {
                SelectIndex(0, notify: false);
            }
        }

        public void SelectByTag(object tag) {
            for (int i = 0; i < _items.Count; i++) {
                if (Equals(_items[i].Tag, tag)) {
                    SelectIndex(i, notify: false);
                    return;
                }
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            UpdateOptionWidths();
            LayoutChrome();
        }

        private void LayoutChrome() {
            if (!_layoutSafe) {
                return;
            }

            int w = Math.Max(0, ClientSize.Width);
            _header.SetBounds(0, 0, w, HeaderHeight);
            _listHost.SetBounds(0, HeaderHeight, w, _listChromeHeight);
        }

        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            LayoutChrome();
        }

        private void OnDisposed(object sender, EventArgs e) {
            RemoveMessageFilterSafe();
        }

        private void OnHeaderLayout(object sender, LayoutEventArgs e) {
            Panel? panel = sender as Panel;
            if (panel == null || panel.ClientSize.Height <= 0) {
                return;
            }

            Font font = _chevronLabel.Font ?? DefaultFont;
            int chevronWidth = TextRenderer.MeasureText(_chevronLabel.Text, font).Width + 12;
            int h = panel.ClientSize.Height;
            int gap = 8;
            _chevronLabel.SetBounds(panel.ClientSize.Width - chevronWidth, 0, chevronWidth, h);
            _selectedLabel.SetBounds(
                0,
                0,
                Math.Max(0, panel.ClientSize.Width - chevronWidth - gap),
                h
            );
        }

        public bool PreFilterMessage(ref Message m) {
            if (!_isOpen || !IsHandleCreated) {
                return false;
            }

            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_RBUTTONDOWN = 0x0204;
            if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_RBUTTONDOWN) {
                return false;
            }

            Point cursor = Cursor.Position;
            Rectangle bounds = RectangleToScreen(ClientRectangle);
            if (!bounds.Contains(cursor)) {
                if (IsHandleCreated && !IsDisposed) {
                    BeginInvoke(
                        new Action(() => {
                            if (_isOpen) {
                                Collapse();
                            }
                        })
                    );
                }
            }

            return false;
        }

        private void RemoveMessageFilterSafe() {
            if (!_messageFilterActive) {
                return;
            }

            Application.RemoveMessageFilter(this);
            _messageFilterActive = false;
        }

        private void OnHeaderClicked(object sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) {
                return;
            }

            Toggle();
        }

        private void OnOptionClicked(object sender, EventArgs e) {
            Control? control = sender as Control;
            if (control != null && control.Tag is int index) {
                SelectIndex(index, notify: true);
                Collapse();
            }
        }

        private void Toggle() {
            if (_items.Count == 0) {
                return;
            }

            if (_isOpen) {
                Collapse();
            }
            else {
                Expand();
            }
        }

        private void Expand() {
            if (_isOpen || _items.Count == 0) {
                return;
            }

            _isOpen = true;
            _listHost.Visible = true;
            if (!_messageFilterActive) {
                Application.AddMessageFilter(this);
                _messageFilterActive = true;
            }

            UpdateOptionWidths();
            UpdateLayoutMetrics();
        }

        private void Collapse() {
            if (!_isOpen) {
                return;
            }

            _isOpen = false;
            _listHost.Visible = false;
            RemoveMessageFilterSafe();
            UpdateLayoutMetrics();
        }

        private void SelectIndex(int index, bool notify) {
            if (index < 0 || index >= _items.Count) {
                return;
            }

            _selectedIndex = index;
            _selectedLabel.Text = _items[index].DisplayText;

            if (notify) {
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateOptionWidths() {
            int innerWidth = Math.Max(0, Width - _listHost.Padding.Horizontal);
            _listFlow.SuspendLayout();
            _listFlow.Width = innerWidth;
            foreach (RoundedButton button in _optionButtons) {
                button.Width = innerWidth;
                button.Height = 36;
            }

            _listFlow.ResumeLayout(performLayout: true);
        }

        private void UpdateLayoutMetrics() {
            _header.Height = HeaderHeight;

            if (_isOpen) {
                _listChromeHeight = Math.Max(
                    _listFlow.PreferredSize.Height + _listHost.Padding.Vertical,
                    1
                );
                _listHost.Visible = true;
            }
            else {
                _listChromeHeight = 0;
                _listHost.Visible = false;
            }

            int totalHeight = HeaderHeight + _listChromeHeight;
            if (Height != totalHeight) {
                Height = totalHeight;
            }

            LayoutChrome();
        }

        private sealed class DropdownItem {
            public DropdownItem(string displayText, object tag) {
                DisplayText = displayText;
                Tag = tag;
            }

            public string DisplayText { get; }
            public object Tag { get; }
        }
    }
}
