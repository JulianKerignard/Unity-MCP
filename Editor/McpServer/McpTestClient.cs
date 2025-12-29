using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Server
{
    /// <summary>
    /// Test client for verifying MCP server functionality
    /// </summary>
    public class McpTestClient : EditorWindow
    {
        private ClientWebSocket _webSocket;
        private string _serverUrl = "ws://localhost:8090";
        private string _requestJson = "";
        private string _responseText = "";
        private Vector2 _scrollPosition;
        private bool _isConnected;

        [MenuItem("Tools/MCP Unity/Test Client")]
        public static void ShowWindow()
        {
            var window = GetWindow<McpTestClient>("MCP Test Client");
            window.minSize = new Vector2(400, 500);
        }

        private void OnEnable()
        {
            // Default test request
            _requestJson = @"{
    ""jsonrpc"": ""2.0"",
    ""id"": 1,
    ""method"": ""initialize"",
    ""params"": {
        ""protocolVersion"": ""2024-11-05"",
        ""capabilities"": {},
        ""clientInfo"": {
            ""name"": ""test-client"",
            ""version"": ""1.0.0""
        }
    }
}";
        }

        private void OnDisable()
        {
            Disconnect();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("MCP WebSocket Test Client", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // Connection section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Connection", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _serverUrl = EditorGUILayout.TextField("Server URL:", _serverUrl);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isConnected;
            if (GUILayout.Button("Connect", GUILayout.Width(100)))
            {
                _ = ConnectAsync();
            }
            GUI.enabled = _isConnected;
            if (GUILayout.Button("Disconnect", GUILayout.Width(100)))
            {
                Disconnect();
            }
            GUI.enabled = true;

            GUILayout.Label(_isConnected ? "Connected" : "Disconnected",
                _isConnected ? EditorStyles.boldLabel : EditorStyles.label);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Request section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Request (JSON-RPC)", EditorStyles.boldLabel);

            _requestJson = EditorGUILayout.TextArea(_requestJson, GUILayout.Height(150));

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _isConnected;
            if (GUILayout.Button("Send Request"))
            {
                _ = SendRequestAsync();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Initialize"))
            {
                _requestJson = GetInitializeRequest();
            }
            if (GUILayout.Button("List Tools"))
            {
                _requestJson = GetListToolsRequest();
            }
            if (GUILayout.Button("List Resources"))
            {
                _requestJson = GetListResourcesRequest();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Response section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.Label("Response", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.TextArea(_responseText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Response"))
            {
                _responseText = "";
            }

            EditorGUILayout.EndVertical();
        }

        private async Task ConnectAsync()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_serverUrl), CancellationToken.None);
                _isConnected = true;
                _responseText = "Connected to server\n";
                Repaint();

                // Start receiving messages
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                _responseText = $"Connection failed: {ex.Message}\n";
                _isConnected = false;
                Repaint();
            }
        }

        private void Disconnect()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch { }
            }
            _webSocket?.Dispose();
            _webSocket = null;
            _isConnected = false;
            Repaint();
        }

        private async Task SendRequestAsync()
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            {
                _responseText += "Not connected to server\n";
                Repaint();
                return;
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(_requestJson);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                _responseText += $"Sent: {_requestJson.Substring(0, Math.Min(100, _requestJson.Length))}...\n";
                Repaint();
            }
            catch (Exception ex)
            {
                _responseText += $"Send error: {ex.Message}\n";
                Repaint();
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];

            while (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _isConnected = false;
                        _responseText += "Server closed connection\n";
                        Repaint();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _responseText += $"Received:\n{FormatJson(message)}\n\n";
                        Repaint();
                    }
                }
                catch (Exception ex)
                {
                    if (_isConnected)
                    {
                        _responseText += $"Receive error: {ex.Message}\n";
                        _isConnected = false;
                        Repaint();
                    }
                    break;
                }
            }
        }

        private string FormatJson(string json)
        {
            // Simple JSON formatting
            try
            {
                int indent = 0;
                var result = new StringBuilder();
                bool inString = false;

                foreach (char c in json)
                {
                    if (c == '"' && (result.Length == 0 || result[result.Length - 1] != '\\'))
                    {
                        inString = !inString;
                        result.Append(c);
                    }
                    else if (!inString)
                    {
                        switch (c)
                        {
                            case '{':
                            case '[':
                                result.Append(c);
                                result.AppendLine();
                                indent++;
                                result.Append(new string(' ', indent * 2));
                                break;
                            case '}':
                            case ']':
                                result.AppendLine();
                                indent--;
                                result.Append(new string(' ', indent * 2));
                                result.Append(c);
                                break;
                            case ',':
                                result.Append(c);
                                result.AppendLine();
                                result.Append(new string(' ', indent * 2));
                                break;
                            case ':':
                                result.Append(": ");
                                break;
                            default:
                                if (!char.IsWhiteSpace(c))
                                    result.Append(c);
                                break;
                        }
                    }
                    else
                    {
                        result.Append(c);
                    }
                }

                return result.ToString();
            }
            catch
            {
                return json;
            }
        }

        private string GetInitializeRequest()
        {
            return @"{
    ""jsonrpc"": ""2.0"",
    ""id"": 1,
    ""method"": ""initialize"",
    ""params"": {
        ""protocolVersion"": ""2024-11-05"",
        ""capabilities"": {},
        ""clientInfo"": {
            ""name"": ""test-client"",
            ""version"": ""1.0.0""
        }
    }
}";
        }

        private string GetListToolsRequest()
        {
            return @"{
    ""jsonrpc"": ""2.0"",
    ""id"": 2,
    ""method"": ""tools/list"",
    ""params"": {}
}";
        }

        private string GetListResourcesRequest()
        {
            return @"{
    ""jsonrpc"": ""2.0"",
    ""id"": 3,
    ""method"": ""resources/list"",
    ""params"": {}
}";
        }
    }
}
