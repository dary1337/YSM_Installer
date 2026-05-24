using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    static class Program {
        private static int _fatalErrorDialogShown;

        [STAThread]
        static void Main() {
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
            catch {
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
            catch {
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
            catch {
                return false;
            }
        }

        private static Exception ToException(object exceptionObject) {
            return exceptionObject as Exception
                ?? new Exception($"Unhandled non-Exception object: {exceptionObject}");
        }
    }
}
