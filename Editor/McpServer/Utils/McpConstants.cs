using System;

namespace McpUnity.Utils
{
    /// <summary>
    /// Centralized constants for MCP Unity Server
    /// Eliminates magic numbers and provides single source of truth
    /// </summary>
    public static class McpConstants
    {
        #region Server Configuration

        /// <summary>
        /// Default WebSocket server port
        /// </summary>
        public const int DefaultPort = 8090;

        /// <summary>
        /// EditorPrefs key for server port setting
        /// </summary>
        public const string ServerPrefsKey = "McpUnity_ServerPort";

        /// <summary>
        /// EditorPrefs key for auto-start setting
        /// </summary>
        public const string AutoStartPrefsKey = "McpUnity_AutoStart";

        #endregion

        #region Processing Limits

        /// <summary>
        /// Number of frames to wait after domain reload before starting server
        /// </summary>
        public const int StartupFrameDelay = 10;

        /// <summary>
        /// Maximum messages to process per editor frame (prevents blocking)
        /// </summary>
        public const int MaxMessagesPerFrame = 10;

        /// <summary>
        /// Maximum console log entries to keep in buffer
        /// </summary>
        public const int MaxLogEntries = 100;

        #endregion

        #region Asset & Screenshot Limits

        /// <summary>
        /// Maximum screenshot dimension (width or height) in pixels
        /// </summary>
        public const int MaxScreenshotDimension = 4096;

        /// <summary>
        /// Maximum number of assets to return in search results
        /// </summary>
        public const int MaxAssetSearchResults = 200;

        /// <summary>
        /// Default screenshot width in pixels
        /// </summary>
        public const int DefaultScreenshotWidth = 1920;

        /// <summary>
        /// Default screenshot height in pixels
        /// </summary>
        public const int DefaultScreenshotHeight = 1080;

        #endregion

        #region Memory Cache

        /// <summary>
        /// Path to MCP cache directory (relative to project)
        /// </summary>
        public const string CacheDirectory = "Assets/.mcp-cache";

        /// <summary>
        /// Cache file name
        /// </summary>
        public const string CacheFileName = "memory.json";

        /// <summary>
        /// Maximum age in minutes before cache is considered stale
        /// </summary>
        public const int CacheMaxAgeMinutes = 5;

        /// <summary>
        /// Maximum recent operations to keep in cache
        /// </summary>
        public const int MaxRecentOperations = 20;

        #endregion

        #region Paths & Prefixes

        /// <summary>
        /// Required prefix for all file paths (security)
        /// </summary>
        public const string AssetsPathPrefix = "Assets/";

        /// <summary>
        /// Default screenshots directory
        /// </summary>
        public const string DefaultScreenshotsPath = "Assets/Screenshots/";

        #endregion
    }
}
