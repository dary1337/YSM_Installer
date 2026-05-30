using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    static class Program {
        private static int _fatalErrorDialogShown;
        // Held for the process lifetime so the single-instance lock stays owned until exit.
        private static Mutex? instanceMutex;
        // Local\ scopes the lock to the current login session (per-user), so it doesn't block a
        // second user on the same machine or another RDP session.
        private const string InstanceMutexName = @"Local\YSMInstaller.SingleInstance";

        [STAThread]
        static void Main() {
            // Process-wide HTTP defaults, set before any HttpClient is created. The stock 4.7.2
            // protocol set still includes TLS 1.0/1.1 on older registry configs — GitHub rejects
            // both, so force a 1.2 floor. DefaultConnectionLimit caps concurrent connections per
            // host at 2 by default, which throttles parallel HEAD probes on the release CDN.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 16;

            // A second copy would race the first over the shared WARNO mod folder and Config.ini
            // (the ActivatedMods read-modify-write isn't cross-process safe), so hand off to the
            // already-running window instead of running concurrently. Checked before anything else
            // so the second instance doesn't touch the shared log or init the UI.
            instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
            if (!createdNew) {
                ActivateExistingInstance();
                return;
            }
            // Owning instance only: dispose the lock on shutdown (covers Environment.Exit paths a
            // UI-thread finally would miss). Never registered for a second instance.
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => instanceMutex?.Dispose();

            AppLogger.Initialize();
            RegisterCriticalErrorHandlers();

            try {
                Application.EnableVisualStyles();
                // Native GDI text rendering for stock controls; our owner-drawn text uses AntiAliasGridFit
                // (smooth but grid-fitted, so not blurry) via SoftLabel and the Material controls.
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            catch (Exception exception) {
                AppLogger.Critical("Unhandled application startup exception.", exception);
                ShowFatalError(exception);
                Environment.ExitCode = 1;
            }
        }

        // Best-effort: if the running instance's window handle isn't ready yet, exit quietly rather
        // than spawning a duplicate.
        private static void ActivateExistingInstance() {
            try {
                using (Process current = Process.GetCurrentProcess()) {
                    foreach (Process other in Process.GetProcessesByName(current.ProcessName)) {
                        using (other) {
                            if (other.Id == current.Id) {
                                continue;
                            }
                            IntPtr handle = other.MainWindowHandle;
                            if (handle == IntPtr.Zero) {
                                continue;
                            }
                            if (NativeMethods.IsIconic(handle)) {
                                NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
                            }
                            NativeMethods.SetForegroundWindow(handle);
                            break;
                        }
                    }
                }
            }
            catch (Exception exception) {
                // Logger may not be initialized on the second-instance path (it then no-ops). Focusing
                // the other window is a nicety; failing it must not crash the exit.
                AppLogger.Critical("Could not focus the existing instance.", exception);
            }
        }

        private static void RegisterCriticalErrorHandlers() {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, args) => {
                AppLogger.Critical("Unhandled UI thread exception.", args.Exception);
                ShowThreadExceptionDialog(args.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
                Exception exception = ToException(args.ExceptionObject);
                AppLogger.Critical("Unhandled application exception.", exception);
                ShowFatalError(exception);
                Environment.Exit(1);
            };

            TaskScheduler.UnobservedTaskException += (sender, args) => {
                AppLogger.Critical("Unhandled task exception.", args.Exception);
                ShowRecoverableCriticalError(args.Exception);
                args.SetObserved();
            };
        }

        private static void ShowFatalError(Exception exception) {
            if (Interlocked.Exchange(ref _fatalErrorDialogShown, 1) == 1) {
                return;
            }

            ShowCriticalErrorMessage(exception);
        }

        private static void ShowRecoverableCriticalError(Exception exception) {
            ShowCriticalErrorMessage(exception);
        }

        private static void ShowThreadExceptionDialog(Exception exception) {
            string trace = exception.StackTrace ?? string.Empty;
            if (trace.Length > 3500) {
                trace = trace.Substring(0, 3500) + "\n…";
            }
            string body = $"{exception.GetType().Name}: {exception.Message}\n\n{trace}\n\n(log file: {AppLogger.LogPath})";

            if (TryShowMaterialError("UI error", body)) {
                return;
            }

            try {
                MessageBox.Show(body, "YSM Installer — UI error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception fallbackException) {
                AppLogger.Critical("MessageBox fallback for UI error also failed.", fallbackException);
            }
        }

        private static void ShowCriticalErrorMessage(Exception exception) {
            string body = $"A critical error occurred. Details were written to:\n{AppLogger.LogPath}\n\n{exception.Message}";

            if (TryShowMaterialError("Critical error", body)) {
                return;
            }

            try {
                MessageBox.Show(body, "YSM Installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception fallbackException) {
                AppLogger.Critical("MessageBox fallback for critical error also failed.", fallbackException);
            }
        }

        // Material first, MessageBox as the unconditional fallback. Returns false if any prerequisite
        // is missing (no open form, disposed form) or if the dialog itself blew up — caller falls
        // through to MessageBox in that case. We must never let an error in the error dialog kill
        // the error reporting path.
        private static bool TryShowMaterialError(string title, string body) {
            try {
                Form? owner = null;
                foreach (Form form in Application.OpenForms) {
                    if (!form.IsDisposed && form.IsHandleCreated) {
                        owner = form;
                        break;
                    }
                }
                if (owner == null) {
                    return false;
                }

                if (owner.InvokeRequired) {
                    owner.Invoke((Action)(() => UserMessages.ShowError(owner, title, body)));
                }
                else {
                    UserMessages.ShowError(owner, title, body);
                }
                return true;
            }
            catch (Exception materialException) {
                AppLogger.Critical("Material error dialog failed; falling back to MessageBox.", materialException);
                return false;
            }
        }

        private static Exception ToException(object exceptionObject) {
            return exceptionObject as Exception
                ?? new Exception($"Unhandled non-Exception object: {exceptionObject}");
        }

        private static class NativeMethods {
            public const int SwRestore = 9;

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);
        }
    }
}
