using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace McpUnity.Helpers
{
    /// <summary>
    /// Helper methods for Material operations in MCP Unity Server
    /// </summary>
    public static class MaterialHelpers
    {
        /// <summary>
        /// Serialize a Material to a JSON-compatible dictionary
        /// </summary>
        public static Dictionary<string, object> SerializeMaterial(Material material)
        {
            if (material == null) return null;

            var result = new Dictionary<string, object>
            {
                ["name"] = material.name,
                ["shader"] = material.shader != null ? material.shader.name : "Unknown",
                ["renderQueue"] = material.renderQueue,
                ["keywords"] = material.shaderKeywords.ToList(),
                ["doubleSidedGI"] = material.doubleSidedGI,
                ["enableInstancing"] = material.enableInstancing,
                ["globalIlluminationFlags"] = material.globalIlluminationFlags.ToString()
            };

            // Get properties by type
            var properties = new Dictionary<string, object>();

            properties["colors"] = GetPropertiesByType(material, ShaderPropertyType.Color);
            properties["floats"] = GetPropertiesByType(material, ShaderPropertyType.Float);
            properties["ranges"] = GetPropertiesByType(material, ShaderPropertyType.Range);
            properties["textures"] = GetTextureProperties(material);
            properties["vectors"] = GetPropertiesByType(material, ShaderPropertyType.Vector);
            properties["integers"] = GetPropertiesByType(material, ShaderPropertyType.Int);

            result["properties"] = properties;

            return result;
        }

        /// <summary>
        /// Get properties of a specific type from a material
        /// </summary>
        private static Dictionary<string, object> GetPropertiesByType(Material material, ShaderPropertyType propertyType)
        {
            var result = new Dictionary<string, object>();
            var shader = material.shader;
            if (shader == null) return result;

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != propertyType) continue;

                string name = shader.GetPropertyName(i);
                try
                {
                    object value = GetPropertyValue(material, name, propertyType);
                    if (value != null)
                        result[name] = value;
                }
                catch (Exception)
                {
                    // Skip properties that can't be read
                }
            }

            return result;
        }

        /// <summary>
        /// Get texture properties with additional info
        /// </summary>
        private static Dictionary<string, object> GetTextureProperties(Material material)
        {
            var result = new Dictionary<string, object>();
            var shader = material.shader;
            if (shader == null) return result;

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;

                string name = shader.GetPropertyName(i);
                try
                {
                    var texture = material.GetTexture(name);
                    if (texture != null)
                    {
                        string path = AssetDatabase.GetAssetPath(texture);
                        result[name] = new Dictionary<string, object>
                        {
                            ["name"] = texture.name,
                            ["path"] = string.IsNullOrEmpty(path) ? null : path,
                            ["width"] = texture.width,
                            ["height"] = texture.height
                        };
                    }
                    else
                    {
                        result[name] = null;
                    }

                    // Also get texture scale and offset
                    var scale = material.GetTextureScale(name);
                    var offset = material.GetTextureOffset(name);
                    if (scale != Vector2.one || offset != Vector2.zero)
                    {
                        result[name + "_ST"] = new Dictionary<string, object>
                        {
                            ["scale"] = new { x = scale.x, y = scale.y },
                            ["offset"] = new { x = offset.x, y = offset.y }
                        };
                    }
                }
                catch (Exception)
                {
                    // Skip properties that can't be read
                }
            }

            return result;
        }

        /// <summary>
        /// Get a single property value from a material
        /// </summary>
        public static object GetPropertyValue(Material material, string name, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                    var color = material.GetColor(name);
                    return new { r = color.r, g = color.g, b = color.b, a = color.a };

                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return material.GetFloat(name);

                case ShaderPropertyType.Vector:
                    var vec = material.GetVector(name);
                    return new { x = vec.x, y = vec.y, z = vec.z, w = vec.w };

                case ShaderPropertyType.Int:
                    return material.GetInt(name);

                case ShaderPropertyType.Texture:
                    var tex = material.GetTexture(name);
                    if (tex == null) return null;
                    return new { name = tex.name, path = AssetDatabase.GetAssetPath(tex) };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Set a property value on a material from JSON data
        /// </summary>
        public static bool SetPropertyValue(Material material, string propertyName, object value)
        {
            if (material == null || string.IsNullOrEmpty(propertyName))
                return false;

            // Determine property type from shader
            var shader = material.shader;
            if (shader == null) return false;

            int propertyIndex = shader.FindPropertyIndex(propertyName);
            if (propertyIndex < 0)
            {
                Debug.LogWarning($"[MCP MaterialHelpers] Property not found: {propertyName}");
                return false;
            }

            var propertyType = shader.GetPropertyType(propertyIndex);

            try
            {
                switch (propertyType)
                {
                    case ShaderPropertyType.Color:
                        var colorDict = value as Dictionary<string, object>;
                        if (colorDict != null)
                        {
                            var color = new Color(
                                Convert.ToSingle(colorDict.GetValueOrDefault("r", 1f)),
                                Convert.ToSingle(colorDict.GetValueOrDefault("g", 1f)),
                                Convert.ToSingle(colorDict.GetValueOrDefault("b", 1f)),
                                Convert.ToSingle(colorDict.GetValueOrDefault("a", 1f))
                            );
                            material.SetColor(propertyName, color);
                            return true;
                        }
                        break;

                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        material.SetFloat(propertyName, Convert.ToSingle(value));
                        return true;

                    case ShaderPropertyType.Int:
                        material.SetInt(propertyName, Convert.ToInt32(value));
                        return true;

                    case ShaderPropertyType.Vector:
                        var vecDict = value as Dictionary<string, object>;
                        if (vecDict != null)
                        {
                            var vec = new Vector4(
                                Convert.ToSingle(vecDict.GetValueOrDefault("x", 0f)),
                                Convert.ToSingle(vecDict.GetValueOrDefault("y", 0f)),
                                Convert.ToSingle(vecDict.GetValueOrDefault("z", 0f)),
                                Convert.ToSingle(vecDict.GetValueOrDefault("w", 0f))
                            );
                            material.SetVector(propertyName, vec);
                            return true;
                        }
                        break;

                    case ShaderPropertyType.Texture:
                        // Value can be a path string or null
                        if (value == null)
                        {
                            material.SetTexture(propertyName, null);
                            return true;
                        }

                        string texturePath = value as string;
                        if (texturePath == null && value is Dictionary<string, object> texDict)
                        {
                            texturePath = texDict.GetValueOrDefault("path", null) as string;
                        }

                        if (!string.IsNullOrEmpty(texturePath))
                        {
                            var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                            if (texture != null)
                            {
                                material.SetTexture(propertyName, texture);
                                return true;
                            }
                            Debug.LogWarning($"[MCP MaterialHelpers] Texture not found: {texturePath}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP MaterialHelpers] Failed to set {propertyName}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Find a material by asset path or from a GameObject's renderer
        /// </summary>
        public static Material FindMaterial(string materialPath = null, string gameObjectPath = null, int materialIndex = 0)
        {
            // Priority 1: Load from asset path
            if (!string.IsNullOrEmpty(materialPath))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material != null) return material;
            }

            // Priority 2: Get from GameObject renderer
            if (!string.IsNullOrEmpty(gameObjectPath))
            {
                var go = GameObjectHelpers.FindGameObject(gameObjectPath);
                if (go != null)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        var materials = renderer.sharedMaterials;
                        if (materialIndex >= 0 && materialIndex < materials.Length)
                        {
                            return materials[materialIndex];
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get list of common shader names for suggestions
        /// </summary>
        public static List<string> GetCommonShaders()
        {
            return new List<string>
            {
                // Built-in
                "Standard",
                "Standard (Specular setup)",
                "Unlit/Color",
                "Unlit/Texture",
                "Unlit/Transparent",
                "Unlit/Transparent Cutout",
                "Mobile/Diffuse",
                "Mobile/Unlit (Supports Lightmap)",
                "Sprites/Default",
                "Sprites/Diffuse",
                "UI/Default",
                "Skybox/6 Sided",
                "Skybox/Procedural",

                // URP
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Particles/Lit",
                "Universal Render Pipeline/Particles/Unlit",

                // HDRP
                "HDRP/Lit",
                "HDRP/Unlit",
                "HDRP/Eye",
                "HDRP/Hair"
            };
        }

        /// <summary>
        /// Detect the current render pipeline type
        /// </summary>
        public static string DetectRenderPipeline()
        {
            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP == null)
                return "BuiltIn";

            string rpTypeName = currentRP.GetType().Name;
            if (rpTypeName.Contains("Universal") || rpTypeName.Contains("URP"))
                return "URP";
            if (rpTypeName.Contains("HD") || rpTypeName.Contains("HDRP"))
                return "HDRP";

            return "BuiltIn";
        }

        /// <summary>
        /// Get the default lit shader for the current render pipeline
        /// </summary>
        public static string GetDefaultShaderName()
        {
            string pipeline = DetectRenderPipeline();
            switch (pipeline)
            {
                case "URP":
                    return "Universal Render Pipeline/Lit";
                case "HDRP":
                    return "HDRP/Lit";
                default:
                    return "Standard";
            }
        }

        /// <summary>
        /// Create a new material with specified shader (auto-detects pipeline if shader not found)
        /// </summary>
        public static Material CreateMaterial(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                // Try to find the correct shader for the current render pipeline
                string defaultShader = GetDefaultShaderName();
                Debug.LogWarning($"[MCP MaterialHelpers] Shader not found: {shaderName}, using {defaultShader}");
                shader = Shader.Find(defaultShader);

                // Ultimate fallback
                if (shader == null)
                {
                    Debug.LogWarning($"[MCP MaterialHelpers] Default shader not found, trying alternatives...");
                    // Try common fallbacks
                    shader = Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("HDRP/Lit") ??
                             Shader.Find("Standard") ??
                             Shader.Find("Unlit/Color");
                }
            }

            return shader != null ? new Material(shader) : null;
        }

        /// <summary>
        /// Map property names between different render pipelines
        /// Converts Standard shader property names to URP/HDRP equivalents
        /// </summary>
        public static string MapPropertyName(string propertyName, string targetPipeline)
        {
            if (targetPipeline == "URP" || targetPipeline == "HDRP")
            {
                // Standard → URP/HDRP mapping
                switch (propertyName)
                {
                    case "_Color": return "_BaseColor";
                    case "_MainTex": return "_BaseMap";
                    case "_Glossiness": return "_Smoothness";
                    case "_GlossMapScale": return "_Smoothness";
                    case "_BumpMap": return "_BumpMap"; // Same in URP
                    case "_EmissionColor": return "_EmissionColor"; // Same
                }
            }
            else if (targetPipeline == "BuiltIn")
            {
                // URP/HDRP → Standard mapping
                switch (propertyName)
                {
                    case "_BaseColor": return "_Color";
                    case "_BaseMap": return "_MainTex";
                    case "_Smoothness": return "_Glossiness";
                }
            }

            return propertyName; // No mapping needed
        }
    }
}
