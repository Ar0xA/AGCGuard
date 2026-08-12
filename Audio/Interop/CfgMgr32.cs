using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard.Audio.Interop
{
    /// <summary>
    /// A Core Audio endpoint (IMMDevice) is itself a software device node
    /// ("SWD\MMDEVAPI\{endpoint-id}") - its own DEVPKEY_Device_InstanceId is just
    /// that same software id, never the underlying hardware's. To get the real USB
    /// VID/PID we have to walk up the PnP device tree via cfgmgr32 to find the
    /// actual hardware devnode (e.g. "USB\VID_xxxx&amp;PID_xxxx&amp;MI_00\...") behind it.
    /// </summary>
    internal static class CfgMgr32
    {
        private const int CrSuccess = 0;
        private const uint CmLocateDevnodeNormal = 0;
        private const int MaxDeviceIdLen = 200;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceId, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_IDW(uint dnDevInst, StringBuilder buffer, int bufferLen, uint ulFlags);

        /// <summary>
        /// Returns the device instance id of each ancestor of the given audio
        /// endpoint, closest parent first, up to <paramref name="maxDepth"/> levels.
        /// Empty if the endpoint's devnode can't be located (logged as a warning) -
        /// running out of ancestors before maxDepth is normal and not logged.
        /// </summary>
        public static IReadOnlyList<string> GetAncestorDeviceIds(string endpointId, int maxDepth = 8)
        {
            var results = new List<string>();
            var devNodeId = "SWD\\MMDEVAPI\\" + endpointId;

            int locateHr = CM_Locate_DevNodeW(out uint devInst, devNodeId, CmLocateDevnodeNormal);
            if (locateHr != CrSuccess)
            {
                Logger.Warn($"CM_Locate_DevNodeW failed for '{devNodeId}' (CONFIGRET=0x{locateHr:X}).");
                return results;
            }

            for (int depth = 0; depth < maxDepth; depth++)
            {
                int parentHr = CM_Get_Parent(out uint parentInst, devInst, 0);
                if (parentHr != CrSuccess)
                {
                    // Reached the root of the device tree (or a node cfgmgr32 won't
                    // walk past) - expected once we run out of ancestors, not an error.
                    break;
                }

                var buffer = new StringBuilder(MaxDeviceIdLen);
                int idHr = CM_Get_Device_IDW(parentInst, buffer, buffer.Capacity, 0);
                if (idHr != CrSuccess)
                {
                    Logger.Warn($"CM_Get_Device_IDW failed at ancestor depth {depth} (CONFIGRET=0x{idHr:X}).");
                    break;
                }

                results.Add(buffer.ToString());
                devInst = parentInst;
            }

            return results;
        }
    }
}
