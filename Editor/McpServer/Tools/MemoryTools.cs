using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Protocol;
using McpUnity.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Server
{
    /// <summary>
    /// Memory/Cache management tools for MCP Unity Server.
    /// Contains 3 tools: MemoryGet, MemoryRefresh, MemoryClear
    /// </summary>
    public partial class McpUnityServer
    {
        private const string CacheDirectory = "Assets/.mcp-cache";
        private const string CacheFilePath = "Assets/.mcp-cache/memory.json";
        private const int MaxOperationsHistory = 20;
        private const int CacheMaxAgeMinutes = 5;

        /// <summary>
        /// Register all memory/cache tools
        /// </summary>
        static partial void RegisterMemoryTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_get",
                description = "Get cached project information (assets, scenes, hierarchy, recent operations)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema { type = "string", description = "Section to retrieve: 'all' (default), 'assets', 'scenes', 'hierarchy', 'operations'" }
                    },
                    required = new List<string>()
                }
            }, MemoryGet);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_refresh",
                description = "Refresh cached project information",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema { type = "string", description = "Section to refresh: 'all', 'assets', 'scenes', 'hierarchy'" }
                    },
                    required = new List<string> { "section" }
                }
            }, MemoryRefresh);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_memory_clear",
                description = "Clear cached project information",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["section"] = new McpPropertySchema { type = "string", description = "Section to clear: 'all' (default), 'assets', 'scenes', 'hierarchy', 'operations'" }
                    },
                    required = new List<string>()
                }
            }, MemoryClear);
        }

        #region Memory Helpers

        private static Dictionary<string, object> LoadMemoryCache()
        {
            EnsureCacheDirectory();

            if (!System.IO.File.Exists(CacheFilePath))
            {
                return CreateEmptyCache();
            }

            try
            {
                var json = System.IO.File.ReadAllText(CacheFilePath);
                return JsonHelper.FromJson<Dictionary<string, object>>(json) ?? CreateEmptyCache();
            }
            catch (Exception e)
            {
                McpDebug.LogWarning($"[MCP Unity] Failed to load cache: {e.Message}");
                return CreateEmptyCache();
            }
        }

        private static Dictionary<string, object> CreateEmptyCache()
        {
            return new Dictionary<string, object>
            {
                ["version"] = "1.0",
                ["lastUpdated"] = DateTime.Now.ToString("o"),
                ["projectName"] = Application.productName,
                ["assets"] = null,
                ["scenes"] = null,
                ["hierarchy"] = null,
                ["operations"] = new List<object>()
            };
        }

        private static void SaveMemoryCache(Dictionary<string, object> cache)
        {
            EnsureCacheDirectory();
            cache["lastUpdated"] = DateTime.Now.ToString("o");

            try
            {
                var json = JsonHelper.ToJson(cache);
                System.IO.File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Unity] Failed to save cache: {e.Message}");
            }
        }

        private static void EnsureCacheDirectory()
        {
            if (!System.IO.Directory.Exists(CacheDirectory))
            {
                System.IO.Directory.CreateDirectory(CacheDirectory);

                // Create .gitignore
                var gitignorePath = System.IO.Path.Combine(CacheDirectory, ".gitignore");
                System.IO.File.WriteAllText(gitignorePath, "# MCP cache files\nmemory.json\n");
            }
        }

        private static bool IsSectionStale(Dictionary<string, object> cache, string section)
        {
            if (cache[section] == null) return true;

            var sectionData = cache[section] as Dictionary<string, object>;
            if (sectionData == null || !sectionData.ContainsKey("lastFetch")) return true;

            if (DateTime.TryParse(sectionData["lastFetch"] as string, out var lastFetch))
            {
                return (DateTime.Now - lastFetch).TotalMinutes > CacheMaxAgeMinutes;
            }
            return true;
        }

        private static void LogOperation(string tool, string result)
        {
            var cache = LoadMemoryCache();
            var operations = cache["operations"] as List<object> ?? new List<object>();

            operations.Insert(0, new Dictionary<string, object>
            {
                ["time"] = DateTime.Now.ToString("HH:mm:ss"),
                ["tool"] = tool,
                ["result"] = result
            });

            // Keep only MaxOperationsHistory
            while (operations.Count > MaxOperationsHistory)
            {
                operations.RemoveAt(operations.Count - 1);
            }

            cache["operations"] = operations;
            SaveMemoryCache(cache);
        }

        private static Dictionary<string, object> RefreshAssetsCache()
        {
            var byType = new Dictionary<string, List<string>>();
            var allAssets = AssetDatabase.GetAllAssetPaths();

            foreach (var path in allAssets)
            {
                if (!path.StartsWith("Assets/")) continue;
                if (path.StartsWith("Assets/.mcp-cache")) continue;

                var ext = System.IO.Path.GetExtension(path).ToLower();
                var type = GetAssetTypeFromExtension(ext);

                if (!byType.ContainsKey(type))
                    byType[type] = new List<string>();

                byType[type].Add(path);
            }

            // Convert to serializable format
            var byTypeSerializable = new Dictionary<string, object>();
            foreach (var kvp in byType)
            {
                byTypeSerializable[kvp.Key] = kvp.Value;
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["count"] = allAssets.Count(p => p.StartsWith("Assets/") && !p.StartsWith("Assets/.mcp-cache")),
                ["byType"] = byTypeSerializable
            };
        }

        private static string GetAssetTypeFromExtension(string extension)
        {
            switch (extension)
            {
                case ".prefab": return "Prefab";
                case ".unity": return "Scene";
                case ".cs": return "Script";
                case ".mat": return "Material";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": return "Texture";
                case ".fbx": case ".obj": case ".blend": return "Model";
                case ".anim": return "Animation";
                case ".controller": return "AnimatorController";
                case ".mp3": case ".wav": case ".ogg": return "Audio";
                case ".shader": case ".shadergraph": return "Shader";
                case ".asset": return "ScriptableObject";
                case ".json": case ".xml": case ".txt": return "TextAsset";
                default: return "Other";
            }
        }

        private static Dictionary<string, object> RefreshScenesCache()
        {
            var activeScene = SceneManager.GetActiveScene();
            var sceneList = new List<Dictionary<string, object>>();

            // Get all scenes in build settings
            var scenesInBuild = EditorBuildSettings.scenes;
            for (int i = 0; i < scenesInBuild.Length; i++)
            {
                var scene = scenesInBuild[i];
                sceneList.Add(new Dictionary<string, object>
                {
                    ["path"] = scene.path,
                    ["name"] = System.IO.Path.GetFileNameWithoutExtension(scene.path),
                    ["buildIndex"] = i,
                    ["enabled"] = scene.enabled
                });
            }

            // Also add currently loaded scenes not in build settings
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!sceneList.Any(s => s["path"] as string == scene.path))
                {
                    sceneList.Add(new Dictionary<string, object>
                    {
                        ["path"] = scene.path,
                        ["name"] = scene.name,
                        ["buildIndex"] = -1,
                        ["enabled"] = true,
                        ["loaded"] = true
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["active"] = activeScene.name,
                ["activePath"] = activeScene.path,
                ["count"] = sceneList.Count,
                ["list"] = sceneList
            };
        }

        private static Dictionary<string, object> RefreshHierarchyCache()
        {
            var activeScene = SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();
            var rootNames = rootObjects.Select(go => go.name).ToList();

            // Count total GameObjects
            int totalCount = 0;
            foreach (var root in rootObjects)
            {
                totalCount += CountGameObjectsInHierarchy(root.transform);
            }

            return new Dictionary<string, object>
            {
                ["lastFetch"] = DateTime.Now.ToString("o"),
                ["scene"] = activeScene.name,
                ["rootObjects"] = rootNames,
                ["rootCount"] = rootObjects.Length,
                ["totalCount"] = totalCount,
                ["summary"] = $"{rootObjects.Length} root objects, {totalCount} total GameObjects"
            };
        }

        private static int CountGameObjectsInHierarchy(Transform parent)
        {
            int count = 1;
            foreach (Transform child in parent)
            {
                count += CountGameObjectsInHierarchy(child);
            }
            return count;
        }

        #endregion

        #region Memory Handlers

        private static McpToolResult MemoryGet(Dictionary<string, object> args)
        {
            var section = "all";
            if (args.TryGetValue("section", out var sectionObj) && sectionObj != null)
            {
                section = sectionObj.ToString().ToLower();
            }

            var cache = LoadMemoryCache();
            var result = new Dictionary<string, object>();

            if (section == "all")
            {
                result["version"] = cache["version"];
                result["lastUpdated"] = cache["lastUpdated"];
                result["projectName"] = cache["projectName"];
                result["assets"] = cache["assets"];
                result["scenes"] = cache["scenes"];
                result["hierarchy"] = cache["hierarchy"];
                result["operations"] = cache["operations"];

                result["stale"] = new Dictionary<string, object>
                {
                    ["assets"] = cache["assets"] == null || IsSectionStale(cache, "assets"),
                    ["scenes"] = cache["scenes"] == null || IsSectionStale(cache, "scenes"),
                    ["hierarchy"] = cache["hierarchy"] == null || IsSectionStale(cache, "hierarchy")
                };
            }
            else if (cache.ContainsKey(section))
            {
                result["section"] = section;
                result["data"] = cache[section];
                result["needsRefresh"] = cache[section] == null || (section != "operations" && IsSectionStale(cache, section));
            }
            else
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, operations, all")
                    },
                    isError = true
                };
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(result) },
                isError = false
            };
        }

        private static McpToolResult MemoryRefresh(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("section", out var sectionObj) || sectionObj == null)
            {
                return new McpToolResult
                {
                    content = new List<McpContent> { McpContent.Text("Missing required parameter: section") },
                    isError = true
                };
            }

            var section = sectionObj.ToString().ToLower();
            var cache = LoadMemoryCache();
            var refreshed = new List<string>();

            if (section == "all" || section == "assets")
            {
                cache["assets"] = RefreshAssetsCache();
                refreshed.Add("assets");
            }

            if (section == "all" || section == "scenes")
            {
                cache["scenes"] = RefreshScenesCache();
                refreshed.Add("scenes");
            }

            if (section == "all" || section == "hierarchy")
            {
                cache["hierarchy"] = RefreshHierarchyCache();
                refreshed.Add("hierarchy");
            }

            if (refreshed.Count == 0)
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, all")
                    },
                    isError = true
                };
            }

            SaveMemoryCache(cache);
            LogOperation("unity_memory_refresh", $"Refreshed: {string.Join(", ", refreshed)}");

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["refreshed"] = refreshed,
                        ["assets"] = section == "all" || section == "assets" ? cache["assets"] : null,
                        ["scenes"] = section == "all" || section == "scenes" ? cache["scenes"] : null,
                        ["hierarchy"] = section == "all" || section == "hierarchy" ? cache["hierarchy"] : null
                    })
                },
                isError = false
            };
        }

        private static McpToolResult MemoryClear(Dictionary<string, object> args)
        {
            var section = "all";
            if (args.TryGetValue("section", out var sectionObj) && sectionObj != null)
            {
                section = sectionObj.ToString().ToLower();
            }

            var cache = LoadMemoryCache();
            var cleared = new List<string>();

            if (section == "all")
            {
                cache = CreateEmptyCache();
                cleared.AddRange(new[] { "assets", "scenes", "hierarchy", "operations" });
            }
            else if (section == "assets" || section == "scenes" || section == "hierarchy")
            {
                cache[section] = null;
                cleared.Add(section);
            }
            else if (section == "operations")
            {
                cache["operations"] = new List<object>();
                cleared.Add("operations");
            }
            else
            {
                return new McpToolResult
                {
                    content = new List<McpContent>
                    {
                        McpContent.Text($"Unknown section: {section}. Valid: assets, scenes, hierarchy, operations, all")
                    },
                    isError = true
                };
            }

            SaveMemoryCache(cache);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["cleared"] = cleared,
                        ["message"] = $"Cleared: {string.Join(", ", cleared)}"
                    })
                },
                isError = false
            };
        }

        #endregion
    }
}
