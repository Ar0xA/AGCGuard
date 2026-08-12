using System;
using System.Threading;
using System.Windows.Forms;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using var singleInstanceMutex = new Mutex(true, "Global\\HamstuffAgcGuard_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "Hamstuff AGC Guard is already running. Look for its icon in the system tray.",
                    "Hamstuff AGC Guard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (_, e) =>
                Logger.Error("Unhandled UI thread exception.", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Logger.Error("Unhandled exception.", e.ExceptionObject as Exception);

            Application.Run(new TrayApplicationContext());
        }
    }
}
