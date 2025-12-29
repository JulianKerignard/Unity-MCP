using UnityEngine;

namespace McpUnity.Editor
{
    /// <summary>
    /// Utility class for conditional logging that respects MCP settings
    /// </summary>
    public static class McpDebug
    {
        /// <summary>
        /// Log info message (respects LogToConsole setting)
        /// </summary>
        public static void Log(string message)
        {
            if (McpSettings.Instance.LogToConsole)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning message (respects LogToConsole setting)
        /// </summary>
        public static void LogWarning(string message)
        {
            if (McpSettings.Instance.LogToConsole)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error message (always shown - errors are important)
        /// </summary>
        public static void LogError(string message)
        {
            // Errors are always logged regardless of setting
            Debug.LogError(message);
        }

        /// <summary>
        /// Log with format (respects LogToConsole setting)
        /// </summary>
        public static void LogFormat(string format, params object[] args)
        {
            if (McpSettings.Instance.LogToConsole)
            {
                Debug.LogFormat(format, args);
            }
        }
    }
}
