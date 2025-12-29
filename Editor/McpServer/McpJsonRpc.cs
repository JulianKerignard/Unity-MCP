using System;
using System.Collections.Generic;
using UnityEngine;
using McpUnity.Protocol;
using McpUnity.Editor;

namespace McpUnity.Server
{
    /// <summary>
    /// JSON-RPC 2.0 message handler for MCP protocol
    /// </summary>
    public static class McpJsonRpc
    {
        private static readonly Dictionary<string, Func<JsonRpcRequest, JsonRpcResponse>> _methodHandlers
            = new Dictionary<string, Func<JsonRpcRequest, JsonRpcResponse>>();

        private static McpToolRegistry _toolRegistry;
        private static McpResourceRegistry _resourceRegistry;

        /// <summary>
        /// Initialize the JSON-RPC handler with tool and resource registries
        /// </summary>
        public static void Initialize(McpToolRegistry toolRegistry, McpResourceRegistry resourceRegistry)
        {
            _toolRegistry = toolRegistry;
            _resourceRegistry = resourceRegistry;
            RegisterDefaultHandlers();
        }

        private static void RegisterDefaultHandlers()
        {
            _methodHandlers.Clear();

            // Core MCP methods
            RegisterMethod("initialize", HandleInitialize);
            RegisterMethod("initialized", HandleInitialized);
            RegisterMethod("ping", HandlePing);

            // Tools
            RegisterMethod("tools/list", HandleToolsList);
            RegisterMethod("tools/call", HandleToolsCall);

            // Resources
            RegisterMethod("resources/list", HandleResourcesList);
            RegisterMethod("resources/read", HandleResourcesRead);

            // Prompts (minimal implementation)
            RegisterMethod("prompts/list", HandlePromptsList);
        }

        /// <summary>
        /// Register a custom method handler
        /// </summary>
        public static void RegisterMethod(string method, Func<JsonRpcRequest, JsonRpcResponse> handler)
        {
            _methodHandlers[method] = handler;
        }

        /// <summary>
        /// Process an incoming JSON-RPC message and return the response
        /// </summary>
        public static string ProcessMessage(string jsonMessage)
        {
            object requestId = null;

            try
            {
                // Parse using our custom parser to properly handle nested objects
                var parsed = JsonHelper.ParseJsonObject(jsonMessage);
                if (parsed == null)
                {
                    return SerializeResponse(JsonRpcResponse.Error(
                        null,
                        JsonRpcError.ParseError,
                        "Failed to parse JSON"
                    ));
                }

                parsed.TryGetValue("id", out requestId);
                parsed.TryGetValue("method", out var methodObj);
                parsed.TryGetValue("params", out var paramsObj);

                string method = methodObj?.ToString();

                if (string.IsNullOrEmpty(method))
                {
                    return SerializeResponse(JsonRpcResponse.Error(
                        requestId,
                        JsonRpcError.InvalidRequest,
                        "Invalid JSON-RPC request: method is required"
                    ));
                }

                // Create request object with properly parsed params
                var request = new JsonRpcRequest
                {
                    id = requestId,
                    method = method,
                    @params = paramsObj
                };

                // Check if this is a notification (no id)
                bool isNotification = requestId == null;

                var response = ProcessRequest(request);

                // Notifications don't get responses
                if (isNotification && response.error == null)
                {
                    return null;
                }

                return SerializeResponse(response);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP JSON-RPC] Error processing message: {ex.Message}\n{ex.StackTrace}");
                return SerializeResponse(JsonRpcResponse.Error(
                    requestId,
                    JsonRpcError.InternalError,
                    ex.Message
                ));
            }
        }

        private static JsonRpcResponse ProcessRequest(JsonRpcRequest request)
        {
            if (!_methodHandlers.TryGetValue(request.method, out var handler))
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.MethodNotFound,
                    $"Method not found: {request.method}"
                );
            }

            try
            {
                return handler(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP JSON-RPC] Handler error for {request.method}: {ex.Message}");
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.InternalError,
                    ex.Message
                );
            }
        }

        #region Default Handlers

        private static JsonRpcResponse HandleInitialize(JsonRpcRequest request)
        {
            var result = new McpInitializeResult
            {
                protocolVersion = "2024-11-05",
                capabilities = new McpServerCapabilities
                {
                    tools = new McpToolsCapability { listChanged = true },
                    resources = new McpResourcesCapability { subscribe = false, listChanged = true },
                    prompts = new McpPromptsCapability { listChanged = false }
                },
                serverInfo = new McpServerInfo
                {
                    name = "mcp-unity",
                    version = "1.0.0"
                }
            };

            return JsonRpcResponse.Success(request.id, result);
        }

        private static JsonRpcResponse HandleInitialized(JsonRpcRequest request)
        {
            // This is a notification, no response needed
            McpDebug.Log("[MCP Unity] Client initialized successfully");
            return JsonRpcResponse.Success(request.id, new { });
        }

        private static JsonRpcResponse HandlePing(JsonRpcRequest request)
        {
            return JsonRpcResponse.Success(request.id, new { });
        }

        private static JsonRpcResponse HandleToolsList(JsonRpcRequest request)
        {
            if (_toolRegistry == null)
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.InternalError,
                    "Tool registry not initialized"
                );
            }

            var tools = _toolRegistry.GetAllTools();
            return JsonRpcResponse.Success(request.id, new { tools = tools });
        }

        private static JsonRpcResponse HandleToolsCall(JsonRpcRequest request)
        {
            if (_toolRegistry == null)
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.InternalError,
                    "Tool registry not initialized"
                );
            }

            try
            {
                // Extract tool name and arguments from params (already parsed as Dictionary)
                string toolName = null;
                Dictionary<string, object> arguments = new Dictionary<string, object>();

                if (request.@params is Dictionary<string, object> paramsDict)
                {
                    if (paramsDict.TryGetValue("name", out var nameObj))
                    {
                        toolName = nameObj?.ToString();
                    }

                    if (paramsDict.TryGetValue("arguments", out var argsObj))
                    {
                        if (argsObj is Dictionary<string, object> argsDict)
                        {
                            arguments = argsDict;
                        }
                    }
                }

                if (string.IsNullOrEmpty(toolName))
                {
                    return JsonRpcResponse.Error(
                        request.id,
                        JsonRpcError.InvalidParams,
                        "Tool name is required"
                    );
                }

                McpDebug.Log($"[MCP JSON-RPC] Executing tool: {toolName} with {arguments.Count} arguments");

                var result = _toolRegistry.ExecuteTool(toolName, arguments);
                return JsonRpcResponse.Success(request.id, result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP JSON-RPC] Tool execution error: {ex.Message}\n{ex.StackTrace}");
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.ExecutionError,
                    $"Tool execution failed: {ex.Message}"
                );
            }
        }

        private static JsonRpcResponse HandleResourcesList(JsonRpcRequest request)
        {
            if (_resourceRegistry == null)
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.InternalError,
                    "Resource registry not initialized"
                );
            }

            var resources = _resourceRegistry.GetAllResources();
            return JsonRpcResponse.Success(request.id, new { resources = resources });
        }

        private static JsonRpcResponse HandleResourcesRead(JsonRpcRequest request)
        {
            if (_resourceRegistry == null)
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.InternalError,
                    "Resource registry not initialized"
                );
            }

            try
            {
                string uri = null;

                if (request.@params is Dictionary<string, object> paramsDict)
                {
                    if (paramsDict.TryGetValue("uri", out var uriObj))
                    {
                        uri = uriObj?.ToString();
                    }
                }

                if (string.IsNullOrEmpty(uri))
                {
                    return JsonRpcResponse.Error(
                        request.id,
                        JsonRpcError.InvalidParams,
                        "Resource URI is required"
                    );
                }

                var result = _resourceRegistry.ReadResource(uri);
                return JsonRpcResponse.Success(request.id, result);
            }
            catch (Exception ex)
            {
                return JsonRpcResponse.Error(
                    request.id,
                    JsonRpcError.ResourceNotFound,
                    ex.Message
                );
            }
        }

        private static JsonRpcResponse HandlePromptsList(JsonRpcRequest request)
        {
            // Minimal prompts implementation - return empty list
            return JsonRpcResponse.Success(request.id, new { prompts = new List<object>() });
        }

        #endregion

        private static string SerializeResponse(JsonRpcResponse response)
        {
            return JsonHelper.ToJson(response);
        }
    }

    /// <summary>
    /// JSON helper with full Dictionary support for MCP protocol
    /// </summary>
    public static class JsonHelper
    {
        public static string ToJson(object obj)
        {
            if (obj == null) return "null";

            if (obj is string s) return $"\"{EscapeString(s)}\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int i) return i.ToString();
            if (obj is long l) return l.ToString();
            if (obj is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (obj is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Handle all Dictionary types (generic and non-generic)
            if (obj is IDictionary<string, object> dict)
            {
                return SerializeDictionary(dict);
            }

            // Handle other generic Dictionary types like Dictionary<string, McpPropertySchema>
            if (obj is System.Collections.IDictionary nonGenericDict)
            {
                return SerializeNonGenericDictionary(nonGenericDict);
            }

            if (obj is System.Collections.IList list)
            {
                return SerializeList(list);
            }

            // For complex objects, use reflection-based serialization
            return SerializeObject(obj);
        }

        public static T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                McpDebug.LogWarning($"[JsonHelper] Failed to parse JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse JSON string to a Dictionary (supports nested objects)
        /// </summary>
        public static Dictionary<string, object> ParseJsonObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            json = json.Trim();
            if (!json.StartsWith("{")) return null;

            var parser = new SimpleJsonParser(json);
            return parser.ParseObject();
        }

        private static string SerializeDictionary(IDictionary<string, object> dict)
        {
            var pairs = new List<string>();
            foreach (var kvp in dict)
            {
                pairs.Add($"\"{EscapeString(kvp.Key)}\":{ToJson(kvp.Value)}");
            }
            return "{" + string.Join(",", pairs) + "}";
        }

        private static string SerializeNonGenericDictionary(System.Collections.IDictionary dict)
        {
            var pairs = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString() ?? "";
                pairs.Add($"\"{EscapeString(key)}\":{ToJson(entry.Value)}");
            }
            return "{" + string.Join(",", pairs) + "}";
        }

        private static string SerializeList(System.Collections.IList list)
        {
            var items = new List<string>();
            foreach (var item in list)
            {
                items.Add(ToJson(item));
            }
            return "[" + string.Join(",", items) + "]";
        }

        private static string SerializeObject(object obj)
        {
            var type = obj.GetType();
            var pairs = new List<string>();
            var serializedNames = new HashSet<string>();

            // 1. D'abord les PROPRIÉTÉS (pour types anonymes et classes modernes)
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    var value = prop.GetValue(obj);
                    if (value != null)
                    {
                        var name = prop.Name;
                        pairs.Add($"\"{EscapeString(name)}\":{ToJson(value)}");
                        serializedNames.Add(name);
                    }
                }
                catch { } // Ignorer les propriétés qui lèvent des exceptions
            }

            // 2. Ensuite les CHAMPS (pour classes [Serializable] Unity)
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                var value = field.GetValue(obj);
                if (value != null)
                {
                    var name = field.Name;
                    if (name.StartsWith("@")) name = name.Substring(1);
                    // Éviter les doublons
                    if (!serializedNames.Contains(name))
                    {
                        pairs.Add($"\"{EscapeString(name)}\":{ToJson(value)}");
                    }
                }
            }

            return "{" + string.Join(",", pairs) + "}";
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Simple JSON parser that supports Dictionary<string, object>
    /// </summary>
    public class SimpleJsonParser
    {
        private readonly string _json;
        private int _pos;

        public SimpleJsonParser(string json)
        {
            _json = json;
            _pos = 0;
        }

        public Dictionary<string, object> ParseObject()
        {
            SkipWhitespace();
            if (_pos >= _json.Length || _json[_pos] != '{') return null;
            _pos++; // skip '{'

            var result = new Dictionary<string, object>();

            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == '}')
            {
                _pos++;
                return result;
            }

            while (_pos < _json.Length)
            {
                SkipWhitespace();

                // Parse key
                var key = ParseString();
                if (key == null) break;

                SkipWhitespace();
                if (_pos >= _json.Length || _json[_pos] != ':') break;
                _pos++; // skip ':'

                SkipWhitespace();
                var value = ParseValue();
                result[key] = value;

                SkipWhitespace();
                if (_pos >= _json.Length) break;

                if (_json[_pos] == '}')
                {
                    _pos++;
                    break;
                }

                if (_json[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                break;
            }

            return result;
        }

        private object ParseValue()
        {
            SkipWhitespace();
            if (_pos >= _json.Length) return null;

            char c = _json[_pos];

            if (c == '"') return ParseString();
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == 't' || c == 'f') return ParseBool();
            if (c == 'n') return ParseNull();
            if (c == '-' || char.IsDigit(c)) return ParseNumber();

            return null;
        }

        private string ParseString()
        {
            if (_pos >= _json.Length || _json[_pos] != '"') return null;
            _pos++; // skip opening quote

            var sb = new System.Text.StringBuilder();
            while (_pos < _json.Length)
            {
                char c = _json[_pos];
                if (c == '"')
                {
                    _pos++;
                    return sb.ToString();
                }
                if (c == '\\' && _pos + 1 < _json.Length)
                {
                    _pos++;
                    char escaped = _json[_pos];
                    switch (escaped)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(escaped); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                _pos++;
            }
            return sb.ToString();
        }

        private List<object> ParseArray()
        {
            if (_pos >= _json.Length || _json[_pos] != '[') return null;
            _pos++; // skip '['

            var result = new List<object>();

            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == ']')
            {
                _pos++;
                return result;
            }

            while (_pos < _json.Length)
            {
                SkipWhitespace();
                var value = ParseValue();
                result.Add(value);

                SkipWhitespace();
                if (_pos >= _json.Length) break;

                if (_json[_pos] == ']')
                {
                    _pos++;
                    break;
                }

                if (_json[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                break;
            }

            return result;
        }

        private object ParseNumber()
        {
            int start = _pos;
            if (_json[_pos] == '-') _pos++;

            while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;

            bool isFloat = false;
            if (_pos < _json.Length && _json[_pos] == '.')
            {
                isFloat = true;
                _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
            }

            if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
            {
                isFloat = true;
                _pos++;
                if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
            }

            string numStr = _json.Substring(start, _pos - start);

            if (isFloat)
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (int.TryParse(numStr, out int i)) return i;
                if (long.TryParse(numStr, out long l)) return l;
            }

            return 0;
        }

        private bool ParseBool()
        {
            if (_json.Substring(_pos).StartsWith("true"))
            {
                _pos += 4;
                return true;
            }
            if (_json.Substring(_pos).StartsWith("false"))
            {
                _pos += 5;
                return false;
            }
            return false;
        }

        private object ParseNull()
        {
            if (_json.Substring(_pos).StartsWith("null"))
            {
                _pos += 4;
                return null;
            }
            return null;
        }

        private void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                _pos++;
        }
    }
}
