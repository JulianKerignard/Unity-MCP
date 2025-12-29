using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using McpUnity.Protocol;
using McpUnity.Utils;
using McpUnity.Helpers;
using McpUnity.Editor;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace McpUnity.Server
{
    /// <summary>
    /// Helper class for creating standardized MCP tool responses
    /// </summary>
    public static class McpResponse
    {
        /// <summary>
        /// Create a success response with structured data
        /// </summary>
        public static McpToolResult Success(object data)
        {
            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["data"] = data
                    })
                },
                isError = false
            };
        }

        /// <summary>
        /// Create a success response with message and optional data
        /// </summary>
        public static McpToolResult Success(string message, object data = null)
        {
            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["message"] = message
            };
            if (data != null)
                result["data"] = data;

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }
    }

    /// <summary>
    /// WebSocket server for MCP Unity plugin using websocket-sharp.
    /// Handles multiple client connections and message routing.
    ///
    /// This is a partial class - tool implementations are in separate files under Tools/ folder:
    /// - Tools/UITools.cs (4 tools)
    /// - Tools/GameObjectTools.cs (7 tools)
    /// - Tools/ComponentTools.cs (3 tools)
    /// - Tools/AnimatorTools.cs (16 tools)
    /// - Tools/AssetTools.cs (5 tools)
    /// - Tools/SceneTools.cs (4 tools)
    /// - Tools/PrefabTools.cs (4 tools)
    /// - Tools/MaterialTools.cs (3 tools)
    /// - Tools/TagLayerTools.cs (6 tools)
    /// - Tools/EditorTools.cs (8 tools)
    /// - Tools/MemoryTools.cs (3 tools)
    /// </summary>
    [InitializeOnLoad]
    public partial class McpUnityServer
    {
        #region Static Fields

        private static WebSocketServer _wss;
        private static bool _isRunning;
        private static readonly ConcurrentDictionary<string, McpBehavior> _connectedClients
            = new ConcurrentDictionary<string, McpBehavior>();

        private static McpToolRegistry _toolRegistry;
        private static McpResourceRegistry _resourceRegistry;

        // Message queue for main thread processing
        private static readonly ConcurrentQueue<QueuedMessage> _messageQueue
            = new ConcurrentQueue<QueuedMessage>();
        private static bool _updateRegistered = false;
        private static bool _initialized = false;
        private static int _startupFrameCount = 0;

        // Console log capture
        private static readonly ConcurrentQueue<LogEntry> _consoleLogs = new ConcurrentQueue<LogEntry>();
        private const int MaxLogEntries = 100;
        private static bool _logHandlerRegistered = false;

        // Configuration
        private const string ServerPrefsKey = "McpUnity_ServerPort";
        private const string AutoStartPrefsKey = "McpUnity_AutoStart";
        private const int DefaultPort = 8090;

        // Constants for magic numbers
        private const int StartupFrameDelay = 10;
        private const int MaxMessagesPerFrame = 10;

        #endregion

        #region Properties

        public static int Port
        {
            get => EditorPrefs.GetInt(ServerPrefsKey, DefaultPort);
            set => EditorPrefs.SetInt(ServerPrefsKey, value);
        }

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartPrefsKey, true);
            set => EditorPrefs.SetBool(AutoStartPrefsKey, value);
        }

        public static bool IsRunning => _isRunning;
        public static int ConnectedClientCount => _connectedClients.Count;

        #endregion

        #region Events

        public static event Action<string> OnMessageReceived;
        public static event Action<string> OnClientConnected;
        public static event Action<string> OnClientDisconnected;
        public static event Action OnServerStarted;
        public static event Action OnServerStopped;

        #endregion

        #region Initialization

        static McpUnityServer()
        {
            EditorApplication.quitting += Stop;
            EditorApplication.delayCall += OnEditorLoaded;
            RegisterUpdateCallback();
            RegisterLogHandler();
        }

        private static void RegisterLogHandler()
        {
            if (!_logHandlerRegistered)
            {
                Application.logMessageReceived += HandleLogMessage;
                _logHandlerRegistered = true;
            }
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = condition,
                StackTrace = stackTrace,
                Type = type.ToString()
            };

            _consoleLogs.Enqueue(entry);

            // Keep only the last MaxLogEntries
            while (_consoleLogs.Count > MaxLogEntries)
            {
                _consoleLogs.TryDequeue(out _);
            }
        }

        private static void RegisterUpdateCallback()
        {
            if (!_updateRegistered)
            {
                EditorApplication.update += ProcessMessageQueue;
                _updateRegistered = true;
            }
        }

        /// <summary>
        /// Queue a message for processing on the main thread
        /// </summary>
        internal static void QueueMessage(string message, McpBehavior sender)
        {
            _messageQueue.Enqueue(new QueuedMessage { Message = message, Sender = sender });
        }

        /// <summary>
        /// Process queued messages on the main thread (called by EditorApplication.update)
        /// </summary>
        private static void ProcessMessageQueue()
        {
            // Fallback initialization check - ensures server starts even if delayCall doesn't fire
            if (!_initialized && !EditorApplication.isCompiling)
            {
                _startupFrameCount++;
                // Wait a few frames after domain reload before attempting to start
                if (_startupFrameCount > StartupFrameDelay)
                {
                    _initialized = true;
                    InitializeRegistries();
                    if (AutoStart && !_isRunning)
                    {
                        McpDebug.Log("[MCP Unity] Fallback auto-start triggered");
                        Start();
                    }
                }
            }

            // Process up to MaxMessagesPerFrame messages per frame to avoid blocking
            int processed = 0;
            while (processed < MaxMessagesPerFrame && _messageQueue.TryDequeue(out var queued))
            {
                processed++;
                try
                {
                    McpDebug.Log("[MCP Unity] Processing message on main thread...");
                    var response = ProcessMessage(queued.Message);

                    if (!string.IsNullOrEmpty(response))
                    {
                        McpDebug.Log($"[MCP Unity] Sending response: {(response.Length > 200 ? response.Substring(0, 200) + "..." : response)}");
                        queued.Sender.SendMessage(response);
                        McpDebug.Log("[MCP Unity] Response sent successfully");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCP Unity] Error processing message: {ex.Message}\n{ex.StackTrace}");
                    try
                    {
                        queued.Sender.SendMessage(JsonHelper.ToJson(
                            JsonRpcResponse.Error(null, JsonRpcError.InternalError, ex.Message)));
                    }
                    catch (Exception sendEx)
                    {
                        McpDebug.LogWarning($"[MCP Unity] Failed to send error response: {sendEx.Message}");
                    }
                }
            }
        }

        private static void OnEditorLoaded()
        {
            if (_initialized) return; // Prevent double initialization
            _initialized = true;

            InitializeRegistries();

            if (AutoStart)
            {
                Start();
            }
        }

        private static void InitializeRegistries()
        {
            _toolRegistry = new McpToolRegistry();
            _resourceRegistry = new McpResourceRegistry();

            // Register default tools and resources
            RegisterDefaultTools();
            RegisterDefaultResources();

            // Initialize JSON-RPC handler
            McpJsonRpc.Initialize(_toolRegistry, _resourceRegistry);
        }

        /// <summary>
        /// Register all default tools by calling partial class registration methods.
        /// Tool implementations are in separate files under Tools/ folder.
        /// </summary>
        private static void RegisterDefaultTools()
        {
            // UI Tools (4 tools)
            RegisterUITools();

            // GameObject Tools (7 tools)
            RegisterGameObjectTools();

            // Component Tools (3 tools)
            RegisterComponentTools();

            // Animator Tools (16 tools)
            RegisterAnimatorTools();

            // Asset Tools (5 tools)
            RegisterAssetTools();

            // Scene Tools (4 tools)
            RegisterSceneTools();

            // Prefab Tools (4 tools)
            RegisterPrefabTools();

            // Material Tools (3 tools)
            RegisterMaterialTools();

            // Tag/Layer Tools (6 tools)
            RegisterTagLayerTools();

            // Editor Tools (8 tools)
            RegisterEditorTools();

            // Memory Tools (3 tools)
            RegisterMemoryTools();

            // Project Settings Tools (6 tools)
            RegisterProjectSettingsTools();

            McpDebug.Log("[MCP Unity] Registered 69 tools from partial classes");
        }

        private static void RegisterDefaultResources()
        {
            _resourceRegistry.RegisterResource(new McpResourceDefinition
            {
                uri = "unity://project/settings",
                name = "Project Settings",
                description = "Current Unity project settings",
                mimeType = "application/json"
            }, () => GetProjectSettings());

            _resourceRegistry.RegisterResource(new McpResourceDefinition
            {
                uri = "unity://scene/hierarchy",
                name = "Scene Hierarchy",
                description = "Current scene object hierarchy",
                mimeType = "application/json"
            }, () => GetSceneHierarchy());

            _resourceRegistry.RegisterResource(new McpResourceDefinition
            {
                uri = "unity://console/logs",
                name = "Console Logs",
                description = "Recent Unity console log entries",
                mimeType = "application/json"
            }, () => GetConsoleLogs());
        }

        #endregion

        #region Shared Utilities

        /// <summary>
        /// Sanitize and validate file paths for security (prevent path traversal attacks)
        /// </summary>
        internal static string SanitizePath(string path, string requiredPrefix = "Assets/")
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be empty");

            // Normalize path separators
            path = path.Replace("\\", "/");

            // Block path traversal attempts
            if (path.Contains(".."))
                throw new ArgumentException("Path traversal (..) is not allowed for security reasons");

            // Verify path starts with required prefix (default: Assets/)
            if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Path must be within '{requiredPrefix}' folder");

            // Block absolute paths outside project
            if (path.StartsWith("/") && !path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Absolute paths outside project are not allowed");

            return path;
        }

        /// <summary>
        /// Get the full hierarchy path of a GameObject
        /// </summary>
        internal static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        /// <summary>
        /// Get console logs as a list (used by multiple tools)
        /// </summary>
        internal static ConcurrentQueue<LogEntry> GetConsoleLogQueue()
        {
            return _consoleLogs;
        }

        #endregion

        #region Resource Implementations

        private static McpResourceContent GetProjectSettings()
        {
            var settings = new
            {
                productName = PlayerSettings.productName,
                companyName = PlayerSettings.companyName,
                version = PlayerSettings.bundleVersion,
                targetPlatform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)).ToString()
            };

            return new McpResourceContent
            {
                uri = "unity://project/settings",
                mimeType = "application/json",
                text = JsonUtility.ToJson(settings, true)
            };
        }

        private static McpResourceContent GetSceneHierarchy()
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var hierarchy = new List<object>();

            foreach (var obj in rootObjects)
            {
                hierarchy.Add(GetGameObjectInfoForResource(obj, 0));
            }

            return new McpResourceContent
            {
                uri = "unity://scene/hierarchy",
                mimeType = "application/json",
                text = JsonHelper.ToJson(new { sceneName = scene.name, rootObjects = hierarchy })
            };
        }

        private static object GetGameObjectInfoForResource(GameObject obj, int depth)
        {
            var children = new List<object>();
            if (depth < 3)
            {
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    children.Add(GetGameObjectInfoForResource(obj.transform.GetChild(i).gameObject, depth + 1));
                }
            }

            return new
            {
                name = obj.name,
                active = obj.activeSelf,
                components = GetComponentNames(obj),
                children = children
            };
        }

        private static string[] GetComponentNames(GameObject obj)
        {
            var components = obj.GetComponents<Component>();
            var names = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                names[i] = components[i]?.GetType().Name ?? "null";
            }
            return names;
        }

        private static McpResourceContent GetConsoleLogs()
        {
            return new McpResourceContent
            {
                uri = "unity://console/logs",
                mimeType = "application/json",
                text = JsonHelper.ToJson(new { message = "Console log access requires LogHandler setup" })
            };
        }

        #endregion

        #region Server Lifecycle

        /// <summary>
        /// Menu item to quickly start the server
        /// </summary>
        [MenuItem("Tools/MCP Unity/Start Server", priority = 1)]
        public static void MenuStartServer()
        {
            if (!_initialized)
            {
                _initialized = true;
                InitializeRegistries();
            }
            Start();
        }

        [MenuItem("Tools/MCP Unity/Stop Server", priority = 2)]
        public static void MenuStopServer()
        {
            Stop();
        }

        /// <summary>
        /// Start the WebSocket server using websocket-sharp
        /// </summary>
        public static void Start()
        {
            if (_isRunning)
            {
                McpDebug.LogWarning("[MCP Unity] Server is already running");
                return;
            }

            try
            {
                _wss = new WebSocketServer($"ws://127.0.0.1:{Port}");
                _wss.AddWebSocketService<McpBehavior>("/");
                _wss.Start();
                _isRunning = true;

                McpDebug.Log($"[MCP Unity] Server started on ws://127.0.0.1:{Port}");
                OnServerStarted?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Unity] Failed to start server on port {Port}: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Stop the WebSocket server
        /// </summary>
        public static void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _wss?.Stop();
            }
            catch (Exception ex)
            {
                McpDebug.LogWarning($"[MCP Unity] Error stopping server: {ex.Message}");
            }

            _connectedClients.Clear();
            _isRunning = false;
            McpDebug.Log("[MCP Unity] Server stopped");
            OnServerStopped?.Invoke();
        }

        /// <summary>
        /// Restart the server
        /// </summary>
        public static void Restart()
        {
            Stop();
            EditorApplication.delayCall += Start;
        }

        /// <summary>
        /// Process an incoming message (called from McpBehavior)
        /// </summary>
        internal static string ProcessMessage(string message)
        {
            OnMessageReceived?.Invoke(message);
            return McpJsonRpc.ProcessMessage(message);
        }

        /// <summary>
        /// Register a client connection
        /// </summary>
        internal static void RegisterClient(string id, McpBehavior behavior)
        {
            _connectedClients[id] = behavior;
            McpDebug.Log($"[MCP Unity] Client connected: {id}");
            OnClientConnected?.Invoke(id);
        }

        /// <summary>
        /// Unregister a client connection
        /// </summary>
        internal static void UnregisterClient(string id)
        {
            _connectedClients.TryRemove(id, out _);
            McpDebug.Log($"[MCP Unity] Client disconnected: {id}");
            OnClientDisconnected?.Invoke(id);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Register a custom tool
        /// </summary>
        public static void RegisterTool(McpToolDefinition definition, Func<Dictionary<string, object>, McpToolResult> handler)
        {
            _toolRegistry?.RegisterTool(definition, handler);
        }

        /// <summary>
        /// Register a custom resource
        /// </summary>
        public static void RegisterResource(McpResourceDefinition definition, Func<McpResourceContent> handler)
        {
            _resourceRegistry?.RegisterResource(definition, handler);
        }

        /// <summary>
        /// Broadcast a message to all connected clients
        /// </summary>
        public static void Broadcast(string message)
        {
            foreach (var kvp in _connectedClients)
            {
                try
                {
                    kvp.Value.SendMessage(message);
                }
                catch (Exception ex)
                {
                    McpDebug.LogWarning($"[MCP Unity] Failed to broadcast to client {kvp.Key}: {ex.Message}");
                }
            }
        }

        #endregion

        #region Partial Class Registration Methods (implemented in Tools/*.cs)

        // These methods are implemented in partial class files under Tools/ folder
        // They register tool definitions and handlers with _toolRegistry

        static partial void RegisterUITools();
        static partial void RegisterGameObjectTools();
        static partial void RegisterComponentTools();
        static partial void RegisterAnimatorTools();
        static partial void RegisterAssetTools();
        static partial void RegisterSceneTools();
        static partial void RegisterPrefabTools();
        static partial void RegisterMaterialTools();
        static partial void RegisterTagLayerTools();
        static partial void RegisterEditorTools();
        static partial void RegisterMemoryTools();
        static partial void RegisterProjectSettingsTools();

        #endregion
    }

    /// <summary>
    /// WebSocket behavior for handling MCP client connections
    /// </summary>
    public class McpBehavior : WebSocketBehavior
    {
        protected override void OnOpen()
        {
            McpUnityServer.RegisterClient(ID, this);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            McpUnityServer.UnregisterClient(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                var message = e.Data;
                McpDebug.Log($"[MCP Unity] Received message: {(message.Length > 100 ? message.Substring(0, 100) + "..." : message)}");

                // Queue message for processing on main thread
                McpUnityServer.QueueMessage(message, this);
            }
        }

        protected override void OnError(ErrorEventArgs e)
        {
            Debug.LogError($"[MCP Unity] WebSocket error: {e.Message}");
        }

        public void SendMessage(string message)
        {
            Send(message);
        }
    }

    /// <summary>
    /// Message queue item for main thread processing
    /// </summary>
    internal class QueuedMessage
    {
        public string Message;
        public McpBehavior Sender;
    }

    /// <summary>
    /// Console log entry for MCP log retrieval
    /// </summary>
    internal class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string Type { get; set; }

        public LogEntry() { }

        public LogEntry(DateTime timestamp, string message, string stackTrace, string type)
        {
            Timestamp = timestamp;
            Message = message;
            StackTrace = stackTrace;
            Type = type;
        }
    }
}
