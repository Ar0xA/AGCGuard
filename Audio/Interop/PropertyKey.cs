using System;
using System.Runtime.InteropServices;

namespace HamstuffAgcGuard.Audio.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;

        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    /// <summary>
    /// Well-known PROPERTYKEY values used to read/write audio endpoint properties
    /// via IPropertyStore. These are stable, documented Windows SDK identifiers.
    /// </summary>
    internal static class PropertyKeys
    {
        /// PKEY_Device_FriendlyName - human readable endpoint name ("Speakers (USB Audio CODEC)").
        public static readonly PropertyKey FriendlyName =
            new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

        /// PKEY_Device_InstanceId - the underlying PnP device instance id of the device
        /// that exposes this endpoint, e.g. "USB\VID_0483&amp;PID_5740&amp;MI_00\...".
        /// This is what lets us recognize "this transceiver" regardless of which USB
        /// port it is plugged into.
        public static readonly PropertyKey DeviceInstanceId =
            new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);

        /// PKEY_AudioEndpoint_Disable_SysFx - the master "disable all audio effects"
        /// switch. This is exactly the property the classic Sound control panel's
        /// Enhancements tab ("Disable all enhancements" checkbox) and the modern
        /// Settings app's "Enhance audio" toggle read and write - so setting it is
        /// equivalent to a user turning off AGC/enhancements by hand.
        /// Property id 5 within the PKEY_AudioEndpoint_* family (0=FormFactor,
        /// 1=ControlPanelPageProvider, 2=Association, 3=PhysicalSpeakers, 4=GUID,
        /// 5=Disable_SysFx) - do not confuse with id 3 (PhysicalSpeakers).
        public static readonly PropertyKey DisableSysFx =
            new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 5);

        /// Not a documented Microsoft property - found empirically by diffing a
        /// render endpoint's registry Properties key before/after toggling
        /// "Spatial sound" off in Windows. Best guess, unconfirmed: likely the
        /// spatial audio on/off state (candidate for one of the unpublished
        /// PKEY_SpatialAudio_* / PKEY_RS2_SpatialAudioEndpoint_* keys). Lives in
        /// the endpoint's normal "Properties" store, not "FxProperties".
        public static readonly PropertyKey SpatialSoundCandidate =
            new(new Guid("1E94C58F-3E40-4DDB-B10C-A86D8B870A31"), 2);
    }
}
