using System;
using System.IO;

namespace HamstuffAgcGuard.Logging
{
    /// <summary>
    /// Minimal best-effort file logger. Never throws - a logging failure must never
    /// take the whole tray app down.
    /// </summary>
    internal static class Logger
    {
        private static readonly object Lock = new();

        private static string _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Hamstuff", "AgcGuard", "logs");

        public static string LogDirectory => _logDirectory;

        public static void Initialize(string logDirectory)
        {
            _logDirectory = logDirectory;
            try
            {
                Directory.CreateDirectory(_logDirectory);
                PurgeOldLogs();
            }
            catch
            {
                // Nowhere useful to report this if logging itself can't start.
            }
        }

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message} :: {ex}");

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(_logDirectory);
                    var path = Path.Combine(_logDirectory, $"agcguard-{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // Best effort only.
                }
            }
        }

        private static void PurgeOldLogs()
        {
            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var file in Directory.GetFiles(_logDirectory, "agcguard-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore - not worth failing startup over a stale log file.
                }
            }
        }
    }
}
