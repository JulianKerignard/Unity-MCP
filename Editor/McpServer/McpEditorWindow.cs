using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using McpUnity.Server;

namespace McpUnity.Editor
{
    /// <summary>
    /// Main Editor Window for MCP Unity Server configuration and control.
    /// Accessible via Tools > MCP Unity > Server Window
    /// </summary>
    public class McpEditorWindow : EditorWindow
    {
        // UI State
        private Vector2 _logScrollPosition;
        private Vector2 _mainScrollPosition;
        private string _cachedLogs = "";
        private int _lastLogCount = 0;

        // Server State - synced with McpUnityServer
        private DateTime _serverStartTime;

        // Cached settings for UI
        private int _port;
        private bool _allowRemote;
        private int _requestTimeout;
        private bool _autoStart;
        private bool _showNotifications;
        private int _maxLogEntries;
        private bool _logToFile;
        private bool _logToConsole;
        private LogLevel _logLevel;
        private bool _useCustomPath;
        private string _customPath;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _statusLabelStyle;
        private GUIStyle _logAreaStyle;
        private GUIStyle _boxStyle;
        private bool _stylesInitialized = false;

        // Tab management
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "Server", "Claude Config", "Logs", "Settings" };

        [MenuItem("Tools/MCP Unity/Server Window", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<McpEditorWindow>("MCP Unity Server");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        [MenuItem("Tools/MCP Unity/Quick Start Server", priority = 101)]
        public static void QuickStartServer()
        {
            var window = GetWindow<McpEditorWindow>("MCP Unity Server");
            window.StartServer();
        }

        private void OnEnable()
        {
            LoadSettings();
            McpServerLogger.Instance.OnLogAdded += OnLogAdded;

            // Auto-start if configured (McpUnityServer handles this automatically)
            // UI just reflects the server state
        }

        private void OnDisable()
        {
            McpServerLogger.Instance.OnLogAdded -= OnLogAdded;
        }

        private void OnLogAdded(LogEntry entry)
        {
            Repaint();
        }

        private void LoadSettings()
        {
            var settings = McpSettings.Instance;
            _port = settings.Port;
            _allowRemote = settings.AllowRemoteConnections;
            _requestTimeout = settings.RequestTimeoutMs;
            _autoStart = settings.AutoStartServer;
            _showNotifications = settings.ShowNotifications;
            _maxLogEntries = settings.MaxLogEntries;
            _logToFile = settings.LogToFile;
            _logToConsole = settings.LogToConsole;
            _logLevel = settings.MinimumLogLevel;
            _useCustomPath = settings.UseCustomServerPath;
            _customPath = settings.CustomServerPath;
        }

        private void SaveSettings()
        {
            var settings = McpSettings.Instance;
            settings.Port = _port;
            settings.AllowRemoteConnections = _allowRemote;
            settings.RequestTimeoutMs = _requestTimeout;
            settings.AutoStartServer = _autoStart;
            settings.ShowNotifications = _showNotifications;
            settings.MaxLogEntries = _maxLogEntries;
            settings.LogToFile = _logToFile;
            settings.LogToConsole = _logToConsole;
            settings.MinimumLogLevel = _logLevel;
            settings.UseCustomServerPath = _useCustomPath;
            settings.CustomServerPath = _customPath;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _statusLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            _logAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                richText = true,
                wordWrap = true,
                fontSize = 11,
                font = Font.CreateDynamicFontFromOSFont("Consolas", 11)
            };

            _boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 5, 5)
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            _mainScrollPosition = EditorGUILayout.BeginScrollView(_mainScrollPosition);

            DrawHeader();

            EditorGUILayout.Space(5);

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);

            EditorGUILayout.Space(10);

            switch (_selectedTab)
            {
                case 0:
                    DrawServerTab();
                    break;
                case 1:
                    DrawClaudeConfigTab();
                    break;
                case 2:
                    DrawLogsTab();
                    break;
                case 3:
                    DrawSettingsTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("MCP Unity Server", _headerStyle);

            GUILayout.FlexibleSpace();

            // Status indicator
            DrawStatusIndicator();

            EditorGUILayout.EndHorizontal();

            // Separator
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void DrawStatusIndicator()
        {
            EditorGUILayout.BeginHorizontal();

            bool isRunning = McpUnityServer.IsRunning;
            var statusColor = isRunning ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
            var statusText = isRunning ? "RUNNING" : "STOPPED";

            var originalColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label("\u25CF", GUILayout.Width(15));
            GUI.color = originalColor;

            EditorGUILayout.LabelField(statusText, _statusLabelStyle, GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawServerTab()
        {
            bool isRunning = McpUnityServer.IsRunning;
            int connectedClients = McpUnityServer.ConnectedClientCount;

            // Server Status Section
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Server Status", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(120));
            EditorGUILayout.LabelField(isRunning ? "Running" : "Stopped");
            EditorGUILayout.EndHorizontal();

            if (isRunning)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connected Clients:", GUILayout.Width(120));
                EditorGUILayout.LabelField(connectedClients.ToString());
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Uptime:", GUILayout.Width(120));
                var uptime = DateTime.Now - _serverStartTime;
                EditorGUILayout.LabelField($"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Endpoint:", GUILayout.Width(120));
                string endpoint = $"ws://localhost:{McpUnityServer.Port}";
                EditorGUILayout.SelectableLabel(endpoint, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Server Configuration Section
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(isRunning);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Port:", GUILayout.Width(120));
            int newPort = EditorGUILayout.IntField(McpUnityServer.Port);
            if (newPort != McpUnityServer.Port)
            {
                McpUnityServer.Port = newPort;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Control Buttons
            EditorGUILayout.BeginHorizontal();

            if (isRunning)
            {
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("Stop Server", GUILayout.Height(35)))
                {
                    StopServer();
                }
                GUI.backgroundColor = Color.white;

                GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
                if (GUILayout.Button("Restart Server", GUILayout.Height(35)))
                {
                    RestartServer();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("Start Server", GUILayout.Height(35)))
                {
                    StartServer();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            // Auto-start toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            bool newAutoStart = EditorGUILayout.Toggle("Auto-start on Editor load", McpUnityServer.AutoStart);
            if (newAutoStart != McpUnityServer.AutoStart)
            {
                McpUnityServer.AutoStart = newAutoStart;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawClaudeConfigTab()
        {
            // Claude Desktop Configuration
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Claude Desktop Integration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Configure Claude Desktop to connect to this Unity MCP server. " +
                "Click 'Auto Configure' to update Claude's configuration file automatically.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // Server path
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Server Script:", GUILayout.Width(100));
            string serverPath = McpSettings.Instance.EffectiveServerPath;
            bool serverExists = File.Exists(serverPath);

            var originalColor = GUI.color;
            GUI.color = serverExists ? Color.white : new Color(1f, 0.5f, 0.5f);
            EditorGUILayout.SelectableLabel(serverPath, GUILayout.Height(18));
            GUI.color = originalColor;
            EditorGUILayout.EndHorizontal();

            if (!serverExists)
            {
                EditorGUILayout.HelpBox("Server script not found. Please build the server first.", MessageType.Error);
            }

            // Claude config path
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Claude Config:", GUILayout.Width(100));
            string configPath = McpSettings.GetClaudeConfigPath();
            bool configExists = File.Exists(configPath);
            EditorGUILayout.SelectableLabel(configPath, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Buttons
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
            if (GUILayout.Button("Auto Configure Claude Desktop", GUILayout.Height(30)))
            {
                ConfigureClaudeDesktop();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Copy Config to Clipboard", GUILayout.Height(30)))
            {
                CopyConfigToClipboard();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Config Folder", GUILayout.Height(25)))
            {
                OpenConfigFolder();
            }

            if (GUILayout.Button("View Current Config", GUILayout.Height(25)))
            {
                ViewCurrentConfig();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Configuration Preview
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Configuration Preview", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            string preview = McpSettings.Instance.GenerateClaudeConfig();
            EditorGUILayout.TextArea(preview, GUILayout.Height(150));

            EditorGUILayout.EndVertical();
        }

        private void DrawLogsTab()
        {
            // Log controls
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"Log Entries: {McpServerLogger.Instance.Count}", GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                McpServerLogger.Instance.Clear();
                _cachedLogs = "";
            }

            if (GUILayout.Button("Export", GUILayout.Width(60)))
            {
                ExportLogs();
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                RefreshLogs();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Log filter
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Min Level:", GUILayout.Width(70));
            _logLevel = (LogLevel)EditorGUILayout.EnumPopup(_logLevel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Log area
            RefreshLogsIfNeeded();

            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_cachedLogs, _logAreaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // Quick log buttons
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Test Info Log"))
            {
                McpServerLogger.Instance.Info("Test info message");
            }
            if (GUILayout.Button("Test Warning"))
            {
                McpServerLogger.Instance.Warning("Test warning message");
            }
            if (GUILayout.Button("Test Error"))
            {
                McpServerLogger.Instance.Error("Test error message");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsTab()
        {
            // General Settings
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Auto-start Server:", GUILayout.Width(150));
            _autoStart = EditorGUILayout.Toggle(_autoStart);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Show Notifications:", GUILayout.Width(150));
            _showNotifications = EditorGUILayout.Toggle(_showNotifications);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Request Timeout (ms):", GUILayout.Width(150));
            _requestTimeout = EditorGUILayout.IntField(_requestTimeout);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Logging Settings
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Logging Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log to Unity Console:", GUILayout.Width(150));
            _logToConsole = EditorGUILayout.Toggle(_logToConsole);
            EditorGUILayout.EndHorizontal();

            if (!_logToConsole)
            {
                EditorGUILayout.HelpBox("MCP logs are hidden from Unity Console. View them in the Logs tab.", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Max Log Entries:", GUILayout.Width(150));
            _maxLogEntries = EditorGUILayout.IntField(_maxLogEntries);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log to File:", GUILayout.Width(150));
            _logToFile = EditorGUILayout.Toggle(_logToFile);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Minimum Log Level:", GUILayout.Width(150));
            _logLevel = (LogLevel)EditorGUILayout.EnumPopup(_logLevel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Custom Server Path
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("Advanced Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Use Custom Server Path:", GUILayout.Width(150));
            _useCustomPath = EditorGUILayout.Toggle(_useCustomPath);
            EditorGUILayout.EndHorizontal();

            if (_useCustomPath)
            {
                EditorGUILayout.BeginHorizontal();
                _customPath = EditorGUILayout.TextField(_customPath);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Server Script", "", "js");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _customPath = path;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(20);

            // Action buttons
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
            {
                SaveSettings();
                McpServerLogger.Instance.Info("Settings saved");
                if (_showNotifications)
                {
                    EditorUtility.DisplayDialog("Settings Saved", "MCP Unity settings have been saved.", "OK");
                }
            }

            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Reset to Defaults", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Reset Settings",
                    "Are you sure you want to reset all settings to defaults?", "Reset", "Cancel"))
                {
                    McpSettings.Instance.ResetToDefaults();
                    LoadSettings();
                    McpServerLogger.Instance.Info("Settings reset to defaults");
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void RefreshLogsIfNeeded()
        {
            int currentCount = McpServerLogger.Instance.Count;
            if (currentCount != _lastLogCount)
            {
                RefreshLogs();
            }
        }

        private void RefreshLogs()
        {
            _cachedLogs = McpServerLogger.Instance.GetFormattedLogs(_logLevel);
            _lastLogCount = McpServerLogger.Instance.Count;
        }

        private void StartServer()
        {
            SaveSettings();

            // Call the real server
            McpUnityServer.Start();
            _serverStartTime = DateTime.Now;

            McpServerLogger.Instance.Info($"Server started on port {McpUnityServer.Port}");

            if (_showNotifications)
            {
                ShowNotification(new GUIContent("MCP Server Started"));
            }

            Repaint();
        }

        private void StopServer()
        {
            // Call the real server
            McpUnityServer.Stop();

            McpServerLogger.Instance.Info("Server stopped");

            if (_showNotifications)
            {
                ShowNotification(new GUIContent("MCP Server Stopped"));
            }

            Repaint();
        }

        private void RestartServer()
        {
            StopServer();
            EditorApplication.delayCall += StartServer;
        }

        private void ConfigureClaudeDesktop()
        {
            try
            {
                string configPath = McpSettings.GetClaudeConfigPath();
                string directory = Path.GetDirectoryName(configPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string newConfig = McpSettings.Instance.GenerateClaudeConfig();

                // Check if config already exists and try to merge
                if (File.Exists(configPath))
                {
                    try
                    {
                        string existingConfig = File.ReadAllText(configPath);
                        // For now, we'll just overwrite. A more sophisticated implementation
                        // would parse and merge the JSON
                        McpServerLogger.Instance.Warning("Existing Claude config will be backed up and replaced");

                        // Backup existing config
                        string backupPath = configPath + ".backup";
                        File.Copy(configPath, backupPath, true);
                    }
                    catch (Exception ex)
                    {
                        McpServerLogger.Instance.Error("Failed to backup existing config", ex);
                    }
                }

                File.WriteAllText(configPath, newConfig);

                McpServerLogger.Instance.Info($"Claude Desktop configured: {configPath}");

                EditorUtility.DisplayDialog("Success",
                    "Claude Desktop configuration updated!\n\n" +
                    "Please restart Claude Desktop for changes to take effect.",
                    "OK");
            }
            catch (Exception ex)
            {
                McpServerLogger.Instance.Error("Failed to configure Claude Desktop", ex);
                EditorUtility.DisplayDialog("Error",
                    $"Failed to configure Claude Desktop:\n{ex.Message}",
                    "OK");
            }
        }

        private void CopyConfigToClipboard()
        {
            string config = McpSettings.Instance.GenerateClaudeConfig();
            EditorGUIUtility.systemCopyBuffer = config;

            McpServerLogger.Instance.Info("Configuration copied to clipboard");

            if (_showNotifications)
            {
                ShowNotification(new GUIContent("Config Copied!"));
            }
        }

        private void OpenConfigFolder()
        {
            string configPath = McpSettings.GetClaudeConfigPath();
            string directory = Path.GetDirectoryName(configPath);

            if (Directory.Exists(directory))
            {
                EditorUtility.RevealInFinder(configPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Folder Not Found",
                    $"The Claude configuration folder does not exist yet:\n{directory}",
                    "OK");
            }
        }

        private void ViewCurrentConfig()
        {
            string configPath = McpSettings.GetClaudeConfigPath();

            if (File.Exists(configPath))
            {
                string content = File.ReadAllText(configPath);
                EditorUtility.DisplayDialog("Current Claude Config", content, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("No Config Found",
                    $"Claude Desktop configuration file not found at:\n{configPath}",
                    "OK");
            }
        }

        private void ExportLogs()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export MCP Unity Logs",
                "",
                $"mcp_unity_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                "txt");

            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string content = McpServerLogger.Instance.Export();
                    File.WriteAllText(path, content);

                    McpServerLogger.Instance.Info($"Logs exported to: {path}");
                    EditorUtility.RevealInFinder(path);
                }
                catch (Exception ex)
                {
                    McpServerLogger.Instance.Error("Failed to export logs", ex);
                    EditorUtility.DisplayDialog("Export Failed",
                        $"Failed to export logs:\n{ex.Message}",
                        "OK");
                }
            }
        }
    }
}
