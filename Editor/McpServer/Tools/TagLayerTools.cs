using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Helpers;
using McpUnity.Protocol;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace McpUnity.Server
{
    /// <summary>
    /// Tag and Layer management tools for MCP Unity Server.
    /// Contains 6 tools: ListTags, ListLayers, SetTag, SetLayer, CreateTag, CreateLayer
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all tag and layer related tools
        /// </summary>
        static partial void RegisterTagLayerTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_tags",
                description = "List all available tags in the project",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>(),
                    required = new List<string>()
                }
            }, ListTags);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_layers",
                description = "List all layers in the project",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["includeEmpty"] = new McpPropertySchema { type = "boolean", description = "Include empty layer slots (default: false)" }
                    },
                    required = new List<string>()
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
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the GameObject in the hierarchy" },
                        ["tag"] = new McpPropertySchema { type = "string", description = "Tag name to set (must exist in project)" },
                        ["recursive"] = new McpPropertySchema { type = "boolean", description = "Apply to all children (default: false)" }
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
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the GameObject in the hierarchy" },
                        ["layer"] = new McpPropertySchema { type = "string", description = "Layer name to set (must exist in project)" },
                        ["recursive"] = new McpPropertySchema { type = "boolean", description = "Apply to all children (default: false)" }
                    },
                    required = new List<string> { "gameObjectPath", "layer" }
                }
            }, SetLayer);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_tag",
                description = "Create a new tag in the project",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["tagName"] = new McpPropertySchema { type = "string", description = "Name for the new tag (max 64 characters)" }
                    },
                    required = new List<string> { "tagName" }
                }
            }, CreateTag);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_layer",
                description = "Create a new layer in the project",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["layerName"] = new McpPropertySchema { type = "string", description = "Name for the new layer (max 64 characters)" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Optional: specific layer index (8-31). If not specified, uses first available slot." }
                    },
                    required = new List<string> { "layerName" }
                }
            }, CreateLayer);
        }

        #region Tag/Layer Helpers

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

        #region Tag/Layer Handlers

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
            bool includeEmpty = args.TryGetValue("includeEmpty", out var ie) && bool.TryParse(ie?.ToString(), out var parsedIe) && parsedIe;

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
                return McpToolResult.Error("Missing required parameter: gameObjectPath");

            if (!args.TryGetValue("tag", out var tagObj) || tagObj == null)
                return McpToolResult.Error("Missing required parameter: tag");

            string gameObjectPath = pathObj.ToString();
            string newTag = tagObj.ToString();
            bool recursive = args.TryGetValue("recursive", out var r) && bool.TryParse(r?.ToString(), out var parsedR) && parsedR;

            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            // Verify tag exists
            var validTags = InternalEditorUtility.tags;
            if (!validTags.Contains(newTag))
                return McpToolResult.Error($"Tag '{newTag}' does not exist. Available tags: {string.Join(", ", validTags)}");

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

        private static McpToolResult SetLayer(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("gameObjectPath", out var pathObj) || pathObj == null)
                return McpToolResult.Error("Missing required parameter: gameObjectPath");

            if (!args.TryGetValue("layer", out var layerObj) || layerObj == null)
                return McpToolResult.Error("Missing required parameter: layer");

            string gameObjectPath = pathObj.ToString();
            string layerName = layerObj.ToString();
            bool recursive = args.TryGetValue("recursive", out var r) && bool.TryParse(r?.ToString(), out var parsedRecursive) && parsedRecursive;

            var go = GameObjectHelpers.FindGameObject(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex == -1)
                return McpToolResult.Error($"Layer '{layerName}' does not exist");

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

        private static McpToolResult CreateTag(Dictionary<string, object> args)
        {
            try
            {
                if (!args.TryGetValue("tagName", out var tagObj) || tagObj == null)
                    return McpToolResult.Error("Missing required parameter: tagName");

                string tagName = tagObj.ToString().Trim();

                if (string.IsNullOrEmpty(tagName))
                    return McpToolResult.Error("Tag name cannot be empty");

                if (tagName.Length > 64)
                    return McpToolResult.Error("Tag name too long (max 64 characters)");

                // Check if tag already exists
                var existingTags = InternalEditorUtility.tags;
                if (existingTags.Contains(tagName))
                    return McpToolResult.Error($"Tag '{tagName}' already exists");

                // Open the TagManager
                SerializedObject tagManager = new SerializedObject(
                    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

                SerializedProperty tagsProp = tagManager.FindProperty("tags");

                // Add the tag
                int newIndex = tagsProp.arraySize;
                tagsProp.InsertArrayElementAtIndex(newIndex);
                tagsProp.GetArrayElementAtIndex(newIndex).stringValue = tagName;

                tagManager.ApplyModifiedProperties();

                return McpResponse.Success($"Created tag '{tagName}'", new { tagName });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create tag: {ex.Message}");
            }
        }

        private static McpToolResult CreateLayer(Dictionary<string, object> args)
        {
            try
            {
                if (!args.TryGetValue("layerName", out var layerObj) || layerObj == null)
                    return McpToolResult.Error("Missing required parameter: layerName");

                string layerName = layerObj.ToString().Trim();

                if (string.IsNullOrEmpty(layerName))
                    return McpToolResult.Error("Layer name cannot be empty");

                if (layerName.Length > 64)
                    return McpToolResult.Error("Layer name too long (max 64 characters)");

                // Check if layer already exists
                int existingIndex = LayerMask.NameToLayer(layerName);
                if (existingIndex != -1)
                    return McpToolResult.Error($"Layer '{layerName}' already exists at index {existingIndex}");

                // Determine target index
                int targetIndex = -1;

                if (args.TryGetValue("layerIndex", out var indexObj) && indexObj != null)
                {
                    targetIndex = ArgumentParser.GetInt(args, "layerIndex", -1);

                    // Validate the index
                    if (targetIndex < 0 || targetIndex > 31)
                        return McpToolResult.Error("Layer index must be between 0 and 31");

                    // Check that it's not a non-modifiable builtin
                    if (targetIndex == 0 || targetIndex == 1 || targetIndex == 2 ||
                        targetIndex == 4 || targetIndex == 5)
                        return McpToolResult.Error($"Layer index {targetIndex} is a builtin layer and cannot be modified");

                    // Check that the slot is empty
                    string currentName = LayerMask.LayerToName(targetIndex);
                    if (!string.IsNullOrEmpty(currentName))
                        return McpToolResult.Error($"Layer index {targetIndex} is already used by '{currentName}'");
                }
                else
                {
                    // Find first empty slot
                    // Search order: 8-31 (user layers), then 3, 6, 7
                    int[] searchOrder = new int[] {
                        8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
                        3, 6, 7
                    };

                    foreach (int idx in searchOrder)
                    {
                        string name = LayerMask.LayerToName(idx);
                        if (string.IsNullOrEmpty(name))
                        {
                            targetIndex = idx;
                            break;
                        }
                    }

                    if (targetIndex == -1)
                        return McpToolResult.Error("No empty layer slot available (all 27 user slots are used)");
                }

                // Open the TagManager
                SerializedObject tagManager = new SerializedObject(
                    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

                SerializedProperty layersProp = tagManager.FindProperty("layers");

                // Set the layer
                layersProp.GetArrayElementAtIndex(targetIndex).stringValue = layerName;

                tagManager.ApplyModifiedProperties();

                return McpResponse.Success($"Created layer '{layerName}' at index {targetIndex}", new { layerName, layerIndex = targetIndex });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create layer: {ex.Message}");
            }
        }

        #endregion
    }
}
