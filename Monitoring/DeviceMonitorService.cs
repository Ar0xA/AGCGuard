using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using HamstuffAgcGuard.Audio;
using HamstuffAgcGuard.Logging;
using HamstuffAgcGuard.Notifications;
using HamstuffAgcGuard.Storage;

namespace HamstuffAgcGuard.Monitoring
{
    /// <summary>
    /// Ties everything together: whenever the audio device list changes (or on
    /// demand), checks connected endpoints against the monitored device list and
    /// disables Windows audio enhancements/AGC on any match.
    /// </summary>
    internal sealed class DeviceMonitorService
    {
        private readonly AudioDeviceService _audio;
        private readonly DeviceStore _store;
        private readonly SettingsStore _settings;
        private readonly ToastNotifier _toast;
        private readonly SynchronizationContext _uiContext;
        private readonly object _sweepLock = new();
        private bool _started;

        public DeviceMonitorService(
            AudioDeviceService audio,
            DeviceStore store,
            SettingsStore settings,
            ToastNotifier toast,
            SynchronizationContext uiContext)
        {
            _audio = audio;
            _store = store;
            _settings = settings;
            _toast = toast;
            _uiContext = uiContext;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _audio.DeviceListChanged += OnDeviceListChangedFromAudioThread;
            Sweep(announce: false);
        }

        /// <summary>Re-check all monitored devices right now, silently (no toast).</summary>
        public void SweepNow() => Sweep(announce: false);

        private void OnDeviceListChangedFromAudioThread()
        {
            // IMMNotificationClient callbacks land on an arbitrary worker thread.
            // Hop to the UI thread before doing any further COM/UI work, both to
            // avoid re-entrancy problems in the audio subsystem and so we can safely
            // touch the tray icon for the toast notification.
            _uiContext.Post(_ =>
            {
                // Windows can take a moment to finish setting up a freshly plugged
                // in device's property store, so give it a beat before reading it.
                // (Fully qualified: System.Threading.Timer is also in scope here.)
                var settleTimer = new System.Windows.Forms.Timer { Interval = 700 };
                settleTimer.Tick += (_, _) =>
                {
                    settleTimer.Stop();
                    settleTimer.Dispose();
                    Sweep(announce: true);
                };
                settleTimer.Start();
            }, null);
        }

        private void Sweep(bool announce)
        {
            if (!_settings.Current.MonitoringEnabled)
            {
                return;
            }

            lock (_sweepLock)
            {
                var monitoredIds = new HashSet<string>(
                    _store.Devices.Select(d => d.Id),
                    StringComparer.OrdinalIgnoreCase);

                if (monitoredIds.Count == 0)
                {
                    return;
                }

                List<AudioEndpointInfo> endpoints;
                try
                {
                    endpoints = _audio.GetActiveEndpoints();
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to enumerate audio endpoints during sweep.", ex);
                    return;
                }

                foreach (var endpoint in endpoints)
                {
                    if (endpoint.HardwareId == null || !monitoredIds.Contains(endpoint.HardwareId))
                    {
                        continue;
                    }

                    ApplyToEndpoint(endpoint, announce);
                }
            }
        }

        private void ApplyToEndpoint(AudioEndpointInfo endpoint, bool announce)
        {
            var changes = new List<string>();

            try
            {
                if (!_audio.IsEnhancementsDisabled(endpoint.EndpointId))
                {
                    _audio.SetEnhancementsDisabled(endpoint.EndpointId, true);
                    Logger.Info($"Disabled audio enhancements on '{endpoint.FriendlyName}' ({endpoint.Flow}, {endpoint.HardwareId}).");
                    changes.Add("enhancements/AGC");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to disable enhancements on '{endpoint.FriendlyName}' ({endpoint.EndpointId}).", ex);
            }

            // Hardware AGC (KSNODETYPE_AGC via IAudioAutoGainControl) is a
            // capture-only concept - only meaningful for microphones. Also
            // best-effort/never-throwing; most devices won't have this node at
            // all, which is a normal, silently-logged outcome, not a failure.
            if (endpoint.Flow == AudioFlow.Capture)
            {
                try
                {
                    if (_audio.TryDisableHardwareAgc(endpoint.EndpointId, endpoint.FriendlyName))
                    {
                        changes.Add("hardware AGC");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unexpected error attempting hardware AGC disable on '{endpoint.FriendlyName}'.", ex);
                }
            }

            if (announce && changes.Count > 0)
            {
                _toast.Show(
                    "Hamstuff AGC Guard",
                    $"Disabled Windows {string.Join(" and ", changes)} on {endpoint.Flow.ToString().ToLowerInvariant()} device:\n{endpoint.FriendlyName}");
            }
        }
    }
}
