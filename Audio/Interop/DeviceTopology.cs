using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    // devicetopology.h defines its own DataFlow (In/Out), distinct from
    // mmdeviceapi.h's EDataFlow (eRender/eCapture/eAll) used elsewhere in this
    // project - don't mix them up.
    internal enum PartDataFlow
    {
        In = 0,
        Out = 1,
    }

    internal enum PartType
    {
        Connector = 0,
        Subunit = 1,
    }

    internal enum ConnectorType
    {
        Unknown_Connector = 0,
        Physical_Internal = 1,
        Physical_External = 2,
        Software_IO = 3,
        Software_Fixed = 4,
        Network = 5,
    }

    internal static class DeviceTopologyGuids
    {
        public static readonly Guid IID_IDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
        public static readonly Guid IID_IAudioAutoGainControl = new("85401FD4-6DE4-4B9D-9869-2D6753A82F3C");

        /// KSNODETYPE_AGC - identifies a hardware/driver Automatic Gain Control
        /// node in a device's topology, if the driver exposes one.
        public static readonly Guid KSNODETYPE_AGC = new("E88C9BA0-C557-11D0-8A2B-00A0C9255AC1");

        public const uint CLSCTX_ALL = 0x17;
    }

    [ComImport]
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceTopology
    {
        int GetConnectorCount(out uint count);
        int GetConnector(uint index, [MarshalAs(UnmanagedType.Interface)] out IConnector? connector);
        int GetSubunitCount(out uint count);
        int GetSubunit(uint index, [MarshalAs(UnmanagedType.IUnknown)] out object? subunit);
        int GetPartById(uint id, [MarshalAs(UnmanagedType.Interface)] out IPart? part);
        int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
    }

    /// <summary>
    /// Does NOT derive from IPart despite the conceptual relationship - it's a
    /// separate interface on the same COM object, reached via QueryInterface
    /// (i.e. a plain C# cast to IPart), not vtable inheritance.
    /// </summary>
    [ComImport]
    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IConnector
    {
        int GetConnectorType(out ConnectorType type);
        int GetDataFlow(out PartDataFlow flow);
        int ConnectTo([MarshalAs(UnmanagedType.Interface)] IConnector connectTo);
        int Disconnect();
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        int GetConnectedTo([MarshalAs(UnmanagedType.Interface)] out IConnector? connectedTo);
        int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPart
    {
        int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int GetLocalId(out uint id);
        int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetPartType(out PartType partType);
        int GetSubType(out Guid subType);
        int GetControlInterfaceCount(out uint count);
        int GetControlInterface(uint index, out IntPtr controlInterface);
        int EnumPartsIncoming(out IntPtr parts);
        int EnumPartsOutgoing(out IntPtr parts);
        int GetTopologyObject([MarshalAs(UnmanagedType.Interface)] out IDeviceTopology? topologyObject);
        int Activate(uint dwClsContext, ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object? interfacePointer);
        int RegisterControlChangeCallback(ref Guid refiid, IntPtr notify);
        int UnregisterControlChangeCallback(IntPtr notify);
    }

    /// <summary>
    /// Documented, simple boolean control for a KSNODETYPE_AGC part - obtained
    /// via IPart::Activate, unlike everything else in this project's DisableSysFx
    /// / spatial sound handling which goes through undocumented/reverse-engineered
    /// mechanisms. Activate only succeeds if the part actually supports it.
    /// </summary>
    [ComImport]
    [Guid("85401FD4-6DE4-4B9D-9869-2D6753A82F3C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioAutoGainControl
    {
        int GetEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        int SetEnabled([MarshalAs(UnmanagedType.Bool)] bool enabled);
    }
}
