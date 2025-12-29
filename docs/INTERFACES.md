# Unity MCP Plugin - Interface Specifications

**Version:** 1.0.0
**Date:** 2025-12-27

---

## C# Interfaces

### IToolHandler

```csharp
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace McpUnity.Core
{
    /// <summary>
    /// Interface for implementing MCP tools that execute Unity operations.
    /// Each tool handler is responsible for a single tool definition.
    /// </summary>
    public interface IToolHandler
    {
        /// <summary>
        /// Unique tool name used in MCP tool calls.
        /// Must be lowercase with underscores (e.g., "create_gameobject").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Human-readable description of what the tool does.
        /// Displayed to the LLM for tool selection.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// JSON Schema defining the tool's input parameters.
        /// Must be a valid JSON Schema object.
        /// </summary>
        JObject InputSchema { get; }

        /// <summary>
        /// Executes the tool with the provided arguments.
        /// Must be called on the Unity main thread.
        /// </summary>
        /// <param name="arguments">Tool arguments matching InputSchema</param>
        /// <returns>Tool execution result</returns>
        Task<ToolResult> Execute(JObject arguments);
    }
}
```

### IResourceProvider

```csharp
using System.Threading.Tasks;

namespace McpUnity.Core
{
    /// <summary>
    /// Interface for implementing MCP resources that expose Unity data.
    /// Each provider handles one or more related resource URIs.
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// Base URI pattern for this resource.
        /// Can include path parameters (e.g., "unity://gameobject/{id}").
        /// </summary>
        string UriPattern { get; }

        /// <summary>
        /// Human-readable resource name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what data this resource provides.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// MIME type of the resource content.
        /// Typically "application/json" for structured data.
        /// </summary>
        string MimeType { get; }

        /// <summary>
        /// Checks if this provider can handle the given URI.
        /// </summary>
        /// <param name="uri">Resource URI to check</param>
        /// <returns>True if this provider handles the URI</returns>
        bool CanHandle(string uri);

        /// <summary>
        /// Reads the resource data for the given URI.
        /// Must be called on the Unity main thread.
        /// </summary>
        /// <param name="uri">Full resource URI including parameters</param>
        /// <returns>Resource contents</returns>
        Task<ResourceContents> Read(string uri);
    }
}
```

### IJsonRpcHandler

```csharp
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace McpUnity.Core
{
    /// <summary>
    /// Interface for JSON-RPC method handlers.
    /// Internal interface for extending protocol support.
    /// </summary>
    public interface IJsonRpcHandler
    {
        /// <summary>
        /// JSON-RPC method name this handler processes.
        /// </summary>
        string Method { get; }

        /// <summary>
        /// Handles the JSON-RPC method call.
        /// </summary>
        /// <param name="params">Method parameters</param>
        /// <returns>Result object to include in response</returns>
        Task<object> Handle(JObject @params);
    }
}
```

---

## Data Transfer Objects

### JSON-RPC Messages

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McpUnity.Models
{
    /// <summary>
    /// JSON-RPC 2.0 Request object.
    /// </summary>
    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JObject Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 Response object.
    /// </summary>
    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 Error object.
    /// </summary>
    public class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }
}
```

### Tool Result Types

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Result of a tool execution.
    /// </summary>
    public class ToolResult
    {
        [JsonProperty("content")]
        public List<ContentItem> Content { get; set; } = new List<ContentItem>();

        [JsonProperty("isError")]
        public bool IsError { get; set; }

        public static ToolResult Success(string text)
        {
            return new ToolResult
            {
                Content = new List<ContentItem>
                {
                    new ContentItem { Type = "text", Text = text }
                },
                IsError = false
            };
        }

        public static ToolResult Error(string message)
        {
            return new ToolResult
            {
                Content = new List<ContentItem>
                {
                    new ContentItem { Type = "text", Text = message }
                },
                IsError = true
            };
        }
    }

    /// <summary>
    /// Content item in tool result or resource.
    /// </summary>
    public class ContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }
}
```

### Resource Types

```csharp
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Resource definition for MCP resources/list.
    /// </summary>
    public class ResourceDefinition
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }
    }

    /// <summary>
    /// Resource contents for MCP resources/read.
    /// </summary>
    public class ResourceContents
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("blob", NullValueHandling = NullValueHandling.Ignore)]
        public string Blob { get; set; }
    }
}
```

### Tool Definition

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McpUnity.Models
{
    /// <summary>
    /// Tool definition for MCP tools/list.
    /// </summary>
    public class ToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }
    }
}
```

---

## Unity Data Models

### GameObject Serialization

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Serializable representation of a Unity GameObject.
    /// </summary>
    public class GameObjectData
    {
        [JsonProperty("instanceId")]
        public int InstanceId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("layer")]
        public int Layer { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("isStatic")]
        public bool IsStatic { get; set; }

        [JsonProperty("transform")]
        public TransformData Transform { get; set; }

        [JsonProperty("components")]
        public List<string> ComponentTypes { get; set; }

        [JsonProperty("children", NullValueHandling = NullValueHandling.Ignore)]
        public List<GameObjectData> Children { get; set; }
    }

    /// <summary>
    /// Detailed GameObject with full component data.
    /// </summary>
    public class GameObjectDetailData : GameObjectData
    {
        [JsonProperty("components")]
        public new List<ComponentData> Components { get; set; }

        [JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
        public GameObjectRefData Parent { get; set; }
    }

    /// <summary>
    /// Lightweight GameObject reference.
    /// </summary>
    public class GameObjectRefData
    {
        [JsonProperty("instanceId")]
        public int InstanceId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }
    }
}
```

### Transform Serialization

```csharp
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Serializable representation of a Unity Transform.
    /// </summary>
    public class TransformData
    {
        [JsonProperty("position")]
        public float[] Position { get; set; }

        [JsonProperty("rotation")]
        public float[] Rotation { get; set; }

        [JsonProperty("scale")]
        public float[] Scale { get; set; }

        [JsonProperty("localPosition")]
        public float[] LocalPosition { get; set; }

        [JsonProperty("localRotation")]
        public float[] LocalRotation { get; set; }

        [JsonProperty("localScale")]
        public float[] LocalScale { get; set; }

        public static TransformData FromTransform(UnityEngine.Transform t)
        {
            return new TransformData
            {
                Position = new[] { t.position.x, t.position.y, t.position.z },
                Rotation = new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z },
                Scale = new[] { t.lossyScale.x, t.lossyScale.y, t.lossyScale.z },
                LocalPosition = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                LocalRotation = new[] { t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z },
                LocalScale = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
            };
        }
    }
}
```

### Component Serialization

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Serializable representation of a Unity Component.
    /// </summary>
    public class ComponentData
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("instanceId")]
        public int InstanceId { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, object> Properties { get; set; }
    }
}
```

### Scene Data

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Hierarchy resource response.
    /// </summary>
    public class HierarchyData
    {
        [JsonProperty("sceneName")]
        public string SceneName { get; set; }

        [JsonProperty("scenePath")]
        public string ScenePath { get; set; }

        [JsonProperty("roots")]
        public List<GameObjectData> Roots { get; set; }
    }

    /// <summary>
    /// Scene resource response.
    /// </summary>
    public class SceneData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("buildIndex")]
        public int BuildIndex { get; set; }

        [JsonProperty("isLoaded")]
        public bool IsLoaded { get; set; }

        [JsonProperty("isDirty")]
        public bool IsDirty { get; set; }

        [JsonProperty("rootCount")]
        public int RootCount { get; set; }

        [JsonProperty("isSubScene")]
        public bool IsSubScene { get; set; }
    }
}
```

### Asset Data

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Assets resource response.
    /// </summary>
    public class AssetsData
    {
        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        [JsonProperty("assetsPath")]
        public string AssetsPath { get; set; }

        [JsonProperty("assets")]
        public List<AssetData> Assets { get; set; }

        [JsonProperty("folders")]
        public List<string> Folders { get; set; }
    }

    /// <summary>
    /// Single asset entry.
    /// </summary>
    public class AssetData
    {
        [JsonProperty("guid")]
        public string Guid { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("labels")]
        public string[] Labels { get; set; }
    }
}
```

### Console Data

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace McpUnity.Models
{
    /// <summary>
    /// Console resource response.
    /// </summary>
    public class ConsoleData
    {
        [JsonProperty("entries")]
        public List<ConsoleLogEntry> Entries { get; set; }

        [JsonProperty("counts")]
        public ConsoleCounts Counts { get; set; }
    }

    /// <summary>
    /// Single console log entry.
    /// </summary>
    public class ConsoleLogEntry
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("stackTrace")]
        public string StackTrace { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// Console log type counts.
    /// </summary>
    public class ConsoleCounts
    {
        [JsonProperty("log")]
        public int Log { get; set; }

        [JsonProperty("warning")]
        public int Warning { get; set; }

        [JsonProperty("error")]
        public int Error { get; set; }
    }
}
```

---

## TypeScript Interfaces

### Unity Connection Types

```typescript
// types/protocol.ts

export interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: number | string;
  method: string;
  params?: unknown;
}

export interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: number | string;
  result?: unknown;
  error?: JsonRpcError;
}

export interface JsonRpcError {
  code: number;
  message: string;
  data?: unknown;
}

export interface ToolResult {
  content: ContentItem[];
  isError?: boolean;
}

export interface ContentItem {
  type: "text" | "image" | "resource";
  text?: string;
  data?: string;
  mimeType?: string;
}

export interface ResourceContents {
  uri: string;
  mimeType: string;
  text?: string;
  blob?: string;
}
```

### Unity Data Types

```typescript
// types/unity.ts

export interface GameObjectData {
  instanceId: number;
  name: string;
  path: string;
  tag: string;
  layer: number;
  isActive: boolean;
  isStatic: boolean;
  transform: TransformData;
  components: string[] | ComponentData[];
  children?: GameObjectData[];
}

export interface TransformData {
  position: [number, number, number];
  rotation: [number, number, number];
  scale: [number, number, number];
  localPosition: [number, number, number];
  localRotation: [number, number, number];
  localScale: [number, number, number];
}

export interface ComponentData {
  type: string;
  instanceId: number;
  enabled: boolean;
  properties: Record<string, unknown>;
}

export interface HierarchyData {
  sceneName: string;
  scenePath: string;
  roots: GameObjectData[];
}

export interface SceneData {
  name: string;
  path: string;
  buildIndex: number;
  isLoaded: boolean;
  isDirty: boolean;
  rootCount: number;
  isSubScene: boolean;
}

export interface AssetData {
  guid: string;
  path: string;
  name: string;
  type: string;
  labels: string[];
}

export interface AssetsData {
  projectPath: string;
  assetsPath: string;
  assets: AssetData[];
  folders: string[];
}

export interface ConsoleLogEntry {
  message: string;
  stackTrace: string;
  type: "Log" | "Warning" | "Error" | "Assert" | "Exception";
  timestamp: string;
}

export interface ConsoleData {
  entries: ConsoleLogEntry[];
  counts: {
    log: number;
    warning: number;
    error: number;
  };
}
```

### Tool Input Schemas

```typescript
// types/tools.ts

export interface CreateGameObjectInput {
  name: string;
  position?: [number, number, number];
  rotation?: [number, number, number];
  scale?: [number, number, number];
  parent?: number;
  primitive?: "Cube" | "Sphere" | "Capsule" | "Cylinder" | "Plane" | "Quad";
}

export interface UpdateTransformInput {
  instanceId: number;
  position?: [number, number, number];
  rotation?: [number, number, number];
  scale?: [number, number, number];
  local?: boolean;
}

export interface DeleteGameObjectInput {
  instanceId: number;
}

export interface FindGameObjectInput {
  name?: string;
  tag?: string;
  component?: string;
  path?: string;
}

export interface AddComponentInput {
  instanceId: number;
  componentType: string;
  properties?: Record<string, unknown>;
}

export interface UpdateComponentInput {
  instanceId: number;
  properties: Record<string, unknown>;
}

export interface RemoveComponentInput {
  instanceId: number;
}

export interface LoadSceneInput {
  path: string;
  mode?: "Single" | "Additive";
}

export interface SaveSceneInput {
  path?: string;
}

export interface NewSceneInput {
  setup?: "EmptyScene" | "DefaultGameObjects";
}

export interface CreatePrefabInput {
  instanceId: number;
  path: string;
}

export interface InstantiatePrefabInput {
  path: string;
  position?: [number, number, number];
  rotation?: [number, number, number];
  parent?: number;
}

export interface ImportAssetInput {
  sourcePath: string;
  destinationPath: string;
}

export interface ExecuteMenuItemInput {
  menuPath: string;
}

export interface RunTestsInput {
  mode?: "EditMode" | "PlayMode" | "All";
  filter?: string;
}
```

---

## Error Codes Reference

```typescript
// Error codes following JSON-RPC 2.0 specification
export const ErrorCodes = {
  // JSON-RPC standard errors
  PARSE_ERROR: -32700,
  INVALID_REQUEST: -32600,
  METHOD_NOT_FOUND: -32601,
  INVALID_PARAMS: -32602,
  INTERNAL_ERROR: -32603,

  // Unity MCP custom errors (-32000 to -32099)
  UNITY_ERROR: -32000,
  TOOL_NOT_FOUND: -32001,
  RESOURCE_NOT_FOUND: -32002,
  EXECUTION_FAILED: -32003,
  INVALID_INSTANCE_ID: -32004,
  ASSET_NOT_FOUND: -32005,
  SCENE_NOT_FOUND: -32006,
  COMPONENT_NOT_FOUND: -32007,
  PERMISSION_DENIED: -32008,
  NOT_IN_EDIT_MODE: -32009,
} as const;

export type ErrorCode = typeof ErrorCodes[keyof typeof ErrorCodes];
```

---

*Interface specifications for Unity MCP Plugin. Last updated: 2025-12-27*
