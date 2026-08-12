using System.Windows.Forms;

namespace HamstuffAgcGuard.Notifications
{
    /// <summary>
    /// Shows a small, non-intrusive taskbar popup near the tray icon. Uses the
    /// classic NotifyIcon balloon tip, which Windows 10/11 render with the modern
    /// toast styling automatically - no app packaging/manifest gymnastics required.
    /// </summary>
    internal sealed class ToastNotifier
    {
        private readonly NotifyIcon _notifyIcon;

        public ToastNotifier(NotifyIcon notifyIcon)
        {
            _notifyIcon = notifyIcon;
        }

        public void Show(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }
    }
}
