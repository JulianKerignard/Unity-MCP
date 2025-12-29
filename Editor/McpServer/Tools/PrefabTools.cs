using System;
using System.Collections.Generic;
using McpUnity.Helpers;
using McpUnity.Protocol;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Server
{
    /// <summary>
    /// Prefab management tools for MCP Unity Server.
    /// Contains 4 tools: InstantiatePrefab, CreatePrefab, UnpackPrefab, ApplyPrefabOverrides
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all prefab-related tools
        /// </summary>
        static partial void RegisterPrefabTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_instantiate_prefab",
                description = "Instantiate a prefab in the scene with optional position, rotation, and parent",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["prefabPath"] = new McpPropertySchema { type = "string", description = "Path to the prefab asset (e.g., 'Assets/Prefabs/Player.prefab')" },
                        ["position"] = new McpPropertySchema { type = "object", description = "Position as {x, y, z}" },
                        ["rotation"] = new McpPropertySchema { type = "object", description = "Euler rotation as {x, y, z}" },
                        ["parentPath"] = new McpPropertySchema { type = "string", description = "Optional: Path to the parent GameObject" },
                        ["name"] = new McpPropertySchema { type = "string", description = "Optional: Override the instance name" }
                    },
                    required = new List<string> { "prefabPath" }
                }
            }, InstantiatePrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_prefab",
                description = "Create a prefab from an existing GameObject in the scene",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the source GameObject in the hierarchy" },
                        ["savePath"] = new McpPropertySchema { type = "string", description = "Path to save the prefab (must end with .prefab)" },
                        ["connectInstance"] = new McpPropertySchema { type = "boolean", description = "If true (default), keep the scene instance connected to the prefab" }
                    },
                    required = new List<string> { "gameObjectPath", "savePath" }
                }
            }, CreatePrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_unpack_prefab",
                description = "Unpack a prefab instance, breaking the link to the source prefab",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the prefab instance in the hierarchy" },
                        ["unpackMode"] = new McpPropertySchema { type = "string", description = "Unpack mode: 'completely' (default) or 'root'" }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, UnpackPrefab);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_apply_prefab_overrides",
                description = "Apply all overrides from a prefab instance back to the source prefab",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the prefab instance in the hierarchy" }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, ApplyPrefabOverrides);
        }

        #region Prefab Helpers

        private static Vector3 ParseVector3(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                float x = 0f, y = 0f, z = 0f;
                if (dict.TryGetValue("x", out var xVal))
                    float.TryParse(xVal?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                if (dict.TryGetValue("y", out var yVal))
                    float.TryParse(yVal?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
                if (dict.TryGetValue("z", out var zVal))
                    float.TryParse(zVal?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
                return new Vector3(x, y, z);
            }
            return Vector3.zero;
        }

        #endregion

        #region Prefab Handlers

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
                        instancePath = GameObjectHelpers.GetGameObjectPath(instance),
                        prefabPath = prefabPath,
                        position = new { x = instance.transform.position.x, y = instance.transform.position.y, z = instance.transform.position.z },
                        rotation = new { x = instance.transform.eulerAngles.x, y = instance.transform.eulerAngles.y, z = instance.transform.eulerAngles.z }
                    })
                },
                isError = false
            };
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
                bool.TryParse(connectObj.ToString(), out connectInstance);
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

        #endregion
    }
}
