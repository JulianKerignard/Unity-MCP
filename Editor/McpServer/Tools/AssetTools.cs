using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using McpUnity.Helpers;
using McpUnity.Protocol;

namespace McpUnity.Server
{
    /// <summary>
    /// Asset Browser tools for MCP Unity Server.
    /// Provides 5 tools for asset management and inspection.
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all Asset Browser tools.
        /// </summary>
        static partial void RegisterAssetTools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_search_assets",
                description = "Search for assets in the project using Unity's search filter syntax",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["filter"] = new McpPropertySchema { type = "string", description = "Search filter (e.g., 't:Texture', 'l:MyLabel', 'name')" },
                        ["maxResults"] = new McpPropertySchema { type = "integer", description = "Maximum results to return (default: 50, max: 200)" },
                        ["searchFolders"] = new McpPropertySchema { type = "array", description = "Specific folders to search in" }
                    },
                    required = new List<string>()
                }
            }, SearchAssets);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_asset_info",
                description = "Get detailed information about a specific asset including dependencies and type-specific data",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["assetPath"] = new McpPropertySchema { type = "string", description = "Path to the asset" },
                        ["includeDependencies"] = new McpPropertySchema { type = "boolean", description = "Include asset dependencies (default: false)" },
                        ["includeReferences"] = new McpPropertySchema { type = "boolean", description = "Include references to this asset (can be slow)" }
                    },
                    required = new List<string> { "assetPath" }
                }
            }, GetAssetInfo);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_folders",
                description = "List folders in the project hierarchy",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["parentPath"] = new McpPropertySchema { type = "string", description = "Parent folder path (default: 'Assets')" },
                        ["recursive"] = new McpPropertySchema { type = "boolean", description = "Include subfolders recursively (default: true)" },
                        ["maxDepth"] = new McpPropertySchema { type = "integer", description = "Maximum recursion depth (default: 5, max: 20)" }
                    },
                    required = new List<string>()
                }
            }, ListFolders);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_list_folder_contents",
                description = "List assets in a specific folder with optional type filtering",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["folderPath"] = new McpPropertySchema { type = "string", description = "Path to the folder" },
                        ["typeFilter"] = new McpPropertySchema { type = "string", description = "Filter by asset type (e.g., 'Texture2D', 'Material')" },
                        ["includeSubfolders"] = new McpPropertySchema { type = "boolean", description = "Include assets from subfolders (default: false)" }
                    },
                    required = new List<string> { "folderPath" }
                }
            }, ListFolderContents);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_asset_preview",
                description = "Get a preview image of an asset as base64-encoded data",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["assetPath"] = new McpPropertySchema { type = "string", description = "Path to the asset" },
                        ["size"] = new McpPropertySchema { type = "string", description = "Preview size: tiny(32), small(64), medium(128), large(256). Default: small" },
                        ["format"] = new McpPropertySchema { type = "string", description = "Image format: png or jpg. Default: jpg" },
                        ["jpgQuality"] = new McpPropertySchema { type = "integer", description = "JPG quality 1-100 (default: 75)" }
                    },
                    required = new List<string> { "assetPath" }
                }
            }, GetAssetPreview);
        }

        #region Asset Browser Helpers

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private static void CollectFolders(string path, List<object> folders, bool recursive, int maxDepth, int currentDepth)
        {
            var subFolders = AssetDatabase.GetSubFolders(path);

            foreach (var folder in subFolders)
            {
                var assetCount = AssetDatabase.FindAssets("", new[] { folder }).Length;
                var subFolderCount = AssetDatabase.GetSubFolders(folder).Length;

                folders.Add(new
                {
                    path = folder,
                    name = System.IO.Path.GetFileName(folder),
                    depth = currentDepth + 1,
                    assetCount = assetCount,
                    subFolderCount = subFolderCount
                });

                if (recursive && currentDepth < maxDepth - 1)
                {
                    CollectFolders(folder, folders, true, maxDepth, currentDepth + 1);
                }
            }
        }

        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture.active = rt;
            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        #endregion

        #region Asset Browser Handlers

        private static McpToolResult SearchAssets(Dictionary<string, object> args)
        {
            string filter = ArgumentParser.GetString(args, "filter", "");
            int maxResults = ArgumentParser.GetIntClamped(args, "maxResults", 50, 1, 200);

            string[] searchFolders = null;
            if (args.TryGetValue("searchFolders", out var foldersObj) && foldersObj != null)
            {
                if (foldersObj is List<object> folderList)
                {
                    searchFolders = folderList.Select(f => f.ToString()).ToArray();
                }
            }

            string[] guids;
            if (searchFolders != null && searchFolders.Length > 0)
            {
                guids = AssetDatabase.FindAssets(filter, searchFolders);
            }
            else
            {
                guids = AssetDatabase.FindAssets(filter);
            }

            var results = new List<object>();
            int count = 0;

            foreach (var guid in guids)
            {
                if (count >= maxResults) break;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);

                if (type != null)
                {
                    results.Add(new
                    {
                        path = path,
                        name = System.IO.Path.GetFileNameWithoutExtension(path),
                        type = type.Name,
                        guid = guid
                    });
                    count++;
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        filter = filter,
                        totalFound = guids.Length,
                        returned = results.Count,
                        assets = results
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAssetInfo(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("assetPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("assetPath is required");
            }

            string assetPath;
            try
            {
                assetPath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid asset path: {ex.Message}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset == null)
            {
                return McpToolResult.Error($"Asset not found: {assetPath}");
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            var labels = AssetDatabase.GetLabels(asset);

            // Get file info
            var fileInfo = new System.IO.FileInfo(assetPath);
            long fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            var info = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["name"] = asset.name,
                ["type"] = type?.Name ?? "Unknown",
                ["guid"] = guid,
                ["labels"] = labels,
                ["fileSize"] = fileSize,
                ["fileSizeFormatted"] = FormatFileSize(fileSize)
            };

            // Include dependencies if requested
            bool includeDeps = ArgumentParser.GetBool(args, "includeDependencies", false);
            if (includeDeps)
            {
                var deps = AssetDatabase.GetDependencies(assetPath, false);
                info["dependencies"] = deps.Where(d => d != assetPath).ToArray();
            }

            // Include references if requested (can be slow)
            bool includeRefs = ArgumentParser.GetBool(args, "includeReferences", false);
            if (includeRefs)
            {
                var allAssets = AssetDatabase.GetAllAssetPaths();
                var references = new List<string>();

                foreach (var otherPath in allAssets)
                {
                    if (otherPath == assetPath) continue;
                    var deps = AssetDatabase.GetDependencies(otherPath, false);
                    if (deps.Contains(assetPath))
                    {
                        references.Add(otherPath);
                    }
                }
                info["referencedBy"] = references.ToArray();
            }

            // Add type-specific info
            if (asset is Texture2D tex)
            {
                info["textureInfo"] = new
                {
                    width = tex.width,
                    height = tex.height,
                    format = tex.format.ToString(),
                    mipmapCount = tex.mipmapCount
                };
            }
            else if (asset is AudioClip audio)
            {
                info["audioInfo"] = new
                {
                    length = audio.length,
                    channels = audio.channels,
                    frequency = audio.frequency,
                    samples = audio.samples
                };
            }
            else if (asset is Mesh mesh)
            {
                info["meshInfo"] = new
                {
                    vertexCount = mesh.vertexCount,
                    triangles = mesh.triangles.Length / 3,
                    subMeshCount = mesh.subMeshCount,
                    bounds = new
                    {
                        center = new { x = mesh.bounds.center.x, y = mesh.bounds.center.y, z = mesh.bounds.center.z },
                        size = new { x = mesh.bounds.size.x, y = mesh.bounds.size.y, z = mesh.bounds.size.z }
                    }
                };
            }

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(info) },
                isError = false
            };
        }

        private static McpToolResult ListFolders(Dictionary<string, object> args)
        {
            string parentPath = ArgumentParser.GetString(args, "parentPath", "Assets");
            bool recursive = ArgumentParser.GetBool(args, "recursive", true);
            int maxDepth = ArgumentParser.GetIntClamped(args, "maxDepth", 5, 1, 20);

            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                return McpToolResult.Error($"Folder not found: {parentPath}");
            }

            var folders = new List<object>();
            CollectFolders(parentPath, folders, recursive, maxDepth, 0);

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        parentPath = parentPath,
                        recursive = recursive,
                        folderCount = folders.Count,
                        folders = folders
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ListFolderContents(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("folderPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("folderPath is required");
            }

            var folderPath = pathObj.ToString();

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return McpToolResult.Error($"Folder not found: {folderPath}");
            }

            string typeFilter = "";
            if (args.TryGetValue("typeFilter", out var typeObj) && typeObj != null)
            {
                typeFilter = "t:" + typeObj.ToString();
            }

            bool includeSubfolders = ArgumentParser.GetBool(args, "includeSubfolders", false);

            string[] guids;
            if (includeSubfolders)
            {
                guids = AssetDatabase.FindAssets(typeFilter, new[] { folderPath });
            }
            else
            {
                // Get only direct children
                guids = AssetDatabase.FindAssets(typeFilter, new[] { folderPath });
                guids = guids.Where(g =>
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    var parent = System.IO.Path.GetDirectoryName(p).Replace("\\", "/");
                    return parent == folderPath;
                }).ToArray();
            }

            var assets = new List<object>();
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);

                // Skip folders
                if (AssetDatabase.IsValidFolder(assetPath)) continue;

                assets.Add(new
                {
                    path = assetPath,
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                    extension = System.IO.Path.GetExtension(assetPath),
                    type = type?.Name ?? "Unknown",
                    guid = guid
                });
            }

            // Also list subfolders
            var subFolders = AssetDatabase.GetSubFolders(folderPath)
                .Select(f => new { path = f, name = System.IO.Path.GetFileName(f), isFolder = true })
                .ToList();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        folderPath = folderPath,
                        assetCount = assets.Count,
                        subFolderCount = subFolders.Count,
                        assets = assets,
                        subFolders = subFolders
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAssetPreview(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("assetPath", out var pathObj) || pathObj == null)
            {
                return McpToolResult.Error("assetPath is required");
            }

            string assetPath;
            try
            {
                assetPath = SanitizePath(pathObj.ToString());
            }
            catch (ArgumentException ex)
            {
                return McpToolResult.Error($"Invalid asset path: {ex.Message}");
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset == null)
            {
                return McpToolResult.Error($"Asset not found: {assetPath}");
            }

            // Parse size preset (default: small = 64px for token efficiency)
            int size = 64;
            var sizeStr = ArgumentParser.GetString(args, "size", "small");
            switch (sizeStr?.ToLower())
            {
                case "tiny": size = 32; break;
                case "small": size = 64; break;
                case "medium": size = 128; break;
                case "large": size = 256; break;
            }

            // Parse format (default: jpg for smaller size)
            string format = ArgumentParser.GetString(args, "format", "jpg")?.ToLower();

            // Parse JPG quality
            int jpgQuality = ArgumentParser.GetIntClamped(args, "jpgQuality", 75, 1, 100);

            // Get the asset preview
            var preview = AssetPreview.GetAssetPreview(asset);

            // If no preview available, try getting the mini thumbnail
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(asset);
            }

            if (preview == null)
            {
                return McpToolResult.Error($"Could not generate preview for asset: {assetPath}");
            }

            // Resize if needed
            Texture2D resized = preview;
            if (preview.width != size || preview.height != size)
            {
                resized = ResizeTexture(preview, size, size);
            }

            // Encode based on format
            byte[] imageData;
            string mimeType;
            if (format == "png")
            {
                imageData = resized.EncodeToPNG();
                mimeType = "image/png";
            }
            else
            {
                imageData = resized.EncodeToJPG(jpgQuality);
                mimeType = "image/jpeg";
            }

            string base64 = Convert.ToBase64String(imageData);

            // Clean up temporary texture
            if (resized != preview)
            {
                UnityEngine.Object.DestroyImmediate(resized);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        assetPath = assetPath,
                        size = size,
                        format = format,
                        fileSizeBytes = imageData.Length,
                        base64 = base64,
                        dataUri = $"data:{mimeType};base64,{base64}"
                    })
                },
                isError = false
            };
        }

        #endregion
    }
}
