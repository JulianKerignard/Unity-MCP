using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace McpUnity.Utils
{
    /// <summary>
    /// Utility class for converting between Unity types and JSON-serializable formats
    /// Handles Vector3, Quaternion, Color, and other Unity-specific types
    /// </summary>
    public static class TypeConverter
    {
        #region Unity to JSON Conversion

        /// <summary>
        /// Convert a Unity value to a JSON-serializable format
        /// </summary>
        public static object ConvertToJson(object value)
        {
            if (value == null) return null;

            var type = value.GetType();

            // Primitives
            if (type.IsPrimitive || value is string || value is decimal)
                return value;

            // Unity types
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
                return new { center = ConvertToJson(b.center), size = ConvertToJson(b.size) };
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
        public static Dictionary<string, object> SerializeComponent(Component component)
        {
            var result = new Dictionary<string, object>();
            if (component == null) return result;

            var type = component.GetType();

            // Properties to skip (cause issues or are redundant)
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
                    result[prop.Name] = ConvertToJson(value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP TypeConverter] Cannot serialize property '{prop.Name}': {ex.Message}");
                }
            }

            return result;
        }

        #endregion

        #region JSON to Unity Conversion

        /// <summary>
        /// Convert a JSON value to a Unity type
        /// </summary>
        public static object ConvertFromJson(object jsonValue, Type targetType)
        {
            if (jsonValue == null) return null;

            var dict = jsonValue as Dictionary<string, object>;

            // Vector3
            if (targetType == typeof(Vector3) && dict != null)
            {
                return new Vector3(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f),
                    GetFloat(dict, "z", 0f)
                );
            }

            // Vector2
            if (targetType == typeof(Vector2) && dict != null)
            {
                return new Vector2(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f)
                );
            }

            // Vector4
            if (targetType == typeof(Vector4) && dict != null)
            {
                return new Vector4(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f),
                    GetFloat(dict, "z", 0f),
                    GetFloat(dict, "w", 0f)
                );
            }

            // Quaternion
            if (targetType == typeof(Quaternion) && dict != null)
            {
                return new Quaternion(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f),
                    GetFloat(dict, "z", 0f),
                    GetFloat(dict, "w", 1f)
                );
            }

            // Color
            if (targetType == typeof(Color) && dict != null)
            {
                return new Color(
                    GetFloat(dict, "r", 1f),
                    GetFloat(dict, "g", 1f),
                    GetFloat(dict, "b", 1f),
                    GetFloat(dict, "a", 1f)
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
                Debug.LogWarning($"[MCP TypeConverter] Cannot convert value to type '{targetType.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse a dictionary to Vector3
        /// </summary>
        public static Vector3 ParseVector3(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                return new Vector3(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f),
                    GetFloat(dict, "z", 0f)
                );
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Parse a dictionary to Quaternion (from euler angles)
        /// </summary>
        public static Quaternion ParseQuaternionFromEuler(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                return Quaternion.Euler(
                    GetFloat(dict, "x", 0f),
                    GetFloat(dict, "y", 0f),
                    GetFloat(dict, "z", 0f)
                );
            }
            return Quaternion.identity;
        }

        #endregion

        #region Helper Methods

        private static float GetFloat(Dictionary<string, object> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out var val) && val != null)
            {
                return Convert.ToSingle(val);
            }
            return defaultValue;
        }

        /// <summary>
        /// Apply properties from a dictionary to a component
        /// </summary>
        public static List<string> ApplyPropertiesToComponent(Component component, Dictionary<string, object> properties)
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
                        var convertedValue = ConvertFromJson(kvp.Value, prop.PropertyType);
                        if (convertedValue != null)
                        {
                            prop.SetValue(component, convertedValue);
                            modified.Add(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP TypeConverter] Cannot set property {kvp.Key}: {ex.Message}");
                    }
                    continue;
                }

                // Try field
                var field = type.GetField(kvp.Key, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    try
                    {
                        var convertedValue = ConvertFromJson(kvp.Value, field.FieldType);
                        if (convertedValue != null)
                        {
                            field.SetValue(component, convertedValue);
                            modified.Add(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP TypeConverter] Cannot set field {kvp.Key}: {ex.Message}");
                    }
                }
            }

            return modified;
        }

        #endregion
    }
}
