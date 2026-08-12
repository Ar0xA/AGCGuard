using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HamstuffAgcGuard.Audio.Interop;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard.Audio
{
    /// <summary>
    /// Thin wrapper around the Windows Core Audio (MMDevice) APIs: enumerating
    /// render/capture endpoints, deriving a stable USB VID/PID id for each one, and
    /// reading/writing the "disable all audio enhancements" endpoint property.
    /// </summary>
    internal sealed class AudioDeviceService
    {
        private static readonly Regex HardwareIdPattern =
            new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IPolicyConfig _policyConfig;
        private readonly NotificationClientSink _sink;

        /// <summary>Fired (on an arbitrary thread) whenever an audio endpoint is added, removed, or changes state.</summary>
        public event Action? DeviceListChanged
        {
            add => _sink.DeviceListChanged += value;
            remove => _sink.DeviceListChanged -= value;
        }

        public AudioDeviceService()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
            _sink = new NotificationClientSink();
            _enumerator.RegisterEndpointNotificationCallback(_sink);
        }

        public static string? ExtractHardwareId(string? instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                return null;
            }

            var match = HardwareIdPattern.Match(instanceId);
            if (!match.Success)
            {
                return null;
            }

            return $"VID_{match.Groups[1].Value.ToUpperInvariant()}&PID_{match.Groups[2].Value.ToUpperInvariant()}";
        }

        public List<AudioEndpointInfo> GetActiveEndpoints()
        {
            var results = new List<AudioEndpointInfo>();
            CollectEndpoints(EDataFlow.eRender, AudioFlow.Render, results);
            CollectEndpoints(EDataFlow.eCapture, AudioFlow.Capture, results);
            return results;
        }

        private void CollectEndpoints(EDataFlow dataFlow, AudioFlow flow, List<AudioEndpointInfo> results)
        {
            int hr = _enumerator.EnumAudioEndpoints(dataFlow, DeviceState.Active, out var collection);
            if (hr != 0 || collection == null)
            {
                Logger.Warn($"EnumAudioEndpoints failed (flow={flow}, hr=0x{hr:X8}).");
                return;
            }

            collection.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    collection.Item(i, out var device);
                    if (device == null)
                    {
                        continue;
                    }

                    var info = ReadEndpointInfo(device, flow);
                    if (info != null)
                    {
                        results.Add(info);
                    }
                }
                catch (Exception ex)
                {
                    // A device can legitimately disappear mid-enumeration (unplugged
                    // right as we're reading it) - just skip it and move on.
                    Logger.Warn($"Failed to read audio endpoint #{i} ({flow}): {ex.Message}");
                }
            }
        }

        private static AudioEndpointInfo? ReadEndpointInfo(IMMDevice device, AudioFlow flow)
        {
            device.GetId(out string endpointId);
            device.OpenPropertyStore(StorageAccessMode.STGM_READ, out var store);
            if (store == null)
            {
                return null;
            }

            string friendlyName = ReadString(store, PropertyKeys.FriendlyName) ?? endpointId;

            // DEVPKEY_Device_InstanceId read off the endpoint's own property store
            // just returns the endpoint's own software devnode id (SWD\MMDEVAPI\...),
            // never a real USB VID/PID - it's kept here only as extra debug context.
            string? instanceId = ReadString(store, PropertyKeys.DeviceInstanceId);

            string? hardwareId = FindAncestorHardwareId(endpointId);

            return new AudioEndpointInfo
            {
                EndpointId = endpointId,
                FriendlyName = friendlyName,
                Flow = flow,
                InstanceId = instanceId,
                HardwareId = hardwareId,
            };
        }

        /// <summary>
        /// Walks up the PnP device tree from the endpoint to find the real
        /// hardware devnode behind it (see CfgMgr32.GetAncestorDeviceIds), trying
        /// each ancestor in turn until one contains a USB VID/PID.
        /// </summary>
        private static string? FindAncestorHardwareId(string endpointId)
        {
            foreach (var ancestorDeviceId in CfgMgr32.GetAncestorDeviceIds(endpointId))
            {
                var hardwareId = ExtractHardwareId(ancestorDeviceId);
                if (hardwareId != null)
                {
                    return hardwareId;
                }
            }

            return null;
        }

        private static string? ReadString(IPropertyStore store, PropertyKey key)
        {
            var localKey = key;
            int hr = store.GetValue(ref localKey, out var value);
            if (hr != 0)
            {
                return null;
            }

            try
            {
                return value.GetValue() as string;
            }
            finally
            {
                value.Clear();
            }
        }

        // These two go through IPolicyConfig (bFxStore=true), not
        // IMMDevice::OpenPropertyStore, because PKEY_AudioEndpoint_Disable_SysFx
        // lives in the endpoint's "FxProperties" store, a different registry key
        // from the "Properties" store OpenPropertyStore exposes - and because a
        // non-administrator caller only gets read-only access via
        // OpenPropertyStore anyway (confirmed: SetValue+Commit returned S_OK
        // there but a fresh read showed no change). IPolicyConfig is what the
        // Sound control panel itself uses, and works unelevated because the
        // actual registry write happens inside the Windows Audio service.

        public bool IsEnhancementsDisabled(string endpointId)
        {
            var key = PropertyKeys.DisableSysFx;
            var value = new PropVariant();
            int hr = _policyConfig.GetPropertyValue(endpointId, true, ref key, ref value);
            if (hr != 0)
            {
                Logger.Warn($"IPolicyConfig.GetPropertyValue failed for '{endpointId}' (hr=0x{hr:X8}).");
                return false;
            }

            try
            {
                return value.GetValue() is uint u && u != 0;
            }
            finally
            {
                value.Clear();
            }
        }

        public void SetEnhancementsDisabled(string endpointId, bool disabled)
        {
            var key = PropertyKeys.DisableSysFx;
            var value = PropVariant.FromUInt32(disabled ? 1u : 0u);
            int hr = _policyConfig.SetPropertyValue(endpointId, true, ref key, ref value);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            // Belt and braces: a success HRESULT is not, on its own, solid proof
            // the value actually stuck (this exact gap - OpenPropertyStore's
            // SetValue+Commit both returning S_OK while silently not
            // persisting - is what led us to IPolicyConfig in the first place).
            // Read it back so a caller gets a clear failure instead of a false
            // "it worked" if it still didn't.
            bool actual = IsEnhancementsDisabled(endpointId);
            if (actual != disabled)
            {
                throw new InvalidOperationException(
                    $"Set DisableSysFx={disabled} on '{endpointId}' via IPolicyConfig (hr=0x{hr:X8}), but " +
                    $"reading the property back afterwards shows {actual} - the change did not actually take effect.");
            }
        }
    }
}
