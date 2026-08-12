using System;

namespace HamstuffAgcGuard.Audio.Interop
{
    internal enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2,
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    [Flags]
    internal enum DeviceState : uint
    {
        Active = 0x1,
        Disabled = 0x2,
        NotPresent = 0x4,
        Unplugged = 0x8,
        All = 0xF,
    }

    internal static class StorageAccessMode
    {
        public const int STGM_READ = 0x0;
        public const int STGM_WRITE = 0x1;
        public const int STGM_READWRITE = 0x2;
    }
}
