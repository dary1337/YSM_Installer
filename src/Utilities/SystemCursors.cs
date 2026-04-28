using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller
{
    public static class SystemCursors
    {
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int cursorName);

        private static readonly IntPtr NativeHand = LoadCursor(IntPtr.Zero, 32649); // IDC_HAND

        public static readonly Cursor Pointer = new Cursor(NativeHand);
    }
}
