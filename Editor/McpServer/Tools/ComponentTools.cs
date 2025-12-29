using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using McpUnity.Protocol;
using McpUnity.Helpers;
using McpUnity.Editor;

namespace McpUnity.Server
{
    /// <summary>
    /// Partial class containing Component manipulation tools
    /// Tools: get_component, add_component, modify_component, list_project_scripts
    /// </summary>
    public partial class McpUnityServer
    {
        #region Component Discovery

        // Built-in Unity component types for quick lookup (always allowed)
        private static readonly HashSet<string> BuiltInComponentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

        // Cache for project scripts (invalidated on domain reload)
        private static Dictionary<string, Type> _projectScriptsCache = null;
        private static DateTime _projectScriptsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan ProjectScriptsCacheDuration = TimeSpan.FromSeconds(30);

        #endregion

        #region Component Tool Registrations

        static partial void RegisterComponentTools()
        {
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

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_project_scripts",
                description = "List all MonoBehaviour scripts available in the project that can be added as components",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["nameFilter"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional filter by script name (case-insensitive, supports * wildcard)"
                        },
                        ["includeNamespace"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Include namespace in results (default: false)"
                        }
                    },
                    required = new List<string>()
                }
            }, ListProjectScripts);
        }

        #endregion

        #region Component Handlers

        private static McpToolResult GetComponentProperties(Dictionary<string, object> args)
        {
            var gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var error);
            if (gameObjectPath == null)
                return McpToolResult.Error(error);

            var componentType = ArgumentParser.RequireString(args, "componentType", out error);
            if (componentType == null)
                return McpToolResult.Error(error);

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var type = FindComponentType(componentType);
            if (type == null)
                return McpToolResult.Error($"Component type not found or not allowed: {componentType}");

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
            try
            {
                var gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var error);
                if (gameObjectPath == null)
                    return McpToolResult.Error(error);

                var componentType = ArgumentParser.RequireString(args, "componentType", out error);
                if (componentType == null)
                    return McpToolResult.Error(error);

                var initialProperties = args.GetValueOrDefault("initialProperties") as Dictionary<string, object>;

                var go = GameObject.Find(gameObjectPath);
                if (go == null)
                    return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

                var type = FindComponentType(componentType);
                if (type == null)
                    return McpToolResult.Error($"Component type not found or not allowed: {componentType}");

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
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to add component: {ex.Message}");
            }
        }

        private static McpToolResult ModifyComponentProperties(Dictionary<string, object> args)
        {
            try
            {
                var gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var error);
                if (gameObjectPath == null)
                    return McpToolResult.Error(error);

                var componentType = ArgumentParser.RequireString(args, "componentType", out error);
                if (componentType == null)
                    return McpToolResult.Error(error);

                var properties = args.GetValueOrDefault("properties") as Dictionary<string, object>;
                if (properties == null || properties.Count == 0)
                    return McpToolResult.Error("properties is required and must not be empty");

                var go = GameObject.Find(gameObjectPath);
                if (go == null)
                    return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

                var type = FindComponentType(componentType);
                if (type == null)
                    return McpToolResult.Error($"Component type not found or not allowed: {componentType}");

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
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to modify component properties: {ex.Message}");
            }
        }

        private static McpToolResult ListProjectScripts(Dictionary<string, object> args)
        {
            try
            {
                var nameFilter = args.GetValueOrDefault("nameFilter") as string;
                var includeNamespace = args.GetValueOrDefault("includeNamespace") is bool b && b;

                // Force refresh to get latest scripts
                _projectScriptsCache = null;
                var projectScripts = GetProjectScripts();

                var scripts = new List<object>();

                foreach (var kvp in projectScripts.OrderBy(x => x.Key))
                {
                    var scriptName = kvp.Key;
                    var scriptType = kvp.Value;

                    // Apply filter
                    if (!string.IsNullOrEmpty(nameFilter))
                    {
                        var pattern = nameFilter.Replace("*", "");
                        bool matches = false;

                        if (nameFilter.StartsWith("*") && nameFilter.EndsWith("*"))
                            matches = scriptName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        else if (nameFilter.StartsWith("*"))
                            matches = scriptName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
                        else if (nameFilter.EndsWith("*"))
                            matches = scriptName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
                        else
                            matches = scriptName.Equals(nameFilter, StringComparison.OrdinalIgnoreCase);

                        if (!matches) continue;
                    }

                    if (includeNamespace)
                    {
                        scripts.Add(new
                        {
                            name = scriptName,
                            fullName = scriptType.FullName,
                            @namespace = scriptType.Namespace ?? "(global)",
                            assembly = scriptType.Assembly.GetName().Name
                        });
                    }
                    else
                    {
                        scripts.Add(new { name = scriptName });
                    }
                }

                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Json(new
                        {
                            count = scripts.Count,
                            scripts = scripts,
                            hint = "Use these script names with unity_add_component to add them to GameObjects"
                        })
                    },
                    isError = false
                };
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to list project scripts: {ex.Message}");
            }
        }

        #endregion

        #region Component Helpers

        /// <summary>
        /// Find a component type by name - supports both Unity built-in and custom project scripts
        /// </summary>
        private static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Check cache first (performance optimization)
            if (_componentTypeCache.TryGetValue(typeName, out var cachedType))
                return cachedType;

            Type type = null;

            // 1. Check if it's a built-in Unity component
            if (BuiltInComponentTypes.Contains(typeName))
            {
                type = ComponentHelpers.FindComponentType(typeName);
            }
            else
            {
                // 2. Search in project scripts (MonoBehaviours)
                var projectScripts = GetProjectScripts();
                if (projectScripts.TryGetValue(typeName, out type))
                {
                    McpDebug.Log($"[MCP Unity] Found custom script: {typeName}");
                }
                else
                {
                    // 3. Try case-insensitive search
                    var match = projectScripts.FirstOrDefault(kvp =>
                        kvp.Key.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                    if (match.Value != null)
                    {
                        type = match.Value;
                        McpDebug.Log($"[MCP Unity] Found custom script (case-insensitive): {match.Key}");
                    }
                }
            }

            // Cache the result
            if (type != null)
                _componentTypeCache[typeName] = type;

            return type;
        }

        /// <summary>
        /// Get all MonoBehaviour scripts in the project (cached)
        /// </summary>
        private static Dictionary<string, Type> GetProjectScripts()
        {
            // Check if cache is still valid
            if (_projectScriptsCache != null && DateTime.Now - _projectScriptsCacheTime < ProjectScriptsCacheDuration)
                return _projectScriptsCache;

            _projectScriptsCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                // Skip Unity internal and system assemblies for performance
                var assemblyName = assembly.GetName().Name;
                if (assemblyName.StartsWith("Unity") ||
                    assemblyName.StartsWith("System") ||
                    assemblyName.StartsWith("mscorlib") ||
                    assemblyName.StartsWith("Mono") ||
                    assemblyName.StartsWith("netstandard") ||
                    assemblyName.StartsWith("nunit") ||
                    assemblyName.StartsWith("Newtonsoft"))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        // Only include MonoBehaviour subclasses that can be added as components
                        if (type.IsClass && !type.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(type))
                        {
                            // Use simple name (without namespace) as key
                            if (!_projectScriptsCache.ContainsKey(type.Name))
                            {
                                _projectScriptsCache[type.Name] = type;
                            }
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
            }

            _projectScriptsCacheTime = DateTime.Now;
            McpDebug.Log($"[MCP Unity] Discovered {_projectScriptsCache.Count} project scripts");

            return _projectScriptsCache;
        }

        /// <summary>
        /// Invalidate the project scripts cache (call after creating new scripts)
        /// </summary>
        public static void InvalidateProjectScriptsCache()
        {
            _projectScriptsCache = null;
            _componentTypeCache.Clear();
            McpDebug.Log("[MCP Unity] Project scripts cache invalidated");
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
                    McpDebug.LogWarning($"[MCP Unity] Cannot serialize property '{prop.Name}': {ex.Message}");
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
                    ArgumentParser.GetFloat(dict, "x", 0f),
                    ArgumentParser.GetFloat(dict, "y", 0f),
                    ArgumentParser.GetFloat(dict, "z", 0f)
                );
            }

            // Vector2
            if (targetType == typeof(Vector2) && dict != null)
            {
                return new Vector2(
                    ArgumentParser.GetFloat(dict, "x", 0f),
                    ArgumentParser.GetFloat(dict, "y", 0f)
                );
            }

            // Quaternion
            if (targetType == typeof(Quaternion) && dict != null)
            {
                return new Quaternion(
                    ArgumentParser.GetFloat(dict, "x", 0f),
                    ArgumentParser.GetFloat(dict, "y", 0f),
                    ArgumentParser.GetFloat(dict, "z", 0f),
                    ArgumentParser.GetFloat(dict, "w", 1f)
                );
            }

            // Color
            if (targetType == typeof(Color) && dict != null)
            {
                return new Color(
                    ArgumentParser.GetFloat(dict, "r", 1f),
                    ArgumentParser.GetFloat(dict, "g", 1f),
                    ArgumentParser.GetFloat(dict, "b", 1f),
                    ArgumentParser.GetFloat(dict, "a", 1f)
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
                McpDebug.LogWarning($"[MCP Unity] Cannot convert value to type '{targetType.Name}': {ex.Message}");
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
                        McpDebug.LogWarning($"[MCP Unity] Cannot set property {kvp.Key}: {ex.Message}");
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
                        McpDebug.LogWarning($"[MCP Unity] Cannot set field {kvp.Key}: {ex.Message}");
                    }
                }
            }

            return modified;
        }

        #endregion
    }
}
