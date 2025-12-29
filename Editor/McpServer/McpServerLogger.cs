using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace McpUnity.Editor
{
    /// <summary>
    /// Centralized logging system for MCP Unity Server
    /// </summary>
    public class McpServerLogger
    {
        private static McpServerLogger _instance;
        public static McpServerLogger Instance => _instance ??= new McpServerLogger();

        private readonly List<LogEntry> _logs = new List<LogEntry>();
        private readonly object _lock = new object();

        public event Action<LogEntry> OnLogAdded;

        /// <summary>
        /// All log entries
        /// </summary>
        public IReadOnlyList<LogEntry> Logs
        {
            get
            {
                lock (_lock)
                {
                    return _logs.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Log count
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _logs.Count;
                }
            }
        }

        /// <summary>
        /// Add a debug log entry
        /// </summary>
        public void Debug(string message, string context = null)
        {
            AddLog(LogLevel.Debug, message, context);
        }

        /// <summary>
        /// Add an info log entry
        /// </summary>
        public void Info(string message, string context = null)
        {
            AddLog(LogLevel.Info, message, context);
        }

        /// <summary>
        /// Add a warning log entry
        /// </summary>
        public void Warning(string message, string context = null)
        {
            AddLog(LogLevel.Warning, message, context);
        }

        /// <summary>
        /// Add an error log entry
        /// </summary>
        public void Error(string message, string context = null)
        {
            AddLog(LogLevel.Error, message, context);
        }

        /// <summary>
        /// Add an error log entry with exception
        /// </summary>
        public void Error(string message, Exception exception, string context = null)
        {
            string fullMessage = $"{message}: {exception.Message}";
            if (exception.StackTrace != null)
            {
                fullMessage += $"\n{exception.StackTrace}";
            }
            AddLog(LogLevel.Error, fullMessage, context);
        }

        private void AddLog(LogLevel level, string message, string context)
        {
            if (level < McpSettings.Instance.MinimumLogLevel)
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Context = context
            };

            lock (_lock)
            {
                _logs.Add(entry);

                // Trim old logs if exceeding max
                int maxEntries = McpSettings.Instance.MaxLogEntries;
                while (_logs.Count > maxEntries)
                {
                    _logs.RemoveAt(0);
                }
            }

            // Write to file if enabled
            if (McpSettings.Instance.LogToFile)
            {
                WriteToFile(entry);
            }

            // Also log to Unity console
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log($"[MCP] {message}");
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning($"[MCP] {message}");
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError($"[MCP] {message}");
                    break;
            }

            // Notify listeners
            OnLogAdded?.Invoke(entry);
        }

        private void WriteToFile(LogEntry entry)
        {
            try
            {
                string logPath = McpSettings.Instance.LogFilePath;
                string directory = Path.GetDirectoryName(logPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}\n";
                File.AppendAllText(logPath, line);
            }
            catch
            {
                // Silently fail file logging to avoid infinite loops
            }
        }

        /// <summary>
        /// Clear all logs
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }

        /// <summary>
        /// Export logs to string
        /// </summary>
        public string Export(LogLevel? minimumLevel = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MCP Unity Server Logs ===");
            sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            lock (_lock)
            {
                foreach (var entry in _logs)
                {
                    if (minimumLevel.HasValue && entry.Level < minimumLevel.Value)
                        continue;

                    sb.AppendLine(entry.ToString());
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get formatted logs for display
        /// </summary>
        public string GetFormattedLogs(LogLevel? minimumLevel = null)
        {
            var sb = new StringBuilder();

            lock (_lock)
            {
                foreach (var entry in _logs)
                {
                    if (minimumLevel.HasValue && entry.Level < minimumLevel.Value)
                        continue;

                    sb.AppendLine(entry.ToShortString());
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a single log entry
    /// </summary>
    public struct LogEntry
    {
        public DateTime Timestamp;
        public LogLevel Level;
        public string Message;
        public string Context;

        public override string ToString()
        {
            string contextStr = string.IsNullOrEmpty(Context) ? "" : $" [{Context}]";
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}]{contextStr} {Message}";
        }

        public string ToShortString()
        {
            string prefix = Level switch
            {
                LogLevel.Debug => "[D]",
                LogLevel.Info => "[I]",
                LogLevel.Warning => "[W]",
                LogLevel.Error => "[E]",
                _ => "[?]"
            };
            return $"[{Timestamp:HH:mm:ss}] {prefix} {Message}";
        }

        public Color GetColor()
        {
            return Level switch
            {
                LogLevel.Debug => new Color(0.6f, 0.6f, 0.6f),
                LogLevel.Info => Color.white,
                LogLevel.Warning => new Color(1f, 0.8f, 0.2f),
                LogLevel.Error => new Color(1f, 0.4f, 0.4f),
                _ => Color.white
            };
        }
    }
}
