using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard.Storage
{
    /// <summary>JSON-backed persistence for the list of monitored device hardware ids.</summary>
    internal sealed class DeviceStore
    {
        private readonly string _filePath;
        private readonly object _lock = new();
        private List<MonitoredDevice> _devices = new();

        public DeviceStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public IReadOnlyList<MonitoredDevice> Devices
        {
            get { lock (_lock) { return _devices.ToList(); } }
        }

        public void Add(MonitoredDevice device)
        {
            lock (_lock)
            {
                if (_devices.Any(d => string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                _devices.Add(device);
                Save();
            }
        }

        public void Remove(string id)
        {
            lock (_lock)
            {
                _devices.RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _devices = JsonSerializer.Deserialize<List<MonitoredDevice>>(json) ?? new List<MonitoredDevice>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load monitored device list from '{_filePath}'.", ex);
                _devices = new List<MonitoredDevice>();
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_devices, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save monitored device list to '{_filePath}'.", ex);
            }
        }
    }
}
