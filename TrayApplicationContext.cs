using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using HamstuffAgcGuard.Audio;
using HamstuffAgcGuard.Logging;
using HamstuffAgcGuard.Monitoring;
using HamstuffAgcGuard.Notifications;
using HamstuffAgcGuard.Startup;
using HamstuffAgcGuard.Storage;

namespace HamstuffAgcGuard
{
    /// <summary>
    /// The app has no main window - it lives entirely in the system tray. This
    /// context owns the NotifyIcon/menu and wires together the audio/monitoring/
    /// storage services.
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly AudioDeviceService _audio;
        private readonly DeviceStore _deviceStore;
        private readonly SettingsStore _settingsStore;
        private readonly DeviceMonitorService _monitor;

        private readonly ToolStripMenuItem _monitoringItem;
        private readonly ToolStripMenuItem _startupItem;

        public TrayApplicationContext()
        {
            // A NotifyIcon-only tray app (no Form) does not reliably trigger the
            // usual WindowsFormsSynchronizationContext auto-install that happens
            // when a Control's handle is created, so install it explicitly here -
            // DeviceMonitorService relies on SynchronizationContext.Current being a
            // real UI-thread context to marshal hot-plug callbacks back correctly.
            if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hamstuff", "AgcGuard");

            Logger.Initialize(Path.Combine(baseDir, "logs"));
            Logger.Info($"{AppInfo.DisplayNameWithVersion} starting.");

            _deviceStore = new DeviceStore(Path.Combine(baseDir, "devices.json"));
            _settingsStore = new SettingsStore(Path.Combine(baseDir, "settings.json"));
            _audio = new AudioDeviceService();

            _trayIcon = new NotifyIcon
            {
                Icon = UI.TrayIconFactory.CreateIcon(),
                Text = AppInfo.DisplayNameWithVersion,
                Visible = true,
            };

            var toast = new ToastNotifier(_trayIcon);

            _monitor = new DeviceMonitorService(
                _audio,
                _deviceStore,
                _settingsStore,
                toast,
                SynchronizationContext.Current ?? new SynchronizationContext());

            var menu = new ContextMenuStrip();

            var headerItem = new ToolStripMenuItem(AppInfo.DisplayNameWithVersion) { Enabled = false };

            _monitoringItem = new ToolStripMenuItem("Monitoring Enabled")
            {
                CheckOnClick = true,
                Checked = _settingsStore.Current.MonitoringEnabled,
            };
            _monitoringItem.Click += OnToggleMonitoring;

            var manageItem = new ToolStripMenuItem("Manage Devices...");
            manageItem.Click += (_, _) => ShowDeviceListForm();

            _startupItem = new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = StartupManager.IsEnabled(),
            };
            _startupItem.Click += OnToggleStartup;

            var openLogsItem = new ToolStripMenuItem("Open Log Folder");
            openLogsItem.Click += (_, _) => OpenLogFolder();

            var dumpPropertiesItem = new ToolStripMenuItem("Dump Audio Properties to Log (Debug)");
            dumpPropertiesItem.Click += (_, _) => DumpAudioProperties();

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(headerItem);
            menu.Items.Add(_monitoringItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(manageItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openLogsItem);
            menu.Items.Add(dumpPropertiesItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            menu.Opening += (_, _) => _startupItem.Checked = StartupManager.IsEnabled();

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowDeviceListForm();
                }
            };

            _monitor.Start();
            Logger.Info(
                $"Startup complete. Monitoring {_deviceStore.Devices.Count} device(s); " +
                $"monitoring enabled = {_settingsStore.Current.MonitoringEnabled}.");
        }

        private void OnToggleMonitoring(object? sender, EventArgs e)
        {
            _settingsStore.Current.MonitoringEnabled = _monitoringItem.Checked;
            _settingsStore.Save();
            Logger.Info($"Monitoring {(_monitoringItem.Checked ? "enabled" : "disabled")} from tray menu.");

            if (_monitoringItem.Checked)
            {
                _monitor.SweepNow();
            }
        }

        private void OnToggleStartup(object? sender, EventArgs e)
        {
            try
            {
                StartupManager.SetEnabled(_startupItem.Checked);
                Logger.Info($"Start-with-Windows {(_startupItem.Checked ? "enabled" : "disabled")}.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update the start-with-Windows registry setting.", ex);
                MessageBox.Show(
                    "Could not update the startup setting. See the log folder for details.",
                    "Hamstuff AGC Guard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                _startupItem.Checked = StartupManager.IsEnabled();
            }
        }

        private void ShowDeviceListForm()
        {
            using var form = new UI.DeviceListForm(_deviceStore, () => new UI.AddDeviceWizardForm(_audio, _deviceStore));
            form.DevicesChanged += () => _monitor.SweepNow();
            form.ShowDialog();
        }

        private static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(Logger.LogDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Logger.LogDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open log folder.", ex);
            }
        }

        private void DumpAudioProperties()
        {
            try
            {
                Logger.Info("Manual audio property dump requested from tray menu.");
                _audio.DumpAllActiveEndpointProperties();
                MessageBox.Show(
                    "Dumped every registry property for all currently connected audio devices to the log. " +
                    "To find which property controls a setting: dump once, change the setting in Windows, " +
                    "dump again, then compare the two dumps in the log.",
                    "Hamstuff AGC Guard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to dump audio properties.", ex);
            }
        }

        private void ExitApplication()
        {
            Logger.Info("Hamstuff AGC Guard exiting.");
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            ExitThread();
        }
    }
}
