using System;
using System.Collections.Generic;

namespace McpUnity.Models
{
    /// <summary>
    /// Represents the structure of the MCP memory cache file
    /// </summary>
    [Serializable]
    public class MemoryCacheData
    {
        public string version = "1.0";
        public string lastUpdated;
        public string projectName;

        public AssetsCacheSection assets;
        public ScenesCacheSection scenes;
        public HierarchyCacheSection hierarchy;
        public List<RecentOperation> recentOperations;

        public MemoryCacheData()
        {
            assets = new AssetsCacheSection();
            scenes = new ScenesCacheSection();
            hierarchy = new HierarchyCacheSection();
            recentOperations = new List<RecentOperation>();
        }
    }

    [Serializable]
    public class AssetsCacheSection
    {
        public string lastFetch;
        public int count;
        public Dictionary<string, List<string>> byType;
        public Dictionary<string, object> tree;

        public AssetsCacheSection()
        {
            byType = new Dictionary<string, List<string>>();
            tree = new Dictionary<string, object>();
        }
    }

    [Serializable]
    public class ScenesCacheSection
    {
        public string lastFetch;
        public string active;
        public List<SceneInfo> list;

        public ScenesCacheSection()
        {
            list = new List<SceneInfo>();
        }
    }

    [Serializable]
    public class SceneInfo
    {
        public string name;
        public string path;
        public int buildIndex;
    }

    [Serializable]
    public class HierarchyCacheSection
    {
        public string lastFetch;
        public string scene;
        public List<string> rootObjects;
        public string summary;

        public HierarchyCacheSection()
        {
            rootObjects = new List<string>();
        }
    }

    [Serializable]
    public class RecentOperation
    {
        public string time;
        public string tool;
        public string result;

        public RecentOperation() { }

        public RecentOperation(string tool, string result)
        {
            this.time = DateTime.Now.ToString("HH:mm:ss");
            this.tool = tool;
            this.result = result;
        }
    }
}
