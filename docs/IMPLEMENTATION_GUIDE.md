# Unity MCP Plugin - Implementation Guide

**Version:** 1.0.0
**Date:** 2025-12-27

---

## Implementation Phases

### Phase 1: Core Infrastructure (Priority: Critical)

1. **McpUnityServer.cs** - WebSocket server
2. **McpJsonRpc.cs** - JSON-RPC handler
3. **McpMainThreadDispatcher.cs** - Thread safety
4. **McpToolRegistry.cs** - Tool management
5. **McpResourceRegistry.cs** - Resource management

### Phase 2: Basic Tools (Priority: High)

1. **GameObjectTools.cs** - create, update, delete, find
2. **ComponentTools.cs** - add, update, remove

### Phase 3: Basic Resources (Priority: High)

1. **HierarchyResource.cs** - Scene hierarchy
2. **GameObjectResource.cs** - GameObject details

### Phase 4: Node.js Bridge (Priority: High)

1. **index.ts** - Entry point
2. **UnityConnection.ts** - WebSocket client
3. **McpBridge.ts** - Protocol bridge

### Phase 5: Extended Features (Priority: Medium)

1. **SceneTools.cs** - Scene operations
2. **AssetTools.cs** - Asset management
3. **EditorTools.cs** - Editor utilities
4. **ConsoleResource.cs** - Log access
5. **SceneResource.cs** - Scene metadata
6. **AssetResource.cs** - Asset listing

### Phase 6: UI and Polish (Priority: Low)

1. **McpEditorWindow.cs** - Configuration UI
2. Error handling improvements
3. Performance optimization
4. Documentation

---

## Implementation Details

### McpUnityServer.cs

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Core
{
    [InitializeOnLoad]
    public class McpUnityServer : IDisposable
    {
        private static McpUnityServer _instance;
        public static McpUnityServer Instance => _instance ??= new McpUnityServer();

        // Configuration
        private const int DEFAULT_PORT = 8090;
        private int _port = DEFAULT_PORT;
        public int Port
        {
            get => _port;
            set
            {
                if (IsRunning)
                    throw new InvalidOperationException("Cannot change port while server is running");
                _port = value;
            }
        }

        public bool IsRunning { get; private set; }

        // Events
        public event Action<string> OnLog;
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action<bool> OnServerStateChanged;

        // Internal state
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private McpJsonRpc _jsonRpc;

        static McpUnityServer()
        {
            // Restore server state on domain reload
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool("McpUnity_AutoStart", false))
                {
                    Instance.Start();
                }
            };
        }

        public McpUnityServer()
        {
            _jsonRpc = new McpJsonRpc();
            RegisterDefaultHandlers();
        }

        private void RegisterDefaultHandlers()
        {
            // Register built-in tools
            _jsonRpc.ToolRegistry.RegisterTools(new IToolHandler[]
            {
                new Tools.CreateGameObjectTool(),
                new Tools.UpdateTransformTool(),
                new Tools.DeleteGameObjectTool(),
                new Tools.FindGameObjectTool(),
                new Tools.AddComponentTool(),
                new Tools.UpdateComponentTool(),
                new Tools.RemoveComponentTool(),
            });

            // Register built-in resources
            _jsonRpc.ResourceRegistry.RegisterResources(new IResourceProvider[]
            {
                new Resources.HierarchyResource(),
                new Resources.GameObjectResource(),
                new Resources.ConsoleResource(),
                new Resources.SceneResource(),
            });
        }

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _httpListener.Start();

                IsRunning = true;
                OnServerStateChanged?.Invoke(true);
                Log($"MCP Server started on ws://127.0.0.1:{_port}");

                _ = AcceptClientsAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Log($"Failed to start server: {ex.Message}");
                IsRunning = false;
                OnServerStateChanged?.Invoke(false);
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();

            foreach (var client in _clients.Values)
            {
                try
                {
                    client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None)
                        .Wait(1000);
                }
                catch { }
            }
            _clients.Clear();

            _httpListener?.Stop();
            _httpListener?.Close();
            _httpListener = null;

            IsRunning = false;
            OnServerStateChanged?.Invoke(false);
            Log("MCP Server stopped");
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        var clientId = Guid.NewGuid().ToString();
                        _clients[clientId] = wsContext.WebSocket;

                        Log($"Client connected: {clientId}");
                        OnClientConnected?.Invoke(clientId);

                        _ = HandleClientAsync(clientId, wsContext.WebSocket, ct);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Log($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(string clientId, WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];

            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log($"Received: {message}");

                        // Process on main thread
                        var response = await McpMainThreadDispatcher.EnqueueAsync(
                            () => _jsonRpc.ProcessMessage(message));

                        if (!string.IsNullOrEmpty(response))
                        {
                            Log($"Sending: {response}");
                            var bytes = Encoding.UTF8.GetBytes(response);
                            await ws.SendAsync(
                                new ArraySegment<byte>(bytes),
                                WebSocketMessageType.Text,
                                true,
                                ct);
                        }
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log($"Client error: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                Log($"Client disconnected: {clientId}");
                OnClientDisconnected?.Invoke(clientId);
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[McpUnity] {message}");
            Debug.Log($"[McpUnity] {message}");
        }

        public void Dispose()
        {
            Stop();
            _instance = null;
        }
    }
}
```

### McpMainThreadDispatcher.cs

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;

namespace McpUnity.Core
{
    [InitializeOnLoad]
    public static class McpMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _executionQueue = new();
        private static readonly ConcurrentQueue<(TaskCompletionSource<object>, Func<object>)> _asyncQueue = new();

        static McpMainThreadDispatcher()
        {
            EditorApplication.update += Update;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _executionQueue.Enqueue(action);
        }

        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<object>();
            _asyncQueue.Enqueue((tcs, () => func()));
            return tcs.Task.ContinueWith(t => (T)t.Result);
        }

        public static Task EnqueueAsync(Action action)
        {
            var tcs = new TaskCompletionSource<object>();
            _asyncQueue.Enqueue((tcs, () => { action(); return null; }));
            return tcs.Task;
        }

        private static void Update()
        {
            // Process synchronous actions
            while (_executionQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }

            // Process async actions
            while (_asyncQueue.TryDequeue(out var item))
            {
                try
                {
                    var result = item.Item2();
                    item.Item1.SetResult(result);
                }
                catch (Exception ex)
                {
                    item.Item1.SetException(ex);
                }
            }
        }
    }
}
```

### McpJsonRpc.cs

```csharp
using System;
using System.Threading.Tasks;
using McpUnity.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McpUnity.Core
{
    public class McpJsonRpc
    {
        public McpToolRegistry ToolRegistry { get; } = new();
        public McpResourceRegistry ResourceRegistry { get; } = new();

        private const string MCP_VERSION = "2024-11-05";

        public string ProcessMessage(string jsonMessage)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<JsonRpcRequest>(jsonMessage);

                if (request == null || request.JsonRpc != "2.0")
                {
                    return CreateErrorResponse(null, -32600, "Invalid Request");
                }

                var result = RouteMethod(request.Method, request.Params);
                return CreateSuccessResponse(request.Id, result);
            }
            catch (JsonException)
            {
                return CreateErrorResponse(null, -32700, "Parse error");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(null, -32603, ex.Message);
            }
        }

        private object RouteMethod(string method, JObject @params)
        {
            return method switch
            {
                "initialize" => HandleInitialize(@params),
                "ping" => HandlePing(),
                "tools/list" => HandleToolsList(),
                "tools/call" => HandleToolsCall(@params),
                "resources/list" => HandleResourcesList(),
                "resources/read" => HandleResourcesRead(@params),
                _ => throw new Exception($"Method not found: {method}")
            };
        }

        private object HandleInitialize(JObject @params)
        {
            return new
            {
                protocolVersion = MCP_VERSION,
                capabilities = new
                {
                    tools = new { },
                    resources = new { }
                },
                serverInfo = new
                {
                    name = "unity-mcp",
                    version = "1.0.0"
                }
            };
        }

        private object HandlePing()
        {
            return new { };
        }

        private object HandleToolsList()
        {
            return new
            {
                tools = ToolRegistry.GetAllTools()
            };
        }

        private object HandleToolsCall(JObject @params)
        {
            var name = @params["name"]?.ToString();
            var arguments = @params["arguments"] as JObject ?? new JObject();

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Tool name required");
            }

            var result = ToolRegistry.ExecuteTool(name, arguments);
            return result;
        }

        private object HandleResourcesList()
        {
            return new
            {
                resources = ResourceRegistry.GetAllResources()
            };
        }

        private object HandleResourcesRead(JObject @params)
        {
            var uri = @params["uri"]?.ToString();

            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("Resource URI required");
            }

            var contents = ResourceRegistry.ReadResource(uri);
            return new
            {
                contents = new[] { contents }
            };
        }

        private string CreateSuccessResponse(object id, object result)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Result = result
            };
            return JsonConvert.SerializeObject(response);
        }

        private string CreateErrorResponse(object id, int code, string message)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message
                }
            };
            return JsonConvert.SerializeObject(response);
        }
    }
}
```

### Example Tool: CreateGameObjectTool.cs

```csharp
using System.Threading.Tasks;
using McpUnity.Core;
using McpUnity.Models;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tools
{
    public class CreateGameObjectTool : IToolHandler
    {
        public string Name => "create_gameobject";

        public string Description => "Creates a new GameObject in the active scene";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": {
                    ""type"": ""string"",
                    ""description"": ""Name of the GameObject""
                },
                ""position"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""number"" },
                    ""minItems"": 3,
                    ""maxItems"": 3,
                    ""description"": ""World position [x, y, z]""
                },
                ""rotation"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""number"" },
                    ""minItems"": 3,
                    ""maxItems"": 3,
                    ""description"": ""Euler rotation [x, y, z] in degrees""
                },
                ""scale"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""number"" },
                    ""minItems"": 3,
                    ""maxItems"": 3,
                    ""description"": ""Local scale [x, y, z]""
                },
                ""parent"": {
                    ""type"": ""integer"",
                    ""description"": ""Instance ID of parent GameObject""
                },
                ""primitive"": {
                    ""type"": ""string"",
                    ""enum"": [""Cube"", ""Sphere"", ""Capsule"", ""Cylinder"", ""Plane"", ""Quad""],
                    ""description"": ""Create as primitive type""
                }
            },
            ""required"": [""name""]
        }");

        public Task<ToolResult> Execute(JObject arguments)
        {
            try
            {
                var name = arguments["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    return Task.FromResult(ToolResult.Error("Name is required"));
                }

                GameObject go;

                // Create primitive or empty
                var primitiveStr = arguments["primitive"]?.ToString();
                if (!string.IsNullOrEmpty(primitiveStr) &&
                    System.Enum.TryParse<PrimitiveType>(primitiveStr, out var primitiveType))
                {
                    go = GameObject.CreatePrimitive(primitiveType);
                    go.name = name;
                }
                else
                {
                    go = new GameObject(name);
                }

                // Set parent
                var parentId = arguments["parent"]?.Value<int>();
                if (parentId.HasValue)
                {
                    var parent = EditorUtility.InstanceIDToObject(parentId.Value) as GameObject;
                    if (parent != null)
                    {
                        go.transform.SetParent(parent.transform, false);
                    }
                }

                // Set transform
                var position = ParseVector3(arguments["position"]);
                if (position.HasValue)
                {
                    go.transform.position = position.Value;
                }

                var rotation = ParseVector3(arguments["rotation"]);
                if (rotation.HasValue)
                {
                    go.transform.eulerAngles = rotation.Value;
                }

                var scale = ParseVector3(arguments["scale"]);
                if (scale.HasValue)
                {
                    go.transform.localScale = scale.Value;
                }

                // Register undo
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

                var result = $"Created GameObject '{name}' (ID: {go.GetInstanceID()}) at {go.transform.position}";
                return Task.FromResult(ToolResult.Success(result));
            }
            catch (System.Exception ex)
            {
                return Task.FromResult(ToolResult.Error($"Failed to create GameObject: {ex.Message}"));
            }
        }

        private Vector3? ParseVector3(JToken token)
        {
            if (token is JArray arr && arr.Count == 3)
            {
                return new Vector3(
                    arr[0].Value<float>(),
                    arr[1].Value<float>(),
                    arr[2].Value<float>()
                );
            }
            return null;
        }
    }
}
```

### Example Resource: HierarchyResource.cs

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using McpUnity.Core;
using McpUnity.Models;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Resources
{
    public class HierarchyResource : IResourceProvider
    {
        public string UriPattern => "unity://hierarchy";
        public string Name => "Scene Hierarchy";
        public string Description => "Tree structure of all GameObjects in the active scene";
        public string MimeType => "application/json";

        public bool CanHandle(string uri) => uri == "unity://hierarchy";

        public Task<ResourceContents> Read(string uri)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var data = new HierarchyData
            {
                SceneName = scene.name,
                ScenePath = scene.path,
                Roots = new List<GameObjectData>()
            };

            foreach (var root in roots)
            {
                data.Roots.Add(SerializeGameObject(root, true));
            }

            return Task.FromResult(new ResourceContents
            {
                Uri = uri,
                MimeType = MimeType,
                Text = JsonConvert.SerializeObject(data, Formatting.Indented)
            });
        }

        private GameObjectData SerializeGameObject(GameObject go, bool includeChildren)
        {
            var data = new GameObjectData
            {
                InstanceId = go.GetInstanceID(),
                Name = go.name,
                Path = GetPath(go),
                Tag = go.tag,
                Layer = go.layer,
                IsActive = go.activeSelf,
                IsStatic = go.isStatic,
                Transform = TransformData.FromTransform(go.transform),
                ComponentTypes = new List<string>()
            };

            foreach (var component in go.GetComponents<Component>())
            {
                if (component != null)
                {
                    data.ComponentTypes.Add(component.GetType().Name);
                }
            }

            if (includeChildren && go.transform.childCount > 0)
            {
                data.Children = new List<GameObjectData>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i).gameObject;
                    data.Children.Add(SerializeGameObject(child, true));
                }
            }

            return data;
        }

        private string GetPath(GameObject go)
        {
            var path = "/" + go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
```

---

## Node.js Bridge Implementation

### package.json

```json
{
  "name": "unity-mcp-bridge",
  "version": "1.0.0",
  "description": "MCP bridge for Unity Editor",
  "type": "module",
  "main": "dist/index.js",
  "scripts": {
    "build": "tsc",
    "start": "node dist/index.js",
    "dev": "tsx src/index.ts"
  },
  "dependencies": {
    "@modelcontextprotocol/sdk": "^1.0.0",
    "ws": "^8.14.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "@types/ws": "^8.5.0",
    "typescript": "^5.3.0",
    "tsx": "^4.0.0"
  }
}
```

### tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "declaration": true
  },
  "include": ["src/**/*"]
}
```

### src/index.ts

```typescript
#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { McpBridge } from "./McpBridge.js";

const server = new Server(
  {
    name: "unity-mcp",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
      resources: {},
    },
  }
);

const bridge = new McpBridge(server);

async function main() {
  const port = parseInt(process.env.UNITY_MCP_PORT || "8090", 10);

  console.error(`[unity-mcp] Connecting to Unity on port ${port}...`);

  try {
    await bridge.connect(port);
    console.error("[unity-mcp] Connected to Unity");
  } catch (error) {
    console.error(`[unity-mcp] Failed to connect to Unity: ${error}`);
    console.error("[unity-mcp] Make sure the Unity MCP server is running");
    process.exit(1);
  }

  const transport = new StdioServerTransport();
  await server.connect(transport);

  console.error("[unity-mcp] MCP server started");
}

process.on("SIGINT", async () => {
  await bridge.disconnect();
  process.exit(0);
});

main().catch((error) => {
  console.error(`[unity-mcp] Fatal error: ${error}`);
  process.exit(1);
});
```

### src/McpBridge.ts

```typescript
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { UnityConnection } from "./UnityConnection.js";

export class McpBridge {
  private server: Server;
  private unity: UnityConnection;

  constructor(server: Server) {
    this.server = server;
    this.unity = new UnityConnection();
    this.setupHandlers();
  }

  async connect(port: number = 8090): Promise<void> {
    await this.unity.connect(`ws://127.0.0.1:${port}`);
  }

  async disconnect(): Promise<void> {
    await this.unity.disconnect();
  }

  private setupHandlers(): void {
    // List tools - forward to Unity
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      const response = await this.unity.call("tools/list");
      return response as { tools: unknown[] };
    });

    // Call tool - forward to Unity
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const response = await this.unity.call("tools/call", {
        name: request.params.name,
        arguments: request.params.arguments,
      });
      return response as { content: unknown[] };
    });

    // List resources - forward to Unity
    this.server.setRequestHandler(ListResourcesRequestSchema, async () => {
      const response = await this.unity.call("resources/list");
      return response as { resources: unknown[] };
    });

    // Read resource - forward to Unity
    this.server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
      const response = await this.unity.call("resources/read", {
        uri: request.params.uri,
      });
      return response as { contents: unknown[] };
    });
  }
}
```

### src/UnityConnection.ts

```typescript
import WebSocket from "ws";

interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: number;
  method: string;
  params?: unknown;
}

interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: number;
  result?: unknown;
  error?: {
    code: number;
    message: string;
    data?: unknown;
  };
}

export class UnityConnection {
  private ws: WebSocket | null = null;
  private requestId = 0;
  private pendingRequests = new Map<
    number,
    {
      resolve: (value: unknown) => void;
      reject: (error: Error) => void;
    }
  >();

  async connect(url: string): Promise<void> {
    return new Promise((resolve, reject) => {
      this.ws = new WebSocket(url);

      this.ws.on("open", () => {
        resolve();
      });

      this.ws.on("error", (error) => {
        reject(error);
      });

      this.ws.on("message", (data) => {
        this.handleMessage(data);
      });

      this.ws.on("close", () => {
        console.error("[unity-mcp] Connection to Unity closed");
      });

      // Timeout after 5 seconds
      setTimeout(() => {
        if (this.ws?.readyState !== WebSocket.OPEN) {
          reject(new Error("Connection timeout"));
        }
      }, 5000);
    });
  }

  async disconnect(): Promise<void> {
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  async call(method: string, params?: unknown): Promise<unknown> {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
      throw new Error("Not connected to Unity");
    }

    const id = ++this.requestId;
    const request: JsonRpcRequest = {
      jsonrpc: "2.0",
      id,
      method,
      params: params as Record<string, unknown> | undefined,
    };

    return new Promise((resolve, reject) => {
      this.pendingRequests.set(id, { resolve, reject });

      this.ws!.send(JSON.stringify(request));

      // Timeout after 30 seconds
      setTimeout(() => {
        if (this.pendingRequests.has(id)) {
          this.pendingRequests.delete(id);
          reject(new Error("Request timeout"));
        }
      }, 30000);
    });
  }

  private handleMessage(data: WebSocket.RawData): void {
    try {
      const response: JsonRpcResponse = JSON.parse(data.toString());

      const pending = this.pendingRequests.get(response.id as number);
      if (pending) {
        this.pendingRequests.delete(response.id as number);

        if (response.error) {
          pending.reject(
            new Error(`${response.error.message} (${response.error.code})`)
          );
        } else {
          pending.resolve(response.result);
        }
      }
    } catch (error) {
      console.error("[unity-mcp] Failed to parse response:", error);
    }
  }
}
```

---

## Testing Strategy

### Unit Tests (C#)

```csharp
using NUnit.Framework;
using McpUnity.Core;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;

[TestFixture]
public class CreateGameObjectToolTests
{
    [Test]
    public async Task Execute_WithValidName_CreatesGameObject()
    {
        var tool = new CreateGameObjectTool();
        var args = JObject.Parse(@"{ ""name"": ""TestObject"" }");

        var result = await tool.Execute(args);

        Assert.IsFalse(result.IsError);
        Assert.IsNotNull(GameObject.Find("TestObject"));
    }

    [Test]
    public async Task Execute_WithPrimitive_CreatesPrimitive()
    {
        var tool = new CreateGameObjectTool();
        var args = JObject.Parse(@"{ ""name"": ""TestCube"", ""primitive"": ""Cube"" }");

        var result = await tool.Execute(args);

        Assert.IsFalse(result.IsError);
        var go = GameObject.Find("TestCube");
        Assert.IsNotNull(go.GetComponent<MeshFilter>());
    }

    [TearDown]
    public void TearDown()
    {
        var testObjects = new[] { "TestObject", "TestCube" };
        foreach (var name in testObjects)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}
```

### Integration Tests (TypeScript)

```typescript
// tests/integration.test.ts
import { UnityConnection } from "../src/UnityConnection.js";

describe("Unity MCP Integration", () => {
  let unity: UnityConnection;

  beforeAll(async () => {
    unity = new UnityConnection();
    await unity.connect("ws://127.0.0.1:8090");
  });

  afterAll(async () => {
    await unity.disconnect();
  });

  test("can list tools", async () => {
    const result = await unity.call("tools/list");
    expect(result).toHaveProperty("tools");
    expect(Array.isArray((result as any).tools)).toBe(true);
  });

  test("can create gameobject", async () => {
    const result = await unity.call("tools/call", {
      name: "create_gameobject",
      arguments: { name: "IntegrationTestObject" },
    });
    expect((result as any).content[0].text).toContain("Created");
  });

  test("can read hierarchy", async () => {
    const result = await unity.call("resources/read", {
      uri: "unity://hierarchy",
    });
    expect((result as any).contents[0]).toHaveProperty("text");
  });
});
```

---

## Claude Configuration

### settings.json

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["/path/to/Assets/Server~/dist/index.js"],
      "env": {
        "UNITY_MCP_PORT": "8090"
      }
    }
  }
}
```

### Alternative: npx Configuration

```json
{
  "mcpServers": {
    "unity": {
      "command": "npx",
      "args": ["-y", "unity-mcp-bridge@latest"],
      "env": {
        "UNITY_MCP_PORT": "8090"
      }
    }
  }
}
```

---

*Implementation guide for Unity MCP Plugin. Last updated: 2025-12-27*
