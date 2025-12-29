using System;
using System.Collections.Generic;
using McpUnity.Helpers;
using McpUnity.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace McpUnity.Server
{
    /// <summary>
    /// Scene management tools for MCP Unity Server.
    /// Contains 4 tools: GetSceneInfo, LoadScene, SaveScene, CreateScene
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all scene-related tools
        /// </summary>
        static partial void RegisterSceneTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_scene_info",
                description = "Get information about the current scene and all loaded scenes",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>(),
                    required = new List<string>()
                }
            }, GetSceneInfo);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_load_scene",
                description = "Load a scene in the editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["scenePath"] = new McpPropertySchema { type = "string", description = "Path to the scene file (must end with .unity)" },
                        ["mode"] = new McpPropertySchema { type = "string", description = "Load mode: 'Single' (default) or 'Additive'" }
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
                        ["saveAll"] = new McpPropertySchema { type = "boolean", description = "If true, save all open scenes" },
                        ["scenePath"] = new McpPropertySchema { type = "string", description = "Optional: Save as a new scene at this path (must end with .unity)" }
                    },
                    required = new List<string>()
                }
            }, SaveScene);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_scene",
                description = "Create a new scene",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["sceneName"] = new McpPropertySchema { type = "string", description = "Name for the new scene" },
                        ["setup"] = new McpPropertySchema { type = "string", description = "Setup type: 'default' (with Main Camera and Light) or 'empty'" },
                        ["mode"] = new McpPropertySchema { type = "string", description = "Scene mode: 'single' (replaces current) or 'additive'" },
                        ["savePath"] = new McpPropertySchema { type = "string", description = "Optional: Path to save the scene (must end with .unity)" }
                    },
                    required = new List<string> { "sceneName" }
                }
            }, CreateScene);
        }

        #region Scene Handlers

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
                bool.TryParse(saveAllObj.ToString(), out saveAll);
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

        #endregion
    }
}
