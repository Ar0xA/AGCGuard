using System;

namespace HamstuffAgcGuard.Storage
{
    internal sealed class MonitoredDevice
    {
        /// <summary>Stable USB hardware id, e.g. "VID_0483&amp;PID_5740".</summary>
        public string Id { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public DateTime DateAdded { get; set; }
    }
}
