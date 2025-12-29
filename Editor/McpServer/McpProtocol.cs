using System;
using System.Collections.Generic;

namespace McpUnity.Protocol
{
    /// <summary>
    /// JSON-RPC 2.0 Request object
    /// </summary>
    [Serializable]
    public class JsonRpcRequest
    {
        public string jsonrpc = "2.0";
        public object id;
        public string method;
        public object @params;

        public T GetParams<T>() where T : class
        {
            if (@params == null) return null;

            if (@params is T typed)
                return typed;

            // Handle Dictionary conversion for Unity's JsonUtility limitations
            var json = UnityEngine.JsonUtility.ToJson(@params);
            return UnityEngine.JsonUtility.FromJson<T>(json);
        }
    }

    /// <summary>
    /// JSON-RPC 2.0 Response object
    /// </summary>
    [Serializable]
    public class JsonRpcResponse
    {
        public string jsonrpc = "2.0";
        public object id;
        public object result;
        public JsonRpcError error;

        public static JsonRpcResponse Success(object id, object result)
        {
            return new JsonRpcResponse
            {
                id = id,
                result = result
            };
        }

        public static JsonRpcResponse Error(object id, int code, string message, object data = null)
        {
            return new JsonRpcResponse
            {
                id = id,
                error = new JsonRpcError
                {
                    code = code,
                    message = message,
                    data = data
                }
            };
        }
    }

    /// <summary>
    /// JSON-RPC 2.0 Error object
    /// </summary>
    [Serializable]
    public class JsonRpcError
    {
        public int code;
        public string message;
        public object data;

        // Standard JSON-RPC error codes
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        // MCP specific error codes (aligned with TypeScript types.ts)
        public const int ToolNotFound = -32001;
        public const int ResourceNotFound = -32002;
        public const int ExecutionError = -32003;
        public const int TimeoutError = -32004;
        public const int UnityError = -32005;
    }

    /// <summary>
    /// MCP Content block for tool results
    /// </summary>
    [Serializable]
    public class McpContent
    {
        public string type = "text";
        public string text;
        public string mimeType;
        public string data; // For binary/base64 content

        public static McpContent Text(string text)
        {
            return new McpContent { type = "text", text = text };
        }

        public static McpContent Json(object obj)
        {
            return new McpContent
            {
                type = "text",
                text = McpUnity.Server.JsonHelper.ToJson(obj),
                mimeType = "application/json"
            };
        }

        public static McpContent Image(string base64Data, string mimeType = "image/png")
        {
            return new McpContent
            {
                type = "image",
                data = base64Data,
                mimeType = mimeType
            };
        }
    }

    /// <summary>
    /// MCP Tool call result
    /// </summary>
    [Serializable]
    public class McpToolResult
    {
        public List<McpContent> content = new List<McpContent>();
        public bool isError;

        public static McpToolResult Success(string text)
        {
            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Text(text) },
                isError = false
            };
        }

        public static McpToolResult Success(List<McpContent> content)
        {
            return new McpToolResult
            {
                content = content,
                isError = false
            };
        }

        public static McpToolResult Error(string errorMessage)
        {
            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Text(errorMessage) },
                isError = true
            };
        }
    }

    /// <summary>
    /// MCP Resource content
    /// </summary>
    [Serializable]
    public class McpResourceContent
    {
        public string uri;
        public string mimeType;
        public string text;
        public string blob; // Base64 for binary
    }

    /// <summary>
    /// MCP Resource read result
    /// </summary>
    [Serializable]
    public class McpResourceResult
    {
        public List<McpResourceContent> contents = new List<McpResourceContent>();
    }

    /// <summary>
    /// MCP Tool definition
    /// </summary>
    [Serializable]
    public class McpToolDefinition
    {
        public string name;
        public string description;
        public McpInputSchema inputSchema;
    }

    /// <summary>
    /// MCP Input Schema (JSON Schema subset)
    /// </summary>
    [Serializable]
    public class McpInputSchema
    {
        public string type = "object";
        public Dictionary<string, McpPropertySchema> properties = new Dictionary<string, McpPropertySchema>();
        public List<string> required = new List<string>();
    }

    /// <summary>
    /// MCP Property Schema
    /// </summary>
    [Serializable]
    public class McpPropertySchema
    {
        public string type;
        public string description;
        public List<string> @enum;
        public object @default;
    }

    /// <summary>
    /// MCP Resource definition
    /// </summary>
    [Serializable]
    public class McpResourceDefinition
    {
        public string uri;
        public string name;
        public string description;
        public string mimeType;
    }

    /// <summary>
    /// MCP Server capabilities
    /// </summary>
    [Serializable]
    public class McpServerCapabilities
    {
        public McpToolsCapability tools;
        public McpResourcesCapability resources;
        public McpPromptsCapability prompts;
    }

    [Serializable]
    public class McpToolsCapability
    {
        public bool listChanged = true;
    }

    [Serializable]
    public class McpResourcesCapability
    {
        public bool subscribe = false;
        public bool listChanged = true;
    }

    [Serializable]
    public class McpPromptsCapability
    {
        public bool listChanged = false;
    }

    /// <summary>
    /// MCP Initialize result
    /// </summary>
    [Serializable]
    public class McpInitializeResult
    {
        public string protocolVersion = "2024-11-05";
        public McpServerCapabilities capabilities;
        public McpServerInfo serverInfo;
    }

    [Serializable]
    public class McpServerInfo
    {
        public string name;
        public string version;
    }

    /// <summary>
    /// Parameters for tools/call
    /// </summary>
    [Serializable]
    public class ToolCallParams
    {
        public string name;
        public Dictionary<string, object> arguments;
    }

    /// <summary>
    /// Parameters for resources/read
    /// </summary>
    [Serializable]
    public class ResourceReadParams
    {
        public string uri;
    }
}
