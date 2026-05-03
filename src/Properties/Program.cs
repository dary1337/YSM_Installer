using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller
{
    static class Program
    {
        private static int _fatalErrorDialogShown;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AppLogger.Initialize();
            RegisterCriticalErrorHandlers();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            catch (Exception exception)
            {
                AppLogger.Critical("Unhandled application startup exception.", exception);
                ShowFatalError(exception);
                Environment.ExitCode = 1;
            }
        }

        private static void RegisterCriticalErrorHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, args) =>
            {
                AppLogger.Critical("Unhandled UI thread exception.", args.Exception);
                ShowFatalError(args.Exception);
                Application.Exit();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Exception exception = ToException(args.ExceptionObject);
                AppLogger.Critical("Unhandled application exception.", exception);
                ShowFatalError(exception);
                Environment.Exit(1);
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                AppLogger.Critical("Unhandled task exception.", args.Exception);
                ShowRecoverableCriticalError(args.Exception);
                args.SetObserved();
            };
        }

        private static void ShowFatalError(Exception exception)
        {
            if (Interlocked.Exchange(ref _fatalErrorDialogShown, 1) == 1)
            {
                return;
            }

            ShowCriticalErrorMessage(exception);
        }

        private static void ShowRecoverableCriticalError(Exception exception)
        {
            ShowCriticalErrorMessage(exception);
        }

        private static void ShowCriticalErrorMessage(Exception exception)
        {
            try
            {
                MessageBox.Show(
                    $"A critical error occurred. Details were written to:\n{AppLogger.LogPath}\n\n{exception.Message}",
                    "YSM Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            catch
            {
                // At process-failure time there is nowhere safer to report this than the log file.
            }
        }

        private static Exception ToException(object exceptionObject)
        {
            return exceptionObject as Exception
                ?? new Exception($"Unhandled non-Exception object: {exceptionObject}");
        }
    }
}
