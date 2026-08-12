using System;
using System.IO;
using System.Text.Json;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard.Storage
{
    internal sealed class SettingsStore
    {
        private readonly string _filePath;

        public AppSettings Current { get; private set; } = new();

        public SettingsStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save settings to '{_filePath}'.", ex);
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load settings from '{_filePath}'.", ex);
                Current = new AppSettings();
            }
        }
    }
}
