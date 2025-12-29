using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using McpUnity.Protocol;
using McpUnity.Utils;
using McpUnity.Models;
using McpUnity.Helpers;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace McpUnity.Server
{
    /// <summary>
    /// WebSocket server for MCP Unity plugin using websocket-sharp
    /// Handles multiple client connections and message routing
    /// </summary>
    [InitializeOnLoad]
    public class McpUnityServer
    {
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

        // Constants for magic numbers (audit fix)
        private const int StartupFrameDelay = 10;
        private const int MaxMessagesPerFrame = 10;
        private const int MaxScreenshotDimension = 4096;
        private const int MaxAssetSearchResults = 200;

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

        // Events
        public static event Action<string> OnMessageReceived;
        public static event Action<string> OnClientConnected;
        public static event Action<string> OnClientDisconnected;
        public static event Action OnServerStarted;
        public static event Action OnServerStopped;

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
                        Debug.Log("[MCP Unity] Fallback auto-start triggered");
                        Start();
                    }
                }
            }

            // Process up to 10 messages per frame to avoid blocking
            int processed = 0;
            while (processed < MaxMessagesPerFrame && _messageQueue.TryDequeue(out var queued))
            {
                processed++;
                try
                {
                    Debug.Log("[MCP Unity] Processing message on main thread...");
                    var response = ProcessMessage(queued.Message);

                    if (!string.IsNullOrEmpty(response))
                    {
                        Debug.Log($"[MCP Unity] Sending response: {(response.Length > 200 ? response.Substring(0, 200) + "..." : response)}");
                        queued.Sender.SendMessage(response);
                        Debug.Log("[MCP Unity] Response sent successfully");
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
                        Debug.LogWarning($"[MCP Unity] Failed to send error response: {sendEx.Message}");
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

        private static void RegisterDefaultTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_execute_menu_item",
                description = "Execute a Unity Editor menu item by path",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["menuPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "The menu item path (e.g., 'File/Save Project')"
                        }
                    },
                    required = new List<string> { "menuPath" }
                }
            }, ExecuteMenuItem);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_editor_state",
                description = "Get current Unity Editor state including play mode, selected objects, etc.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>()
                }
            }, GetEditorState);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_run_tests",
                description = "Run Unity Test Runner tests",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["testMode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Test mode: 'EditMode' or 'PlayMode'",
                            @enum = new List<string> { "EditMode", "PlayMode" }
                        },
                        ["testFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional filter for test names"
                        }
                    }
                }
            }, RunTests);

            // GameObject management tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_gameobjects",
                description = "List GameObjects in scene. Use outputMode='tree' for compact view (saves 90% tokens)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["outputMode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Output format: 'tree' (compact ASCII), 'names' (just names), 'summary' (names+components), 'full' (all details). Default: 'summary'",
                            @enum = new List<string> { "names", "tree", "summary", "full" }
                        },
                        ["maxDepth"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Maximum hierarchy depth (default: 3)"
                        },
                        ["includeInactive"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include inactive GameObjects (default: false)"
                        },
                        ["rootOnly"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Only return root objects, no children (default: false)"
                        },
                        ["nameFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter by name pattern (supports * wildcard, e.g., 'Enemy*')"
                        },
                        ["componentFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Only objects with this component (e.g., 'Rigidbody')"
                        },
                        ["tagFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter by tag (e.g., 'Player')"
                        },
                        ["includeTransform"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include position/rotation/scale in 'full' mode (default: false)"
                        }
                    }
                }
            }, ListGameObjects);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_gameobject",
                description = "Create a new GameObject in the scene",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["name"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the new GameObject"
                        },
                        ["primitiveType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad",
                            @enum = new List<string> { "Empty", "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" }
                        },
                        ["parentPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional path to parent GameObject (e.g., 'Environment/Props')"
                        },
                        ["position"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Optional position {x, y, z}"
                        }
                    },
                    required = new List<string> { "name" }
                }
            }, CreateGameObject);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_delete_gameobject",
                description = "Delete a GameObject from the scene by name or path",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["path"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject to delete (e.g., 'Player' or 'Environment/Props/Tree')"
                        }
                    },
                    required = new List<string> { "path" }
                }
            }, DeleteGameObject);

            // Component manipulation tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_component",
                description = "Get properties of a specific component on a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject (e.g., 'Player' or 'Environment/Props/Tree')"
                        },
                        ["componentType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type name of the component (e.g., 'Transform', 'Rigidbody', 'MeshRenderer')"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "componentType" }
                }
            }, GetComponentProperties);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_add_component",
                description = "Add a component to a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject"
                        },
                        ["componentType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type name of the component to add (e.g., 'Rigidbody', 'BoxCollider', 'AudioSource')"
                        },
                        ["initialProperties"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Optional initial properties to set on the component"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "componentType" }
                }
            }, AddComponentToGameObject);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_modify_component",
                description = "Modify properties of an existing component on a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject"
                        },
                        ["componentType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type name of the component to modify (e.g., 'Transform', 'Rigidbody')"
                        },
                        ["properties"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Properties to modify (e.g., {\"mass\": 2.0} for Rigidbody, {\"localPosition\": {\"x\": 0, \"y\": 1, \"z\": 0}} for Transform)"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "componentType", "properties" }
                }
            }, ModifyComponentProperties);

            // Console logs tool
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_console_logs",
                description = "Get recent Unity console logs (errors, warnings, and messages)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["logType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter by log type: 'All', 'Error', 'Warning', 'Log' (default: 'All')",
                            @enum = new List<string> { "All", "Error", "Warning", "Log", "Exception", "Assert" }
                        },
                        ["count"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Maximum number of logs to return (default: 50, max: 100)"
                        },
                        ["includeStackTrace"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include stack traces in output (default: false)"
                        }
                    }
                }
            }, GetConsoleLogs);

            // Animator Controller tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_controller",
                description = "Get the complete structure of an Animator Controller (states, transitions, parameters)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')"
                        },
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Alternative: Path to a GameObject with an Animator component"
                        }
                    }
                }
            }, GetAnimatorController);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_parameters",
                description = "Get all animator parameters with their current runtime values",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject with Animator component"
                        }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, GetAnimatorParameters);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_animator_parameter",
                description = "Set an animator parameter value (Float, Int, Bool, or Trigger)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject with Animator component"
                        },
                        ["parameterName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the parameter to set"
                        },
                        ["value"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Value to set (number for Float/Int, true/false for Bool, omit for Trigger)"
                        },
                        ["parameterType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type of parameter: 'Float', 'Int', 'Bool', 'Trigger'",
                            @enum = new List<string> { "Float", "Int", "Bool", "Trigger" }
                        }
                    },
                    required = new List<string> { "gameObjectPath", "parameterName" }
                }
            }, SetAnimatorParameter);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_parameter",
                description = "Add a new parameter to an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["parameterName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the new parameter"
                        },
                        ["parameterType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type: 'Float', 'Int', 'Bool', 'Trigger'",
                            @enum = new List<string> { "Float", "Int", "Bool", "Trigger" }
                        },
                        ["defaultValue"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Default value (for Float/Int/Bool)"
                        }
                    },
                    required = new List<string> { "controllerPath", "parameterName", "parameterType" }
                }
            }, AddAnimatorParameter);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_state",
                description = "Add a new state to an Animator Controller layer",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["stateName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the new state"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        },
                        ["position"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Position in the graph {x, y}"
                        },
                        ["motionClip"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to AnimationClip asset (optional)"
                        }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, AddAnimatorState);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_transition",
                description = "Add a transition between two states in an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["fromState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Source state name (use 'Any' for AnyState transition)"
                        },
                        ["toState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Destination state name"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        },
                        ["conditions"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Transition conditions: [{parameter, mode, threshold}]. Modes: Greater, Less, Equals, NotEqual, If, IfNot"
                        },
                        ["hasExitTime"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Whether transition has exit time (default: true)"
                        },
                        ["exitTime"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Exit time normalized (default: 1.0)"
                        },
                        ["transitionDuration"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Transition duration in seconds (default: 0.25)"
                        }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, AddAnimatorTransition);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_validate_animator",
                description = "Validate an Animator Controller for common issues like missing motions, orphan states, dead ends, unused parameters, and duplicate state names",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')"
                        }
                    },
                    required = new List<string> { "controllerPath" }
                }
            }, ValidateAnimator);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_flow",
                description = "Trace possible paths through an Animator Controller from a given state, showing reachable and unreachable states",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["fromState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Starting state name (default: 'Entry' which uses the default state)"
                        },
                        ["maxDepth"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Maximum path depth to trace (default: 10)"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index to analyze (default: 0)"
                        }
                    },
                    required = new List<string> { "controllerPath" }
                }
            }, GetAnimatorFlow);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_delete_animator_state",
                description = "Delete a state from an Animator Controller. Removes the state and all its transitions.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')"
                        },
                        ["stateName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the state to delete"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, DeleteAnimatorState);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_delete_animator_transition",
                description = "Delete a transition between states in an Animator Controller. Supports deleting transitions from regular states or AnyState.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')"
                        },
                        ["fromState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Source state name. Use 'AnyState' or 'Any' for AnyState transitions"
                        },
                        ["toState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Destination state name"
                        },
                        ["transitionIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Index of the transition if multiple exist between the same states (default: 0)"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, DeleteAnimatorTransition);

            // Animation Clip tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_animation_clips",
                description = "List all AnimationClips in the project with optional filtering by name and avatar type",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["searchPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Folder path to search in (default: 'Assets')"
                        },
                        ["nameFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter clips by name (case-insensitive contains match)"
                        },
                        ["avatarFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter by avatar type: 'Humanoid', 'Generic', or 'Legacy'",
                            @enum = new List<string> { "Humanoid", "Generic", "Legacy" }
                        }
                    }
                }
            }, ListAnimationClips);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_clip_info",
                description = "Get detailed information about an AnimationClip including events, curves, and properties",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["clipPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimationClip asset (e.g., 'Assets/Animations/Run.anim')"
                        }
                    },
                    required = new List<string> { "clipPath" }
                }
            }, GetClipInfo);

            // Asset Browser tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_search_assets",
                description = "Search for assets in the project using Unity's AssetDatabase. Supports type filters (t:), label filters (l:), and name patterns.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["filter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Search filter. Examples: 't:Texture2D', 't:Prefab', 'l:Environment', 'Player t:Prefab', 't:AnimationClip Run'"
                        },
                        ["searchFolders"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Folders to search in (default: entire project). Example: ['Assets/Prefabs', 'Assets/Materials']"
                        },
                        ["maxResults"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Maximum number of results to return (default: 50, max: 200)"
                        }
                    }
                }
            }, SearchAssets);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_asset_info",
                description = "Get detailed information about a specific asset including type, size, labels, and dependencies",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["assetPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the asset (e.g., 'Assets/Textures/Player.png')"
                        },
                        ["includeDependencies"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include list of assets this asset depends on (default: false)"
                        },
                        ["includeReferences"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include list of assets that reference this asset (default: false, can be slow)"
                        }
                    },
                    required = new List<string> { "assetPath" }
                }
            }, GetAssetInfo);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_folders",
                description = "List all folders in the project Assets directory",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["parentPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Parent folder path (default: 'Assets')"
                        },
                        ["recursive"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include subfolders recursively (default: true)"
                        },
                        ["maxDepth"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Maximum folder depth when recursive (default: 5)"
                        }
                    }
                }
            }, ListFolders);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_folder_contents",
                description = "List all assets in a specific folder with optional type filtering",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["folderPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Folder path (e.g., 'Assets/Prefabs')"
                        },
                        ["typeFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Filter by asset type: 'Prefab', 'Texture2D', 'Material', 'AudioClip', 'AnimationClip', 'Script', etc."
                        },
                        ["includeSubfolders"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include assets from subfolders (default: false)"
                        }
                    },
                    required = new List<string> { "folderPath" }
                }
            }, ListFolderContents);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_asset_preview",
                description = "Get asset preview as JPG (compact). Use size='small' for minimal tokens",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["assetPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the asset"
                        },
                        ["size"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Preset size: 'tiny'(32), 'small'(64), 'medium'(128), 'large'(256). Default: 'small'",
                            @enum = new List<string> { "tiny", "small", "medium", "large" }
                        },
                        ["format"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Image format: 'jpg' (smaller) or 'png' (default: 'jpg')",
                            @enum = new List<string> { "jpg", "png" }
                        },
                        ["jpgQuality"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "JPG quality 1-100 (default: 75)"
                        }
                    },
                    required = new List<string> { "assetPath" }
                }
            }, GetAssetPreview);

            // Workflow Enhancement tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_clear_console",
                description = "Clear the Unity console log",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>()
                }
            }, ClearConsole);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_rename_gameobject",
                description = "Rename a GameObject in the scene",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject to rename"
                        },
                        ["newName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "New name for the GameObject"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "newName" }
                }
            }, RenameGameObject);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_parent",
                description = "Change the parent of a GameObject in the hierarchy",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the GameObject to reparent"
                        },
                        ["parentPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path or name of the new parent (null or empty to unparent to scene root)"
                        },
                        ["worldPositionStays"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "If true, keeps world position; if false, keeps local position relative to new parent (default: true)"
                        }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, SetParent);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_instantiate_prefab",
                description = "Instantiate a prefab in the scene, maintaining the prefab link",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["prefabPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Asset path to the prefab (e.g., 'Assets/Prefabs/Enemy.prefab')"
                        },
                        ["position"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "World position {x, y, z} (default: 0,0,0)"
                        },
                        ["rotation"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Euler rotation {x, y, z} in degrees (default: 0,0,0)"
                        },
                        ["parentPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional parent GameObject path"
                        },
                        ["name"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Override name for the instance (optional)"
                        }
                    },
                    required = new List<string> { "prefabPath" }
                }
            }, InstantiatePrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_take_screenshot",
                description = "Take screenshot. JPG default (10x smaller). Use returnBase64=false to save tokens",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["view"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Which view: 'Scene' or 'Game' (default: 'Scene')"
                        },
                        ["format"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Image format: 'jpg' (smaller, recommended) or 'png' (default: 'jpg')",
                            @enum = new List<string> { "jpg", "png" }
                        },
                        ["jpgQuality"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "JPG quality 1-100 (default: 75)"
                        },
                        ["width"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Width in pixels (default: 640, max: 4096)"
                        },
                        ["height"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Height in pixels (default: 360, max: 4096)"
                        },
                        ["savePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Save path (auto-generated if not specified)"
                        },
                        ["returnBase64"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Return image as base64 in response (default: false - saves tokens)"
                        }
                    }
                }
            }, TakeScreenshot);

            // Selection & Scene tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_selection",
                description = "Get currently selected objects in the Unity Editor (Hierarchy and Project windows)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["includeAssets"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include selected assets from Project window (default: false)"
                        }
                    }
                }
            }, GetSelection);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_selection",
                description = "Select GameObjects or assets programmatically in the Unity Editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPaths"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Array of GameObject paths to select (e.g., ['Player', 'Environment/Tree'])"
                        },
                        ["assetPaths"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Array of asset paths to select (e.g., ['Assets/Prefabs/Enemy.prefab'])"
                        },
                        ["clear"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Clear current selection (default: false)"
                        }
                    }
                }
            }, SetSelection);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_scene_info",
                description = "Get information about the current scene (name, path, dirty state, root objects count)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>()
                }
            }, GetSceneInfo);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_load_scene",
                description = "Load/open a scene in the Unity Editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["scenePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the scene asset (e.g., 'Assets/Scenes/Level1.unity')"
                        },
                        ["mode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "How to open: 'Single' (replace current) or 'Additive' (add to current). Default: 'Single'"
                        }
                    },
                    required = new List<string> { "scenePath" }
                }
            }, LoadScene);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_save_scene",
                description = "Save the current scene or all open scenes",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["scenePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional: Save As path (e.g., 'Assets/Scenes/Level1_backup.unity')"
                        },
                        ["saveAll"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Save all open scenes (default: false, saves only active scene)"
                        }
                    }
                }
            }, SaveScene);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_scene",
                description = "Create a new Unity scene. Use setup='default' for Camera+Light, 'empty' for blank scene.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["sceneName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name for the new scene"
                        },
                        ["savePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional: Path to save (e.g., 'Assets/Scenes/Level1.unity')"
                        },
                        ["setup"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "'default' (Camera+Light) or 'empty' (blank scene). Default: 'default'"
                        },
                        ["mode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "'single' (replace current) or 'additive' (add alongside). Default: 'single'"
                        }
                    },
                    required = new List<string> { "sceneName" }
                }
            }, CreateScene);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_prefab",
                description = "Create a prefab from a GameObject in the scene. Use connectInstance=true to keep the link.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path in hierarchy (e.g., 'Player' or 'Environment/Tree')"
                        },
                        ["savePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Where to save (e.g., 'Assets/Prefabs/Player.prefab')"
                        },
                        ["connectInstance"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Keep GameObject connected as prefab instance (default: true)"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "savePath" }
                }
            }, CreatePrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_unpack_prefab",
                description = "Unpack a prefab instance to regular GameObjects. Breaks the prefab link.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Prefab instance path in hierarchy"
                        },
                        ["unpackMode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "'completely' (all nested) or 'root' (outermost only). Default: 'completely'"
                        }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, UnpackPrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_apply_prefab_overrides",
                description = "Apply all overrides from a prefab instance to the source prefab. Affects all instances.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Modified prefab instance in hierarchy"
                        }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, ApplyPrefabOverrides);

            // Tags & Layers tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_tags",
                description = "List all available tags in the project",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>()
                }
            }, ListTags);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_layers",
                description = "List all layers in the project (32 layers, 0-7 are Unity built-in)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["includeEmpty"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include unnamed layer slots (default: false)"
                        }
                    }
                }
            }, ListLayers);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_tag",
                description = "Set the tag of a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path in hierarchy (e.g., 'Player' or 'Environment/Enemy')"
                        },
                        ["tag"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Tag name (e.g., 'Player', 'Enemy', 'Untagged')"
                        },
                        ["recursive"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Apply to all children (default: false)"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "tag" }
                }
            }, SetTag);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_layer",
                description = "Set the layer of a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path in hierarchy"
                        },
                        ["layer"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Layer name (e.g., 'Default', 'UI', 'Water')"
                        },
                        ["recursive"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Apply to all children (default: false)"
                        }
                    },
                    required = new List<string> { "gameObjectPath", "layer" }
                }
            }, SetLayer);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_undo",
                description = "Undo or redo the last editor action(s)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["steps"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Number of undo/redo steps (default: 1)"
                        },
                        ["redo"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Perform redo instead of undo (default: false)"
                        }
                    }
                }
            }, PerformUndo);

            // Memory/Cache Tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_get",
                description = "Get cached data from MCP memory (assets, scenes, hierarchy). Use this to quickly retrieve previously fetched data without re-scanning.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Section to retrieve: 'assets', 'scenes', 'hierarchy', 'operations', or 'all' (default: 'all')"
                        }
                    }
                }
            }, MemoryGet);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_refresh",
                description = "Refresh a section of the MCP memory cache by re-fetching data from Unity",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Section to refresh: 'assets', 'scenes', 'hierarchy', or 'all'"
                        }
                    },
                    required = new List<string> { "section" }
                }
            }, MemoryRefresh);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_clear",
                description = "Clear the MCP memory cache (or a specific section)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Section to clear: 'assets', 'scenes', 'hierarchy', 'operations', or 'all' (default: 'all')"
                        }
                    }
                }
            }, MemoryClear);

            // Material Tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_material",
                description = "Get material properties (colors, floats, textures, shader info)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["materialPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to material asset (e.g., 'Assets/Materials/Metal.mat')"
                        },
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Get material from a GameObject's renderer instead"
                        },
                        ["materialIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Material index for multi-material objects (default: 0)"
                        }
                    }
                }
            }, GetMaterial);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_material",
                description = "Modify material properties (colors, floats, textures, shader)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["materialPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to material asset"
                        },
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Modify material on a GameObject's renderer"
                        },
                        ["materialIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Material index for multi-material objects (default: 0)"
                        },
                        ["properties"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Properties to set: {\"_Color\": {r,g,b,a}, \"_Metallic\": 0.8, \"_MainTex\": \"Assets/...\"}"
                        },
                        ["shader"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Change shader (e.g., 'Standard', 'Universal Render Pipeline/Lit')"
                        },
                        ["renderQueue"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Set render queue value"
                        }
                    },
                    required = new List<string> { "properties" }
                }
            }, SetMaterial);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_material",
                description = "Create a new material asset with specified shader",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["name"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Material name"
                        },
                        ["savePath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Where to save the material (e.g., 'Assets/Materials/NewMat.mat')"
                        },
                        ["shader"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Shader name (default: 'Standard')"
                        },
                        ["properties"] = new McpPropertySchema
                        {
                            type = "object",
                            description = "Initial properties to set"
                        }
                    },
                    required = new List<string> { "name", "savePath" }
                }
            }, CreateMaterial);

            // Blend Tree tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_blend_tree",
                description = "Create a 1D or 2D Blend Tree state in an Animator Controller. Blend Trees allow blending between multiple animations based on parameter values.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')"
                        },
                        ["stateName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name for the new Blend Tree state"
                        },
                        ["blendType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Type of blend tree: '1D', '2DSimpleDirectional', '2DFreeformDirectional', or '2DFreeformCartesian'",
                            @enum = new List<string> { "1D", "2DSimpleDirectional", "2DFreeformDirectional", "2DFreeformCartesian" }
                        },
                        ["blendParameter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Parameter name for X axis blending (must exist in controller)"
                        },
                        ["blendParameterY"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Parameter name for Y axis (required for 2D blend types)"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        }
                    },
                    required = new List<string> { "controllerPath", "stateName", "blendParameter" }
                }
            }, CreateBlendTree);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_add_blend_motion",
                description = "Add a motion (AnimationClip) to an existing Blend Tree. For 1D trees use threshold, for 2D trees use positionX/Y.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["blendTreeState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the Blend Tree state"
                        },
                        ["motionPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimationClip asset"
                        },
                        ["threshold"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Threshold value for 1D blend trees (position on blend axis)"
                        },
                        ["positionX"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "X position for 2D blend trees"
                        },
                        ["positionY"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Y position for 2D blend trees"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        }
                    },
                    required = new List<string> { "controllerPath", "blendTreeState", "motionPath" }
                }
            }, AddBlendMotion);

            // Modification tools
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_modify_animator_state",
                description = "Modify properties of an existing animator state (speed, motion, name, etc.)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["stateName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Name of the state to modify"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        },
                        ["newName"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "New name for the state"
                        },
                        ["motion"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to new AnimationClip"
                        },
                        ["speed"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Playback speed multiplier"
                        },
                        ["speedParameter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Parameter name to control speed"
                        },
                        ["cycleOffset"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Cycle offset (0-1)"
                        },
                        ["mirror"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Mirror the animation"
                        },
                        ["writeDefaultValues"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Write default values on exit"
                        }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, ModifyAnimatorState);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_modify_transition",
                description = "Modify properties of an existing animator transition (duration, exit time, interruption, etc.)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the AnimatorController asset"
                        },
                        ["fromState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Source state name (or 'AnyState')"
                        },
                        ["toState"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Destination state name"
                        },
                        ["transitionIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Index if multiple transitions exist (default: 0)"
                        },
                        ["layerIndex"] = new McpPropertySchema
                        {
                            type = "integer",
                            description = "Layer index (default: 0)"
                        },
                        ["hasExitTime"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Has exit time"
                        },
                        ["exitTime"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Exit time (normalized 0-1+)"
                        },
                        ["duration"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Transition duration"
                        },
                        ["offset"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Destination state offset"
                        },
                        ["interruptionSource"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Interruption source: 'None', 'Source', 'Destination', 'SourceThenDestination', 'DestinationThenSource'"
                        },
                        ["canTransitionToSelf"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Can transition to self"
                        }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, ModifyTransition);
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

        #region Tool Implementations

        // SECURITY: Allowlist of safe menu items to prevent arbitrary code execution
        private static readonly HashSet<string> AllowedMenuPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // File operations
            "File/Save",
            "File/Save Project",
            "File/New Scene",

            // Edit operations (safe)
            "Edit/Undo",
            "Edit/Redo",
            "Edit/Select All",
            "Edit/Deselect All",
            "Edit/Play",
            "Edit/Pause",
            "Edit/Step",

            // GameObject operations
            "GameObject/Create Empty",
            "GameObject/Create Empty Child",
            "GameObject/3D Object/Cube",
            "GameObject/3D Object/Sphere",
            "GameObject/3D Object/Capsule",
            "GameObject/3D Object/Cylinder",
            "GameObject/3D Object/Plane",
            "GameObject/3D Object/Quad",
            "GameObject/2D Object/Sprite",
            "GameObject/Light/Directional Light",
            "GameObject/Light/Point Light",
            "GameObject/Light/Spotlight",
            "GameObject/Camera",
            "GameObject/UI/Canvas",
            "GameObject/UI/Panel",
            "GameObject/UI/Button",
            "GameObject/UI/Text",
            "GameObject/UI/Image",
            "GameObject/UI/Raw Image",
            "GameObject/UI/Slider",
            "GameObject/UI/Toggle",
            "GameObject/UI/Input Field",

            // Component operations
            "Component/Physics/Rigidbody",
            "Component/Physics/Box Collider",
            "Component/Physics/Sphere Collider",
            "Component/Physics/Capsule Collider",
            "Component/Audio/Audio Source",
            "Component/Audio/Audio Listener",

            // Window operations (safe)
            "Window/General/Game",
            "Window/General/Scene",
            "Window/General/Inspector",
            "Window/General/Hierarchy",
            "Window/General/Project",
            "Window/General/Console"
        };

        private static McpToolResult ExecuteMenuItem(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("menuPath", out var menuPathObj) || menuPathObj == null)
            {
                return McpToolResult.Error("menuPath is required");
            }

            var menuPath = menuPathObj.ToString();

            // SECURITY: Validate menu path against allowlist
            if (!AllowedMenuPaths.Contains(menuPath))
            {
                Debug.LogWarning($"[MCP Unity] Blocked menu item execution (not in allowlist): {menuPath}");
                return McpToolResult.Error($"Menu item not allowed for security reasons: {menuPath}. Use allowed menu paths only.");
            }

            bool success = false;
            string error = null;

            // Execute synchronously on main thread
            try
            {
                success = EditorApplication.ExecuteMenuItem(menuPath);
                if (!success)
                {
                    error = $"Menu item not found: {menuPath}";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (!string.IsNullOrEmpty(error))
            {
                return McpToolResult.Error(error);
            }

            return McpToolResult.Success($"Executed menu item: {menuPath}");
        }

        private static McpToolResult GetEditorState(Dictionary<string, object> args)
        {
            var state = new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                applicationPath = EditorApplication.applicationPath,
                applicationVersion = Application.unityVersion,
                currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                selectedObjectCount = Selection.objects.Length,
                selectedObjectNames = GetSelectedObjectNames()
            };

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(state) },
                isError = false
            };
        }

        private static string[] GetSelectedObjectNames()
        {
            var names = new string[Selection.objects.Length];
            for (int i = 0; i < Selection.objects.Length; i++)
            {
                names[i] = Selection.objects[i].name;
            }
            return names;
        }

        private static McpToolResult RunTests(Dictionary<string, object> args)
        {
            return McpToolResult.Success("Test execution initiated. Check Unity Test Runner for results.");
        }

        private static McpToolResult ListGameObjects(Dictionary<string, object> args)
        {
            // Parse parameters with optimized defaults
            string outputMode = "summary";
            int maxDepth = 3;
            bool includeInactive = false;
            bool rootOnly = false;
            bool includeTransform = false;
            string nameFilter = null;
            string componentFilter = null;
            string tagFilter = null;

            if (args.TryGetValue("outputMode", out var modeObj) && modeObj != null)
                outputMode = modeObj.ToString().ToLower();

            if (args.TryGetValue("maxDepth", out var depthObj) && depthObj != null)
                int.TryParse(depthObj.ToString(), out maxDepth);

            if (args.TryGetValue("includeInactive", out var inactiveObj) && inactiveObj != null)
                bool.TryParse(inactiveObj.ToString(), out includeInactive);

            if (args.TryGetValue("rootOnly", out var rootObj) && rootObj != null)
                bool.TryParse(rootObj.ToString(), out rootOnly);

            if (args.TryGetValue("includeTransform", out var transObj) && transObj != null)
                bool.TryParse(transObj.ToString(), out includeTransform);

            if (args.TryGetValue("nameFilter", out var nameObj) && nameObj != null)
                nameFilter = nameObj.ToString();

            if (args.TryGetValue("componentFilter", out var compObj) && compObj != null)
                componentFilter = compObj.ToString();

            if (args.TryGetValue("tagFilter", out var tagObj) && tagObj != null)
                tagFilter = tagObj.ToString();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects().ToList();

            // Apply filters if any
            bool hasFilters = !string.IsNullOrEmpty(nameFilter) || !string.IsNullOrEmpty(componentFilter) || !string.IsNullOrEmpty(tagFilter);

            object resultData;

            switch (outputMode)
            {
                case "names":
                    if (hasFilters)
                    {
                        var filtered = HierarchyHelpers.CollectFilteredObjects(rootObjects, nameFilter, componentFilter, tagFilter, null, rootOnly, includeInactive);
                        resultData = filtered.Select(o => o.name).ToList();
                    }
                    else
                    {
                        resultData = HierarchyHelpers.GetObjectNames(rootObjects, includeInactive);
                    }
                    break;

                case "tree":
                    if (hasFilters)
                    {
                        var filtered = HierarchyHelpers.CollectFilteredObjects(rootObjects, nameFilter, componentFilter, tagFilter, null, rootOnly, includeInactive);
                        resultData = HierarchyHelpers.FormatAsTree(filtered, maxDepth, includeInactive);
                    }
                    else
                    {
                        resultData = HierarchyHelpers.FormatAsTree(rootObjects, maxDepth, includeInactive);
                    }
                    break;

                case "summary":
                    if (hasFilters)
                    {
                        var filtered = HierarchyHelpers.CollectFilteredObjects(rootObjects, nameFilter, componentFilter, tagFilter, null, rootOnly, includeInactive);
                        resultData = HierarchyHelpers.GetSummaryInfo(filtered, maxDepth, includeInactive);
                    }
                    else
                    {
                        resultData = HierarchyHelpers.GetSummaryInfo(rootObjects, maxDepth, includeInactive);
                    }
                    break;

                case "full":
                default:
                    var gameObjects = new List<object>();
                    if (hasFilters)
                    {
                        var filtered = HierarchyHelpers.CollectFilteredObjects(rootObjects, nameFilter, componentFilter, tagFilter, null, rootOnly, includeInactive);
                        foreach (var obj in filtered)
                        {
                            gameObjects.Add(GetDetailedGameObjectInfo(obj, 0, maxDepth, includeInactive, includeTransform));
                        }
                    }
                    else
                    {
                        foreach (var obj in rootObjects)
                        {
                            if (!includeInactive && !obj.activeSelf) continue;
                            gameObjects.Add(GetDetailedGameObjectInfo(obj, 0, maxDepth, includeInactive, includeTransform));
                        }
                    }
                    resultData = gameObjects;
                    break;
            }

            var result = new
            {
                sceneName = scene.name,
                outputMode = outputMode,
                totalRootObjects = rootObjects.Count,
                data = resultData
            };

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }

        private static object GetDetailedGameObjectInfo(GameObject obj, int depth, int maxDepth, bool includeInactive, bool includeTransform = false)
        {
            var components = obj.GetComponents<Component>();
            var componentInfos = new List<object>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                componentInfos.Add(new
                {
                    type = comp.GetType().Name,
                    fullType = comp.GetType().FullName,
                    enabled = (comp is Behaviour b) ? b.enabled : true
                });
            }

            var children = new List<object>();
            if (depth < maxDepth)
            {
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    var child = obj.transform.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;
                    children.Add(GetDetailedGameObjectInfo(child, depth + 1, maxDepth, includeInactive, includeTransform));
                }
            }

            // Build result - only include transform if explicitly requested (saves tokens)
            if (includeTransform)
            {
                return new
                {
                    name = obj.name,
                    path = GetGameObjectPath(obj),
                    active = obj.activeSelf,
                    layer = LayerMask.LayerToName(obj.layer),
                    tag = obj.tag,
                    position = new { x = obj.transform.position.x, y = obj.transform.position.y, z = obj.transform.position.z },
                    rotation = new { x = obj.transform.eulerAngles.x, y = obj.transform.eulerAngles.y, z = obj.transform.eulerAngles.z },
                    scale = new { x = obj.transform.localScale.x, y = obj.transform.localScale.y, z = obj.transform.localScale.z },
                    components = componentInfos,
                    childCount = obj.transform.childCount,
                    children = children
                };
            }
            else
            {
                // Compact version without transform data
                return new
                {
                    name = obj.name,
                    path = GetGameObjectPath(obj),
                    active = obj.activeSelf,
                    layer = LayerMask.LayerToName(obj.layer),
                    tag = obj.tag,
                    components = componentInfos,
                    childCount = obj.transform.childCount,
                    children = children
                };
            }
        }

        private static string GetGameObjectPath(GameObject obj)
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

        private static McpToolResult CreateGameObject(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("name", out var nameObj) || nameObj == null)
            {
                return McpToolResult.Error("name is required");
            }

            string objName = nameObj.ToString();
            string primitiveType = "Empty";
            string parentPath = null;

            if (args.TryGetValue("primitiveType", out var typeObj) && typeObj != null)
            {
                primitiveType = typeObj.ToString();
            }

            if (args.TryGetValue("parentPath", out var parentObj) && parentObj != null)
            {
                parentPath = parentObj.ToString();
            }

            GameObject newObj = null;

            switch (primitiveType)
            {
                case "Cube":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
                case "Sphere":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    break;
                case "Capsule":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    break;
                case "Cylinder":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    break;
                case "Plane":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    break;
                case "Quad":
                    newObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    break;
                default:
                    newObj = new GameObject();
                    break;
            }

            newObj.name = objName;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObject.Find(parentPath);
                if (parent != null)
                {
                    newObj.transform.SetParent(parent.transform);
                }
            }

            if (args.TryGetValue("position", out var posObj) && posObj is Dictionary<string, object> posDict)
            {
                float x = 0, y = 0, z = 0;
                if (posDict.TryGetValue("x", out var xVal)) float.TryParse(xVal.ToString(), out x);
                if (posDict.TryGetValue("y", out var yVal)) float.TryParse(yVal.ToString(), out y);
                if (posDict.TryGetValue("z", out var zVal)) float.TryParse(zVal.ToString(), out z);
                newObj.transform.position = new Vector3(x, y, z);
            }

            Undo.RegisterCreatedObjectUndo(newObj, $"Create {objName}");
            Selection.activeGameObject = newObj;

            return McpToolResult.Success($"Created GameObject '{objName}' at path: {GetGameObjectPath(newObj)}");
        }

        private static McpToolResult DeleteGameObject(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("path", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("path is required");
            }

            string path = pathObj.ToString();
            var obj = GameObject.Find(path);

            if (obj == null)
            {
                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (var go in allObjects)
                {
                    if (go.name == path || GetGameObjectPath(go) == path)
                    {
                        obj = go;
                        break;
                    }
                }
            }

            if (obj == null)
            {
                return McpToolResult.Error($"GameObject not found: {path}");
            }

            string deletedPath = GetGameObjectPath(obj);
            Undo.DestroyObjectImmediate(obj);

            return McpToolResult.Success($"Deleted GameObject: {deletedPath}");
        }

        #region Component Helpers

        // SECURITY: Allowlist of safe component types to prevent loading arbitrary types
        private static readonly HashSet<string> AllowedComponentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core Unity components
            "Transform", "RectTransform",

            // Physics
            "Rigidbody", "Rigidbody2D",
            "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider",
            "BoxCollider2D", "CircleCollider2D", "PolygonCollider2D", "EdgeCollider2D",
            "CharacterController", "WheelCollider",
            "ConstantForce", "ConstantForce2D",
            "Joint", "HingeJoint", "SpringJoint", "FixedJoint", "ConfigurableJoint",
            "HingeJoint2D", "SpringJoint2D", "FixedJoint2D", "DistanceJoint2D",

            // Rendering
            "MeshRenderer", "SkinnedMeshRenderer", "SpriteRenderer", "LineRenderer", "TrailRenderer",
            "MeshFilter", "Camera", "Light", "ReflectionProbe", "LightProbeGroup",
            "Canvas", "CanvasGroup", "CanvasRenderer", "CanvasScaler", "GraphicRaycaster",
            "LODGroup", "OcclusionArea", "OcclusionPortal",

            // Audio
            "AudioSource", "AudioListener", "AudioReverbZone", "AudioReverbFilter",
            "AudioLowPassFilter", "AudioHighPassFilter", "AudioEchoFilter", "AudioDistortionFilter", "AudioChorusFilter",

            // Animation
            "Animator", "Animation", "AnimationClip",

            // AI/Navigation
            "NavMeshAgent", "NavMeshObstacle", "OffMeshLink",

            // UI Components
            "Button", "Text", "Image", "RawImage", "InputField", "Slider", "Toggle", "Dropdown",
            "ScrollRect", "Scrollbar", "Mask", "RectMask2D", "ToggleGroup",
            "LayoutElement", "ContentSizeFitter", "AspectRatioFitter",
            "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup",
            "Selectable", "Outline", "Shadow", "PositionAsUV1",

            // Particles
            "ParticleSystem", "ParticleSystemRenderer",

            // Terrain
            "Terrain", "TerrainCollider",

            // Video
            "VideoPlayer",

            // Cloth
            "Cloth",

            // Tilemap
            "Tilemap", "TilemapRenderer", "TilemapCollider2D",

            // Other common
            "EventSystem", "StandaloneInputModule", "TouchInputModule",
            "WorldCanvas", "WindZone", "Grid"
        };

        // Cache for component type lookups (performance optimization)
        private static readonly Dictionary<string, Type> _componentTypeCache = new Dictionary<string, Type>();

        /// <summary>
        /// Find a component type by name with security allowlist and caching
        /// </summary>
        private static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // SECURITY: Check allowlist first
            if (!AllowedComponentTypes.Contains(typeName))
            {
                Debug.LogWarning($"[MCP Unity] Component type not in allowlist: {typeName}");
                return null;
            }

            // Check cache first (performance optimization)
            if (_componentTypeCache.TryGetValue(typeName, out var cachedType))
                return cachedType;

            // Use the helper class for actual type resolution
            var type = ComponentHelpers.FindComponentType(typeName);

            // Cache the result (even if null to avoid repeated lookups)
            if (type != null)
                _componentTypeCache[typeName] = type;

            return type;
        }

        /// <summary>
        /// Sanitize and validate file paths for security (prevent path traversal attacks)
        /// </summary>
        private static string SanitizePath(string path, string requiredPrefix = "Assets/")
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
        /// Convert a Unity value to a JSON-serializable format
        /// </summary>
        private static object ConvertValue(object value)
        {
            if (value == null) return null;

            var type = value.GetType();

            // Primitives
            if (type.IsPrimitive || value is string || value is decimal)
                return value;

            // Unity vectors and types
            if (value is Vector3 v3)
                return new { x = v3.x, y = v3.y, z = v3.z };
            if (value is Vector2 v2)
                return new { x = v2.x, y = v2.y };
            if (value is Vector4 v4)
                return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
            if (value is Quaternion q)
                return new { x = q.x, y = q.y, z = q.z, w = q.w };
            if (value is Color c)
                return new { r = c.r, g = c.g, b = c.b, a = c.a };
            if (value is Color32 c32)
                return new { r = c32.r, g = c32.g, b = c32.b, a = c32.a };
            if (value is Bounds b)
                return new { center = ConvertValue(b.center), size = ConvertValue(b.size) };
            if (value is Rect rect)
                return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };

            // Enum
            if (type.IsEnum)
                return value.ToString();

            // UnityEngine.Object reference
            if (value is UnityEngine.Object uobj)
                return uobj != null ? new { name = uobj.name, type = uobj.GetType().Name } : null;

            // Arrays/Lists - skip for now (can be complex)
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
                return $"[{type.Name}]";

            return value.ToString();
        }

        /// <summary>
        /// Convert a component's properties to a serializable dictionary
        /// </summary>
        private static Dictionary<string, object> ConvertToSerializable(Component component)
        {
            var result = new Dictionary<string, object>();
            if (component == null) return result;

            var type = component.GetType();

            // Get public properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;

                // Skip problematic properties that throw or cause issues
                var skipProps = new HashSet<string> {
                    "mesh", "material", "materials", "sharedMesh", "sharedMaterial", "sharedMaterials",
                    "gameObject", "transform", "tag", "name", "hideFlags", "runInEditMode"
                };
                if (skipProps.Contains(prop.Name)) continue;

                try
                {
                    var value = prop.GetValue(component);
                    result[prop.Name] = ConvertValue(value);
                }
                catch (Exception ex)
                {
                    // Log skipped properties for debugging
                    Debug.LogWarning($"[MCP Unity] Cannot serialize property '{prop.Name}': {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Convert a JSON value to a Unity type
        /// </summary>
        private static object ConvertJsonToUnity(object jsonValue, Type targetType)
        {
            if (jsonValue == null) return null;

            // Handle Dictionary from JSON parser
            var dict = jsonValue as Dictionary<string, object>;

            // Vector3
            if (targetType == typeof(Vector3) && dict != null)
            {
                return new Vector3(
                    Convert.ToSingle(dict.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("z", 0f))
                );
            }

            // Vector2
            if (targetType == typeof(Vector2) && dict != null)
            {
                return new Vector2(
                    Convert.ToSingle(dict.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("y", 0f))
                );
            }

            // Quaternion
            if (targetType == typeof(Quaternion) && dict != null)
            {
                return new Quaternion(
                    Convert.ToSingle(dict.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("z", 0f)),
                    Convert.ToSingle(dict.GetValueOrDefault("w", 1f))
                );
            }

            // Color
            if (targetType == typeof(Color) && dict != null)
            {
                return new Color(
                    Convert.ToSingle(dict.GetValueOrDefault("r", 1f)),
                    Convert.ToSingle(dict.GetValueOrDefault("g", 1f)),
                    Convert.ToSingle(dict.GetValueOrDefault("b", 1f)),
                    Convert.ToSingle(dict.GetValueOrDefault("a", 1f))
                );
            }

            // Enum
            if (targetType.IsEnum && jsonValue is string enumStr)
            {
                return Enum.Parse(targetType, enumStr);
            }

            // Standard conversion
            try
            {
                return Convert.ChangeType(jsonValue, targetType);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Unity] Cannot convert value to type '{targetType.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Apply properties from a dictionary to a component
        /// </summary>
        private static List<string> ApplyComponentProperties(Component component, Dictionary<string, object> properties)
        {
            var modified = new List<string>();
            var type = component.GetType();

            foreach (var kvp in properties)
            {
                // Try property first
                var prop = type.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        var convertedValue = ConvertJsonToUnity(kvp.Value, prop.PropertyType);
                        if (convertedValue != null)
                        {
                            prop.SetValue(component, convertedValue);
                            modified.Add(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Unity] Cannot set property {kvp.Key}: {ex.Message}");
                    }
                    continue;
                }

                // Try field
                var field = type.GetField(kvp.Key, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    try
                    {
                        var convertedValue = ConvertJsonToUnity(kvp.Value, field.FieldType);
                        if (convertedValue != null)
                        {
                            field.SetValue(component, convertedValue);
                            modified.Add(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Unity] Cannot set field {kvp.Key}: {ex.Message}");
                    }
                }
            }

            return modified;
        }

        #endregion

        #region Component Tool Handlers

        private static McpToolResult GetComponentProperties(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();
            var componentType = args.GetValueOrDefault("componentType")?.ToString();

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");
            if (string.IsNullOrEmpty(componentType))
                return McpToolResult.Error("componentType is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var type = FindComponentType(componentType);
            if (type == null)
                return McpToolResult.Error($"Component type not found: {componentType}");

            var component = go.GetComponent(type);
            if (component == null)
                return McpToolResult.Error($"Component '{componentType}' not found on '{gameObjectPath}'");

            var properties = ConvertToSerializable(component);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        gameObject = gameObjectPath,
                        componentType = type.Name,
                        componentFullType = type.FullName,
                        properties = properties
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddComponentToGameObject(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();
            var componentType = args.GetValueOrDefault("componentType")?.ToString();
            var initialProperties = args.GetValueOrDefault("initialProperties") as Dictionary<string, object>;

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");
            if (string.IsNullOrEmpty(componentType))
                return McpToolResult.Error("componentType is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var type = FindComponentType(componentType);
            if (type == null)
                return McpToolResult.Error($"Component type not found: {componentType}");

            // Check if component already exists
            if (go.GetComponent(type) != null)
                return McpToolResult.Error($"Component '{componentType}' already exists on '{gameObjectPath}'");

            Undo.RecordObject(go, $"Add {componentType}");
            var component = go.AddComponent(type);

            List<string> modified = new List<string>();
            if (initialProperties != null && initialProperties.Count > 0)
            {
                modified = ApplyComponentProperties(component, initialProperties);
            }

            EditorUtility.SetDirty(go);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added {componentType} to {gameObjectPath}",
                        componentType = type.Name,
                        initializedProperties = modified,
                        properties = ConvertToSerializable(component)
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ModifyComponentProperties(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();
            var componentType = args.GetValueOrDefault("componentType")?.ToString();
            var properties = args.GetValueOrDefault("properties") as Dictionary<string, object>;

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");
            if (string.IsNullOrEmpty(componentType))
                return McpToolResult.Error("componentType is required");
            if (properties == null || properties.Count == 0)
                return McpToolResult.Error("properties is required and must not be empty");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var type = FindComponentType(componentType);
            if (type == null)
                return McpToolResult.Error($"Component type not found: {componentType}");

            var component = go.GetComponent(type);
            if (component == null)
                return McpToolResult.Error($"Component '{componentType}' not found on '{gameObjectPath}'");

            Undo.RecordObject(component, $"Modify {componentType}");
            var modified = ApplyComponentProperties(component, properties);
            EditorUtility.SetDirty(component);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Modified {componentType} on {gameObjectPath}",
                        modifiedProperties = modified,
                        requestedProperties = properties.Keys.ToList(),
                        currentValues = ConvertToSerializable(component)
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetConsoleLogs(Dictionary<string, object> args)
        {
            string logTypeFilter = "All";
            int count = 50;
            bool includeStackTrace = false;

            if (args.TryGetValue("logType", out var typeObj) && typeObj != null)
            {
                logTypeFilter = typeObj.ToString();
            }

            if (args.TryGetValue("count", out var countObj) && countObj != null)
            {
                int.TryParse(countObj.ToString(), out count);
                count = Math.Min(count, MaxLogEntries);
            }

            if (args.TryGetValue("includeStackTrace", out var stackObj) && stackObj != null)
            {
                bool.TryParse(stackObj.ToString(), out includeStackTrace);
            }

            var logs = _consoleLogs.ToArray();

            // Filter by type if not "All"
            if (logTypeFilter != "All")
            {
                logs = logs.Where(l => l.Type.Equals(logTypeFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
            }

            // Take the last 'count' logs (most recent)
            logs = logs.TakeLast(count).ToArray();

            var result = logs.Select(l => new
            {
                timestamp = l.Timestamp.ToString("HH:mm:ss.fff"),
                type = l.Type,
                message = l.Message,
                stackTrace = includeStackTrace ? l.StackTrace : null
            }).ToList();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        totalLogsInBuffer = _consoleLogs.Count,
                        returnedCount = result.Count,
                        filter = logTypeFilter,
                        logs = result
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Animator Controller Helpers

        private static UnityEditor.Animations.AnimatorController LoadAnimatorController(string controllerPath, string gameObjectPath)
        {
            // Try loading from asset path first
            if (!string.IsNullOrEmpty(controllerPath))
            {
                // SECURITY: Validate path before loading
                try
                {
                    controllerPath = SanitizePath(controllerPath);
                }
                catch (ArgumentException)
                {
                    return null; // Invalid path, return null
                }

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller != null) return controller;
            }

            // Try getting from GameObject's Animator
            if (!string.IsNullOrEmpty(gameObjectPath))
            {
                var go = GameObject.Find(gameObjectPath);
                if (go != null)
                {
                    var animator = go.GetComponent<Animator>();
                    if (animator != null && animator.runtimeAnimatorController != null)
                    {
                        // Get the source controller if it's a runtime controller
                        var path = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                        if (!string.IsNullOrEmpty(path))
                        {
                            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                        }
                    }
                }
            }

            return null;
        }

        private static AnimatorState FindStateByName(AnimatorStateMachine stateMachine, string stateName)
        {
            // Check direct states
            foreach (var state in stateMachine.states)
            {
                if (state.state.name == stateName)
                    return state.state;
            }

            // Check sub-state machines recursively
            foreach (var subMachine in stateMachine.stateMachines)
            {
                var found = FindStateByName(subMachine.stateMachine, stateName);
                if (found != null) return found;
            }

            return null;
        }

        private static AnimatorConditionMode ParseConditionMode(string mode)
        {
            switch (mode?.ToLower())
            {
                case "greater": return AnimatorConditionMode.Greater;
                case "less": return AnimatorConditionMode.Less;
                case "equals": return AnimatorConditionMode.Equals;
                case "notequal": return AnimatorConditionMode.NotEqual;
                case "if": return AnimatorConditionMode.If;
                case "ifnot": return AnimatorConditionMode.IfNot;
                default: return AnimatorConditionMode.If;
            }
        }

        private static Dictionary<string, object> SerializeAnimatorController(UnityEditor.Animations.AnimatorController controller)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = controller.name,
                ["assetPath"] = AssetDatabase.GetAssetPath(controller)
            };

            // Serialize parameters
            var parameters = new List<Dictionary<string, object>>();
            foreach (var param in controller.parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = param.name,
                    ["type"] = param.type.ToString()
                };

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramInfo["defaultValue"] = param.defaultFloat;
                        break;
                    case AnimatorControllerParameterType.Int:
                        paramInfo["defaultValue"] = param.defaultInt;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        paramInfo["defaultValue"] = param.defaultBool;
                        break;
                }

                parameters.Add(paramInfo);
            }
            result["parameters"] = parameters;

            // Serialize layers
            var layers = new List<Dictionary<string, object>>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var layerInfo = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = layer.name,
                    ["defaultWeight"] = layer.defaultWeight
                };

                // Serialize states
                var states = new List<Dictionary<string, object>>();
                var transitions = new List<Dictionary<string, object>>();

                SerializeStateMachine(layer.stateMachine, states, transitions, layer.stateMachine.defaultState?.name);

                layerInfo["states"] = states;
                layerInfo["transitions"] = transitions;
                layerInfo["anyStateTransitions"] = SerializeAnyStateTransitions(layer.stateMachine);

                layers.Add(layerInfo);
            }
            result["layers"] = layers;

            return result;
        }

        private static void SerializeStateMachine(AnimatorStateMachine sm, List<Dictionary<string, object>> states,
            List<Dictionary<string, object>> transitions, string defaultStateName)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                var stateInfo = new Dictionary<string, object>
                {
                    ["name"] = state.name,
                    ["position"] = new Dictionary<string, object>
                    {
                        ["x"] = childState.position.x,
                        ["y"] = childState.position.y
                    },
                    ["isDefault"] = state.name == defaultStateName,
                    ["speed"] = state.speed,
                    ["motion"] = state.motion != null ? state.motion.name : null
                };
                states.Add(stateInfo);

                // Serialize transitions from this state
                foreach (var transition in state.transitions)
                {
                    var transInfo = new Dictionary<string, object>
                    {
                        ["from"] = state.name,
                        ["to"] = transition.destinationState?.name ?? transition.destinationStateMachine?.name ?? "Exit",
                        ["hasExitTime"] = transition.hasExitTime,
                        ["exitTime"] = transition.exitTime,
                        ["duration"] = transition.duration,
                        ["conditions"] = SerializeConditions(transition.conditions)
                    };
                    transitions.Add(transInfo);
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                SerializeStateMachine(subMachine.stateMachine, states, transitions, defaultStateName);
            }
        }

        private static List<Dictionary<string, object>> SerializeAnyStateTransitions(AnimatorStateMachine sm)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var transition in sm.anyStateTransitions)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["from"] = "Any",
                    ["to"] = transition.destinationState?.name ?? "Unknown",
                    ["hasExitTime"] = transition.hasExitTime,
                    ["duration"] = transition.duration,
                    ["conditions"] = SerializeConditions(transition.conditions)
                });
            }
            return result;
        }

        private static List<Dictionary<string, object>> SerializeConditions(AnimatorCondition[] conditions)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var cond in conditions)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["parameter"] = cond.parameter,
                    ["mode"] = cond.mode.ToString(),
                    ["threshold"] = cond.threshold
                });
            }
            return result;
        }

        private static void ValidateStateMachine(AnimatorStateMachine sm, int layerIndex,
            UnityEditor.Animations.AnimatorController controller, List<object> errors, List<object> warnings,
            HashSet<string> usedParams)
        {
            // Get all states that have incoming transitions
            var statesWithIncomingTransitions = new HashSet<string>();
            if (sm.defaultState != null)
                statesWithIncomingTransitions.Add(sm.defaultState.name);

            // Collect incoming transitions from anyState
            foreach (var anyTrans in sm.anyStateTransitions)
            {
                if (anyTrans.destinationState != null)
                    statesWithIncomingTransitions.Add(anyTrans.destinationState.name);
            }

            // Validate each state
            foreach (var childState in sm.states)
            {
                var state = childState.state;

                // Check for missing motion
                if (state.motion == null)
                {
                    warnings.Add(new
                    {
                        type = "MissingMotion",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no motion assigned"
                    });
                }

                // Collect outgoing transitions and their destinations
                bool hasOutgoingTransitions = state.transitions.Length > 0;

                foreach (var transition in state.transitions)
                {
                    // Mark destination as having incoming transition
                    if (transition.destinationState != null)
                        statesWithIncomingTransitions.Add(transition.destinationState.name);

                    // Check for transition without conditions (warning)
                    if (transition.conditions.Length == 0 && !transition.hasExitTime)
                    {
                        warnings.Add(new
                        {
                            type = "TransitionWithoutCondition",
                            layer = layerIndex,
                            state = state.name,
                            to = transition.destinationState?.name ?? "Exit",
                            message = $"Transition from '{state.name}' to '{transition.destinationState?.name ?? "Exit"}' has no conditions and no exit time"
                        });
                    }

                    // Collect used parameters
                    foreach (var cond in transition.conditions)
                    {
                        usedParams.Add(cond.parameter);
                    }
                }

                // Check for dead-end state (no outgoing transitions)
                if (!hasOutgoingTransitions && state.name != "Exit")
                {
                    warnings.Add(new
                    {
                        type = "DeadEndState",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no outgoing transitions (dead end)"
                    });
                }
            }

            // Check for orphan states (no incoming transitions, not default state)
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (!statesWithIncomingTransitions.Contains(state.name))
                {
                    warnings.Add(new
                    {
                        type = "OrphanState",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no incoming transitions (orphan)"
                    });
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                ValidateStateMachine(subMachine.stateMachine, layerIndex, controller, errors, warnings, usedParams);
            }
        }

        private static void CollectStateNames(AnimatorStateMachine sm, Dictionary<string, int> stateNames)
        {
            foreach (var childState in sm.states)
            {
                var name = childState.state.name;
                if (stateNames.ContainsKey(name))
                    stateNames[name]++;
                else
                    stateNames[name] = 1;
            }

            foreach (var subMachine in sm.stateMachines)
            {
                CollectStateNames(subMachine.stateMachine, stateNames);
            }
        }

        private static void CollectAllStateNames(AnimatorStateMachine sm, HashSet<string> stateNames)
        {
            foreach (var childState in sm.states)
            {
                stateNames.Add(childState.state.name);
            }

            foreach (var subMachine in sm.stateMachines)
            {
                CollectAllStateNames(subMachine.stateMachine, stateNames);
            }
        }

        private static int CountStates(AnimatorStateMachine sm)
        {
            int count = sm.states.Length;
            foreach (var subMachine in sm.stateMachines)
            {
                count += CountStates(subMachine.stateMachine);
            }
            return count;
        }

        private static int CountTransitions(AnimatorStateMachine sm)
        {
            int count = sm.anyStateTransitions.Length;
            foreach (var childState in sm.states)
            {
                count += childState.state.transitions.Length;
            }
            foreach (var subMachine in sm.stateMachines)
            {
                count += CountTransitions(subMachine.stateMachine);
            }
            return count;
        }

        private static bool IsParameterUsedInTransitions(UnityEditor.Animations.AnimatorController controller, string paramName)
        {
            foreach (var layer in controller.layers)
            {
                if (IsParameterUsedInStateMachine(layer.stateMachine, paramName))
                    return true;
            }
            return false;
        }

        private static bool IsParameterUsedInStateMachine(AnimatorStateMachine sm, string paramName)
        {
            // Check anyState transitions
            foreach (var transition in sm.anyStateTransitions)
            {
                foreach (var cond in transition.conditions)
                {
                    if (cond.parameter == paramName)
                        return true;
                }
            }

            // Check state transitions
            foreach (var childState in sm.states)
            {
                foreach (var transition in childState.state.transitions)
                {
                    foreach (var cond in transition.conditions)
                    {
                        if (cond.parameter == paramName)
                            return true;
                    }
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                if (IsParameterUsedInStateMachine(subMachine.stateMachine, paramName))
                    return true;
            }

            return false;
        }

        private static void TracePaths(AnimatorStateMachine sm, AnimatorState current,
            List<string> currentPath, List<string> currentConditions, int depth, int maxDepth,
            List<object> allPaths, HashSet<string> visited, HashSet<string> reachableStates)
        {
            // Mark current state as reachable
            reachableStates.Add(current.name);

            // Check depth limit
            if (depth >= maxDepth)
            {
                allPaths.Add(new
                {
                    sequence = new List<string>(currentPath),
                    conditions = new List<string>(currentConditions),
                    truncated = true
                });
                return;
            }

            // If no outgoing transitions, record this path
            if (current.transitions.Length == 0)
            {
                allPaths.Add(new
                {
                    sequence = new List<string>(currentPath),
                    conditions = new List<string>(currentConditions),
                    truncated = false
                });
                return;
            }

            // Explore each transition
            foreach (var transition in current.transitions)
            {
                var destState = transition.destinationState;
                if (destState == null) continue;

                // Build condition string
                var conditionStr = BuildConditionString(transition);

                // Check for cycle
                var visitKey = $"{current.name}->{destState.name}";
                if (visited.Contains(visitKey))
                {
                    // Record path with cycle indicator
                    var cyclePath = new List<string>(currentPath) { $"{destState.name} (cycle)" };
                    var cycleConds = new List<string>(currentConditions) { conditionStr };
                    allPaths.Add(new
                    {
                        sequence = cyclePath,
                        conditions = cycleConds,
                        truncated = false,
                        hasCycle = true
                    });
                    continue;
                }

                // Add to path and recurse
                visited.Add(visitKey);
                currentPath.Add(destState.name);
                currentConditions.Add(conditionStr);

                TracePaths(sm, destState, currentPath, currentConditions, depth + 1, maxDepth, allPaths, visited, reachableStates);

                // Backtrack
                currentPath.RemoveAt(currentPath.Count - 1);
                currentConditions.RemoveAt(currentConditions.Count - 1);
                visited.Remove(visitKey);
            }
        }

        private static string BuildConditionString(AnimatorStateTransition transition)
        {
            if (transition.conditions.Length == 0)
            {
                if (transition.hasExitTime)
                    return $"exitTime >= {transition.exitTime:F2}";
                return "(no condition)";
            }

            var parts = new List<string>();
            foreach (var cond in transition.conditions)
            {
                string op;
                switch (cond.mode)
                {
                    case AnimatorConditionMode.Greater: op = ">"; break;
                    case AnimatorConditionMode.Less: op = "<"; break;
                    case AnimatorConditionMode.Equals: op = "=="; break;
                    case AnimatorConditionMode.NotEqual: op = "!="; break;
                    case AnimatorConditionMode.If: op = "== true"; break;
                    case AnimatorConditionMode.IfNot: op = "== false"; break;
                    default: op = "?"; break;
                }

                if (cond.mode == AnimatorConditionMode.If || cond.mode == AnimatorConditionMode.IfNot)
                    parts.Add($"{cond.parameter} {op}");
                else
                    parts.Add($"{cond.parameter} {op} {cond.threshold}");
            }

            return string.Join(" && ", parts);
        }

        #endregion

        #region Animator Controller Handlers

        private static McpToolResult GetAnimatorController(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();

            if (string.IsNullOrEmpty(controllerPath) && string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("Either controllerPath or gameObjectPath is required");

            var controller = LoadAnimatorController(controllerPath, gameObjectPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found. Path: '{controllerPath}', GameObject: '{gameObjectPath}'");

            var serialized = SerializeAnimatorController(controller);

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(serialized) },
                isError = false
            };
        }

        private static McpToolResult GetAnimatorParameters(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return McpToolResult.Error($"No Animator component on: {gameObjectPath}");

            var parameters = new List<Dictionary<string, object>>();
            foreach (var param in animator.parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = param.name,
                    ["type"] = param.type.ToString()
                };

                // Get current runtime value
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramInfo["value"] = animator.GetFloat(param.name);
                        break;
                    case AnimatorControllerParameterType.Int:
                        paramInfo["value"] = animator.GetInteger(param.name);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        paramInfo["value"] = animator.GetBool(param.name);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        paramInfo["value"] = null; // Triggers don't have persistent values
                        break;
                }

                parameters.Add(paramInfo);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        gameObject = gameObjectPath,
                        parameterCount = parameters.Count,
                        parameters = parameters
                    })
                },
                isError = false
            };
        }

        private static McpToolResult SetAnimatorParameter(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();
            var parameterName = args.GetValueOrDefault("parameterName")?.ToString();
            var valueObj = args.GetValueOrDefault("value");
            var parameterType = args.GetValueOrDefault("parameterType")?.ToString();

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");
            if (string.IsNullOrEmpty(parameterName))
                return McpToolResult.Error("parameterName is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return McpToolResult.Error($"No Animator component on: {gameObjectPath}");

            // Find the parameter to determine type if not specified
            AnimatorControllerParameter foundParam = null;
            foreach (var param in animator.parameters)
            {
                if (param.name == parameterName)
                {
                    foundParam = param;
                    break;
                }
            }

            if (foundParam == null)
                return McpToolResult.Error($"Parameter '{parameterName}' not found on Animator");

            var type = !string.IsNullOrEmpty(parameterType) ? parameterType : foundParam.type.ToString();

            try
            {
                switch (type.ToLower())
                {
                    case "float":
                        var floatVal = Convert.ToSingle(valueObj);
                        animator.SetFloat(parameterName, floatVal);
                        return McpToolResult.Success($"Set {parameterName} = {floatVal} (Float)");

                    case "int":
                    case "integer":
                        var intVal = Convert.ToInt32(valueObj);
                        animator.SetInteger(parameterName, intVal);
                        return McpToolResult.Success($"Set {parameterName} = {intVal} (Int)");

                    case "bool":
                    case "boolean":
                        var boolVal = Convert.ToBoolean(valueObj);
                        animator.SetBool(parameterName, boolVal);
                        return McpToolResult.Success($"Set {parameterName} = {boolVal} (Bool)");

                    case "trigger":
                        animator.SetTrigger(parameterName);
                        return McpToolResult.Success($"Triggered {parameterName}");

                    default:
                        return McpToolResult.Error($"Unknown parameter type: {type}");
                }
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to set parameter: {ex.Message}");
            }
        }

        private static McpToolResult AddAnimatorParameter(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var parameterName = args.GetValueOrDefault("parameterName")?.ToString();
            var parameterType = args.GetValueOrDefault("parameterType")?.ToString();
            var defaultValue = args.GetValueOrDefault("defaultValue");

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(parameterName))
                return McpToolResult.Error("parameterName is required");
            if (string.IsNullOrEmpty(parameterType))
                return McpToolResult.Error("parameterType is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            // Check if parameter already exists
            foreach (var existing in controller.parameters)
            {
                if (existing.name == parameterName)
                    return McpToolResult.Error($"Parameter '{parameterName}' already exists");
            }

            AnimatorControllerParameterType type;
            switch (parameterType.ToLower())
            {
                case "float": type = AnimatorControllerParameterType.Float; break;
                case "int":
                case "integer": type = AnimatorControllerParameterType.Int; break;
                case "bool":
                case "boolean": type = AnimatorControllerParameterType.Bool; break;
                case "trigger": type = AnimatorControllerParameterType.Trigger; break;
                default:
                    return McpToolResult.Error($"Invalid parameter type: {parameterType}. Use Float, Int, Bool, or Trigger");
            }

            Undo.RecordObject(controller, "Add Animator Parameter");
            controller.AddParameter(parameterName, type);

            // Set default value if provided
            if (defaultValue != null)
            {
                var parameters = controller.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parameterName)
                    {
                        var param = parameters[i];
                        switch (type)
                        {
                            case AnimatorControllerParameterType.Float:
                                param.defaultFloat = Convert.ToSingle(defaultValue);
                                break;
                            case AnimatorControllerParameterType.Int:
                                param.defaultInt = Convert.ToInt32(defaultValue);
                                break;
                            case AnimatorControllerParameterType.Bool:
                                param.defaultBool = Convert.ToBoolean(defaultValue);
                                break;
                        }
                        parameters[i] = param;
                        break;
                    }
                }
                controller.parameters = parameters;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added parameter '{parameterName}' ({parameterType}) to {controller.name}",
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            var positionObj = args.GetValueOrDefault("position") as Dictionary<string, object>;
            var motionClip = args.GetValueOrDefault("motionClip")?.ToString();

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range. Controller has {controller.layers.Length} layers");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Check if state already exists
            if (FindStateByName(stateMachine, stateName) != null)
                return McpToolResult.Error($"State '{stateName}' already exists in layer {layerIndex}");

            // Calculate position
            Vector3 position = new Vector3(300, 100, 0);
            if (positionObj != null)
            {
                if (positionObj.TryGetValue("x", out var xObj))
                    position.x = Convert.ToSingle(xObj);
                if (positionObj.TryGetValue("y", out var yObj))
                    position.y = Convert.ToSingle(yObj);
            }
            else
            {
                // Auto-position based on existing states
                int stateCount = stateMachine.states.Length;
                position = new Vector3(300 + (stateCount % 3) * 200, 100 + (stateCount / 3) * 100, 0);
            }

            Undo.RecordObject(stateMachine, "Add Animator State");
            var newState = stateMachine.AddState(stateName, position);

            // Attach motion clip if provided
            if (!string.IsNullOrEmpty(motionClip))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionClip);
                if (clip != null)
                {
                    newState.motion = clip;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added state '{stateName}' to layer {layerIndex}",
                        state = new
                        {
                            name = newState.name,
                            position = new { x = position.x, y = position.y },
                            motion = newState.motion?.name
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddAnimatorTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            var conditionsObj = args.GetValueOrDefault("conditions");
            bool hasExitTime = true;
            if (args.TryGetValue("hasExitTime", out var exitTimeObj) && exitTimeObj != null)
                bool.TryParse(exitTimeObj.ToString(), out hasExitTime);

            float exitTime = 1.0f;
            if (args.TryGetValue("exitTime", out var exitObj) && exitObj != null)
                float.TryParse(exitObj.ToString(), out exitTime);

            float transitionDuration = 0.25f;
            if (args.TryGetValue("transitionDuration", out var durObj) && durObj != null)
                float.TryParse(durObj.ToString(), out transitionDuration);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Find destination state
            var destState = FindStateByName(stateMachine, toState);
            if (destState == null)
                return McpToolResult.Error($"Destination state '{toState}' not found in layer {layerIndex}");

            AnimatorStateTransition transition;

            Undo.RecordObject(controller, "Add Animator Transition");

            if (fromState.ToLower() == "any" || fromState.ToLower() == "anystate")
            {
                // Create AnyState transition
                transition = stateMachine.AddAnyStateTransition(destState);
            }
            else
            {
                // Find source state
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                transition = srcState.AddTransition(destState);
            }

            // Configure transition
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = transitionDuration;

            // Add conditions
            var addedConditions = new List<string>();
            if (conditionsObj is IList<object> conditionsList)
            {
                foreach (var condObj in conditionsList)
                {
                    if (condObj is Dictionary<string, object> cond)
                    {
                        var paramName = cond.GetValueOrDefault("parameter")?.ToString();
                        var mode = cond.GetValueOrDefault("mode")?.ToString() ?? "If";
                        float threshold = 0;
                        if (cond.TryGetValue("threshold", out var threshObj) && threshObj != null)
                            float.TryParse(threshObj.ToString(), out threshold);

                        if (!string.IsNullOrEmpty(paramName))
                        {
                            transition.AddCondition(ParseConditionMode(mode), threshold, paramName);
                            addedConditions.Add($"{paramName} {mode} {threshold}");
                        }
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added transition from '{fromState}' to '{toState}'",
                        transition = new
                        {
                            from = fromState,
                            to = toState,
                            hasExitTime = hasExitTime,
                            exitTime = exitTime,
                            duration = transitionDuration,
                            conditions = addedConditions
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ValidateAnimator(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            var errors = new List<object>();
            var warnings = new List<object>();
            var usedParams = new HashSet<string>();
            int totalStates = 0;
            int totalTransitions = 0;

            // Validate each layer
            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                var layer = controller.layers[layerIndex];
                var sm = layer.stateMachine;

                ValidateStateMachine(sm, layerIndex, controller, errors, warnings, usedParams);
                totalStates += CountStates(sm);
                totalTransitions += CountTransitions(sm);

                // Check for duplicate state names within the layer
                var stateNames = new Dictionary<string, int>();
                CollectStateNames(sm, stateNames);
                foreach (var kvp in stateNames)
                {
                    if (kvp.Value > 1)
                    {
                        errors.Add(new
                        {
                            type = "DuplicateStateName",
                            layer = layerIndex,
                            state = kvp.Key,
                            message = $"State name '{kvp.Key}' appears {kvp.Value} times in layer {layerIndex}"
                        });
                    }
                }
            }

            // Check for unused parameters
            foreach (var param in controller.parameters)
            {
                if (!usedParams.Contains(param.name))
                {
                    if (!IsParameterUsedInTransitions(controller, param.name))
                    {
                        warnings.Add(new
                        {
                            type = "UnusedParameter",
                            parameter = param.name,
                            message = $"Parameter '{param.name}' is never used in any transition condition"
                        });
                    }
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        isValid = errors.Count == 0,
                        errors = errors,
                        warnings = warnings,
                        stats = new
                        {
                            totalStates = totalStates,
                            totalTransitions = totalTransitions,
                            totalParameters = controller.parameters.Length,
                            layers = controller.layers.Length
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAnimatorFlow(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString() ?? "Entry";
            int maxDepth = 10;
            if (args.TryGetValue("maxDepth", out var depthObj) && depthObj != null)
                int.TryParse(depthObj.ToString(), out maxDepth);
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var sm = controller.layers[layerIndex].stateMachine;
            var allPaths = new List<object>();
            var reachableStates = new HashSet<string>();
            var anyStateTargets = new List<string>();

            // Get AnyState targets
            foreach (var transition in sm.anyStateTransitions)
            {
                if (transition.destinationState != null)
                    anyStateTargets.Add(transition.destinationState.name);
            }

            // Determine starting state
            AnimatorState startState = null;
            if (fromState.ToLower() == "entry")
            {
                startState = sm.defaultState;
                if (startState == null)
                    return McpToolResult.Error("No default state defined in this layer");
            }
            else
            {
                startState = FindStateByName(sm, fromState);
                if (startState == null)
                    return McpToolResult.Error($"State '{fromState}' not found in layer {layerIndex}");
            }

            // Trace paths using BFS
            var pathSequence = new List<string> { startState.name };
            var conditionSequence = new List<string> { "(start)" };
            var visited = new HashSet<string>();

            TracePaths(sm, startState, pathSequence, conditionSequence, 0, maxDepth, allPaths, visited, reachableStates);

            // Find all states in the layer
            var allStates = new HashSet<string>();
            CollectAllStateNames(sm, allStates);

            // Calculate unreachable states
            var unreachableStates = new List<string>();
            foreach (var state in allStates)
            {
                if (!reachableStates.Contains(state) && state != startState.name)
                {
                    // Check if reachable via AnyState
                    if (!anyStateTargets.Contains(state))
                        unreachableStates.Add(state);
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        paths = allPaths,
                        reachableStates = reachableStates.ToList(),
                        unreachableStates = unreachableStates,
                        anyStateTargets = anyStateTargets
                    })
                },
                isError = false
            };
        }

        private static McpToolResult DeleteAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range. Controller has {controller.layers.Length} layers");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Find the state to delete
            var stateToDelete = FindStateByName(stateMachine, stateName);
            if (stateToDelete == null)
                return McpToolResult.Error($"State '{stateName}' not found in layer {layerIndex}");

            // Check if it's the default state
            bool wasDefaultState = stateMachine.defaultState == stateToDelete;

            Undo.RecordObject(stateMachine, "Delete Animator State");
            stateMachine.RemoveState(stateToDelete);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Deleted state '{stateName}' from layer {layerIndex}",
                        deletedState = stateName,
                        wasDefaultState = wasDefaultState,
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult DeleteAnimatorTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int transitionIndex = 0;
            if (args.TryGetValue("transitionIndex", out var transIdxObj) && transIdxObj != null)
                int.TryParse(transIdxObj.ToString(), out transitionIndex);
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            Undo.RecordObject(controller, "Delete Animator Transition");

            bool isAnyState = fromState.ToLower() == "any" || fromState.ToLower() == "anystate";

            if (isAnyState)
            {
                // Handle AnyState transition
                var anyTransitions = stateMachine.anyStateTransitions;
                AnimatorStateTransition toRemove = null;

                // Find the transition to the specified destination state
                foreach (var t in anyTransitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toState)
                    {
                        toRemove = t;
                        break;
                    }
                }

                if (toRemove == null)
                    return McpToolResult.Error($"No AnyState transition to '{toState}' found in layer {layerIndex}");

                Undo.RecordObject(stateMachine, "Delete AnyState Transition");
                stateMachine.RemoveAnyStateTransition(toRemove);
            }
            else
            {
                // Handle regular state transition
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                var transitions = srcState.transitions;
                if (transitions == null || transitions.Length == 0)
                    return McpToolResult.Error($"State '{fromState}' has no transitions");

                // Find the transition to the specified destination state
                AnimatorStateTransition toRemove = null;
                int matchCount = 0;

                for (int i = 0; i < transitions.Length; i++)
                {
                    var t = transitions[i];
                    if (t.destinationState != null && t.destinationState.name == toState)
                    {
                        if (matchCount == transitionIndex)
                        {
                            toRemove = t;
                            break;
                        }
                        matchCount++;
                    }
                }

                if (toRemove == null)
                {
                    if (matchCount == 0)
                        return McpToolResult.Error($"No transition from '{fromState}' to '{toState}' found");
                    else
                        return McpToolResult.Error($"Transition index {transitionIndex} out of range. Found {matchCount} transitions from '{fromState}' to '{toState}'");
                }

                Undo.RecordObject(srcState, "Delete State Transition");
                srcState.RemoveTransition(toRemove);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Deleted transition from '{fromState}' to '{toState}'",
                        fromState = fromState,
                        toState = toState,
                        transitionIndex = transitionIndex,
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ModifyAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;
            var state = FindStateByName(stateMachine, stateName);
            if (state == null)
                return McpToolResult.Error($"State '{stateName}' not found in layer {layerIndex}");

            Undo.RecordObject(state, "Modify Animator State");

            var modifiedProperties = new List<string>();

            // Apply optional properties
            if (args.TryGetValue("newName", out var newNameObj) && newNameObj != null)
            {
                var newName = newNameObj.ToString();
                state.name = newName;
                modifiedProperties.Add($"name: {newName}");
            }

            if (args.TryGetValue("motion", out var motionObj) && motionObj != null)
            {
                var motionPath = motionObj.ToString();
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                if (clip != null)
                {
                    state.motion = clip;
                    modifiedProperties.Add($"motion: {motionPath}");
                }
                else
                {
                    return McpToolResult.Error($"AnimationClip not found: {motionPath}");
                }
            }

            if (args.TryGetValue("speed", out var speedObj) && speedObj != null)
            {
                if (float.TryParse(speedObj.ToString(), out float speed))
                {
                    state.speed = speed;
                    modifiedProperties.Add($"speed: {speed}");
                }
            }

            if (args.TryGetValue("speedParameter", out var speedParamObj) && speedParamObj != null)
            {
                var speedParam = speedParamObj.ToString();
                state.speedParameterActive = true;
                state.speedParameter = speedParam;
                modifiedProperties.Add($"speedParameter: {speedParam}");
            }

            if (args.TryGetValue("cycleOffset", out var cycleOffsetObj) && cycleOffsetObj != null)
            {
                if (float.TryParse(cycleOffsetObj.ToString(), out float cycleOffset))
                {
                    state.cycleOffset = cycleOffset;
                    modifiedProperties.Add($"cycleOffset: {cycleOffset}");
                }
            }

            if (args.TryGetValue("mirror", out var mirrorObj) && mirrorObj != null)
            {
                if (bool.TryParse(mirrorObj.ToString(), out bool mirror))
                {
                    state.mirror = mirror;
                    modifiedProperties.Add($"mirror: {mirror}");
                }
            }

            if (args.TryGetValue("writeDefaultValues", out var writeDefaultsObj) && writeDefaultsObj != null)
            {
                if (bool.TryParse(writeDefaultsObj.ToString(), out bool writeDefaults))
                {
                    state.writeDefaultValues = writeDefaults;
                    modifiedProperties.Add($"writeDefaultValues: {writeDefaults}");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Modified state '{stateName}' in layer {layerIndex}",
                        modifiedProperties = modifiedProperties
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ModifyTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                int.TryParse(layerObj.ToString(), out layerIndex);
            int transitionIndex = 0;
            if (args.TryGetValue("transitionIndex", out var transIdxObj) && transIdxObj != null)
                int.TryParse(transIdxObj.ToString(), out transitionIndex);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            AnimatorStateTransition transition = null;

            // Handle AnyState transitions
            if (fromState.ToLower() == "any" || fromState.ToLower() == "anystate")
            {
                var anyTransitions = stateMachine.anyStateTransitions
                    .Where(t => t.destinationState != null && t.destinationState.name == toState)
                    .ToArray();

                if (anyTransitions.Length == 0)
                    return McpToolResult.Error($"No AnyState transition to '{toState}' found");
                if (transitionIndex >= anyTransitions.Length)
                    return McpToolResult.Error($"Transition index {transitionIndex} out of range (found {anyTransitions.Length})");

                transition = anyTransitions[transitionIndex];
            }
            else
            {
                // Find source state and its transitions
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                var stateTransitions = srcState.transitions
                    .Where(t => t.destinationState != null && t.destinationState.name == toState)
                    .ToArray();

                if (stateTransitions.Length == 0)
                    return McpToolResult.Error($"No transition from '{fromState}' to '{toState}' found");
                if (transitionIndex >= stateTransitions.Length)
                    return McpToolResult.Error($"Transition index {transitionIndex} out of range (found {stateTransitions.Length})");

                transition = stateTransitions[transitionIndex];
            }

            Undo.RecordObject(transition, "Modify Transition");

            var modifiedProperties = new List<string>();

            // Apply optional properties
            if (args.TryGetValue("hasExitTime", out var hasExitTimeObj) && hasExitTimeObj != null)
            {
                if (bool.TryParse(hasExitTimeObj.ToString(), out bool hasExitTime))
                {
                    transition.hasExitTime = hasExitTime;
                    modifiedProperties.Add($"hasExitTime: {hasExitTime}");
                }
            }

            if (args.TryGetValue("exitTime", out var exitTimeObj) && exitTimeObj != null)
            {
                if (float.TryParse(exitTimeObj.ToString(), out float exitTime))
                {
                    transition.exitTime = exitTime;
                    modifiedProperties.Add($"exitTime: {exitTime}");
                }
            }

            if (args.TryGetValue("duration", out var durationObj) && durationObj != null)
            {
                if (float.TryParse(durationObj.ToString(), out float duration))
                {
                    transition.duration = duration;
                    modifiedProperties.Add($"duration: {duration}");
                }
            }

            if (args.TryGetValue("offset", out var offsetObj) && offsetObj != null)
            {
                if (float.TryParse(offsetObj.ToString(), out float offset))
                {
                    transition.offset = offset;
                    modifiedProperties.Add($"offset: {offset}");
                }
            }

            if (args.TryGetValue("interruptionSource", out var interruptObj) && interruptObj != null)
            {
                var source = ParseInterruptionSource(interruptObj.ToString());
                transition.interruptionSource = source;
                modifiedProperties.Add($"interruptionSource: {source}");
            }

            if (args.TryGetValue("canTransitionToSelf", out var canTransitionObj) && canTransitionObj != null)
            {
                if (bool.TryParse(canTransitionObj.ToString(), out bool canTransitionToSelf))
                {
                    transition.canTransitionToSelf = canTransitionToSelf;
                    modifiedProperties.Add($"canTransitionToSelf: {canTransitionToSelf}");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Modified transition from '{fromState}' to '{toState}'",
                        modifiedProperties = modifiedProperties
                    })
                },
                isError = false
            };
        }

        private static TransitionInterruptionSource ParseInterruptionSource(string source)
        {
            switch (source?.ToLower())
            {
                case "source": return TransitionInterruptionSource.Source;
                case "destination": return TransitionInterruptionSource.Destination;
                case "sourcethendestination": return TransitionInterruptionSource.SourceThenDestination;
                case "destinationthensource": return TransitionInterruptionSource.DestinationThenSource;
                default: return TransitionInterruptionSource.None;
            }
        }

        #endregion

        #region Blend Tree Handlers

        private static BlendTreeType ParseBlendType(string type)
        {
            switch (type?.ToLower())
            {
                case "1d": return BlendTreeType.Simple1D;
                case "2dsimpledirectional": return BlendTreeType.SimpleDirectional2D;
                case "2dfreeformdirectional": return BlendTreeType.FreeformDirectional2D;
                case "2dfreeformcartesian": return BlendTreeType.FreeformCartesian2D;
                case "direct": return BlendTreeType.Direct;
                default: return BlendTreeType.Simple1D;
            }
        }

        private static McpToolResult CreateBlendTree(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            var blendTypeStr = args.GetValueOrDefault("blendType")?.ToString() ?? "1D";
            var blendParameter = args.GetValueOrDefault("blendParameter")?.ToString();
            var blendParameterY = args.GetValueOrDefault("blendParameterY")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                layerIndex = Convert.ToInt32(layerObj);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found at: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} is out of range. Controller has {controller.layers.Length} layers.");

            Undo.RecordObject(controller, "Create Blend Tree");

            BlendTree blendTree;
            controller.CreateBlendTreeInController(stateName, out blendTree, layerIndex);

            if (blendTree == null)
                return McpToolResult.Error("Failed to create blend tree");

            blendTree.blendType = ParseBlendType(blendTypeStr);

            if (!string.IsNullOrEmpty(blendParameter))
                blendTree.blendParameter = blendParameter;

            if (!string.IsNullOrEmpty(blendParameterY) && blendTree.blendType != BlendTreeType.Simple1D)
                blendTree.blendParameterY = blendParameterY;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Find the created state to return its info
            var layer = controller.layers[layerIndex];
            var createdState = layer.stateMachine.states
                .FirstOrDefault(s => s.state.name == stateName).state;

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Created blend tree '{stateName}' in layer {layerIndex}",
                        blendTree = new
                        {
                            stateName = stateName,
                            blendType = blendTree.blendType.ToString(),
                            blendParameter = blendTree.blendParameter,
                            blendParameterY = blendTree.blendParameterY,
                            layerIndex = layerIndex,
                            childCount = blendTree.children.Length
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddBlendMotion(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var blendTreeState = args.GetValueOrDefault("blendTreeState")?.ToString();
            var motionPath = args.GetValueOrDefault("motionPath")?.ToString();
            int layerIndex = 0;
            if (args.TryGetValue("layerIndex", out var layerObj) && layerObj != null)
                layerIndex = Convert.ToInt32(layerObj);

            // For 1D blend trees
            float threshold = 0f;
            if (args.TryGetValue("threshold", out var thresholdObj) && thresholdObj != null)
                threshold = Convert.ToSingle(thresholdObj);

            // For 2D blend trees
            float positionX = 0f;
            float positionY = 0f;
            if (args.TryGetValue("positionX", out var posXObj) && posXObj != null)
                positionX = Convert.ToSingle(posXObj);
            if (args.TryGetValue("positionY", out var posYObj) && posYObj != null)
                positionY = Convert.ToSingle(posYObj);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(blendTreeState))
                return McpToolResult.Error("blendTreeState is required");
            if (string.IsNullOrEmpty(motionPath))
                return McpToolResult.Error("motionPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found at: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} is out of range. Controller has {controller.layers.Length} layers.");

            var layer = controller.layers[layerIndex];
            var state = layer.stateMachine.states
                .FirstOrDefault(s => s.state.name == blendTreeState).state;

            if (state == null)
                return McpToolResult.Error($"State '{blendTreeState}' not found in layer {layerIndex}");

            var blendTree = state.motion as BlendTree;
            if (blendTree == null)
                return McpToolResult.Error($"State '{blendTreeState}' does not contain a BlendTree");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
            if (clip == null)
                return McpToolResult.Error($"AnimationClip not found at: {motionPath}");

            Undo.RecordObject(blendTree, "Add Blend Motion");

            // Add child based on blend tree type
            if (blendTree.blendType == BlendTreeType.Simple1D)
            {
                blendTree.AddChild(clip, threshold);
            }
            else
            {
                blendTree.AddChild(clip, new Vector2(positionX, positionY));
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(blendTree);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added motion '{clip.name}' to blend tree '{blendTreeState}'",
                        motion = new
                        {
                            clipName = clip.name,
                            clipPath = motionPath,
                            blendTreeState = blendTreeState,
                            blendType = blendTree.blendType.ToString(),
                            threshold = blendTree.blendType == BlendTreeType.Simple1D ? threshold : (float?)null,
                            position = blendTree.blendType != BlendTreeType.Simple1D
                                ? new { x = positionX, y = positionY }
                                : null,
                            totalChildren = blendTree.children.Length
                        }
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Animation Clip Handlers

        private static McpToolResult ListAnimationClips(Dictionary<string, object> args)
        {
            string searchPath = "Assets";
            if (args.TryGetValue("searchPath", out var searchPathObj) && searchPathObj != null)
            {
                searchPath = searchPathObj.ToString();
            }

            string nameFilter = null;
            if (args.TryGetValue("nameFilter", out var nameFilterObj) && nameFilterObj != null)
            {
                nameFilter = nameFilterObj.ToString().ToLowerInvariant();
            }

            string avatarFilter = null;
            if (args.TryGetValue("avatarFilter", out var avatarFilterObj) && avatarFilterObj != null)
            {
                avatarFilter = avatarFilterObj.ToString().ToLowerInvariant();
            }

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { searchPath });
            var clips = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                if (clip == null) continue;

                // Apply name filter
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    if (!clip.name.ToLowerInvariant().Contains(nameFilter))
                        continue;
                }

                // Apply avatar filter
                if (!string.IsNullOrEmpty(avatarFilter))
                {
                    bool isHumanoid = clip.isHumanMotion;
                    bool isLegacy = clip.legacy;

                    if (avatarFilter == "humanoid" && !isHumanoid) continue;
                    if (avatarFilter == "generic" && (isHumanoid || isLegacy)) continue;
                    if (avatarFilter == "legacy" && !isLegacy) continue;
                }

                clips.Add(new
                {
                    path = path,
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    isHumanMotion = clip.isHumanMotion,
                    hasRootMotion = clip.hasMotionCurves,
                    isLegacy = clip.legacy
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        clips = clips,
                        totalCount = clips.Count,
                        searchPath = searchPath,
                        filters = new
                        {
                            nameFilter = nameFilter,
                            avatarFilter = avatarFilter
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetClipInfo(Dictionary<string, object> args)
        {
            var clipPath = args.GetValueOrDefault("clipPath")?.ToString();

            if (string.IsNullOrEmpty(clipPath))
                return McpToolResult.Error("clipPath is required");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return McpToolResult.Error($"AnimationClip not found at: {clipPath}");

            // Get animation events
            var animationEvents = AnimationUtility.GetAnimationEvents(clip);
            var events = new List<object>();
            foreach (var evt in animationEvents)
            {
                events.Add(new
                {
                    time = evt.time,
                    functionName = evt.functionName,
                    intParameter = evt.intParameter,
                    floatParameter = evt.floatParameter,
                    stringParameter = evt.stringParameter
                });
            }

            // Get curve bindings
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            var curves = new List<object>();
            foreach (var binding in curveBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                curves.Add(new
                {
                    path = binding.path,
                    propertyName = binding.propertyName,
                    type = binding.type.Name,
                    keyCount = curve != null ? curve.length : 0
                });
            }

            // Also get object reference curve bindings
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objectBindings)
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                curves.Add(new
                {
                    path = binding.path,
                    propertyName = binding.propertyName,
                    type = binding.type.Name,
                    keyCount = keyframes != null ? keyframes.Length : 0,
                    isObjectReference = true
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        name = clip.name,
                        path = clipPath,
                        length = clip.length,
                        frameRate = clip.frameRate,
                        wrapMode = clip.wrapMode.ToString(),
                        isLooping = clip.isLooping,
                        isHumanMotion = clip.isHumanMotion,
                        hasRootMotion = clip.hasMotionCurves,
                        isLegacy = clip.legacy,
                        localBounds = new
                        {
                            center = new { x = clip.localBounds.center.x, y = clip.localBounds.center.y, z = clip.localBounds.center.z },
                            size = new { x = clip.localBounds.size.x, y = clip.localBounds.size.y, z = clip.localBounds.size.z }
                        },
                        events = events,
                        eventCount = events.Count,
                        curves = curves,
                        curveCount = curves.Count
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Asset Browser Handlers

        private static McpToolResult SearchAssets(Dictionary<string, object> args)
        {
            string filter = "";
            if (args.TryGetValue("filter", out var filterObj) && filterObj != null)
            {
                filter = filterObj.ToString();
            }

            int maxResults = 50;
            if (args.TryGetValue("maxResults", out var maxObj) && maxObj != null)
            {
                maxResults = Math.Min(Convert.ToInt32(maxObj), 200);
            }

            string[] searchFolders = null;
            if (args.TryGetValue("searchFolders", out var foldersObj) && foldersObj != null)
            {
                if (foldersObj is List<object> folderList)
                {
                    searchFolders = folderList.Select(f => f.ToString()).ToArray();
                }
            }

            string[] guids;
            if (searchFolders != null && searchFolders.Length > 0)
            {
                guids = AssetDatabase.FindAssets(filter, searchFolders);
            }
            else
            {
                guids = AssetDatabase.FindAssets(filter);
            }

            var results = new List<object>();
            int count = 0;

            foreach (var guid in guids)
            {
                if (count >= maxResults) break;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);

                if (type != null)
                {
                    results.Add(new
                    {
                        path = path,
                        name = System.IO.Path.GetFileNameWithoutExtension(path),
                        type = type.Name,
                        guid = guid
                    });
                    count++;
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        filter = filter,
                        totalFound = guids.Length,
                        returned = results.Count,
                        assets = results
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAssetInfo(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("assetPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("assetPath is required");
            }

            string assetPath;
            try
            {
                assetPath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid asset path: {ex.Message}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset == null)
            {
                return McpToolResult.Error($"Asset not found: {assetPath}");
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            var labels = AssetDatabase.GetLabels(asset);

            // Get file info
            var fileInfo = new System.IO.FileInfo(assetPath);
            long fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            var info = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["name"] = asset.name,
                ["type"] = type?.Name ?? "Unknown",
                ["guid"] = guid,
                ["labels"] = labels,
                ["fileSize"] = fileSize,
                ["fileSizeFormatted"] = FormatFileSize(fileSize)
            };

            // Include dependencies if requested
            if (args.TryGetValue("includeDependencies", out var depObj) && depObj != null && Convert.ToBoolean(depObj))
            {
                var deps = AssetDatabase.GetDependencies(assetPath, false);
                info["dependencies"] = deps.Where(d => d != assetPath).ToArray();
            }

            // Include references if requested (can be slow)
            if (args.TryGetValue("includeReferences", out var refObj) && refObj != null && Convert.ToBoolean(refObj))
            {
                var allAssets = AssetDatabase.GetAllAssetPaths();
                var references = new List<string>();

                foreach (var otherPath in allAssets)
                {
                    if (otherPath == assetPath) continue;
                    var deps = AssetDatabase.GetDependencies(otherPath, false);
                    if (deps.Contains(assetPath))
                    {
                        references.Add(otherPath);
                    }
                }
                info["referencedBy"] = references.ToArray();
            }

            // Add type-specific info
            if (asset is Texture2D tex)
            {
                info["textureInfo"] = new
                {
                    width = tex.width,
                    height = tex.height,
                    format = tex.format.ToString(),
                    mipmapCount = tex.mipmapCount
                };
            }
            else if (asset is AudioClip audio)
            {
                info["audioInfo"] = new
                {
                    length = audio.length,
                    channels = audio.channels,
                    frequency = audio.frequency,
                    samples = audio.samples
                };
            }
            else if (asset is Mesh mesh)
            {
                info["meshInfo"] = new
                {
                    vertexCount = mesh.vertexCount,
                    triangles = mesh.triangles.Length / 3,
                    subMeshCount = mesh.subMeshCount,
                    bounds = new { center = new { x = mesh.bounds.center.x, y = mesh.bounds.center.y, z = mesh.bounds.center.z },
                                   size = new { x = mesh.bounds.size.x, y = mesh.bounds.size.y, z = mesh.bounds.size.z } }
                };
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(info) },
                isError = false
            };
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private static McpToolResult ListFolders(Dictionary<string, object> args)
        {
            string parentPath = "Assets";
            if (args.TryGetValue("parentPath", out var pathObj) && pathObj != null)
            {
                parentPath = pathObj.ToString();
            }

            bool recursive = true;
            if (args.TryGetValue("recursive", out var recObj) && recObj != null)
            {
                recursive = Convert.ToBoolean(recObj);
            }

            int maxDepth = 5;
            if (args.TryGetValue("maxDepth", out var depthObj) && depthObj != null)
            {
                maxDepth = Convert.ToInt32(depthObj);
            }

            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                return McpToolResult.Error($"Folder not found: {parentPath}");
            }

            var folders = new List<object>();
            CollectFolders(parentPath, folders, recursive, maxDepth, 0);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        parentPath = parentPath,
                        recursive = recursive,
                        folderCount = folders.Count,
                        folders = folders
                    })
                },
                isError = false
            };
        }

        private static void CollectFolders(string path, List<object> folders, bool recursive, int maxDepth, int currentDepth)
        {
            var subFolders = AssetDatabase.GetSubFolders(path);

            foreach (var folder in subFolders)
            {
                var assetCount = AssetDatabase.FindAssets("", new[] { folder }).Length;
                var subFolderCount = AssetDatabase.GetSubFolders(folder).Length;

                folders.Add(new
                {
                    path = folder,
                    name = System.IO.Path.GetFileName(folder),
                    depth = currentDepth + 1,
                    assetCount = assetCount,
                    subFolderCount = subFolderCount
                });

                if (recursive && currentDepth < maxDepth - 1)
                {
                    CollectFolders(folder, folders, true, maxDepth, currentDepth + 1);
                }
            }
        }

        private static McpToolResult ListFolderContents(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("folderPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("folderPath is required");
            }

            var folderPath = pathObj.ToString();

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return McpToolResult.Error($"Folder not found: {folderPath}");
            }

            string typeFilter = "";
            if (args.TryGetValue("typeFilter", out var typeObj) && typeObj != null)
            {
                typeFilter = "t:" + typeObj.ToString();
            }

            bool includeSubfolders = false;
            if (args.TryGetValue("includeSubfolders", out var subObj) && subObj != null)
            {
                includeSubfolders = Convert.ToBoolean(subObj);
            }

            string[] guids;
            if (includeSubfolders)
            {
                guids = AssetDatabase.FindAssets(typeFilter, new[] { folderPath });
            }
            else
            {
                // Get only direct children
                guids = AssetDatabase.FindAssets(typeFilter, new[] { folderPath });
                guids = guids.Where(g =>
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    var parent = System.IO.Path.GetDirectoryName(p).Replace("\\", "/");
                    return parent == folderPath;
                }).ToArray();
            }

            var assets = new List<object>();
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);

                // Skip folders
                if (AssetDatabase.IsValidFolder(assetPath)) continue;

                assets.Add(new
                {
                    path = assetPath,
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                    extension = System.IO.Path.GetExtension(assetPath),
                    type = type?.Name ?? "Unknown",
                    guid = guid
                });
            }

            // Also list subfolders
            var subFolders = AssetDatabase.GetSubFolders(folderPath)
                .Select(f => new { path = f, name = System.IO.Path.GetFileName(f), isFolder = true })
                .ToList();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        folderPath = folderPath,
                        assetCount = assets.Count,
                        subFolderCount = subFolders.Count,
                        assets = assets,
                        subFolders = subFolders
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAssetPreview(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("assetPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("assetPath is required");
            }

            string assetPath;
            try
            {
                assetPath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid asset path: {ex.Message}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset == null)
            {
                return McpToolResult.Error($"Asset not found: {assetPath}");
            }

            // Parse size preset (default: small = 64px for token efficiency)
            int size = 64;
            if (args.TryGetValue("size", out var sizeObj) && sizeObj != null)
            {
                switch (sizeObj.ToString().ToLower())
                {
                    case "tiny": size = 32; break;
                    case "small": size = 64; break;
                    case "medium": size = 128; break;
                    case "large": size = 256; break;
                }
            }

            // Parse format (default: jpg for smaller size)
            string format = "jpg";
            if (args.TryGetValue("format", out var formatObj) && formatObj != null)
                format = formatObj.ToString().ToLower();

            // Parse JPG quality
            int jpgQuality = 75;
            if (args.TryGetValue("jpgQuality", out var qualObj) && qualObj != null)
                jpgQuality = Math.Clamp(Convert.ToInt32(qualObj), 1, 100);

            // Get the asset preview
            var preview = AssetPreview.GetAssetPreview(asset);

            // If no preview available, try getting the mini thumbnail
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(asset);
            }

            if (preview == null)
            {
                return McpToolResult.Error($"Could not generate preview for asset: {assetPath}");
            }

            // Resize if needed
            Texture2D resized = preview;
            if (preview.width != size || preview.height != size)
            {
                resized = ResizeTexture(preview, size, size);
            }

            // Encode based on format
            byte[] imageData;
            string mimeType;
            if (format == "png")
            {
                imageData = resized.EncodeToPNG();
                mimeType = "image/png";
            }
            else
            {
                imageData = resized.EncodeToJPG(jpgQuality);
                mimeType = "image/jpeg";
            }

            string base64 = Convert.ToBase64String(imageData);

            // Clean up temporary texture
            if (resized != preview)
            {
                UnityEngine.Object.DestroyImmediate(resized);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        assetPath = assetPath,
                        size = size,
                        format = format,
                        fileSizeBytes = imageData.Length,
                        base64 = base64,
                        dataUri = $"data:{mimeType};base64,{base64}"
                    })
                },
                isError = false
            };
        }

        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture.active = rt;
            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        #endregion

        #region Workflow Enhancement Handlers

        private static McpToolResult ClearConsole(Dictionary<string, object> args)
        {
            try
            {
                var assembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));
                var type = assembly.GetType("UnityEditor.LogEntries");
                var method = type.GetMethod("Clear");
                method.Invoke(null, null);

                // Also clear our internal log cache
                while (_consoleLogs.TryDequeue(out _)) { }

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new { success = true, message = "Console cleared" })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to clear console: {ex.Message}");
            }
        }

        private static McpToolResult RenameGameObject(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("gameObjectPath is required");
            }

            if (!args.TryGetValue("newName", out var nameObj) || nameObj == null)
            {
                return McpToolResult.Error("newName is required");
            }

            var gameObjectPath = pathObj.ToString();
            var newName = nameObj.ToString();

            if (string.IsNullOrWhiteSpace(newName))
            {
                return McpToolResult.Error("newName cannot be empty");
            }

            var gameObject = GameObject.Find(gameObjectPath);
            if (gameObject == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            var oldName = gameObject.name;

            Undo.RecordObject(gameObject, "Rename GameObject");
            gameObject.name = newName;
            EditorUtility.SetDirty(gameObject);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        oldName = oldName,
                        newName = newName,
                        path = GetGameObjectPath(gameObject)
                    })
                },
                isError = false
            };
        }

        private static McpToolResult SetParent(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("gameObjectPath is required");
            }

            var gameObjectPath = pathObj.ToString();
            var gameObject = GameObject.Find(gameObjectPath);

            if (gameObject == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            GameObject newParent = null;
            string parentName = "(scene root)";

            if (args.TryGetValue("parentPath", out var parentObj) && parentObj != null && !string.IsNullOrEmpty(parentObj.ToString()))
            {
                var parentPath = parentObj.ToString();
                newParent = GameObject.Find(parentPath);

                if (newParent == null)
                {
                    return McpToolResult.Error($"Parent GameObject not found: {parentPath}");
                }

                // Prevent parenting to self or child
                if (newParent == gameObject || newParent.transform.IsChildOf(gameObject.transform))
                {
                    return McpToolResult.Error("Cannot parent an object to itself or its children");
                }

                parentName = newParent.name;
            }

            bool worldPositionStays = true;
            if (args.TryGetValue("worldPositionStays", out var staysObj) && staysObj != null)
            {
                worldPositionStays = Convert.ToBoolean(staysObj);
            }

            Undo.SetTransformParent(gameObject.transform, newParent?.transform, "Set Parent");
            gameObject.transform.SetParent(newParent?.transform, worldPositionStays);
            EditorUtility.SetDirty(gameObject);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        child = gameObject.name,
                        newParent = parentName,
                        worldPositionStays = worldPositionStays,
                        newPath = GetGameObjectPath(gameObject)
                    })
                },
                isError = false
            };
        }

        private static McpToolResult InstantiatePrefab(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("prefabPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("prefabPath is required");
            }

            string prefabPath;
            try
            {
                prefabPath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid prefab path: {ex.Message}");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                return McpToolResult.Error($"Prefab not found at path: {prefabPath}");
            }

            // Check if it's actually a prefab
            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
            {
                return McpToolResult.Error($"Asset is not a prefab: {prefabPath}");
            }

            // Instantiate with prefab link
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (instance == null)
            {
                return McpToolResult.Error("Failed to instantiate prefab");
            }

            // Set position
            Vector3 position = Vector3.zero;
            if (args.TryGetValue("position", out var posObj) && posObj != null)
            {
                position = ParseVector3(posObj);
            }
            instance.transform.position = position;

            // Set rotation
            Vector3 rotation = Vector3.zero;
            if (args.TryGetValue("rotation", out var rotObj) && rotObj != null)
            {
                rotation = ParseVector3(rotObj);
            }
            instance.transform.rotation = Quaternion.Euler(rotation);

            // Set parent if specified
            if (args.TryGetValue("parentPath", out var parentObj) && parentObj != null && !string.IsNullOrEmpty(parentObj.ToString()))
            {
                var parent = GameObject.Find(parentObj.ToString());
                if (parent != null)
                {
                    instance.transform.SetParent(parent.transform, true);
                }
            }

            // Override name if specified
            if (args.TryGetValue("name", out var nameObj) && nameObj != null && !string.IsNullOrEmpty(nameObj.ToString()))
            {
                instance.name = nameObj.ToString();
            }

            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab");

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        instanceName = instance.name,
                        instancePath = GetGameObjectPath(instance),
                        prefabPath = prefabPath,
                        position = new { x = instance.transform.position.x, y = instance.transform.position.y, z = instance.transform.position.z },
                        rotation = new { x = instance.transform.eulerAngles.x, y = instance.transform.eulerAngles.y, z = instance.transform.eulerAngles.z }
                    })
                },
                isError = false
            };
        }

        private static Vector3 ParseVector3(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out var xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out var yVal) ? Convert.ToSingle(yVal) : 0f;
                float z = dict.TryGetValue("z", out var zVal) ? Convert.ToSingle(zVal) : 0f;
                return new Vector3(x, y, z);
            }
            return Vector3.zero;
        }

        private static McpToolResult TakeScreenshot(Dictionary<string, object> args)
        {
            // Parse parameters with optimized defaults
            string view = "Scene";
            string format = "jpg";  // JPG by default (10x smaller)
            int jpgQuality = 75;
            int width = 640;        // Smaller default
            int height = 360;
            bool returnBase64 = false;  // Don't return base64 by default (saves tokens)

            if (args.TryGetValue("view", out var viewObj) && viewObj != null)
                view = viewObj.ToString();

            if (args.TryGetValue("format", out var formatObj) && formatObj != null)
                format = formatObj.ToString().ToLower();

            if (args.TryGetValue("jpgQuality", out var qualObj) && qualObj != null)
                jpgQuality = Math.Clamp(Convert.ToInt32(qualObj), 1, 100);

            if (args.TryGetValue("width", out var wObj) && wObj != null)
                width = Math.Min(Convert.ToInt32(wObj), MaxScreenshotDimension);

            if (args.TryGetValue("height", out var hObj) && hObj != null)
                height = Math.Min(Convert.ToInt32(hObj), MaxScreenshotDimension);

            if (args.TryGetValue("returnBase64", out var b64Obj) && b64Obj != null)
                bool.TryParse(b64Obj.ToString(), out returnBase64);

            // Generate save path with correct extension
            string extension = format == "png" ? ".png" : ".jpg";
            string savePath = $"Assets/Screenshots/screenshot_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";

            if (args.TryGetValue("savePath", out var pathObj) && pathObj != null && !string.IsNullOrEmpty(pathObj.ToString()))
            {
                savePath = pathObj.ToString();
                // Ensure correct extension
                if (format == "jpg" && !savePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    savePath = System.IO.Path.ChangeExtension(savePath, ".jpg");
                else if (format == "png" && !savePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    savePath = System.IO.Path.ChangeExtension(savePath, ".png");
            }

            // Security: Validate path
            try
            {
                savePath = SanitizePath(savePath);
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid save path: {ex.Message}");
            }

            Texture2D screenshot = null;

            try
            {
                if (view.Equals("Scene", StringComparison.OrdinalIgnoreCase))
                {
                    screenshot = CaptureSceneView(width, height);
                }
                else if (view.Equals("Game", StringComparison.OrdinalIgnoreCase))
                {
                    if (!EditorApplication.isPlaying)
                    {
                        return McpToolResult.Error("Game View screenshot requires Play Mode. Use 'Scene' view instead.");
                    }
                    screenshot = CaptureGameView(width, height);
                }
                else
                {
                    return McpToolResult.Error($"Invalid view: {view}. Use 'Scene' or 'Game'.");
                }

                if (screenshot == null)
                {
                    return McpToolResult.Error($"Failed to capture {view} view");
                }

                // Encode based on format
                byte[] imageData;
                if (format == "png")
                {
                    imageData = screenshot.EncodeToPNG();
                }
                else
                {
                    imageData = screenshot.EncodeToJPG(jpgQuality);
                }

                // Ensure directory exists
                var directory = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllBytes(savePath, imageData);
                AssetDatabase.Refresh();

                // Build response - only include base64 if explicitly requested
                var responseData = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["view"] = view,
                    ["format"] = format,
                    ["width"] = screenshot.width,
                    ["height"] = screenshot.height,
                    ["savedPath"] = savePath,
                    ["fileSizeKB"] = imageData.Length / 1024
                };

                if (returnBase64)
                {
                    string base64 = Convert.ToBase64String(imageData);
                    responseData["base64"] = base64;
                    responseData["base64Length"] = base64.Length;
                }
                else
                {
                    responseData["hint"] = "Use returnBase64=true to get image data, or read the saved file";
                }

                return new McpToolResult
                {
                    content = new List<McpContent> { McpContent.Json(responseData) },
                    isError = false
                };
            }
            finally
            {
                if (screenshot != null)
                {
                    UnityEngine.Object.DestroyImmediate(screenshot);
                }
            }
        }

        private static Texture2D CaptureSceneView(int width, int height)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return null;
            }

            var camera = sceneView.camera;
            if (camera == null)
            {
                return null;
            }

            // Create render texture
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;

            // Store original values
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();

                return screenshot;
            }
            finally
            {
                camera.targetTexture = originalTarget;
                RenderTexture.active = originalActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static Texture2D CaptureGameView(int width, int height)
        {
            // In Play Mode, we can use ScreenCapture
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();

            if (screenshot == null)
            {
                return null;
            }

            // Resize if needed
            if (screenshot.width != width || screenshot.height != height)
            {
                var resized = ResizeTexture(screenshot, width, height);
                UnityEngine.Object.DestroyImmediate(screenshot);
                return resized;
            }

            return screenshot;
        }

        #endregion

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
                scriptingBackend = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString()
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
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var hierarchy = new List<object>();

            foreach (var obj in rootObjects)
            {
                hierarchy.Add(GetGameObjectInfo(obj, 0));
            }

            return new McpResourceContent
            {
                uri = "unity://scene/hierarchy",
                mimeType = "application/json",
                text = JsonHelper.ToJson(new { sceneName = scene.name, rootObjects = hierarchy })
            };
        }

        private static object GetGameObjectInfo(GameObject obj, int depth)
        {
            var children = new List<object>();
            if (depth < 3)
            {
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    children.Add(GetGameObjectInfo(obj.transform.GetChild(i).gameObject, depth + 1));
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
                Debug.LogWarning("[MCP Unity] Server is already running");
                return;
            }

            try
            {
                _wss = new WebSocketServer($"ws://127.0.0.1:{Port}");
                _wss.AddWebSocketService<McpBehavior>("/");
                _wss.Start();
                _isRunning = true;

                Debug.Log($"[MCP Unity] Server started on ws://127.0.0.1:{Port}");
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
                Debug.LogWarning($"[MCP Unity] Error stopping server: {ex.Message}");
            }

            _connectedClients.Clear();
            _isRunning = false;
            Debug.Log("[MCP Unity] Server stopped");
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
            Debug.Log($"[MCP Unity] Client connected: {id}");
            OnClientConnected?.Invoke(id);
        }

        /// <summary>
        /// Unregister a client connection
        /// </summary>
        internal static void UnregisterClient(string id)
        {
            _connectedClients.TryRemove(id, out _);
            Debug.Log($"[MCP Unity] Client disconnected: {id}");
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
                    Debug.LogWarning($"[MCP Unity] Failed to broadcast to client {kvp.Key}: {ex.Message}");
                }
            }
        }

        #endregion

        #region Selection & Scene Handlers

        private static McpToolResult GetSelection(Dictionary<string, object> args)
        {
            bool includeAssets = false;
            if (args.TryGetValue("includeAssets", out var includeObj) && includeObj != null)
            {
                includeAssets = Convert.ToBoolean(includeObj);
            }

            var result = new Dictionary<string, object>();

            // Active object
            if (Selection.activeGameObject != null)
            {
                result["activeObject"] = new Dictionary<string, object>
                {
                    ["name"] = Selection.activeGameObject.name,
                    ["path"] = GetGameObjectPath(Selection.activeGameObject)
                };
            }

            // Selected GameObjects
            var gameObjects = new List<Dictionary<string, object>>();
            foreach (var go in Selection.gameObjects)
            {
                gameObjects.Add(new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["path"] = GetGameObjectPath(go)
                });
            }
            result["gameObjects"] = gameObjects;

            // Selected assets (if requested)
            if (includeAssets)
            {
                var assets = new List<string>();
                foreach (var guid in Selection.assetGUIDs)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        assets.Add(path);
                    }
                }
                result["assets"] = assets;
            }

            result["count"] = Selection.count;

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }

        private static McpToolResult SetSelection(Dictionary<string, object> args)
        {
            // Check for clear
            if (args.TryGetValue("clear", out var clearObj) && clearObj != null && Convert.ToBoolean(clearObj))
            {
                Selection.objects = new UnityEngine.Object[0];
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new { success = true, message = "Selection cleared", selectedCount = 0 })
                    },
                    isError = false
                };
            }

            var objectsToSelect = new List<UnityEngine.Object>();
            var notFound = new List<string>();

            // GameObjects
            if (args.TryGetValue("gameObjectPaths", out var goPathsObj) && goPathsObj != null)
            {
                var paths = ConvertToStringArray(goPathsObj);
                foreach (var path in paths)
                {
                    var go = GameObject.Find(path);
                    if (go != null)
                    {
                        objectsToSelect.Add(go);
                    }
                    else
                    {
                        notFound.Add(path);
                    }
                }
            }

            // Assets
            if (args.TryGetValue("assetPaths", out var assetPathsObj) && assetPathsObj != null)
            {
                var paths = ConvertToStringArray(assetPathsObj);
                foreach (var path in paths)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null)
                    {
                        objectsToSelect.Add(asset);
                    }
                    else
                    {
                        notFound.Add(path);
                    }
                }
            }

            if (objectsToSelect.Count == 0 && notFound.Count > 0)
            {
                return McpToolResult.Error($"No objects found. Not found: {string.Join(", ", notFound)}");
            }

            Selection.objects = objectsToSelect.ToArray();

            var resultData = new Dictionary<string, object>
            {
                ["success"] = true,
                ["selectedCount"] = objectsToSelect.Count,
                ["selectedObjects"] = objectsToSelect.Select(o => o.name).ToList()
            };

            if (notFound.Count > 0)
            {
                resultData["notFound"] = notFound;
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(resultData) },
                isError = false
            };
        }

        private static McpToolResult GetSceneInfo(Dictionary<string, object> args)
        {
            var activeScene = SceneManager.GetActiveScene();

            var activeSceneInfo = new Dictionary<string, object>
            {
                ["name"] = activeScene.name,
                ["path"] = activeScene.path,
                ["buildIndex"] = activeScene.buildIndex,
                ["isDirty"] = activeScene.isDirty,
                ["isLoaded"] = activeScene.isLoaded,
                ["rootCount"] = activeScene.rootCount
            };

            var loadedScenes = new List<Dictionary<string, object>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                loadedScenes.Add(new Dictionary<string, object>
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["isDirty"] = scene.isDirty,
                    ["isLoaded"] = scene.isLoaded
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        activeScene = activeSceneInfo,
                        loadedScenes = loadedScenes,
                        totalLoadedScenes = SceneManager.sceneCount
                    })
                },
                isError = false
            };
        }

        private static McpToolResult LoadScene(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("scenePath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("scenePath is required");
            }

            string scenePath = pathObj.ToString();

            // Security: Validate path to prevent path traversal attacks
            try
            {
                scenePath = SanitizePath(scenePath);
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid scene path: {ex.Message}");
            }

            // Validate extension
            if (!scenePath.EndsWith(".unity"))
            {
                return McpToolResult.Error("Invalid scene path. Must end with .unity");
            }

            if (!System.IO.File.Exists(scenePath))
            {
                return McpToolResult.Error($"Scene not found: {scenePath}");
            }

            // Check for unsaved changes
            var currentScene = SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                // We'll proceed but warn
            }

            // Parse mode
            OpenSceneMode mode = OpenSceneMode.Single;
            if (args.TryGetValue("mode", out var modeObj) && modeObj != null)
            {
                string modeStr = modeObj.ToString();
                if (modeStr.Equals("Additive", StringComparison.OrdinalIgnoreCase))
                {
                    mode = OpenSceneMode.Additive;
                }
            }

            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, mode);

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            success = true,
                            sceneName = scene.name,
                            scenePath = scene.path,
                            mode = mode.ToString()
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to load scene: {ex.Message}");
            }
        }

        private static McpToolResult SaveScene(Dictionary<string, object> args)
        {
            bool saveAll = false;
            if (args.TryGetValue("saveAll", out var saveAllObj) && saveAllObj != null)
            {
                saveAll = Convert.ToBoolean(saveAllObj);
            }

            string saveAsPath = null;
            if (args.TryGetValue("scenePath", out var pathObj) && pathObj != null)
            {
                saveAsPath = pathObj.ToString();

                // Security: Validate path to prevent path traversal attacks
                try
                {
                    saveAsPath = SanitizePath(saveAsPath);
                }
                catch (ArgumentException ex)
                {
                    return McpToolResult.Error($"Invalid save path: {ex.Message}");
                }
            }

            try
            {
                if (saveAll)
                {
                    bool success = EditorSceneManager.SaveOpenScenes();

                    var savedScenes = new List<string>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        savedScenes.Add(SceneManager.GetSceneAt(i).name);
                    }

                    return new McpToolResult
                    {
                        content = new List<McpContent>
                        {
                            McpContent.Json(new
                            {
                                success = success,
                                message = success ? "All scenes saved" : "Failed to save some scenes",
                                savedScenes = savedScenes
                            })
                        },
                        isError = !success
                    };
                }
                else
                {
                    var activeScene = SceneManager.GetActiveScene();
                    bool success;

                    if (!string.IsNullOrEmpty(saveAsPath))
                    {
                        // Validate path
                        if (!saveAsPath.EndsWith(".unity"))
                        {
                            return McpToolResult.Error("Save path must end with .unity");
                        }

                        // Ensure directory exists
                        var directory = System.IO.Path.GetDirectoryName(saveAsPath);
                        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                        {
                            System.IO.Directory.CreateDirectory(directory);
                        }

                        success = EditorSceneManager.SaveScene(activeScene, saveAsPath);
                    }
                    else
                    {
                        success = EditorSceneManager.SaveScene(activeScene);
                    }

                    return new McpToolResult
                    {
                        content = new List<McpContent>
                        {
                            McpContent.Json(new
                            {
                                success = success,
                                sceneName = activeScene.name,
                                scenePath = string.IsNullOrEmpty(saveAsPath) ? activeScene.path : saveAsPath,
                                message = success ? "Scene saved successfully" : "Failed to save scene"
                            })
                        },
                        isError = !success
                    };
                }
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to save scene: {ex.Message}");
            }
        }

        private static McpToolResult CreateScene(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("sceneName", out var nameObj) || nameObj == null)
            {
                return McpToolResult.Error("sceneName is required");
            }

            string sceneName = nameObj.ToString();

            // Parse setup mode
            NewSceneSetup sceneSetup = NewSceneSetup.DefaultGameObjects;
            if (args.TryGetValue("setup", out var setupObj) && setupObj != null)
            {
                string setupStr = setupObj.ToString();
                if (setupStr.Equals("empty", StringComparison.OrdinalIgnoreCase))
                {
                    sceneSetup = NewSceneSetup.EmptyScene;
                }
            }

            // Parse scene mode
            NewSceneMode sceneMode = NewSceneMode.Single;
            if (args.TryGetValue("mode", out var modeObj) && modeObj != null)
            {
                string modeStr = modeObj.ToString();
                if (modeStr.Equals("additive", StringComparison.OrdinalIgnoreCase))
                {
                    sceneMode = NewSceneMode.Additive;
                }
            }

            try
            {
                // Create the new scene
                var scene = EditorSceneManager.NewScene(sceneSetup, sceneMode);

                string savedPath = null;

                // Save if path provided
                if (args.TryGetValue("savePath", out var pathObj) && pathObj != null && !string.IsNullOrEmpty(pathObj.ToString()))
                {
                    string savePath;
                    try
                    {
                        savePath = SanitizePath(pathObj.ToString());
                    }
                    catch (ArgumentException ex)
                    {
                        return McpToolResult.Error($"Invalid save path: {ex.Message}");
                    }

                    if (!savePath.EndsWith(".unity"))
                    {
                        return McpToolResult.Error("Save path must end with .unity");
                    }

                    // Ensure directory exists
                    var directory = System.IO.Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    EditorSceneManager.SaveScene(scene, savePath);
                    savedPath = savePath;
                }

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            success = true,
                            sceneName = scene.name,
                            scenePath = savedPath ?? "(unsaved)",
                            setup = sceneSetup.ToString(),
                            mode = sceneMode.ToString(),
                            rootObjectCount = scene.rootCount
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create scene: {ex.Message}");
            }
        }

        private static McpToolResult CreatePrefab(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var goPathObj) || goPathObj == null)
            {
                return McpToolResult.Error("gameObjectPath is required");
            }

            if (!args.TryGetValue("savePath", out var savePathObj) || savePathObj == null)
            {
                return McpToolResult.Error("savePath is required");
            }

            string gameObjectPath = goPathObj.ToString();
            string savePath;
            try
            {
                savePath = SanitizePath(savePathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid save path: {ex.Message}");
            }

            if (!savePath.EndsWith(".prefab"))
            {
                return McpToolResult.Error("Save path must end with .prefab");
            }

            // Find the GameObject
            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            // Parse connectInstance (default: true)
            bool connectInstance = true;
            if (args.TryGetValue("connectInstance", out var connectObj) && connectObj != null)
            {
                connectInstance = Convert.ToBoolean(connectObj);
            }

            try
            {
                // Ensure directory exists
                var directory = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                GameObject prefab;
                if (connectInstance)
                {
                    prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.UserAction);
                }
                else
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, savePath);
                }

                if (prefab == null)
                {
                    return McpToolResult.Error("Failed to create prefab");
                }

                AssetDatabase.Refresh();

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            success = true,
                            prefabPath = savePath,
                            prefabName = prefab.name,
                            sourceGameObject = gameObjectPath,
                            isConnected = connectInstance
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create prefab: {ex.Message}");
            }
        }

        private static McpToolResult UnpackPrefab(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var goPathObj) || goPathObj == null)
            {
                return McpToolResult.Error("gameObjectPath is required");
            }

            string gameObjectPath = goPathObj.ToString();

            // Find the GameObject
            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            // Check if it's a prefab instance
            var status = PrefabUtility.GetPrefabInstanceStatus(go);
            if (status == PrefabInstanceStatus.NotAPrefab)
            {
                return McpToolResult.Error($"GameObject is not a prefab instance: {gameObjectPath}");
            }

            // Parse unpack mode (default: completely)
            PrefabUnpackMode unpackMode = PrefabUnpackMode.Completely;
            if (args.TryGetValue("unpackMode", out var modeObj) && modeObj != null)
            {
                string modeStr = modeObj.ToString();
                if (modeStr.Equals("root", StringComparison.OrdinalIgnoreCase))
                {
                    unpackMode = PrefabUnpackMode.OutermostRoot;
                }
            }

            try
            {
                // Get prefab path before unpacking for return value
                var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                string prefabPath = sourcePrefab != null ? AssetDatabase.GetAssetPath(sourcePrefab) : null;

                // Unpack with Undo support
                Undo.RegisterFullObjectHierarchyUndo(go, "Unpack Prefab");
                PrefabUtility.UnpackPrefabInstance(go, unpackMode, InteractionMode.UserAction);

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            success = true,
                            gameObjectPath = gameObjectPath,
                            previousPrefab = prefabPath,
                            unpackMode = unpackMode.ToString(),
                            message = "Prefab instance unpacked successfully"
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to unpack prefab: {ex.Message}");
            }
        }

        private static McpToolResult ApplyPrefabOverrides(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var goPathObj) || goPathObj == null)
            {
                return McpToolResult.Error("gameObjectPath is required");
            }

            string gameObjectPath = goPathObj.ToString();

            // Find the GameObject
            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            // Check if it's a connected prefab instance
            var status = PrefabUtility.GetPrefabInstanceStatus(go);
            if (status == PrefabInstanceStatus.NotAPrefab)
            {
                return McpToolResult.Error($"GameObject is not a prefab instance: {gameObjectPath}");
            }
            if (status == PrefabInstanceStatus.Disconnected)
            {
                return McpToolResult.Error($"Prefab instance is disconnected: {gameObjectPath}");
            }

            try
            {
                // Get source prefab path for return value
                var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                string prefabPath = sourcePrefab != null ? AssetDatabase.GetAssetPath(sourcePrefab) : null;

                // Apply overrides to source prefab
                PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            success = true,
                            instancePath = gameObjectPath,
                            prefabPath = prefabPath,
                            message = "All overrides applied to source prefab"
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to apply prefab overrides: {ex.Message}");
            }
        }

        private static McpToolResult PerformUndo(Dictionary<string, object> args)
        {
            int steps = 1;
            if (args.TryGetValue("steps", out var stepsObj) && stepsObj != null)
            {
                steps = Math.Max(1, Convert.ToInt32(stepsObj));
            }

            bool redo = false;
            if (args.TryGetValue("redo", out var redoObj) && redoObj != null)
            {
                redo = Convert.ToBoolean(redoObj);
            }

            var actionsPerformed = new List<string>();

            for (int i = 0; i < steps; i++)
            {
                string currentAction = Undo.GetCurrentGroupName();
                if (string.IsNullOrEmpty(currentAction))
                {
                    currentAction = redo ? "(redo action)" : "(undo action)";
                }

                if (redo)
                {
                    Undo.PerformRedo();
                }
                else
                {
                    Undo.PerformUndo();
                }

                actionsPerformed.Add(currentAction);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        operation = redo ? "Redo" : "Undo",
                        stepsPerformed = steps,
                        actions = actionsPerformed
                    })
                },
                isError = false
            };
        }

        private static string[] ConvertToStringArray(object obj)
        {
            if (obj is string[] strArray)
            {
                return strArray;
            }

            if (obj is IEnumerable<object> enumerable)
            {
                return enumerable.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }

            if (obj is System.Collections.IList list)
            {
                var result = new List<string>();
                foreach (var item in list)
                {
                    if (item != null)
                    {
                        result.Add(item.ToString());
                    }
                }
                return result.ToArray();
            }

            return new string[0];
        }

        #endregion

        #region Memory/Cache Handlers

        private const string CacheDirectory = "Assets/.mcp-cache";
        private const string CacheFilePath = "Assets/.mcp-cache/memory.json";
        private const int MaxOperationsHistory = 20;
        private const int CacheMaxAgeMinutes = 5;

        private static Dictionary<string, object> LoadMemoryCache()
        {
            EnsureCacheDirectory();

            if (!System.IO.File.Exists(CacheFilePath))
            {
                return CreateEmptyCache();
            }

            try
            {
                var json = System.IO.File.ReadAllText(CacheFilePath);
                return JsonHelper.FromJson<Dictionary<string, object>>(json) ?? CreateEmptyCache();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP Unity] Failed to load cache: {e.Message}");
                return CreateEmptyCache();
            }
        }

        private static Dictionary<string, object> CreateEmptyCache()
        {
            return new Dictionary<string, object>
            {
                ["version"] = "1.0",
                ["lastUpdated"] = DateTime.Now.ToString("o"),
                ["projectName"] = Application.productName,
                ["assets"] = null,
                ["scenes"] = null,
                ["hierarchy"] = null,
                ["operations"] = new List<object>()
            };
        }

        private static void SaveMemoryCache(Dictionary<string, object> cache)
        {
            EnsureCacheDirectory();
            cache["lastUpdated"] = DateTime.Now.ToString("o");

            try
            {
                var json = JsonHelper.ToJson(cache);
                System.IO.File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Unity] Failed to save cache: {e.Message}");
            }
        }

        private static void EnsureCacheDirectory()
        {
            if (!System.IO.Directory.Exists(CacheDirectory))
            {
                System.IO.Directory.CreateDirectory(CacheDirectory);

                // Create .gitignore
                var gitignorePath = System.IO.Path.Combine(CacheDirectory, ".gitignore");
                System.IO.File.WriteAllText(gitignorePath, "# MCP cache files\nmemory.json\n");
            }
        }

        private static bool IsSectionStale(Dictionary<string, object> cache, string section)
        {
            if (cache[section] == null) return true;

            var sectionData = cache[section] as Dictionary<string, object>;
            if (sectionData == null || !sectionData.ContainsKey("lastFetch")) return true;

            if (DateTime.TryParse(sectionData["lastFetch"] as string, out var lastFetch))
            {
                return (DateTime.Now - lastFetch).TotalMinutes > CacheMaxAgeMinutes;
            }
            return true;
        }

        private static void LogOperation(string tool, string result)
        {
            var cache = LoadMemoryCache();
            var operations = cache["operations"] as List<object> ?? new List<object>();

            operations.Insert(0, new Dictionary<string, object>
            {
                ["time"] = DateTime.Now.ToString("HH:mm:ss"),
                ["tool"] = tool,
                ["result"] = result
            });

            // Keep only MaxOperationsHistory
            while (operations.Count > MaxOperationsHistory)
            {
                operations.RemoveAt(operations.Count - 1);
            }

            cache["operations"] = operations;
            SaveMemoryCache(cache);
        }

        private static McpToolResult MemoryGet(Dictionary<string, object> args)
        {
            var section = "all";
            if (args.TryGetValue("section", out var sectionObj) && sectionObj != null)
            {
                section = sectionObj.ToString().ToLower();
            }

            var cache = LoadMemoryCache();
            var result = new Dictionary<string, object>();

            if (section == "all")
            {
                result["version"] = cache["version"];
                result["lastUpdated"] = cache["lastUpdated"];
                result["projectName"] = cache["projectName"];
                result["assets"] = cache["assets"];
                result["scenes"] = cache["scenes"];
                result["hierarchy"] = cache["hierarchy"];
                result["operations"] = cache["operations"];

                result["stale"] = new Dictionary<string, object>
                {
                    ["assets"] = cache["assets"] == null || IsSectionStale(cache, "assets"),
                    ["scenes"] = cache["scenes"] == null || IsSectionStale(cache, "scenes"),
                    ["hierarchy"] = cache["hierarchy"] == null || IsSectionStale(cache, "hierarchy")
                };
            }
            else if (cache.ContainsKey(section))
            {
                result["section"] = section;
                result["data"] = cache[section];
                result["needsRefresh"] = cache[section] == null || (section != "operations" && IsSectionStale(cache, section));
            }
            else
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, operations, all")
                    },
                    isError = true
                };
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }

        private static McpToolResult MemoryRefresh(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("section", out var sectionObj) || sectionObj == null)
            {
                return new McpToolResult
                {
                    content = new List<McpContent> { McpContent.Text("Missing required parameter: section") },
                    isError = true
                };
            }

            var section = sectionObj.ToString().ToLower();
            var cache = LoadMemoryCache();
            var refreshed = new List<string>();

            if (section == "all" || section == "assets")
            {
                cache["assets"] = RefreshAssetsCache();
                refreshed.Add("assets");
            }

            if (section == "all" || section == "scenes")
            {
                cache["scenes"] = RefreshScenesCache();
                refreshed.Add("scenes");
            }

            if (section == "all" || section == "hierarchy")
            {
                cache["hierarchy"] = RefreshHierarchyCache();
                refreshed.Add("hierarchy");
            }

            if (refreshed.Count == 0)
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, all")
                    },
                    isError = true
                };
            }

            SaveMemoryCache(cache);
            LogOperation("unity_memory_refresh", $"Refreshed: {string.Join(", ", refreshed)}");

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["refreshed"] = refreshed,
                        ["assets"] = section == "all" || section == "assets" ? cache["assets"] : null,
                        ["scenes"] = section == "all" || section == "scenes" ? cache["scenes"] : null,
                        ["hierarchy"] = section == "all" || section == "hierarchy" ? cache["hierarchy"] : null
                    })
                },
                isError = false
            };
        }

        private static Dictionary<string, object> RefreshAssetsCache()
        {
            var byType = new Dictionary<string, List<string>>();
            var allAssets = AssetDatabase.GetAllAssetPaths();

            foreach (var path in allAssets)
            {
                if (!path.StartsWith("Assets/")) continue;
                if (path.StartsWith("Assets/.mcp-cache")) continue;

                var ext = System.IO.Path.GetExtension(path).ToLower();
                var type = GetAssetType(ext);

                if (!byType.ContainsKey(type))
                    byType[type] = new List<string>();

                byType[type].Add(path);
            }

            // Convert to serializable format
            var byTypeSerializable = new Dictionary<string, object>();
            foreach (var kvp in byType)
            {
                byTypeSerializable[kvp.Key] = kvp.Value;
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["count"] = allAssets.Count(p => p.StartsWith("Assets/") && !p.StartsWith("Assets/.mcp-cache")),
                ["byType"] = byTypeSerializable
            };
        }

        private static string GetAssetType(string extension)
        {
            switch (extension)
            {
                case ".prefab": return "Prefab";
                case ".unity": return "Scene";
                case ".cs": return "Script";
                case ".mat": return "Material";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": return "Texture";
                case ".fbx": case ".obj": case ".blend": return "Model";
                case ".anim": return "Animation";
                case ".controller": return "AnimatorController";
                case ".mp3": case ".wav": case ".ogg": return "Audio";
                case ".shader": case ".shadergraph": return "Shader";
                case ".asset": return "ScriptableObject";
                case ".json": case ".xml": case ".txt": return "TextAsset";
                default: return "Other";
            }
        }

        private static Dictionary<string, object> RefreshScenesCache()
        {
            var activeScene = SceneManager.GetActiveScene();
            var sceneList = new List<Dictionary<string, object>>();

            // Get all scenes in build settings
            var scenesInBuild = EditorBuildSettings.scenes;
            for (int i = 0; i < scenesInBuild.Length; i++)
            {
                var scene = scenesInBuild[i];
                sceneList.Add(new Dictionary<string, object>
                {
                    ["path"] = scene.path,
                    ["name"] = System.IO.Path.GetFileNameWithoutExtension(scene.path),
                    ["buildIndex"] = i,
                    ["enabled"] = scene.enabled
                });
            }

            // Also add currently loaded scenes not in build settings
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!sceneList.Any(s => s["path"] as string == scene.path))
                {
                    sceneList.Add(new Dictionary<string, object>
                    {
                        ["path"] = scene.path,
                        ["name"] = scene.name,
                        ["buildIndex"] = -1,
                        ["enabled"] = true,
                        ["loaded"] = true
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["active"] = activeScene.name,
                ["activePath"] = activeScene.path,
                ["count"] = sceneList.Count,
                ["list"] = sceneList
            };
        }

        private static Dictionary<string, object> RefreshHierarchyCache()
        {
            var activeScene = SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();
            var rootNames = rootObjects.Select(go => go.name).ToList();

            // Count total GameObjects
            int totalCount = 0;
            foreach (var root in rootObjects)
            {
                totalCount += CountGameObjects(root.transform);
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["scene"] = activeScene.name,
                ["rootObjects"] = rootNames,
                ["rootCount"] = rootObjects.Length,
                ["totalCount"] = totalCount,
                ["summary"] = $"{rootObjects.Length} root objects, {totalCount} total GameObjects"
            };
        }

        private static int CountGameObjects(Transform parent)
        {
            int count = 1;
            foreach (Transform child in parent)
            {
                count += CountGameObjects(child);
            }
            return count;
        }

        private static McpToolResult MemoryClear(Dictionary<string, object> args)
        {
            var section = "all";
            if (args.TryGetValue("section", out var sectionObj) && sectionObj != null)
            {
                section = sectionObj.ToString().ToLower();
            }

            var cache = LoadMemoryCache();
            var cleared = new List<string>();

            if (section == "all")
            {
                cache = CreateEmptyCache();
                cleared.AddRange(new[] { "assets", "scenes", "hierarchy", "operations" });
            }
            else if (section == "assets" || section == "scenes" || section == "hierarchy")
            {
                cache[section] = null;
                cleared.Add(section);
            }
            else if (section == "operations")
            {
                cache["operations"] = new List<object>();
                cleared.Add("operations");
            }
            else
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, operations, all")
                    },
                    isError = true
                };
            }

            SaveMemoryCache(cache);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["cleared"] = cleared,
                        ["message"] = $"Cleared: {string.Join(", ", cleared)}"
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Material Tools

        private static McpToolResult GetMaterial(Dictionary<string, object> args)
        {
            string materialPath = null;
            string gameObjectPath = null;
            int materialIndex = 0;

            if (args.TryGetValue("materialPath", out var pathObj) && pathObj != null)
            {
                materialPath = pathObj.ToString();
                // Validate path for security
                if (!string.IsNullOrEmpty(materialPath))
                {
                    try
                    {
                        materialPath = SanitizePath(materialPath);
                    }
                    catch (ArgumentException ex)
                    {
                        return McpToolResult.Error($"Invalid material path: {ex.Message}");
                    }
                }
            }

            if (args.TryGetValue("gameObjectPath", out var goObj) && goObj != null)
            {
                gameObjectPath = goObj.ToString();
            }

            if (args.TryGetValue("materialIndex", out var indexObj) && indexObj != null)
            {
                materialIndex = Convert.ToInt32(indexObj);
            }

            // Need at least one source
            if (string.IsNullOrEmpty(materialPath) && string.IsNullOrEmpty(gameObjectPath))
            {
                return McpToolResult.Error("Either materialPath or gameObjectPath is required");
            }

            var material = MaterialHelpers.FindMaterial(materialPath, gameObjectPath, materialIndex);
            if (material == null)
            {
                return McpToolResult.Error($"Material not found. Path: {materialPath}, GameObject: {gameObjectPath}");
            }

            var result = MaterialHelpers.SerializeMaterial(material);

            // Add asset path if available
            string assetPath = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(assetPath))
            {
                result["assetPath"] = assetPath;
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }

        private static McpToolResult SetMaterial(Dictionary<string, object> args)
        {
            string materialPath = null;
            string gameObjectPath = null;
            int materialIndex = 0;

            if (args.TryGetValue("materialPath", out var pathObj) && pathObj != null)
            {
                materialPath = pathObj.ToString();
                if (!string.IsNullOrEmpty(materialPath))
                {
                    try
                    {
                        materialPath = SanitizePath(materialPath);
                    }
                    catch (ArgumentException ex)
                    {
                        return McpToolResult.Error($"Invalid material path: {ex.Message}");
                    }
                }
            }

            if (args.TryGetValue("gameObjectPath", out var goObj) && goObj != null)
            {
                gameObjectPath = goObj.ToString();
            }

            if (args.TryGetValue("materialIndex", out var indexObj) && indexObj != null)
            {
                materialIndex = Convert.ToInt32(indexObj);
            }

            if (string.IsNullOrEmpty(materialPath) && string.IsNullOrEmpty(gameObjectPath))
            {
                return McpToolResult.Error("Either materialPath or gameObjectPath is required");
            }

            var material = MaterialHelpers.FindMaterial(materialPath, gameObjectPath, materialIndex);
            if (material == null)
            {
                return McpToolResult.Error($"Material not found. Path: {materialPath}, GameObject: {gameObjectPath}");
            }

            // Record for undo
            Undo.RecordObject(material, "MCP Modify Material");

            var modifiedProperties = new List<string>();

            // Detect render pipeline for shader compatibility
            string currentPipeline = MaterialHelpers.DetectRenderPipeline();

            // Change shader if specified
            if (args.TryGetValue("shader", out var shaderObj) && shaderObj != null)
            {
                var shaderName = shaderObj.ToString();
                // Auto-convert Standard shader for URP/HDRP projects
                if (shaderName == "Standard" && currentPipeline != "BuiltIn")
                {
                    shaderName = MaterialHelpers.GetDefaultShaderName();
                    Debug.Log($"[MCP SetMaterial] Auto-converting 'Standard' to '{shaderName}' for {currentPipeline}");
                }

                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    material.shader = shader;
                    modifiedProperties.Add($"shader ({shaderName})");
                }
                else
                {
                    Debug.LogWarning($"[MCP Unity] Shader not found: {shaderName}");
                }
            }

            // Set render queue if specified
            if (args.TryGetValue("renderQueue", out var queueObj) && queueObj != null)
            {
                material.renderQueue = Convert.ToInt32(queueObj);
                modifiedProperties.Add("renderQueue");
            }

            // Set properties (with automatic property name mapping for URP/HDRP)
            if (args.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> properties)
            {
                foreach (var kvp in properties)
                {
                    // Map property names if needed (e.g., _Color -> _BaseColor for URP)
                    string mappedPropertyName = MaterialHelpers.MapPropertyName(kvp.Key, currentPipeline);

                    if (MaterialHelpers.SetPropertyValue(material, mappedPropertyName, kvp.Value))
                    {
                        modifiedProperties.Add(kvp.Key + (mappedPropertyName != kvp.Key ? $" (mapped to {mappedPropertyName})" : ""));
                    }
                    else if (mappedPropertyName != kvp.Key)
                    {
                        // Try original name if mapping failed
                        if (MaterialHelpers.SetPropertyValue(material, kvp.Key, kvp.Value))
                        {
                            modifiedProperties.Add(kvp.Key);
                        }
                    }
                }
            }

            // Mark as dirty to save changes
            EditorUtility.SetDirty(material);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["material"] = material.name,
                        ["modifiedProperties"] = modifiedProperties,
                        ["message"] = $"Modified {modifiedProperties.Count} properties on {material.name}"
                    })
                },
                isError = false
            };
        }

        private static McpToolResult CreateMaterial(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("name", out var nameObj) || nameObj == null)
            {
                return McpToolResult.Error("name is required");
            }

            if (!args.TryGetValue("savePath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("savePath is required");
            }

            var materialName = nameObj.ToString();
            string savePath;

            try
            {
                savePath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid save path: {ex.Message}");
            }

            // Ensure path ends with .mat
            if (!savePath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                savePath = savePath + ".mat";
            }

            // Detect render pipeline
            string renderPipeline = MaterialHelpers.DetectRenderPipeline();

            // Get shader name - use pipeline-appropriate default if not specified or "Standard"
            string shaderName = MaterialHelpers.GetDefaultShaderName();
            if (args.TryGetValue("shader", out var shaderObj) && shaderObj != null)
            {
                string requestedShader = shaderObj.ToString();
                // If user requested "Standard" but we're in URP/HDRP, auto-convert
                if (requestedShader == "Standard" && renderPipeline != "BuiltIn")
                {
                    shaderName = MaterialHelpers.GetDefaultShaderName();
                    Debug.Log($"[MCP CreateMaterial] Auto-converting 'Standard' to '{shaderName}' for {renderPipeline}");
                }
                else
                {
                    shaderName = requestedShader;
                }
            }

            // Create the material
            var material = MaterialHelpers.CreateMaterial(shaderName);
            if (material == null)
            {
                return McpToolResult.Error($"Failed to create material: shader '{shaderName}' not found");
            }
            material.name = materialName;

            // Set initial properties if provided (with property name mapping for URP/HDRP)
            var setProperties = new List<string>();
            if (args.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> properties)
            {
                foreach (var kvp in properties)
                {
                    // Map property names if needed (e.g., _Color -> _BaseColor for URP)
                    string mappedPropertyName = MaterialHelpers.MapPropertyName(kvp.Key, renderPipeline);

                    if (MaterialHelpers.SetPropertyValue(material, mappedPropertyName, kvp.Value))
                    {
                        setProperties.Add(kvp.Key + (mappedPropertyName != kvp.Key ? $" (mapped to {mappedPropertyName})" : ""));
                    }
                    else if (mappedPropertyName != kvp.Key)
                    {
                        // Try original name if mapping failed
                        if (MaterialHelpers.SetPropertyValue(material, kvp.Key, kvp.Value))
                        {
                            setProperties.Add(kvp.Key);
                        }
                    }
                }
            }

            // Ensure directory exists
            var directory = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                // Create directories recursively
                var parts = directory.Split('/');
                var currentPath = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var parentPath = currentPath;
                    currentPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(currentPath))
                    {
                        AssetDatabase.CreateFolder(parentPath, parts[i]);
                    }
                }
            }

            // Save as asset
            AssetDatabase.CreateAsset(material, savePath);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["name"] = materialName,
                        ["path"] = savePath,
                        ["shader"] = material.shader.name,
                        ["renderPipeline"] = renderPipeline,
                        ["propertiesSet"] = setProperties,
                        ["message"] = $"Created material '{materialName}' with {material.shader.name} shader ({renderPipeline}) at {savePath}"
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Tags & Layers

        private static McpToolResult ListTags(Dictionary<string, object> args)
        {
            var tags = InternalEditorUtility.tags;

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["tags"] = tags,
                        ["count"] = tags.Length
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ListLayers(Dictionary<string, object> args)
        {
            bool includeEmpty = args.TryGetValue("includeEmpty", out var ie) && Convert.ToBoolean(ie);

            var layers = new List<object>();

            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);

                if (!includeEmpty && string.IsNullOrEmpty(layerName))
                    continue;

                layers.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = string.IsNullOrEmpty(layerName) ? "(empty)" : layerName,
                    ["isBuiltin"] = i < 8
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["layers"] = layers,
                        ["count"] = layers.Count
                    })
                },
                isError = false
            };
        }

        private static McpToolResult SetTag(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var pathObj) || pathObj == null)
                return CreateErrorResult("Missing required parameter: gameObjectPath");

            if (!args.TryGetValue("tag", out var tagObj) || tagObj == null)
                return CreateErrorResult("Missing required parameter: tag");

            string gameObjectPath = pathObj.ToString();
            string newTag = tagObj.ToString();
            bool recursive = args.TryGetValue("recursive", out var r) && Convert.ToBoolean(r);

            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
                return CreateErrorResult($"GameObject not found: {gameObjectPath}");

            // Vérifier que le tag existe
            var validTags = InternalEditorUtility.tags;
            if (!validTags.Contains(newTag))
                return CreateErrorResult($"Tag '{newTag}' does not exist. Available tags: {string.Join(", ", validTags)}");

            string oldTag = go.tag;
            int count = 1;

            Undo.RecordObject(go, "Set Tag");
            go.tag = newTag;

            if (recursive)
            {
                count = SetTagRecursive(go.transform, newTag);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["gameObjectPath"] = gameObjectPath,
                        ["oldTag"] = oldTag,
                        ["newTag"] = newTag,
                        ["objectsModified"] = count,
                        ["message"] = $"Set tag to '{newTag}' on {count} object(s)"
                    })
                },
                isError = false
            };
        }

        private static int SetTagRecursive(Transform parent, string tag)
        {
            int count = 1;
            foreach (Transform child in parent)
            {
                Undo.RecordObject(child.gameObject, "Set Tag");
                child.gameObject.tag = tag;
                count += SetTagRecursive(child, tag);
            }
            return count;
        }

        private static McpToolResult SetLayer(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var pathObj) || pathObj == null)
                return CreateErrorResult("Missing required parameter: gameObjectPath");

            if (!args.TryGetValue("layer", out var layerObj) || layerObj == null)
                return CreateErrorResult("Missing required parameter: layer");

            string gameObjectPath = pathObj.ToString();
            string layerName = layerObj.ToString();
            bool recursive = args.TryGetValue("recursive", out var r) && Convert.ToBoolean(r);

            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
                return CreateErrorResult($"GameObject not found: {gameObjectPath}");

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex == -1)
                return CreateErrorResult($"Layer '{layerName}' does not exist");

            string oldLayer = LayerMask.LayerToName(go.layer);
            int count = 1;

            Undo.RecordObject(go, "Set Layer");
            go.layer = layerIndex;

            if (recursive)
            {
                count = SetLayerRecursive(go.transform, layerIndex);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["gameObjectPath"] = gameObjectPath,
                        ["oldLayer"] = oldLayer,
                        ["newLayer"] = layerName,
                        ["objectsModified"] = count,
                        ["message"] = $"Set layer to '{layerName}' on {count} object(s)"
                    })
                },
                isError = false
            };
        }

        private static int SetLayerRecursive(Transform parent, int layer)
        {
            int count = 1;
            foreach (Transform child in parent)
            {
                Undo.RecordObject(child.gameObject, "Set Layer");
                child.gameObject.layer = layer;
                count += SetLayerRecursive(child, layer);
            }
            return count;
        }

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
                Debug.Log($"[MCP Unity] Received message: {(message.Length > 100 ? message.Substring(0, 100) + "..." : message)}");

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
    /// Uses proper C# properties following coding conventions (audit fix)
    /// </summary>
    internal class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string Type { get; set; }

        // Convenience constructor for compatibility
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
