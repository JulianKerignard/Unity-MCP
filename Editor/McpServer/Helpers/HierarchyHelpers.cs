using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace McpUnity.Helpers
{
    /// <summary>
    /// Helper methods for optimized hierarchy operations - reduces context/token usage
    /// </summary>
    public static class HierarchyHelpers
    {
        // Key component types that are most relevant for AI understanding
        private static readonly HashSet<string> KeyComponentTypes = new HashSet<string>
        {
            // Rendering
            "MeshRenderer", "SkinnedMeshRenderer", "SpriteRenderer", "LineRenderer",
            "ParticleSystemRenderer", "TrailRenderer", "Camera", "Light",

            // Physics
            "Rigidbody", "Rigidbody2D", "Collider", "BoxCollider", "SphereCollider",
            "CapsuleCollider", "MeshCollider", "CharacterController",
            "BoxCollider2D", "CircleCollider2D", "PolygonCollider2D",

            // Audio
            "AudioSource", "AudioListener",

            // UI
            "Canvas", "Image", "Text", "TextMeshProUGUI", "Button", "InputField",

            // Animation
            "Animator", "Animation",

            // Navigation
            "NavMeshAgent", "NavMeshObstacle"
        };

        /// <summary>
        /// Format hierarchy as ASCII tree (very compact)
        /// </summary>
        public static string FormatAsTree(List<GameObject> rootObjects, int maxDepth, bool includeInactive)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < rootObjects.Count; i++)
            {
                var obj = rootObjects[i];
                if (!includeInactive && !obj.activeSelf) continue;

                bool isLast = (i == rootObjects.Count - 1);
                FormatGameObjectAsTree(sb, obj, "", isLast, 0, maxDepth, includeInactive);
            }

            return sb.ToString();
        }

        private static void FormatGameObjectAsTree(StringBuilder sb, GameObject obj, string prefix, bool isLast, int depth, int maxDepth, bool includeInactive)
        {
            // Add current object
            string connector = isLast ? "└─ " : "├─ ";
            string marker = obj.activeSelf ? "" : " [inactive]";

            // Get key components summary
            var keyComps = GetKeyComponentNames(obj);
            string compSuffix = keyComps.Count > 0 ? $" ({string.Join(", ", keyComps)})" : "";

            sb.AppendLine($"{prefix}{connector}{obj.name}{marker}{compSuffix}");

            // Process children if not at max depth
            if (depth < maxDepth)
            {
                string childPrefix = prefix + (isLast ? "   " : "│  ");
                int childCount = obj.transform.childCount;
                int visibleChildIndex = 0;

                for (int i = 0; i < childCount; i++)
                {
                    var child = obj.transform.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;

                    // Count remaining visible children
                    int remainingVisible = 0;
                    for (int j = i + 1; j < childCount; j++)
                    {
                        var c = obj.transform.GetChild(j).gameObject;
                        if (includeInactive || c.activeSelf) remainingVisible++;
                    }

                    bool childIsLast = (remainingVisible == 0);
                    FormatGameObjectAsTree(sb, child, childPrefix, childIsLast, depth + 1, maxDepth, includeInactive);
                    visibleChildIndex++;
                }
            }
            else if (obj.transform.childCount > 0)
            {
                // Indicate there are more children
                string childPrefix = prefix + (isLast ? "   " : "│  ");
                sb.AppendLine($"{childPrefix}└─ ... ({obj.transform.childCount} children)");
            }
        }

        /// <summary>
        /// Get only key/important component names (not Transform, not base MonoBehaviour)
        /// </summary>
        public static List<string> GetKeyComponentNames(GameObject obj)
        {
            var result = new List<string>();
            var components = obj.GetComponents<Component>();

            foreach (var comp in components)
            {
                if (comp == null) continue;

                string typeName = comp.GetType().Name;

                // Skip Transform (every object has it)
                if (typeName == "Transform") continue;

                // Include if it's a known key type
                if (KeyComponentTypes.Contains(typeName))
                {
                    result.Add(typeName);
                    continue;
                }

                // Include custom scripts (not Unity built-in)
                string fullName = comp.GetType().FullName;
                if (!fullName.StartsWith("UnityEngine.") && !fullName.StartsWith("UnityEditor."))
                {
                    result.Add(typeName);
                }
            }

            return result;
        }

        /// <summary>
        /// Get just the names of objects (minimal output)
        /// </summary>
        public static List<string> GetObjectNames(List<GameObject> objects, bool includeInactive)
        {
            var result = new List<string>();
            foreach (var obj in objects)
            {
                if (!includeInactive && !obj.activeSelf) continue;
                CollectNames(obj, result, includeInactive);
            }
            return result;
        }

        private static void CollectNames(GameObject obj, List<string> result, bool includeInactive)
        {
            result.Add(obj.name);
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i).gameObject;
                if (!includeInactive && !child.activeSelf) continue;
                CollectNames(child, result, includeInactive);
            }
        }

        /// <summary>
        /// Get summary info (name + key components only)
        /// </summary>
        public static List<object> GetSummaryInfo(List<GameObject> rootObjects, int maxDepth, bool includeInactive)
        {
            var result = new List<object>();
            foreach (var obj in rootObjects)
            {
                if (!includeInactive && !obj.activeSelf) continue;
                result.Add(GetSummaryForObject(obj, 0, maxDepth, includeInactive));
            }
            return result;
        }

        private static object GetSummaryForObject(GameObject obj, int depth, int maxDepth, bool includeInactive)
        {
            var children = new List<object>();

            if (depth < maxDepth)
            {
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    var child = obj.transform.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;
                    children.Add(GetSummaryForObject(child, depth + 1, maxDepth, includeInactive));
                }
            }

            var keyComps = GetKeyComponentNames(obj);

            return new
            {
                name = obj.name,
                active = obj.activeSelf,
                components = keyComps.Count > 0 ? keyComps : null,
                childCount = obj.transform.childCount,
                children = children.Count > 0 ? children : null
            };
        }

        /// <summary>
        /// Filter objects by name pattern (supports * wildcard)
        /// </summary>
        public static bool MatchesNameFilter(GameObject obj, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;

            // Convert wildcard pattern to regex
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(obj.name, regexPattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Check if object has a specific component type
        /// </summary>
        public static bool HasComponent(GameObject obj, string componentTypeName)
        {
            if (string.IsNullOrEmpty(componentTypeName)) return true;

            var components = obj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(componentTypeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Collect filtered objects from hierarchy
        /// </summary>
        public static List<GameObject> CollectFilteredObjects(
            List<GameObject> rootObjects,
            string nameFilter,
            string componentFilter,
            string tagFilter,
            string layerFilter,
            bool rootOnly,
            bool includeInactive)
        {
            var result = new List<GameObject>();

            foreach (var root in rootObjects)
            {
                if (!includeInactive && !root.activeSelf) continue;
                CollectMatchingObjects(root, result, nameFilter, componentFilter, tagFilter, layerFilter, rootOnly, includeInactive);
            }

            return result;
        }

        private static void CollectMatchingObjects(
            GameObject obj,
            List<GameObject> result,
            string nameFilter,
            string componentFilter,
            string tagFilter,
            string layerFilter,
            bool rootOnly,
            bool includeInactive)
        {
            bool matches = true;

            // Apply filters
            if (!string.IsNullOrEmpty(nameFilter) && !MatchesNameFilter(obj, nameFilter))
                matches = false;

            if (matches && !string.IsNullOrEmpty(componentFilter) && !HasComponent(obj, componentFilter))
                matches = false;

            if (matches && !string.IsNullOrEmpty(tagFilter) && !obj.CompareTag(tagFilter))
                matches = false;

            if (matches && !string.IsNullOrEmpty(layerFilter))
            {
                string layerName = LayerMask.LayerToName(obj.layer);
                if (!layerName.Equals(layerFilter, StringComparison.OrdinalIgnoreCase))
                    matches = false;
            }

            if (matches)
                result.Add(obj);

            // Process children if not rootOnly
            if (!rootOnly)
            {
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    var child = obj.transform.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;
                    CollectMatchingObjects(child, result, nameFilter, componentFilter, tagFilter, layerFilter, false, includeInactive);
                }
            }
        }
    }
}
