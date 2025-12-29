using UnityEditor;
using UnityEngine;

namespace McpUnity.Server
{
    /// <summary>
    /// Editor window for MCP Unity Server control and monitoring
    /// </summary>
    public class McpServerWindow : EditorWindow
    {
        private Vector2 _logScrollPosition;
        private string _portInput;
        private bool _showAdvanced;

        private static readonly System.Collections.Generic.Queue<string> _logMessages
            = new System.Collections.Generic.Queue<string>();
        private const int MaxLogMessages = 100;

        [MenuItem("Tools/MCP Unity/Server Control")]
        public static void ShowWindow()
        {
            var window = GetWindow<McpServerWindow>("MCP Server");
            window.minSize = new Vector2(350, 400);
        }

        private void OnEnable()
        {
            _portInput = McpUnityServer.Port.ToString();

            McpUnityServer.OnMessageReceived += HandleMessageReceived;
            McpUnityServer.OnClientConnected += HandleClientConnected;
            McpUnityServer.OnClientDisconnected += HandleClientDisconnected;
            McpUnityServer.OnServerStarted += HandleServerStarted;
            McpUnityServer.OnServerStopped += HandleServerStopped;
        }

        private void OnDisable()
        {
            McpUnityServer.OnMessageReceived -= HandleMessageReceived;
            McpUnityServer.OnClientConnected -= HandleClientConnected;
            McpUnityServer.OnClientDisconnected -= HandleClientDisconnected;
            McpUnityServer.OnServerStarted -= HandleServerStarted;
            McpUnityServer.OnServerStopped -= HandleServerStopped;
        }

        private void OnGUI()
        {
            GUILayout.Space(10);

            DrawHeader();
            GUILayout.Space(10);

            DrawServerStatus();
            GUILayout.Space(10);

            DrawServerControls();
            GUILayout.Space(10);

            DrawConfiguration();
            GUILayout.Space(10);

            DrawConnectionInfo();
            GUILayout.Space(10);

            DrawLogViewer();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical();
            GUILayout.Label("MCP Unity Server", EditorStyles.boldLabel);
            GUILayout.Label("Model Context Protocol for Unity", EditorStyles.miniLabel);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawServerStatus()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", GUILayout.Width(60));

            var statusStyle = new GUIStyle(EditorStyles.boldLabel);
            if (McpUnityServer.IsRunning)
            {
                statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                GUILayout.Label("Running", statusStyle);
            }
            else
            {
                statusStyle.normal.textColor = new Color(0.8f, 0.2f, 0.2f);
                GUILayout.Label("Stopped", statusStyle);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (McpUnityServer.IsRunning)
            {
                GUILayout.Label($"Listening on: ws://localhost:{McpUnityServer.Port}", EditorStyles.miniLabel);
                GUILayout.Label($"Connected Clients: {McpUnityServer.ConnectedClientCount}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawServerControls()
        {
            GUILayout.BeginHorizontal();

            GUI.enabled = !McpUnityServer.IsRunning;
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
            {
                McpUnityServer.Start();
            }

            GUI.enabled = McpUnityServer.IsRunning;
            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
            {
                McpUnityServer.Stop();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Restart", GUILayout.Height(30), GUILayout.Width(70)))
            {
                McpUnityServer.Restart();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawConfiguration()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Configuration", true);

            if (_showAdvanced)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", GUILayout.Width(60));

                GUI.enabled = !McpUnityServer.IsRunning;
                _portInput = EditorGUILayout.TextField(_portInput, GUILayout.Width(80));

                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    if (int.TryParse(_portInput, out int newPort) && newPort > 0 && newPort < 65536)
                    {
                        McpUnityServer.Port = newPort;
                        AddLog($"Port changed to {newPort}");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Invalid Port", "Please enter a valid port number (1-65535)", "OK");
                        _portInput = McpUnityServer.Port.ToString();
                    }
                }
                GUI.enabled = true;

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Auto-Start:", GUILayout.Width(60));
                McpUnityServer.AutoStart = EditorGUILayout.Toggle(McpUnityServer.AutoStart);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawConnectionInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label("Connection Info", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("WebSocket URL", $"ws://localhost:{McpUnityServer.Port}");
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
            {
                GUIUtility.systemCopyBuffer = $"ws://localhost:{McpUnityServer.Port}";
                AddLog("URL copied to clipboard");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawLogViewer()
        {
            GUILayout.Label("Log", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));

            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition);

            foreach (var message in _logMessages)
            {
                GUILayout.Label(message, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Clear Log"))
            {
                _logMessages.Clear();
                Repaint();
            }
        }

        private void HandleMessageReceived(string message)
        {
            var displayMessage = message.Length > 200
                ? message.Substring(0, 200) + "..."
                : message;
            AddLog($"[MSG] {displayMessage}");
        }

        private void HandleClientConnected(string clientId)
        {
            AddLog($"[CONNECT] Client connected: {(clientId.Length > 8 ? clientId.Substring(0, 8) + "..." : clientId)}");
        }

        private void HandleClientDisconnected(string clientId)
        {
            AddLog($"[DISCONNECT] Client disconnected: {(clientId.Length > 8 ? clientId.Substring(0, 8) + "..." : clientId)}");
        }

        private void HandleServerStarted()
        {
            AddLog($"[SERVER] Started on port {McpUnityServer.Port}");
        }

        private void HandleServerStopped()
        {
            AddLog("[SERVER] Stopped");
        }

        private void AddLog(string message)
        {
            var timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            _logMessages.Enqueue($"[{timestamp}] {message}");

            while (_logMessages.Count > MaxLogMessages)
            {
                _logMessages.Dequeue();
            }

            Repaint();
        }
    }
}
