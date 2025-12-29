using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace McpUnity.Helpers
{
    /// <summary>
    /// Helper methods for Component operations in MCP Unity Server
    /// </summary>
    public static class ComponentHelpers
    {
        /// <summary>
        /// Find a component type by name, searching common assemblies
        /// </summary>
        /// <param name="typeName">Name of the component type (e.g., "Rigidbody", "BoxCollider")</param>
        /// <returns>The Type or null if not found</returns>
        public static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Common Unity namespaces to search
            var searchPrefixes = new[]
            {
                "UnityEngine.",
                "UnityEngine.UI.",
                "UnityEngine.Rendering.",
                "UnityEngine.Audio.",
                "UnityEngine.AI.",
                "UnityEngine.Animations.",
                ""  // No prefix (for custom components)
            };

            // Try each prefix
            foreach (var prefix in searchPrefixes)
            {
                var fullName = prefix + typeName;
                var type = Type.GetType(fullName);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;
            }

            // Search in UnityEngine assembly
            var unityAssembly = typeof(GameObject).Assembly;
            var unityType = unityAssembly.GetTypes().FirstOrDefault(t =>
                t.Name == typeName && typeof(Component).IsAssignableFrom(t));
            if (unityType != null) return unityType;

            // Search in UI assembly
            var uiAssembly = typeof(Button).Assembly;
            var uiType = uiAssembly.GetTypes().FirstOrDefault(t =>
                t.Name == typeName && typeof(Component).IsAssignableFrom(t));
            if (uiType != null) return uiType;

            // Search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var foundType = assembly.GetTypes().FirstOrDefault(t =>
                        t.Name == typeName && typeof(Component).IsAssignableFrom(t));
                    if (foundType != null) return foundType;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP ComponentHelpers] Cannot search assembly: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Get all components on a GameObject as a list of type names
        /// </summary>
        public static List<string> GetComponentTypes(GameObject go)
        {
            if (go == null) return new List<string>();

            return go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToList();
        }

        /// <summary>
        /// Get a component by type name from a GameObject
        /// </summary>
        public static Component GetComponentByTypeName(GameObject go, string typeName)
        {
            if (go == null || string.IsNullOrEmpty(typeName))
                return null;

            var type = FindComponentType(typeName);
            if (type == null) return null;

            return go.GetComponent(type);
        }

        /// <summary>
        /// Check if a GameObject has a specific component
        /// </summary>
        public static bool HasComponent(GameObject go, string typeName)
        {
            return GetComponentByTypeName(go, typeName) != null;
        }

        /// <summary>
        /// Get readable property info for a component
        /// </summary>
        public static Dictionary<string, object> GetComponentProperties(Component component)
        {
            var result = new Dictionary<string, object>();
            if (component == null) return result;

            var type = component.GetType();

            // Properties to skip
            var skipProps = new HashSet<string>
            {
                "mesh", "material", "materials", "sharedMesh", "sharedMaterial", "sharedMaterials",
                "gameObject", "transform", "tag", "name", "hideFlags", "runInEditMode"
            };

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                if (skipProps.Contains(prop.Name)) continue;

                try
                {
                    var value = prop.GetValue(component);
                    result[prop.Name] = ConvertValueForJson(value);
                }
                catch
                {
                    // Skip properties that throw
                }
            }

            return result;
        }

        /// <summary>
        /// Convert a value to JSON-serializable format
        /// </summary>
        private static object ConvertValueForJson(object value)
        {
            if (value == null) return null;

            var type = value.GetType();

            if (type.IsPrimitive || value is string || value is decimal)
                return value;

            if (value is Vector3 v3)
                return new { x = v3.x, y = v3.y, z = v3.z };
            if (value is Vector2 v2)
                return new { x = v2.x, y = v2.y };
            if (value is Quaternion q)
                return new { x = q.x, y = q.y, z = q.z, w = q.w };
            if (value is Color c)
                return new { r = c.r, g = c.g, b = c.b, a = c.a };

            if (type.IsEnum)
                return value.ToString();

            if (value is UnityEngine.Object uobj)
                return uobj != null ? new { name = uobj.name, type = uobj.GetType().Name } : null;

            return value.ToString();
        }

        /// <summary>
        /// Get a list of commonly used component types for suggestions
        /// </summary>
        public static List<string> GetCommonComponentTypes()
        {
            return new List<string>
            {
                // Physics
                "Rigidbody", "Rigidbody2D",
                "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider",
                "BoxCollider2D", "CircleCollider2D", "PolygonCollider2D",
                "CharacterController",

                // Rendering
                "MeshRenderer", "SkinnedMeshRenderer", "SpriteRenderer",
                "MeshFilter", "Camera", "Light",
                "Canvas", "CanvasGroup",

                // Audio
                "AudioSource", "AudioListener",

                // Animation
                "Animator", "Animation",

                // AI/Navigation
                "NavMeshAgent", "NavMeshObstacle",

                // UI
                "Button", "Text", "Image", "RawImage",
                "InputField", "Slider", "Toggle", "Dropdown",
                "ScrollRect", "Mask", "RectMask2D",

                // Other
                "ParticleSystem", "TrailRenderer", "LineRenderer"
            };
        }
    }
}
