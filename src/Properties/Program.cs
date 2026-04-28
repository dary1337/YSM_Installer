using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            AppLogger.Initialize();
            RegisterCriticalErrorHandlers();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void RegisterCriticalErrorHandlers() {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, args) => {
                AppLogger.Critical("Unhandled UI thread exception.", args.Exception);
                MessageBox.Show(
                    $"A critical error occurred. Details were written to:\n{AppLogger.LogPath}",
                    "YSM Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
                AppLogger.Critical("Unhandled application exception.", args.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (sender, args) => {
                AppLogger.Critical("Unhandled task exception.", args.Exception);
                args.SetObserved();
            };
        }
    }
}
