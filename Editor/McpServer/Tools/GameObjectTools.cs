using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using McpUnity.Protocol;
using McpUnity.Helpers;

namespace McpUnity.Server
{
    /// <summary>
    /// Partial class containing GameObject management tools
    /// Tools: list_gameobjects, create_gameobject, delete_gameobject, rename_gameobject, set_parent, get_selection, set_selection
    /// </summary>
    public partial class McpUnityServer
    {
        #region GameObject Tool Registrations

        static partial void RegisterGameObjectTools()
        {
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

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_rename_gameobject",
                description = "Rename a GameObject",
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
                description = "Set or change the parent of a GameObject",
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
                        ["parentPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to new parent (null or empty to move to scene root)"
                        },
                        ["worldPositionStays"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Keep world position when reparenting (default: true)"
                        }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, SetParent);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_selection",
                description = "Get currently selected objects in the Unity Editor",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["includeAssets"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include selected assets (not just scene objects)"
                        }
                    }
                }
            }, GetSelection);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_selection",
                description = "Set the Unity Editor selection",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPaths"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Array of GameObject paths to select"
                        },
                        ["assetPaths"] = new McpPropertySchema
                        {
                            type = "array",
                            description = "Array of asset paths to select"
                        },
                        ["clear"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Clear selection instead of setting it"
                        }
                    }
                }
            }, SetSelection);
        }

        #endregion

        #region GameObject Handlers

        private static McpToolResult ListGameObjects(Dictionary<string, object> args)
        {
            // Parse parameters with optimized defaults
            string outputMode = ArgumentParser.GetString(args, "outputMode", "summary").ToLower();
            int maxDepth = ArgumentParser.GetIntClamped(args, "maxDepth", 3, 1, 50);
            bool includeInactive = ArgumentParser.GetBool(args, "includeInactive", false);
            bool rootOnly = ArgumentParser.GetBool(args, "rootOnly", false);
            bool includeTransform = ArgumentParser.GetBool(args, "includeTransform", false);
            string nameFilter = ArgumentParser.GetString(args, "nameFilter", null);
            string componentFilter = ArgumentParser.GetString(args, "componentFilter", null);
            string tagFilter = ArgumentParser.GetString(args, "tagFilter", null);

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

        private static McpToolResult CreateGameObject(Dictionary<string, object> args)
        {
            try
            {
                var objName = ArgumentParser.RequireString(args, "name", out var error);
                if (objName == null)
                    return McpToolResult.Error(error);

                string primitiveType = ArgumentParser.GetString(args, "primitiveType", "Empty");
                string parentPath = ArgumentParser.GetString(args, "parentPath", null);

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
                    float x = ArgumentParser.GetFloat(posDict, "x", 0f);
                    float y = ArgumentParser.GetFloat(posDict, "y", 0f);
                    float z = ArgumentParser.GetFloat(posDict, "z", 0f);
                    newObj.transform.position = new Vector3(x, y, z);
                }

                Undo.RegisterCreatedObjectUndo(newObj, $"Create {objName}");
                Selection.activeGameObject = newObj;

                return McpToolResult.Success($"Created GameObject '{objName}' at path: {GameObjectHelpers.GetGameObjectPath(newObj)}");
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create GameObject: {ex.Message}");
            }
        }

        private static McpToolResult DeleteGameObject(Dictionary<string, object> args)
        {
            var path = ArgumentParser.RequireString(args, "path", out var error);
            if (path == null)
                return McpToolResult.Error(error);

            var obj = GameObject.Find(path);

            if (obj == null)
            {
                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (var go in allObjects)
                {
                    if (go.name == path || GameObjectHelpers.GetGameObjectPath(go) == path)
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

            string deletedPath = GameObjectHelpers.GetGameObjectPath(obj);
            Undo.DestroyObjectImmediate(obj);

            return McpToolResult.Success($"Deleted GameObject: {deletedPath}");
        }

        private static McpToolResult RenameGameObject(Dictionary<string, object> args)
        {
            var gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var error);
            if (gameObjectPath == null)
                return McpToolResult.Error(error);

            var newName = ArgumentParser.RequireString(args, "newName", out error);
            if (newName == null)
                return McpToolResult.Error(error);

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
                        path = GameObjectHelpers.GetGameObjectPath(gameObject)
                    })
                },
                isError = false
            };
        }

        private static McpToolResult SetParent(Dictionary<string, object> args)
        {
            var gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var error);
            if (gameObjectPath == null)
                return McpToolResult.Error(error);

            var gameObject = GameObject.Find(gameObjectPath);
            if (gameObject == null)
            {
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");
            }

            GameObject newParent = null;
            string parentName = "(scene root)";
            string parentPath = ArgumentParser.GetString(args, "parentPath", null);

            if (!string.IsNullOrEmpty(parentPath))
            {
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

            bool worldPositionStays = ArgumentParser.GetBool(args, "worldPositionStays", true);

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
                        newPath = GameObjectHelpers.GetGameObjectPath(gameObject)
                    })
                },
                isError = false
            };
        }

        #endregion

        #region GameObject Helpers

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
                    path = GameObjectHelpers.GetGameObjectPath(obj),
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
                    path = GameObjectHelpers.GetGameObjectPath(obj),
                    active = obj.activeSelf,
                    layer = LayerMask.LayerToName(obj.layer),
                    tag = obj.tag,
                    components = componentInfos,
                    childCount = obj.transform.childCount,
                    children = children
                };
            }
        }

        #endregion
    }
}
