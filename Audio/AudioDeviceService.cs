using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Debug tool: logs every registry property (Properties + FxProperties)
        /// for every currently active endpoint, for before/after diffing when
        /// hunting down which PROPERTYKEY controls a not-yet-identified setting.
        /// </summary>
        public void DumpAllActiveEndpointProperties()
        {
            foreach (var endpoint in GetActiveEndpoints())
            {
                RegistryPropertyDumper.DumpEndpoint(endpoint.EndpointId, endpoint.Flow, endpoint.FriendlyName);
            }
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

        // EXPERIMENTAL / best-effort: unlike SetEnhancementsDisabled, this is not
        // based on a documented property. PropertyKeys.SpatialSoundCandidate was
        // found by diffing a render endpoint's registry Properties key before and
        // after toggling "Spatial sound" off by hand - we don't know for certain
        // it's the actual control, or that our guess at its binary shape is
        // right. This never throws: if the live value's type doesn't match what
        // we've seen before, it logs full diagnostics and does nothing rather
        // than risk writing a malformed value through an undocumented API.
        // Returns true only when a write was just made and verified - false if
        // it was already in the desired state, or on any failure - so a caller
        // can tell "just changed" from "nothing to do" (e.g. to decide whether
        // to show a notification).
        public bool TryDisableSpatialSound(string endpointId, string friendlyName)
        {
            var key = PropertyKeys.SpatialSoundCandidate;
            var current = new PropVariant();
            int getHr = _policyConfig.GetPropertyValue(endpointId, false, ref key, ref current);
            if (getHr != 0)
            {
                Logger.Warn(
                    $"Spatial sound candidate property not readable for '{friendlyName}' (hr=0x{getHr:X8}). " +
                    "Skipping - this is a best-effort, unconfirmed feature.");
                return false;
            }

            try
            {
                var currentBytes = ExtractComparableBytes(current);
                if (currentBytes == null)
                {
                    Logger.Warn(
                        $"Spatial sound candidate property for '{friendlyName}' has an unexpected type " +
                        $"(vt={current.DataType}) - not attempting a write since the format isn't confirmed for " +
                        "this shape. This is a best-effort, unconfirmed feature.");
                    return false;
                }

                if (currentBytes.Length == 0)
                {
                    Logger.Warn($"Spatial sound candidate property for '{friendlyName}' is empty - skipping.");
                    return false;
                }

                var newBytes = (byte[])currentBytes.Clone();
                newBytes[^1] = 0x01;

                if (newBytes.SequenceEqual(currentBytes))
                {
                    // Already off - not a change, so report false (see method doc:
                    // the return value means "a change was just made", not
                    // "is currently off") so callers don't toast every sweep.
                    Logger.Info($"Spatial sound candidate property already matches the expected 'off' value for '{friendlyName}'.");
                    return false;
                }

                return WriteAndVerifySpatialCandidate(endpointId, friendlyName, key, current.DataType, newBytes);
            }
            finally
            {
                current.Clear();
            }
        }

        private bool WriteAndVerifySpatialCandidate(string endpointId, string friendlyName, PropertyKey key, short vartype, byte[] newBytes)
        {
            const short VT_BLOB = 65;
            var isBlobShaped = vartype == VT_BLOB || vartype == (short)(VarEnum.VT_VECTOR | VarEnum.VT_UI1);

            var writeValue = isBlobShaped
                ? PropVariant.FromBytes(vartype, newBytes)
                : new PropVariant { DataType = vartype, UShortValue = BitConverter.ToUInt16(newBytes, 0) };

            try
            {
                int setHr = _policyConfig.SetPropertyValue(endpointId, false, ref key, ref writeValue);
                if (setHr != 0)
                {
                    Logger.Warn($"Failed to write spatial sound candidate property for '{friendlyName}' (hr=0x{setHr:X8}).");
                    return false;
                }
            }
            finally
            {
                writeValue.FreeBlob();
            }

            var verifyKey = key;
            var verify = new PropVariant();
            int getHr = _policyConfig.GetPropertyValue(endpointId, false, ref verifyKey, ref verify);
            try
            {
                if (getHr != 0)
                {
                    Logger.Warn($"Could not read back spatial sound candidate property for '{friendlyName}' after writing it (hr=0x{getHr:X8}).");
                    return false;
                }

                var verifyBytes = ExtractComparableBytes(verify);
                bool matches = verifyBytes != null && verifyBytes.SequenceEqual(newBytes);
                if (matches)
                {
                    Logger.Info($"Disabled spatial sound on '{friendlyName}' (experimental, unconfirmed property).");
                }
                else
                {
                    Logger.Warn(
                        $"Wrote spatial sound candidate property for '{friendlyName}' but the read-back doesn't " +
                        "match what was written - it may not have taken effect.");
                }

                return matches;
            }
            finally
            {
                verify.Clear();
            }
        }

        private static byte[]? ExtractComparableBytes(PropVariant value)
        {
            var blob = value.GetBytesIfBlob();
            if (blob != null)
            {
                return blob;
            }

            return (VarEnum)value.DataType switch
            {
                VarEnum.VT_I2 or VarEnum.VT_UI2 => BitConverter.GetBytes(value.UShortValue),
                _ => null,
            };
        }

        // Separate, older Windows mechanism from the SysFx/spatial-sound stuff
        // above: a USB Audio Class device can expose hardware AGC as a
        // documented DeviceTopology "part" of subtype KSNODETYPE_AGC, reachable
        // via the equally-documented IAudioAutoGainControl interface. Unlike the
        // spatial sound guess, this uses real Microsoft-documented interfaces
        // throughout - the only real uncertainty is whether this particular
        // device's driver actually exposes such a node at all (most don't) and
        // whether it corresponds to whatever Windows 11's Settings app shows.
        // Always logs every topology part it walks past, so a "nothing found"
        // result is diagnosable rather than a silent no-op.
        public bool TryDisableHardwareAgc(string endpointId, string friendlyName)
        {
            try
            {
                _enumerator.GetDevice(endpointId, out var device);
                if (device == null)
                {
                    Logger.Warn($"Could not re-open endpoint '{friendlyName}' to walk its device topology.");
                    return false;
                }

                var agcPart = FindAgcPart(device, friendlyName);
                if (agcPart == null)
                {
                    return false;
                }

                return DisableAgcOnPart(agcPart, friendlyName);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error walking device topology for '{friendlyName}'.", ex);
                return false;
            }
        }

        private static IPart? FindAgcPart(IMMDevice device, string friendlyName)
        {
            var endpointTopology = ActivateOn<IDeviceTopology>(device, DeviceTopologyGuids.IID_IDeviceTopology);
            if (endpointTopology == null)
            {
                Logger.Info($"'{friendlyName}': could not get its DeviceTopology - no hardware AGC node to try.");
                return null;
            }

            // An endpoint's own topology always has exactly one connector,
            // which links across to a connector in the underlying hardware
            // adapter's topology - that's where any AGC subunit actually lives.
            int hr = endpointTopology.GetConnector(0, out var endpointConnector);
            if (hr != 0 || endpointConnector == null)
            {
                Logger.Info($"'{friendlyName}': no connector on its endpoint topology - no hardware AGC node to try.");
                return null;
            }

            hr = endpointConnector.GetConnectedTo(out var hardwareConnector);
            if (hr != 0 || hardwareConnector == null)
            {
                Logger.Info($"'{friendlyName}': endpoint connector isn't connected to a hardware adapter - no hardware AGC node to try.");
                return null;
            }

            if (hardwareConnector is not IPart hardwareConnectorPart)
            {
                Logger.Info($"'{friendlyName}': hardware connector doesn't support IPart - no hardware AGC node to try.");
                return null;
            }

            hr = hardwareConnectorPart.GetTopologyObject(out var hardwareTopology);
            if (hr != 0 || hardwareTopology == null)
            {
                Logger.Info($"'{friendlyName}': could not get the hardware adapter's topology - no hardware AGC node to try.");
                return null;
            }

            hr = hardwareTopology.GetSubunitCount(out uint subunitCount);
            if (hr != 0)
            {
                Logger.Info($"'{friendlyName}': could not enumerate hardware adapter subunits - no hardware AGC node to try.");
                return null;
            }

            IPart? agcPart = null;
            for (uint i = 0; i < subunitCount; i++)
            {
                int subunitHr = hardwareTopology.GetSubunit(i, out var subunitObj);
                if (subunitHr != 0 || subunitObj is not IPart subunitPart)
                {
                    continue;
                }

                subunitPart.GetSubType(out Guid subType);
                var nameHr = subunitPart.GetName(out string name);
                var displayName = nameHr == 0 ? name : "(unnamed)";
                Logger.Info($"'{friendlyName}' topology subunit #{i}: '{displayName}' (subtype {subType}).");

                if (subType == DeviceTopologyGuids.KSNODETYPE_AGC)
                {
                    agcPart = subunitPart;
                }
            }

            if (agcPart == null)
            {
                Logger.Info($"'{friendlyName}': no KSNODETYPE_AGC subunit found in its hardware topology - this device doesn't expose hardware AGC this way.");
            }

            return agcPart;
        }

        private static bool DisableAgcOnPart(IPart agcPart, string friendlyName)
        {
            var agcIid = DeviceTopologyGuids.IID_IAudioAutoGainControl;
            int hr = agcPart.Activate(DeviceTopologyGuids.CLSCTX_ALL, ref agcIid, out var agcObj);
            if (hr != 0 || agcObj is not IAudioAutoGainControl agc)
            {
                Logger.Warn($"'{friendlyName}': found an AGC node but could not activate IAudioAutoGainControl on it (hr=0x{hr:X8}).");
                return false;
            }

            hr = agc.GetEnabled(out bool currentlyEnabled);
            if (hr != 0)
            {
                Logger.Warn($"'{friendlyName}': found IAudioAutoGainControl but could not read its current state (hr=0x{hr:X8}).");
                return false;
            }

            if (!currentlyEnabled)
            {
                Logger.Info($"'{friendlyName}': hardware AGC is already disabled.");
                return false;
            }

            hr = agc.SetEnabled(false);
            if (hr != 0)
            {
                Logger.Warn($"'{friendlyName}': failed to disable hardware AGC (hr=0x{hr:X8}).");
                return false;
            }

            hr = agc.GetEnabled(out bool verifyEnabled);
            if (hr != 0 || verifyEnabled)
            {
                Logger.Warn($"'{friendlyName}': set hardware AGC disabled, but reading it back afterwards shows enabled={verifyEnabled} (hr=0x{hr:X8}) - it may not have taken effect.");
                return false;
            }

            Logger.Info($"Disabled hardware AGC on '{friendlyName}' via IAudioAutoGainControl.");
            return true;
        }

        private static T? ActivateOn<T>(IMMDevice device, Guid iid) where T : class
        {
            var localIid = iid;
            int hr = device.Activate(ref localIid, (int)DeviceTopologyGuids.CLSCTX_ALL, IntPtr.Zero, out var obj);
            return hr == 0 ? obj as T : null;
        }
    }
}
