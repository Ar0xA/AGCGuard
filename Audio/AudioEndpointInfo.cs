namespace HamstuffAgcGuard.Audio
{
    internal enum AudioFlow
    {
        Render,
        Capture,
    }

    internal sealed class AudioEndpointInfo
    {
        public string EndpointId { get; init; } = "";
        public string FriendlyName { get; init; } = "";
        public AudioFlow Flow { get; init; }
        public string? InstanceId { get; init; }

        /// <summary>"VID_xxxx&amp;PID_xxxx" if this is a USB device, otherwise null.</summary>
        public string? HardwareId { get; init; }
    }
}
