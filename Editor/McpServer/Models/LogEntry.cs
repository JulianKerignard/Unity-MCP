using System;

namespace McpUnity.Models
{
    /// <summary>
    /// Console log entry for MCP log retrieval
    /// Uses proper C# properties following coding conventions
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// Timestamp when the log was captured
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Log message content
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Stack trace (if available)
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// Log type (Log, Warning, Error, Exception, Assert)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public LogEntry() { }

        /// <summary>
        /// Parameterized constructor for convenience
        /// </summary>
        public LogEntry(DateTime timestamp, string message, string stackTrace, string type)
        {
            Timestamp = timestamp;
            Message = message;
            StackTrace = stackTrace;
            Type = type;
        }

        /// <summary>
        /// Create from Unity log callback parameters
        /// </summary>
        public static LogEntry FromUnityLog(string condition, string stackTrace, UnityEngine.LogType type)
        {
            return new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = condition,
                StackTrace = stackTrace,
                Type = type.ToString()
            };
        }

        /// <summary>
        /// Convert to anonymous object for JSON serialization
        /// </summary>
        public object ToSerializable(bool includeStackTrace = false)
        {
            return new
            {
                timestamp = Timestamp.ToString("HH:mm:ss.fff"),
                type = Type,
                message = Message,
                stackTrace = includeStackTrace ? StackTrace : null
            };
        }
    }
}
