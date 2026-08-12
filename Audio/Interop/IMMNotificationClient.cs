using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    /// <summary>
    /// Managed sink for IMMNotificationClient. Windows' audio subsystem invokes this
    /// on an arbitrary worker thread whenever an endpoint is added/removed/changes
    /// state (which is exactly what happens right after a USB audio device is plugged
    /// in). We just bubble a single "something changed, go take a look" event -
    /// callers are responsible for hopping back to a safe thread before doing more work.
    /// </summary>
    internal sealed class NotificationClientSink : IMMNotificationClient
    {
        public event Action? DeviceListChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => DeviceListChanged?.Invoke();

        public void OnDeviceAdded(string deviceId) => DeviceListChanged?.Invoke();

        public void OnDeviceRemoved(string deviceId) => DeviceListChanged?.Invoke();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }
}
