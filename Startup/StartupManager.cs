using Microsoft.Win32;
using System.Windows.Forms;

namespace HamstuffAgcGuard.Startup
{
    /// <summary>Registers/unregisters the app in the current user's HKCU Run key.</summary>
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "HamstuffAgcGuard";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string;
        }

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
    }
}
