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
            try {
                string trace = exception.StackTrace ?? string.Empty;
                if (trace.Length > 3500) {
                    trace = trace.Substring(0, 3500) + "\n…";
                }

                MessageBox.Show(
                    $"{exception.GetType().Name}: {exception.Message}\n\n{trace}\n\n(log file: {AppLogger.LogPath})",
                    "YSM Installer — UI error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            catch {
            }
        }

        private static void ShowCriticalErrorMessage(Exception exception) {
            try {
                MessageBox.Show(
                    $"A critical error occurred. Details were written to:\n{AppLogger.LogPath}\n\n{exception.Message}",
                    "YSM Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            catch {
            }
        }

        private static Exception ToException(object exceptionObject) {
            return exceptionObject as Exception
                ?? new Exception($"Unhandled non-Exception object: {exceptionObject}");
        }
    }
}
