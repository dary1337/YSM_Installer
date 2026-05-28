using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>
    /// Mirrors install progress to the Windows taskbar button via ITaskbarList3
    /// (Win7+). Visible regardless of whether the app is foreground — lets the user
    /// see install progress in the taskbar while the installer is minimised or behind
    /// other windows. No-ops gracefully on platforms without the COM interface.
    /// </summary>
    public static class TaskbarProgress {
        public enum State {
            None = 0,
            Indeterminate = 0x1,
            Normal = 0x2,
            Error = 0x4,
            Paused = 0x8,
        }

        private static readonly object Gate = new object();
        private static ITaskbarList3? _instance;
        private static bool _initFailed;

        public static void SetValue(IWin32Window owner, int percent) {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            ITaskbarList3? tb = TryGetInstance();
            if (tb == null) return;
            try {
                tb.SetProgressValue(owner.Handle, (ulong)percent, 100UL);
            }
            catch (COMException) {
                // Taskbar host can disappear (explorer restart); swallow and try next call.
            }
        }

        public static void SetState(IWin32Window owner, State state) {
            ITaskbarList3? tb = TryGetInstance();
            if (tb == null) return;
            try {
                tb.SetProgressState(owner.Handle, (uint)state);
            }
            catch (COMException) { }
        }

        public static void Clear(IWin32Window owner) {
            SetState(owner, State.None);
        }

        private static ITaskbarList3? TryGetInstance() {
            if (_initFailed) return null;
            if (_instance != null) return _instance;
            lock (Gate) {
                if (_instance != null) return _instance;
                if (_initFailed) return null;
                try {
                    Type t = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))
                        ?? throw new InvalidOperationException("TaskbarList CLSID not registered.");
                    var tb = (ITaskbarList3)Activator.CreateInstance(t)!;
                    tb.HrInit();
                    _instance = tb;
                    return tb;
                }
                catch (Exception) {
                    _initFailed = true;
                    return null;
                }
            }
        }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3 {
            // ITaskbarList — methods must appear in vtable order, even if unused.
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            // ITaskbarList3 — the methods we actually use.
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, uint state);
        }
    }
}
