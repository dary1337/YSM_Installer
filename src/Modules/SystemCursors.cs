using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller {
    public static class SystemCursors {
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        // Native WinAPI cursor handles (IDC_* values)
        public static readonly IntPtr Native_Arrow = LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW
        public static readonly IntPtr Native_IBeam = LoadCursor(IntPtr.Zero, 32513); // IDC_IBEAM
        public static readonly IntPtr Native_Wait = LoadCursor(IntPtr.Zero, 32514); // IDC_WAIT
        public static readonly IntPtr Native_Cross = LoadCursor(IntPtr.Zero, 32515); // IDC_CROSS
        public static readonly IntPtr Native_UpArrow = LoadCursor(IntPtr.Zero, 32516); // IDC_UPARROW
        public static readonly IntPtr Native_SizeAll = LoadCursor(IntPtr.Zero, 32646); // IDC_SIZEALL
        public static readonly IntPtr Native_SizeNESW = LoadCursor(IntPtr.Zero, 32643); // IDC_SIZENESW
        public static readonly IntPtr Native_SizeNS = LoadCursor(IntPtr.Zero, 32645); // IDC_SIZENS
        public static readonly IntPtr Native_SizeNWSE = LoadCursor(IntPtr.Zero, 32642); // IDC_SIZENWSE
        public static readonly IntPtr Native_SizeWE = LoadCursor(IntPtr.Zero, 32644); // IDC_SIZEWE
        public static readonly IntPtr Native_No = LoadCursor(IntPtr.Zero, 32648); // IDC_NO
        public static readonly IntPtr Native_Hand = LoadCursor(IntPtr.Zero, 32649); // IDC_HAND
        public static readonly IntPtr Native_AppStarting = LoadCursor(IntPtr.Zero, 32650); // IDC_APPSTARTING
        public static readonly IntPtr Native_Help = LoadCursor(IntPtr.Zero, 32651); // IDC_HELP

        // Managed Cursor wrappers
        public static readonly Cursor Arrow = new Cursor(Native_Arrow);
        public static readonly Cursor IBeam = new Cursor(Native_IBeam);
        public static readonly Cursor Wait = new Cursor(Native_Wait);
        public static readonly Cursor Cross = new Cursor(Native_Cross);
        public static readonly Cursor UpArrow = new Cursor(Native_UpArrow);
        public static readonly Cursor SizeAll = new Cursor(Native_SizeAll);
        public static readonly Cursor SizeNESW = new Cursor(Native_SizeNESW);
        public static readonly Cursor SizeNS = new Cursor(Native_SizeNS);
        public static readonly Cursor SizeNWSE = new Cursor(Native_SizeNWSE);
        public static readonly Cursor SizeWE = new Cursor(Native_SizeWE);
        public static readonly Cursor No = new Cursor(Native_No);
        public static readonly Cursor Hand = new Cursor(Native_Hand);
        public static readonly Cursor AppStarting = new Cursor(Native_AppStarting);
        public static readonly Cursor Help = new Cursor(Native_Help);

        // Aliases
        public static readonly Cursor Default = Arrow;
        public static readonly Cursor Pointer = Hand;
    }
}
