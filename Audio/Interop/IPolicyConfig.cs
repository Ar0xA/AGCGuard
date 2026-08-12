using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    /// <summary>
    /// Undocumented (reverse-engineered) Windows COM interface implemented by the
    /// Windows Audio service (audiosrv), used internally by the Sound control
    /// panel. It is the only route to an audio endpoint's "FxProperties" store -
    /// where PKEY_AudioEndpoint_Disable_SysFx actually lives - as a standard,
    /// non-elevated user.
    ///
    /// IMMDevice::OpenPropertyStore only exposes the endpoint's "Properties"
    /// store, a DIFFERENT registry key from "FxProperties" despite using the same
    /// PROPERTYKEY, and per Microsoft's own docs a non-administrator caller only
    /// gets read-only access there anyway - which is why that route silently
    /// failed to persist (SetValue/Commit returned S_OK, but a fresh read showed
    /// no change). SetPropertyValue's bFxStore parameter (must be true here)
    /// targets the correct store, and the actual registry write happens inside
    /// audiosrv running as SYSTEM on the caller's behalf, so no elevation is
    /// needed - this is exactly what the Sound control panel itself uses.
    ///
    /// Vtable layout (CLSID/IID, 12 methods in this exact order) cross-verified
    /// against five independently-maintained open source implementations
    /// (SoundSwitch, AudioDeviceCmdlets, EarTrumpet, MartinGC94/AudioConfig,
    /// AkiyaDev/Toggle-Loudness-Equalization) and Microsoft's own leaked
    /// EffectsDiscovery test code. The IID below (F8679F50-...) has been stable
    /// since Windows 10 1607 (RS1) through current Windows 10/11 - only very
    /// early Windows 10 1507/1511 briefly used different IIDs.
    /// </summary>
    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClientComObject
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr ppFormat);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr ppFormat);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pEndpointFormat, IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pmftPeriod);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pMode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

        // bFxStore: false = the endpoint's normal "Properties" store (same one
        // IMMDevice::OpenPropertyStore exposes), true = "FxProperties" - the one
        // that actually backs the classic Enhancements tab / "Enhance audio"
        // toggle. Always pass true for PKEY_AudioEndpoint_Disable_SysFx.
        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PropertyKey key, ref PropVariant pv);

        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PropertyKey key, ref PropVariant pv);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.U4)] ERole role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bVisible);
    }
}
