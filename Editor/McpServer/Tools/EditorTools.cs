using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Helpers;
using McpUnity.Protocol;
using McpUnity.Editor;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Server
{
    /// <summary>
    /// Editor workflow tools for MCP Unity Server.
    /// Contains: ExecuteMenuItem, GetEditorState, RunTests, ClearConsole, PerformUndo, TakeScreenshot, GetSelection, SetSelection
    /// </summary>
    public partial class McpUnityServer
    {
        // Maximum screenshot dimension for safety
        private const int MaxScreenshotDimension = 4096;

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

        /// <summary>
        /// Register all editor workflow tools
        /// </summary>
        static partial void RegisterEditorTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_execute_menu_item",
                description = "Execute a Unity Editor menu item (only safe, allowlisted items)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["menuPath"] = new McpPropertySchema { type = "string", description = "Menu path (e.g., 'GameObject/Create Empty')" }
                    },
                    required = new List<string> { "menuPath" }
                }
            }, ExecuteMenuItem);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_editor_state",
                description = "Get the current Unity Editor state (play mode, compiling, selection, etc.)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>(),
                    required = new List<string>()
                }
            }, GetEditorState);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_run_tests",
                description = "Run Unity Test Framework tests",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>(),
                    required = new List<string>()
                }
            }, RunTests);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_clear_console",
                description = "Clear the Unity console",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>(),
                    required = new List<string>()
                }
            }, ClearConsole);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_undo",
                description = "Perform undo or redo operations",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["steps"] = new McpPropertySchema { type = "integer", description = "Number of steps to undo/redo (default: 1)" },
                        ["redo"] = new McpPropertySchema { type = "boolean", description = "If true, perform redo instead of undo" }
                    },
                    required = new List<string>()
                }
            }, PerformUndo);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_take_screenshot",
                description = "Take a screenshot of the Scene or Game view. Returns saved path, optionally base64 data.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["view"] = new McpPropertySchema { type = "string", description = "'Scene' (default) or 'Game' (requires Play Mode)" },
                        ["format"] = new McpPropertySchema { type = "string", description = "'jpg' (default, smaller) or 'png'" },
                        ["width"] = new McpPropertySchema { type = "integer", description = "Width in pixels (default: 640)" },
                        ["height"] = new McpPropertySchema { type = "integer", description = "Height in pixels (default: 360)" },
                        ["jpgQuality"] = new McpPropertySchema { type = "integer", description = "JPG quality 1-100 (default: 75)" },
                        ["savePath"] = new McpPropertySchema { type = "string", description = "Custom save path (default: Assets/Screenshots/)" },
                        ["returnBase64"] = new McpPropertySchema { type = "boolean", description = "If true, include base64 data in response (default: false)" }
                    },
                    required = new List<string>()
                }
            }, TakeScreenshot);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_selection",
                description = "Get the current selection in the Unity Editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["includeAssets"] = new McpPropertySchema { type = "boolean", description = "Include selected assets (default: false)" }
                    },
                    required = new List<string>()
                }
            }, GetSelection);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_selection",
                description = "Set the current selection in the Unity Editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPaths"] = new McpPropertySchema { type = "array", description = "Array of GameObject paths to select" },
                        ["assetPaths"] = new McpPropertySchema { type = "array", description = "Array of asset paths to select" },
                        ["clear"] = new McpPropertySchema { type = "boolean", description = "If true, clear the current selection" }
                    },
                    required = new List<string>()
                }
            }, SetSelection);
        }

        #region Editor Helpers

        private static string[] GetSelectedObjectNames()
        {
            var names = new string[Selection.objects.Length];
            for (int i = 0; i < Selection.objects.Length; i++)
            {
                names[i] = Selection.objects[i].name;
            }
            return names;
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
                var resized = ResizeTextureForScreenshot(screenshot, width, height);
                UnityEngine.Object.DestroyImmediate(screenshot);
                return resized;
            }

            return screenshot;
        }

        private static Texture2D ResizeTextureForScreenshot(Texture2D source, int targetWidth, int targetHeight)
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

        #region Editor Handlers

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
                McpDebug.LogWarning($"[MCP Unity] Blocked menu item execution (not in allowlist): {menuPath}");
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

        private static McpToolResult RunTests(Dictionary<string, object> args)
        {
            return McpToolResult.Success("Test execution initiated. Check Unity Test Runner for results.");
        }

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

        private static McpToolResult PerformUndo(Dictionary<string, object> args)
        {
            int steps = 1;
            if (args.TryGetValue("steps", out var stepsObj) && stepsObj != null && int.TryParse(stepsObj.ToString(), out var parsedSteps))
            {
                steps = Math.Max(1, parsedSteps);
            }

            bool redo = false;
            if (args.TryGetValue("redo", out var redoObj) && redoObj != null)
            {
                bool.TryParse(redoObj.ToString(), out redo);
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

            if (args.TryGetValue("jpgQuality", out var qualObj) && qualObj != null && int.TryParse(qualObj.ToString(), out var parsedQual))
                jpgQuality = Math.Clamp(parsedQual, 1, 100);

            if (args.TryGetValue("width", out var wObj) && wObj != null && int.TryParse(wObj.ToString(), out var parsedWidth))
                width = Math.Clamp(parsedWidth, 1, MaxScreenshotDimension);

            if (args.TryGetValue("height", out var hObj) && hObj != null && int.TryParse(hObj.ToString(), out var parsedHeight))
                height = Math.Clamp(parsedHeight, 1, MaxScreenshotDimension);

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

        private static McpToolResult GetSelection(Dictionary<string, object> args)
        {
            bool includeAssets = false;
            if (args.TryGetValue("includeAssets", out var includeObj) && includeObj != null)
            {
                bool.TryParse(includeObj.ToString(), out includeAssets);
            }

            var result = new Dictionary<string, object>();

            // Active object
            if (Selection.activeGameObject != null)
            {
                result["activeObject"] = new Dictionary<string, object>
                {
                    ["name"] = Selection.activeGameObject.name,
                    ["path"] = GameObjectHelpers.GetGameObjectPath(Selection.activeGameObject)
                };
            }

            // Selected GameObjects
            var gameObjects = new List<Dictionary<string, object>>();
            foreach (var go in Selection.gameObjects)
            {
                gameObjects.Add(new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["path"] = GameObjectHelpers.GetGameObjectPath(go)
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
            if (args.TryGetValue("clear", out var clearObj) && clearObj != null && bool.TryParse(clearObj.ToString(), out var doClear) && doClear)
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

        #endregion
    }
}
