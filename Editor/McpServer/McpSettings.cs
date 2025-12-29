using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace McpUnity.Editor
{
    /// <summary>
    /// Persistent settings for MCP Unity Server.
    /// Stores configuration in ProjectSettings/McpUnitySettings.json
    /// </summary>
    [Serializable]
    public class McpSettings
    {
        private const string SettingsPath = "ProjectSettings/McpUnitySettings.json";

        private static McpSettings _instance;
        public static McpSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        // Server Configuration
        [SerializeField] private int _port = 8090;
        [SerializeField] private string _host = "localhost";
        [SerializeField] private bool _allowRemoteConnections = false;
        [SerializeField] private int _requestTimeoutMs = 30000;
        [SerializeField] private bool _autoStartServer = false;
        [SerializeField] private bool _showNotifications = true;

        // Logging Configuration
        [SerializeField] private int _maxLogEntries = 500;
        [SerializeField] private bool _logToFile = false;
        [SerializeField] private string _logFilePath = "Logs/McpUnity.log";
        [SerializeField] private LogLevel _minimumLogLevel = LogLevel.Info;

        // Claude Configuration
        [SerializeField] private string _customServerPath = "";
        [SerializeField] private bool _useCustomServerPath = false;

        // Properties
        public int Port
        {
            get => _port;
            set
            {
                if (value < 1 || value > 65535)
                    throw new ArgumentOutOfRangeException(nameof(value), "Port must be between 1 and 65535");
                _port = value;
                Save();
            }
        }

        public string Host
        {
            get => _host;
            set
            {
                _host = value ?? "localhost";
                Save();
            }
        }

        public bool AllowRemoteConnections
        {
            get => _allowRemoteConnections;
            set
            {
                _allowRemoteConnections = value;
                _host = value ? "0.0.0.0" : "localhost";
                Save();
            }
        }

        public int RequestTimeoutMs
        {
            get => _requestTimeoutMs;
            set
            {
                _requestTimeoutMs = Mathf.Max(1000, value);
                Save();
            }
        }

        public bool AutoStartServer
        {
            get => _autoStartServer;
            set
            {
                _autoStartServer = value;
                Save();
            }
        }

        public bool ShowNotifications
        {
            get => _showNotifications;
            set
            {
                _showNotifications = value;
                Save();
            }
        }

        public int MaxLogEntries
        {
            get => _maxLogEntries;
            set
            {
                _maxLogEntries = Mathf.Max(100, value);
                Save();
            }
        }

        public bool LogToFile
        {
            get => _logToFile;
            set
            {
                _logToFile = value;
                Save();
            }
        }

        public string LogFilePath
        {
            get => _logFilePath;
            set
            {
                _logFilePath = value ?? "Logs/McpUnity.log";
                Save();
            }
        }

        public LogLevel MinimumLogLevel
        {
            get => _minimumLogLevel;
            set
            {
                _minimumLogLevel = value;
                Save();
            }
        }

        public string CustomServerPath
        {
            get => _customServerPath;
            set
            {
                _customServerPath = value ?? "";
                Save();
            }
        }

        public bool UseCustomServerPath
        {
            get => _useCustomServerPath;
            set
            {
                _useCustomServerPath = value;
                Save();
            }
        }

        /// <summary>
        /// Gets the effective server path based on settings
        /// </summary>
        public string EffectiveServerPath
        {
            get
            {
                if (_useCustomServerPath && !string.IsNullOrEmpty(_customServerPath))
                {
                    return _customServerPath;
                }
                return GetDefaultServerPath();
            }
        }

        /// <summary>
        /// Gets the default server path relative to the project
        /// </summary>
        public static string GetDefaultServerPath()
        {
            // Check multiple possible locations
            string[] possiblePaths = new string[]
            {
                Path.Combine(Application.dataPath, "Server/build/index.js"),
                Path.Combine(Application.dataPath, "Server/dist/index.js"),
                Path.Combine(Application.dataPath, "../Packages/com.mcp.unity/Server~/build/index.js"),
                Path.Combine(Application.dataPath, "McpUnity/Server/build/index.js")
            };

            foreach (var path in possiblePaths)
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Return first path as default even if it doesn't exist yet
            return Path.GetFullPath(possiblePaths[0]);
        }

        /// <summary>
        /// Gets the Claude Desktop configuration file path
        /// </summary>
        public static string GetClaudeConfigPath()
        {
#if UNITY_EDITOR_OSX
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Application Support/Claude/claude_desktop_config.json"
            );
#elif UNITY_EDITOR_WIN
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Claude/claude_desktop_config.json"
            );
#else
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config/Claude/claude_desktop_config.json"
            );
#endif
        }

        /// <summary>
        /// Load settings from disk
        /// </summary>
        private static McpSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonUtility.FromJson<McpSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP Unity] Failed to load settings: {e.Message}. Using defaults.");
            }

            return new McpSettings();
        }

        /// <summary>
        /// Save settings to disk
        /// </summary>
        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Unity] Failed to save settings: {e.Message}");
            }
        }

        /// <summary>
        /// Reset all settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            _port = 8090;
            _host = "localhost";
            _allowRemoteConnections = false;
            _requestTimeoutMs = 30000;
            _autoStartServer = false;
            _showNotifications = true;
            _maxLogEntries = 500;
            _logToFile = false;
            _logFilePath = "Logs/McpUnity.log";
            _minimumLogLevel = LogLevel.Info;
            _customServerPath = "";
            _useCustomServerPath = false;
            Save();
        }

        /// <summary>
        /// Generates the Claude Desktop configuration JSON
        /// </summary>
        public string GenerateClaudeConfig()
        {
            string serverPath = EffectiveServerPath.Replace("\\", "/");
            string escapedPath = serverPath.Replace("\"", "\\\"");

            return $@"{{
  ""mcpServers"": {{
    ""mcp-unity"": {{
      ""command"": ""node"",
      ""args"": [""{escapedPath}""],
      ""env"": {{
        ""UNITY_PORT"": ""{_port}"",
        ""UNITY_HOST"": ""{_host}""
      }}
    }}
  }}
}}";
        }
    }

    /// <summary>
    /// Log level enumeration
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}
