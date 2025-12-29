using System;
using System.Collections.Generic;
using UnityEngine;
using McpUnity.Protocol;

namespace McpUnity.Server
{
    /// <summary>
    /// Registry for MCP resources - manages resource definitions and read handlers
    /// </summary>
    public class McpResourceRegistry
    {
        private readonly Dictionary<string, McpResourceDefinition> _resourceDefinitions
            = new Dictionary<string, McpResourceDefinition>();

        private readonly Dictionary<string, Func<McpResourceContent>> _resourceHandlers
            = new Dictionary<string, Func<McpResourceContent>>();

        /// <summary>
        /// Register a resource with its definition and handler
        /// </summary>
        public void RegisterResource(McpResourceDefinition definition, Func<McpResourceContent> handler)
        {
            if (string.IsNullOrEmpty(definition?.uri))
            {
                Debug.LogError("[MCP Registry] Cannot register resource without a URI");
                return;
            }

            if (handler == null)
            {
                Debug.LogError($"[MCP Registry] Cannot register resource '{definition.uri}' without a handler");
                return;
            }

            _resourceDefinitions[definition.uri] = definition;
            _resourceHandlers[definition.uri] = handler;

            Debug.Log($"[MCP Registry] Registered resource: {definition.uri}");
        }

        /// <summary>
        /// Unregister a resource by URI
        /// </summary>
        public bool UnregisterResource(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;

            bool removed = _resourceDefinitions.Remove(uri);
            _resourceHandlers.Remove(uri);

            if (removed)
            {
                Debug.Log($"[MCP Registry] Unregistered resource: {uri}");
            }

            return removed;
        }

        /// <summary>
        /// Check if a resource exists
        /// </summary>
        public bool HasResource(string uri)
        {
            return !string.IsNullOrEmpty(uri) && _resourceDefinitions.ContainsKey(uri);
        }

        /// <summary>
        /// Get all registered resource definitions
        /// </summary>
        public List<McpResourceDefinition> GetAllResources()
        {
            return new List<McpResourceDefinition>(_resourceDefinitions.Values);
        }

        /// <summary>
        /// Get a specific resource definition
        /// </summary>
        public McpResourceDefinition GetResource(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            _resourceDefinitions.TryGetValue(uri, out var definition);
            return definition;
        }

        /// <summary>
        /// Read a resource by URI
        /// </summary>
        public McpResourceResult ReadResource(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("Resource URI is required");
            }

            // Handle wildcard/pattern URIs
            var handler = FindHandler(uri);

            if (handler == null)
            {
                throw new KeyNotFoundException($"Resource not found: {uri}");
            }

            try
            {
                Debug.Log($"[MCP Registry] Reading resource: {uri}");
                var content = handler();

                return new McpResourceResult
                {
                    contents = new List<McpResourceContent> { content }
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Registry] Resource read error for '{uri}': {ex.Message}\n{ex.StackTrace}");
                throw new Exception($"Failed to read resource: {ex.Message}");
            }
        }

        private Func<McpResourceContent> FindHandler(string uri)
        {
            // Exact match first
            if (_resourceHandlers.TryGetValue(uri, out var handler))
            {
                return handler;
            }

            // Pattern matching for dynamic URIs (e.g., unity://asset/*)
            foreach (var kvp in _resourceHandlers)
            {
                if (MatchesPattern(kvp.Key, uri))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private bool MatchesPattern(string pattern, string uri)
        {
            // Simple wildcard matching
            if (!pattern.Contains("*")) return false;

            var prefix = pattern.Substring(0, pattern.IndexOf('*'));
            return uri.StartsWith(prefix);
        }

        /// <summary>
        /// Get the count of registered resources
        /// </summary>
        public int Count => _resourceDefinitions.Count;

        /// <summary>
        /// Clear all registered resources
        /// </summary>
        public void Clear()
        {
            _resourceDefinitions.Clear();
            _resourceHandlers.Clear();
            Debug.Log("[MCP Registry] Cleared all resources");
        }
    }
}
