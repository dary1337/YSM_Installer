using System.Drawing;
using System.Windows.Forms;
using YSMInstaller;

namespace YSMInstaller.Controls.Ui {
    internal static class UiChrome {
        public static void ApplyDialogChrome(Form form) {
            form.BackColor = Theme.Background;
            form.ForeColor = Theme.TextPrimary;
            form.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Label CreateHeadingLabel(string text) {
            return new Label {
                AutoSize = true,
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(0, 0, 0, 8),
                Text = text,
            };
        }

        public static Label CreateMutedLabel(string text) {
            return new Label {
                AutoSize = true,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(0, 8, 0, 16),
                Text = text,
            };
        }

        public static RoundedButton CreateDialogButton(string text) {
            return new RoundedButton(14) {
                AutoSize = true,
                BackColor = Theme.ButtonBackground,
                ForeColor = Theme.ButtonForeground,
                Margin = Padding.Empty,
                Text = text,
            };
        }
    }
}
