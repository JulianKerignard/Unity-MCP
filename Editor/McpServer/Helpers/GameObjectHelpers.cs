using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Helpers
{
    /// <summary>
    /// Helper methods for GameObject operations in MCP Unity Server
    /// </summary>
    public static class GameObjectHelpers
    {
        /// <summary>
        /// Find a GameObject by path in the hierarchy
        /// Supports paths like "Parent/Child/GrandChild"
        /// </summary>
        /// <param name="path">Hierarchy path to the GameObject</param>
        /// <returns>The GameObject or null if not found</returns>
        public static GameObject FindGameObject(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Try direct find first (for root objects)
            var go = GameObject.Find(path);
            if (go != null) return go;

            // Split path and traverse
            var parts = path.Split('/');
            if (parts.Length == 0) return null;

            // Find root object
            GameObject current = null;
            foreach (var rootGo in GetRootGameObjects())
            {
                if (rootGo.name == parts[0])
                {
                    current = rootGo;
                    break;
                }
            }

            if (current == null) return null;

            // Traverse children
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        /// <summary>
        /// Get all root GameObjects in the active scene
        /// </summary>
        public static GameObject[] GetRootGameObjects()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects();
        }

        /// <summary>
        /// Get the full hierarchy path of a GameObject
        /// </summary>
        public static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;

            var path = go.name;
            var parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>
        /// Get basic info about a GameObject as a dictionary
        /// </summary>
        public static Dictionary<string, object> GetGameObjectInfo(GameObject go, bool includeComponents = false)
        {
            if (go == null) return null;

            var info = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["path"] = GetGameObjectPath(go),
                ["active"] = go.activeSelf,
                ["activeInHierarchy"] = go.activeInHierarchy,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["tag"] = go.tag,
                ["isStatic"] = go.isStatic,
                ["childCount"] = go.transform.childCount
            };

            // Transform info
            info["transform"] = new Dictionary<string, object>
            {
                ["position"] = new { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z },
                ["localPosition"] = new { x = go.transform.localPosition.x, y = go.transform.localPosition.y, z = go.transform.localPosition.z },
                ["rotation"] = new { x = go.transform.eulerAngles.x, y = go.transform.eulerAngles.y, z = go.transform.eulerAngles.z },
                ["localScale"] = new { x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z }
            };

            // Components list
            if (includeComponents)
            {
                var components = new List<string>();
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp != null)
                        components.Add(comp.GetType().Name);
                }
                info["components"] = components;
            }

            return info;
        }

        /// <summary>
        /// Get info about all children of a GameObject
        /// </summary>
        public static List<Dictionary<string, object>> GetChildrenInfo(GameObject parent, bool recursive = false, int maxDepth = 10)
        {
            var children = new List<Dictionary<string, object>>();
            if (parent == null) return children;

            GetChildrenRecursive(parent.transform, children, recursive, 0, maxDepth);
            return children;
        }

        private static void GetChildrenRecursive(Transform parent, List<Dictionary<string, object>> list, bool recursive, int depth, int maxDepth)
        {
            if (depth >= maxDepth) return;

            foreach (Transform child in parent)
            {
                var info = new Dictionary<string, object>
                {
                    ["name"] = child.name,
                    ["path"] = GetGameObjectPath(child.gameObject),
                    ["active"] = child.gameObject.activeSelf,
                    ["depth"] = depth,
                    ["childCount"] = child.childCount
                };
                list.Add(info);

                if (recursive && child.childCount > 0)
                {
                    GetChildrenRecursive(child, list, true, depth + 1, maxDepth);
                }
            }
        }

        /// <summary>
        /// Count total GameObjects in hierarchy
        /// </summary>
        public static int CountGameObjects(bool includeInactive = true)
        {
            int count = 0;
            foreach (var root in GetRootGameObjects())
            {
                count += CountRecursive(root.transform, includeInactive);
            }
            return count;
        }

        private static int CountRecursive(Transform t, bool includeInactive)
        {
            int count = (includeInactive || t.gameObject.activeSelf) ? 1 : 0;
            foreach (Transform child in t)
            {
                count += CountRecursive(child, includeInactive);
            }
            return count;
        }
    }
}
