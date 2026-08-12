using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using HamstuffAgcGuard.Logging;

namespace HamstuffAgcGuard.Audio
{
    /// <summary>
    /// Debug/diagnostic helper: dumps every registry value under an audio
    /// endpoint's "Properties" and "FxProperties" keys to the log. Read-only, no
    /// special privileges needed - lets a before/after diff (toggle some Windows
    /// audio setting, dump, toggle it, dump again) reveal exactly which
    /// PROPERTYKEY controls a setting we haven't identified yet, instead of
    /// guessing at undocumented GUIDs.
    /// </summary>
    internal static class RegistryPropertyDumper
    {
        private static readonly Regex TrailingGuidPattern = new(@"\{[0-9A-Fa-f-]+\}$", RegexOptions.Compiled);

        public static void DumpEndpoint(string endpointId, AudioFlow flow, string friendlyName)
        {
            var match = TrailingGuidPattern.Match(endpointId);
            if (!match.Success)
            {
                Logger.Warn($"Could not parse a registry GUID out of endpoint id '{endpointId}'.");
                return;
            }

            var guid = match.Value;
            var flowFolder = flow == AudioFlow.Render ? "Render" : "Capture";
            var basePath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{flowFolder}\{guid}";

            Logger.Info($"--- Registry property dump: '{friendlyName}' ({flow}, {guid}) ---");
            DumpKey(basePath + @"\Properties");
            DumpKey(basePath + @"\FxProperties");
            Logger.Info($"--- End dump: '{friendlyName}' ---");
        }

        private static void DumpKey(string path)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null)
                {
                    Logger.Info($"{path}: (key not found)");
                    return;
                }

                var names = key.GetValueNames();
                if (names.Length == 0)
                {
                    Logger.Info($"{path}: (no values)");
                    return;
                }

                foreach (var name in names)
                {
                    var value = key.GetValue(name);
                    var kind = key.GetValueKind(name);
                    Logger.Info($"{path}\\{name} = {FormatValue(value)} ({kind})");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to read registry key '{path}': {ex.Message}");
            }
        }

        private static string FormatValue(object? value)
        {
            if (value is byte[] bytes)
            {
                return BitConverter.ToString(bytes).Replace("-", " ");
            }

            return value?.ToString() ?? "(null)";
        }
    }
}
