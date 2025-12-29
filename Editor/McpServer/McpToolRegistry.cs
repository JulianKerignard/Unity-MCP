using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using McpUnity.Protocol;

namespace McpUnity.Server
{
    /// <summary>
    /// Registry for MCP tools
    /// </summary>
    public class McpToolRegistry
    {
        private readonly Dictionary<string, McpToolDefinition> _toolDefinitions
            = new Dictionary<string, McpToolDefinition>();

        private readonly Dictionary<string, Func<Dictionary<string, object>, McpToolResult>> _toolHandlers
            = new Dictionary<string, Func<Dictionary<string, object>, McpToolResult>>();

        /// <summary>
        /// Register a tool with its definition and handler
        /// </summary>
        public void RegisterTool(McpToolDefinition definition, Func<Dictionary<string, object>, McpToolResult> handler)
        {
            if (string.IsNullOrEmpty(definition?.name))
            {
                Debug.LogError("[MCP Registry] Cannot register tool without a name");
                return;
            }

            if (handler == null)
            {
                Debug.LogError($"[MCP Registry] Cannot register tool '{definition.name}' without a handler");
                return;
            }

            _toolDefinitions[definition.name] = definition;
            _toolHandlers[definition.name] = handler;

            Debug.Log($"[MCP Registry] Registered tool: {definition.name}");
        }

        /// <summary>
        /// Unregister a tool by name
        /// </summary>
        public bool UnregisterTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            bool removed = _toolDefinitions.Remove(name);
            _toolHandlers.Remove(name);

            if (removed)
            {
                Debug.Log($"[MCP Registry] Unregistered tool: {name}");
            }

            return removed;
        }

        /// <summary>
        /// Check if a tool exists
        /// </summary>
        public bool HasTool(string name)
        {
            return !string.IsNullOrEmpty(name) && _toolDefinitions.ContainsKey(name);
        }

        /// <summary>
        /// Get all registered tool definitions
        /// </summary>
        public List<McpToolDefinition> GetAllTools()
        {
            return new List<McpToolDefinition>(_toolDefinitions.Values);
        }

        /// <summary>
        /// Get a specific tool definition
        /// </summary>
        public McpToolDefinition GetTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _toolDefinitions.TryGetValue(name, out var definition);
            return definition;
        }

        /// <summary>
        /// Execute a tool by name with the provided arguments
        /// </summary>
        public McpToolResult ExecuteTool(string name, Dictionary<string, object> arguments)
        {
            if (string.IsNullOrEmpty(name))
            {
                return McpToolResult.Error("Tool name is required");
            }

            if (!_toolHandlers.TryGetValue(name, out var handler))
            {
                return McpToolResult.Error($"Tool not found: {name}");
            }

            // Validate required parameters
            if (_toolDefinitions.TryGetValue(name, out var definition))
            {
                var validationError = ValidateArguments(definition, arguments);
                if (validationError != null)
                {
                    return McpToolResult.Error(validationError);
                }
            }

            try
            {
                Debug.Log($"[MCP Registry] Executing tool: {name}");
                return handler(arguments ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Registry] Tool execution error for '{name}': {ex.Message}\n{ex.StackTrace}");
                return McpToolResult.Error($"Tool execution failed: {ex.Message}");
            }
        }

        private string ValidateArguments(McpToolDefinition definition, Dictionary<string, object> arguments)
        {
            if (definition.inputSchema?.required == null) return null;

            arguments = arguments ?? new Dictionary<string, object>();

            foreach (var required in definition.inputSchema.required)
            {
                if (!arguments.ContainsKey(required) || arguments[required] == null)
                {
                    return $"Missing required argument: {required}";
                }
            }

            return null;
        }

        /// <summary>
        /// Get the count of registered tools
        /// </summary>
        public int Count => _toolDefinitions.Count;

        /// <summary>
        /// Clear all registered tools
        /// </summary>
        public void Clear()
        {
            _toolDefinitions.Clear();
            _toolHandlers.Clear();
            Debug.Log("[MCP Registry] Cleared all tools");
        }

        /// <summary>
        /// Get debug listing of all tools
        /// </summary>
        public string GetDebugList()
        {
            var lines = new List<string> { $"Registered MCP Tools ({Count}):", "" };
            foreach (var tool in _toolDefinitions.Values.OrderBy(t => t.name))
            {
                lines.Add($"  - {tool.name}");
                lines.Add($"    {tool.description}");
                if (tool.inputSchema?.required?.Count > 0)
                {
                    lines.Add($"    Required: {string.Join(", ", tool.inputSchema.required)}");
                }
                lines.Add("");
            }
            return string.Join("\n", lines);
        }
    }
}
