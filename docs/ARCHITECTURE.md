# Unity MCP Plugin - System Architecture Document

**Version:** 1.0.0
**Date:** 2025-12-27
**Author:** System Architect
**MCP Spec Version:** 2025-11-25

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [System Overview](#2-system-overview)
3. [Architecture Diagrams](#3-architecture-diagrams)
4. [Component Architecture](#4-component-architecture)
5. [Communication Protocol](#5-communication-protocol)
6. [Data Models](#6-data-models)
7. [Tool Specifications](#7-tool-specifications)
8. [Resource Specifications](#8-resource-specifications)
9. [Security Considerations](#9-security-considerations)
10. [Deployment Architecture](#10-deployment-architecture)
11. [Architecture Decision Records](#11-architecture-decision-records)

---

## 1. Executive Summary

### 1.1 Purpose

This document defines the complete system architecture for a Unity MCP (Model Context Protocol) plugin that enables Claude/Claude Code to interact with the Unity Editor through a standardized protocol interface.

### 1.2 Scope

The plugin provides:
- A WebSocket server running within Unity Editor
- JSON-RPC 2.0 message handling
- Tool execution for Unity operations (create, modify, delete GameObjects)
- Resource exposure for Unity data (hierarchy, assets, console)
- A Node.js bridge implementing the MCP protocol specification

### 1.3 Key Architectural Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Transport | WebSocket | Bidirectional, low latency, Unity .NET support |
| Protocol | JSON-RPC 2.0 | MCP specification requirement |
| Bridge Runtime | Node.js/TypeScript | MCP SDK availability, npm ecosystem |
| Serialization | Newtonsoft.Json | Unity standard, robust JSON handling |

---

## 2. System Overview

### 2.1 High-Level Architecture

```
+------------------+          +------------------+          +------------------+
|                  |   MCP    |                  |  WebSocket|                  |
|  Claude / Claude |<-------->|   Node.js MCP    |<--------->|   Unity Editor   |
|      Code        |  (stdio) |     Bridge       |  (JSON-RPC)|    Plugin        |
|                  |          |                  |          |                  |
+------------------+          +------------------+          +------------------+
                                     |                              |
                              MCP Protocol                   Unity C# API
                              (Tools/Resources)              (Editor/Runtime)
```

### 2.2 Component Responsibilities

| Component | Responsibility |
|-----------|---------------|
| **Claude/Claude Code** | MCP client, sends tool calls, reads resources |
| **Node.js Bridge** | Protocol translation, MCP server implementation |
| **Unity Plugin** | WebSocket server, executes operations, exposes data |

### 2.3 Technology Stack

**Unity Side (C#):**
- Unity 2022.3+ LTS
- .NET Standard 2.1
- Newtonsoft.Json (Unity package)
- System.Net.WebSockets

**Bridge Side (TypeScript):**
- Node.js 18+
- @modelcontextprotocol/sdk
- ws (WebSocket client)
- TypeScript 5.x

---

## 3. Architecture Diagrams

### 3.1 C4 Context Diagram

```
+-----------------------------------------------------------------------+
|                          System Context                                |
+-----------------------------------------------------------------------+
|                                                                       |
|  +-------------+         +---------------------------+                |
|  |             |         |                           |                |
|  |   Developer | uses    |     Unity MCP Plugin      |                |
|  |             |-------->|                           |                |
|  +-------------+         |  +---------------------+  |                |
|        |                 |  | Unity Editor Plugin |  |                |
|        | prompts         |  +---------------------+  |                |
|        v                 |            ^              |                |
|  +-------------+         |            | WebSocket    |                |
|  |             |   MCP   |            v              |                |
|  |   Claude    |<------->|  +---------------------+  |                |
|  |   (LLM)     |         |  |   Node.js Bridge    |  |                |
|  +-------------+         |  +---------------------+  |                |
|                          +---------------------------+                |
|                                                                       |
+-----------------------------------------------------------------------+
```

### 3.2 C4 Container Diagram

```
+-----------------------------------------------------------------------+
|                         Container Diagram                              |
+-----------------------------------------------------------------------+
|                                                                       |
|  Unity Editor Process                    Node.js Process              |
|  +----------------------------+         +------------------------+    |
|  |                            |         |                        |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |  | McpEditorWindow      |  |   WS    |  | MCP Server       |  |    |
|  |  | (Configuration UI)   |  |<------->|  | (stdio transport)|  |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |            |               |         |          |             |    |
|  |            v               |         |          v             |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |  | McpUnityServer       |  |         |  | McpBridge        |  |    |
|  |  | (WebSocket Server)   |  |         |  | (WS Client)      |  |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |            |               |         |          |             |    |
|  |            v               |         |          v             |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |  | McpJsonRpc           |  |         |  | Tool Handlers    |  |    |
|  |  | (Message Handler)    |  |         |  | Resource Handlers|  |    |
|  |  +----------------------+  |         |  +------------------+  |    |
|  |            |               |         |                        |    |
|  |     +------+------+        |         +------------------------+    |
|  |     v             v        |                                       |
|  | +----------+ +----------+  |                                       |
|  | |Tool      | |Resource  |  |                                       |
|  | |Registry  | |Registry  |  |                                       |
|  | +----------+ +----------+  |                                       |
|  |                            |                                       |
|  +----------------------------+                                       |
+-----------------------------------------------------------------------+
```

### 3.3 Sequence Diagram - Tool Execution

```
Developer          Claude           MCP Bridge        Unity Plugin
    |                |                  |                  |
    |  "Create a     |                  |                  |
    |   Player GO"   |                  |                  |
    |--------------->|                  |                  |
    |                |                  |                  |
    |                | tools/call       |                  |
    |                | create_gameobject|                  |
    |                |----------------->|                  |
    |                |                  |                  |
    |                |                  | JSON-RPC Request |
    |                |                  | (WebSocket)      |
    |                |                  |----------------->|
    |                |                  |                  |
    |                |                  |    Execute on    |
    |                |                  |    Main Thread   |
    |                |                  |                  |
    |                |                  | JSON-RPC Response|
    |                |                  |<-----------------|
    |                |                  |                  |
    |                | Tool Result      |                  |
    |                |<-----------------|                  |
    |                |                  |                  |
    | "Created       |                  |                  |
    |  Player at     |                  |                  |
    |  (0,1,0)"      |                  |                  |
    |<---------------|                  |                  |
    |                |                  |                  |
```

### 3.4 Sequence Diagram - Resource Read

```
Developer          Claude           MCP Bridge        Unity Plugin
    |                |                  |                  |
    |  "Show me the  |                  |                  |
    |   hierarchy"   |                  |                  |
    |--------------->|                  |                  |
    |                |                  |                  |
    |                | resources/read   |                  |
    |                | unity://hierarchy|                  |
    |                |----------------->|                  |
    |                |                  |                  |
    |                |                  | JSON-RPC Request |
    |                |                  | (get_resource)   |
    |                |                  |----------------->|
    |                |                  |                  |
    |                |                  |   Serialize      |
    |                |                  |   Hierarchy      |
    |                |                  |                  |
    |                |                  | JSON-RPC Response|
    |                |                  | (hierarchy data) |
    |                |                  |<-----------------|
    |                |                  |                  |
    |                | Resource Content |                  |
    |                |<-----------------|                  |
    |                |                  |                  |
    | "Here's the    |                  |                  |
    |  scene tree:   |                  |                  |
    |  - Main Camera |                  |                  |
    |  - Player      |                  |                  |
    |  - ..."        |                  |                  |
    |<---------------|                  |                  |
```

### 3.5 Component Interaction Diagram

```
+------------------------------------------------------------------+
|                    Unity Editor Plugin                            |
+------------------------------------------------------------------+
|                                                                  |
|  +----------------+      +------------------+                    |
|  |                |      |                  |                    |
|  | McpEditorWindow|----->| McpUnityServer   |                    |
|  |                |      |                  |                    |
|  | - Port config  |      | - Start/Stop    |                    |
|  | - Status       |      | - Accept clients |                    |
|  | - Logs         |      | - Route messages |                    |
|  +----------------+      +--------+---------+                    |
|                                   |                              |
|                                   v                              |
|                          +------------------+                    |
|                          |                  |                    |
|                          |   McpJsonRpc     |                    |
|                          |                  |                    |
|                          | - Parse JSON-RPC |                    |
|                          | - Validate       |                    |
|                          | - Dispatch       |                    |
|                          | - Format response|                    |
|                          +--------+---------+                    |
|                                   |                              |
|                   +---------------+---------------+              |
|                   |                               |              |
|                   v                               v              |
|          +----------------+              +----------------+      |
|          |                |              |                |      |
|          | McpToolRegistry|              |McpResourceReg. |      |
|          |                |              |                |      |
|          | - Register     |              | - Register     |      |
|          | - Lookup       |              | - Lookup       |      |
|          | - Execute      |              | - Read         |      |
|          +-------+--------+              +-------+--------+      |
|                  |                               |               |
|      +-----------+-----------+       +-----------+-----------+   |
|      |           |           |       |           |           |   |
|      v           v           v       v           v           v   |
| +--------+ +--------+ +-------+ +--------+ +--------+ +-------+  |
| |GameObject| |Scene   | |Asset  | |Hierarchy| |Console | |Scene  | |
| |Tools    | |Tools   | |Tools  | |Resource| |Resource| |Resource| |
| +--------+ +--------+ +-------+ +--------+ +--------+ +-------+  |
|                                                                  |
+------------------------------------------------------------------+
```

---

## 4. Component Architecture

### 4.1 Directory Structure

```
Assets/
  Editor/
    McpUnity/
      Core/
        McpUnityServer.cs         # WebSocket server implementation
        McpJsonRpc.cs             # JSON-RPC 2.0 protocol handler
        McpToolRegistry.cs        # Tool registration and dispatch
        McpResourceRegistry.cs    # Resource registration and access
        McpMainThreadDispatcher.cs# Unity main thread execution

      Window/
        McpEditorWindow.cs        # Editor configuration window
        McpEditorStyles.cs        # UI styles and layout

      Tools/
        IToolHandler.cs           # Tool handler interface
        GameObjectTools.cs        # GameObject manipulation
        SceneTools.cs             # Scene management
        AssetTools.cs             # Asset operations
        ComponentTools.cs         # Component management
        EditorTools.cs            # Editor utilities

      Resources/
        IResourceProvider.cs      # Resource provider interface
        HierarchyResource.cs      # Scene hierarchy
        GameObjectResource.cs     # GameObject details
        AssetResource.cs          # Project assets
        ConsoleResource.cs        # Console logs
        SceneResource.cs          # Active scene info

      Models/
        JsonRpcMessage.cs         # JSON-RPC data structures
        ToolDefinition.cs         # Tool metadata
        ResourceDefinition.cs     # Resource metadata
        UnityDataModels.cs        # Unity object serialization

      Utils/
        JsonHelper.cs             # JSON serialization utilities
        LogCapture.cs             # Console log interception
        InstanceIdCache.cs        # GameObject ID management

      McpUnity.asmdef             # Assembly definition

  Server~/                        # Excluded from Unity import
    src/
      index.ts                    # Entry point
      McpBridge.ts                # MCP <-> Unity bridge
      UnityConnection.ts          # WebSocket client

      tools/
        index.ts                  # Tool exports
        gameObjectTools.ts        # GameObject tool handlers
        sceneTools.ts             # Scene tool handlers
        assetTools.ts             # Asset tool handlers
        componentTools.ts         # Component tool handlers
        editorTools.ts            # Editor tool handlers

      resources/
        index.ts                  # Resource exports
        hierarchyResource.ts      # Hierarchy resource handler
        gameObjectResource.ts     # GameObject resource handler
        assetResource.ts          # Asset resource handler
        consoleResource.ts        # Console resource handler
        sceneResource.ts          # Scene resource handler

      types/
        unity.ts                  # Unity type definitions
        protocol.ts               # Protocol type definitions

      utils/
        logger.ts                 # Logging utilities
        validation.ts             # Input validation

    package.json
    tsconfig.json
    .npmrc
```

### 4.2 Unity Components (C#)

#### 4.2.1 McpUnityServer

```csharp
namespace McpUnity.Core
{
    /// <summary>
    /// WebSocket server that handles connections from MCP bridge.
    /// Runs on a background thread, dispatches to main thread for Unity API calls.
    /// </summary>
    public class McpUnityServer : IDisposable
    {
        // Configuration
        public int Port { get; set; } = 8090;
        public bool IsRunning { get; private set; }

        // Events
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action<string, string> OnMessageReceived;
        public event Action<string> OnError;

        // Core methods
        public void Start();
        public void Stop();
        public void SendMessage(string clientId, string message);
        public void Broadcast(string message);

        // Internal
        private HttpListener _listener;
        private ConcurrentDictionary<string, WebSocket> _clients;
        private CancellationTokenSource _cts;
        private McpJsonRpc _jsonRpc;
    }
}
```

#### 4.2.2 McpJsonRpc

```csharp
namespace McpUnity.Core
{
    /// <summary>
    /// Handles JSON-RPC 2.0 message parsing, validation, and response formatting.
    /// </summary>
    public class McpJsonRpc
    {
        // Dependencies
        private readonly McpToolRegistry _toolRegistry;
        private readonly McpResourceRegistry _resourceRegistry;

        // Message handling
        public async Task<string> ProcessMessage(string jsonMessage);

        // Internal methods
        private JsonRpcRequest ParseRequest(string json);
        private JsonRpcResponse CreateSuccessResponse(object id, object result);
        private JsonRpcResponse CreateErrorResponse(object id, int code, string message);

        // Method routing
        private async Task<object> RouteMethod(string method, JObject params);

        // Standard MCP methods
        private Task<object> HandleInitialize(JObject params);
        private Task<object> HandleToolsList();
        private Task<object> HandleToolsCall(JObject params);
        private Task<object> HandleResourcesList();
        private Task<object> HandleResourcesRead(JObject params);
        private Task<object> HandlePing();
    }
}
```

#### 4.2.3 McpToolRegistry

```csharp
namespace McpUnity.Core
{
    /// <summary>
    /// Manages tool registration, lookup, and execution.
    /// </summary>
    public class McpToolRegistry
    {
        // Tool storage
        private readonly Dictionary<string, IToolHandler> _handlers;
        private readonly List<ToolDefinition> _definitions;

        // Registration
        public void RegisterTool(IToolHandler handler);
        public void RegisterTools(IEnumerable<IToolHandler> handlers);

        // Lookup
        public ToolDefinition[] GetAllTools();
        public IToolHandler GetHandler(string toolName);

        // Execution
        public async Task<ToolResult> ExecuteTool(string name, JObject arguments);
    }

    /// <summary>
    /// Interface for tool implementations.
    /// </summary>
    public interface IToolHandler
    {
        string Name { get; }
        string Description { get; }
        JObject InputSchema { get; }
        Task<ToolResult> Execute(JObject arguments);
    }

    public class ToolResult
    {
        public List<ContentItem> Content { get; set; }
        public bool IsError { get; set; }
    }

    public class ContentItem
    {
        public string Type { get; set; } = "text";
        public string Text { get; set; }
    }
}
```

#### 4.2.4 McpResourceRegistry

```csharp
namespace McpUnity.Core
{
    /// <summary>
    /// Manages resource registration and data access.
    /// </summary>
    public class McpResourceRegistry
    {
        // Resource storage
        private readonly Dictionary<string, IResourceProvider> _providers;
        private readonly List<ResourceDefinition> _definitions;

        // Registration
        public void RegisterResource(IResourceProvider provider);

        // Lookup
        public ResourceDefinition[] GetAllResources();
        public IResourceProvider GetProvider(string uri);

        // Access
        public async Task<ResourceContents> ReadResource(string uri);
    }

    /// <summary>
    /// Interface for resource implementations.
    /// </summary>
    public interface IResourceProvider
    {
        string Uri { get; }
        string Name { get; }
        string Description { get; }
        string MimeType { get; }
        Task<ResourceContents> Read(string uri);
    }

    public class ResourceContents
    {
        public string Uri { get; set; }
        public string MimeType { get; set; }
        public string Text { get; set; }
    }
}
```

#### 4.2.5 McpMainThreadDispatcher

```csharp
namespace McpUnity.Core
{
    /// <summary>
    /// Ensures Unity API calls execute on the main thread.
    /// Uses EditorApplication.update for editor context.
    /// </summary>
    [InitializeOnLoad]
    public static class McpMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _executionQueue;

        static McpMainThreadDispatcher()
        {
            EditorApplication.update += Update;
        }

        public static void Enqueue(Action action);
        public static Task<T> EnqueueAsync<T>(Func<T> func);
        public static Task EnqueueAsync(Action action);

        private static void Update();
    }
}
```

### 4.3 Node.js Bridge Components (TypeScript)

#### 4.3.1 Entry Point (index.ts)

```typescript
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
  await bridge.connect();

  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch(console.error);
```

#### 4.3.2 McpBridge

```typescript
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { UnityConnection } from "./UnityConnection.js";
import { registerTools } from "./tools/index.js";
import { registerResources } from "./resources/index.js";

export class McpBridge {
  private server: Server;
  private unity: UnityConnection;

  constructor(server: Server) {
    this.server = server;
    this.unity = new UnityConnection();
  }

  async connect(port: number = 8090): Promise<void> {
    await this.unity.connect(`ws://localhost:${port}`);
    registerTools(this.server, this.unity);
    registerResources(this.server, this.unity);
  }

  async disconnect(): Promise<void> {
    await this.unity.disconnect();
  }
}
```

#### 4.3.3 UnityConnection

```typescript
import WebSocket from "ws";

export interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: number;
  method: string;
  params?: unknown;
}

export interface JsonRpcResponse {
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
  private pendingRequests = new Map<number, {
    resolve: (value: unknown) => void;
    reject: (error: Error) => void;
  }>();

  async connect(url: string): Promise<void>;
  async disconnect(): Promise<void>;
  async call(method: string, params?: unknown): Promise<unknown>;

  private handleMessage(data: WebSocket.RawData): void;
}
```

---

## 5. Communication Protocol

### 5.1 MCP Lifecycle

```
Client (Claude)                          Server (Bridge)
      |                                        |
      |  ---------- initialize ------------>   |
      |  <--------- initialize result ------   |
      |                                        |
      |  ---------- initialized ----------->   |
      |                                        |
      |  <========= Normal Operation =======>  |
      |                                        |
      |  ----------- shutdown ------------->   |
      |  <---------- shutdown result ------    |
```

### 5.2 JSON-RPC 2.0 Message Format

#### Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "create_gameobject",
    "arguments": {
      "name": "Player",
      "position": [0, 1, 0]
    }
  }
}
```

#### Success Response
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Created GameObject 'Player' with ID 12345 at position (0, 1, 0)"
      }
    ]
  }
}
```

#### Error Response
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32602,
    "message": "Invalid params",
    "data": {
      "field": "position",
      "reason": "Must be an array of 3 numbers"
    }
  }
}
```

### 5.3 Internal Unity Protocol

The WebSocket communication between Bridge and Unity uses a subset of JSON-RPC:

#### Tool Execution
```json
// Request
{
  "jsonrpc": "2.0",
  "id": 42,
  "method": "execute_tool",
  "params": {
    "name": "create_gameobject",
    "arguments": {
      "name": "Player",
      "position": [0, 1, 0]
    }
  }
}

// Response
{
  "jsonrpc": "2.0",
  "id": 42,
  "result": {
    "success": true,
    "data": {
      "instanceId": 12345,
      "name": "Player",
      "path": "/Player"
    }
  }
}
```

#### Resource Read
```json
// Request
{
  "jsonrpc": "2.0",
  "id": 43,
  "method": "read_resource",
  "params": {
    "uri": "unity://hierarchy"
  }
}

// Response
{
  "jsonrpc": "2.0",
  "id": 43,
  "result": {
    "uri": "unity://hierarchy",
    "mimeType": "application/json",
    "text": "{\"roots\":[{\"name\":\"Main Camera\",...}]}"
  }
}
```

### 5.4 Error Codes

| Code | Name | Description |
|------|------|-------------|
| -32700 | Parse error | Invalid JSON |
| -32600 | Invalid Request | Not a valid JSON-RPC request |
| -32601 | Method not found | Unknown method |
| -32602 | Invalid params | Invalid method parameters |
| -32603 | Internal error | Internal JSON-RPC error |
| -32000 | Unity Error | Unity-specific error |
| -32001 | Tool Not Found | Unknown tool name |
| -32002 | Resource Not Found | Unknown resource URI |
| -32003 | Execution Failed | Tool execution failed |

---

## 6. Data Models

### 6.1 Tool Definition

```typescript
interface ToolDefinition {
  name: string;
  description: string;
  inputSchema: {
    type: "object";
    properties: Record<string, JsonSchema>;
    required?: string[];
  };
}

// Example
const createGameObjectTool: ToolDefinition = {
  name: "create_gameobject",
  description: "Creates a new GameObject in the active scene",
  inputSchema: {
    type: "object",
    properties: {
      name: {
        type: "string",
        description: "Name of the GameObject"
      },
      position: {
        type: "array",
        items: { type: "number" },
        minItems: 3,
        maxItems: 3,
        description: "World position [x, y, z]"
      },
      rotation: {
        type: "array",
        items: { type: "number" },
        minItems: 3,
        maxItems: 3,
        description: "Euler rotation [x, y, z] in degrees"
      },
      scale: {
        type: "array",
        items: { type: "number" },
        minItems: 3,
        maxItems: 3,
        description: "Local scale [x, y, z]"
      },
      parent: {
        type: "integer",
        description: "Instance ID of parent GameObject"
      }
    },
    required: ["name"]
  }
};
```

### 6.2 Resource Definition

```typescript
interface ResourceDefinition {
  uri: string;
  name: string;
  description: string;
  mimeType: string;
}

// Example
const hierarchyResource: ResourceDefinition = {
  uri: "unity://hierarchy",
  name: "Scene Hierarchy",
  description: "Tree structure of all GameObjects in the active scene",
  mimeType: "application/json"
};
```

### 6.3 Unity Data Models

```typescript
// GameObject representation
interface GameObjectData {
  instanceId: number;
  name: string;
  path: string;
  tag: string;
  layer: number;
  isActive: boolean;
  isStatic: boolean;
  transform: TransformData;
  components: ComponentData[];
  children?: GameObjectData[];
}

// Transform data
interface TransformData {
  position: [number, number, number];
  rotation: [number, number, number];
  scale: [number, number, number];
  localPosition: [number, number, number];
  localRotation: [number, number, number];
  localScale: [number, number, number];
}

// Component data
interface ComponentData {
  type: string;
  instanceId: number;
  enabled: boolean;
  properties: Record<string, unknown>;
}

// Asset data
interface AssetData {
  guid: string;
  path: string;
  name: string;
  type: string;
  labels: string[];
}

// Console log entry
interface ConsoleLogEntry {
  message: string;
  stackTrace: string;
  type: "Log" | "Warning" | "Error" | "Assert" | "Exception";
  timestamp: string;
}
```

---

## 7. Tool Specifications

### 7.1 GameObject Tools

#### create_gameobject

| Property | Value |
|----------|-------|
| **Name** | `create_gameobject` |
| **Description** | Creates a new GameObject in the active scene |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "GameObject name" },
    "position": { "type": "array", "items": { "type": "number" }, "description": "[x, y, z]" },
    "rotation": { "type": "array", "items": { "type": "number" }, "description": "[x, y, z] degrees" },
    "scale": { "type": "array", "items": { "type": "number" }, "description": "[x, y, z]" },
    "parent": { "type": "integer", "description": "Parent instance ID" },
    "primitive": { "type": "string", "enum": ["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"] }
  },
  "required": ["name"]
}
```

**Response:**
```json
{
  "content": [{
    "type": "text",
    "text": "Created GameObject 'Player' (ID: 12345) at (0, 1, 0)"
  }]
}
```

#### update_transform

| Property | Value |
|----------|-------|
| **Name** | `update_transform` |
| **Description** | Updates the transform of a GameObject |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "GameObject instance ID" },
    "position": { "type": "array", "items": { "type": "number" } },
    "rotation": { "type": "array", "items": { "type": "number" } },
    "scale": { "type": "array", "items": { "type": "number" } },
    "local": { "type": "boolean", "description": "Use local space" }
  },
  "required": ["instanceId"]
}
```

#### delete_gameobject

| Property | Value |
|----------|-------|
| **Name** | `delete_gameobject` |
| **Description** | Deletes a GameObject and its children |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "GameObject instance ID" }
  },
  "required": ["instanceId"]
}
```

#### find_gameobject

| Property | Value |
|----------|-------|
| **Name** | `find_gameobject` |
| **Description** | Finds GameObjects by name, tag, or component |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Exact name match" },
    "tag": { "type": "string", "description": "Tag to search" },
    "component": { "type": "string", "description": "Component type name" },
    "path": { "type": "string", "description": "Hierarchy path" }
  }
}
```

### 7.2 Component Tools

#### add_component

| Property | Value |
|----------|-------|
| **Name** | `add_component` |
| **Description** | Adds a component to a GameObject |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "GameObject instance ID" },
    "componentType": { "type": "string", "description": "Full type name (e.g., 'UnityEngine.BoxCollider')" },
    "properties": { "type": "object", "description": "Initial property values" }
  },
  "required": ["instanceId", "componentType"]
}
```

#### update_component

| Property | Value |
|----------|-------|
| **Name** | `update_component` |
| **Description** | Updates properties of a component |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "Component instance ID" },
    "properties": { "type": "object", "description": "Property values to update" }
  },
  "required": ["instanceId", "properties"]
}
```

#### remove_component

| Property | Value |
|----------|-------|
| **Name** | `remove_component` |
| **Description** | Removes a component from a GameObject |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "Component instance ID" }
  },
  "required": ["instanceId"]
}
```

### 7.3 Scene Tools

#### load_scene

| Property | Value |
|----------|-------|
| **Name** | `load_scene` |
| **Description** | Loads a scene in the editor |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "path": { "type": "string", "description": "Scene asset path" },
    "mode": { "type": "string", "enum": ["Single", "Additive"] }
  },
  "required": ["path"]
}
```

#### save_scene

| Property | Value |
|----------|-------|
| **Name** | `save_scene` |
| **Description** | Saves the current scene |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "path": { "type": "string", "description": "Save path (optional, uses current if not specified)" }
  }
}
```

#### new_scene

| Property | Value |
|----------|-------|
| **Name** | `new_scene` |
| **Description** | Creates a new empty scene |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "setup": { "type": "string", "enum": ["EmptyScene", "DefaultGameObjects"] }
  }
}
```

### 7.4 Asset Tools

#### create_prefab

| Property | Value |
|----------|-------|
| **Name** | `create_prefab` |
| **Description** | Creates a prefab from a GameObject |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "instanceId": { "type": "integer", "description": "Source GameObject instance ID" },
    "path": { "type": "string", "description": "Asset path for prefab" }
  },
  "required": ["instanceId", "path"]
}
```

#### instantiate_prefab

| Property | Value |
|----------|-------|
| **Name** | `instantiate_prefab` |
| **Description** | Instantiates a prefab in the scene |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "path": { "type": "string", "description": "Prefab asset path" },
    "position": { "type": "array", "items": { "type": "number" } },
    "rotation": { "type": "array", "items": { "type": "number" } },
    "parent": { "type": "integer", "description": "Parent instance ID" }
  },
  "required": ["path"]
}
```

#### import_asset

| Property | Value |
|----------|-------|
| **Name** | `import_asset` |
| **Description** | Imports an external file as an asset |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "sourcePath": { "type": "string", "description": "External file path" },
    "destinationPath": { "type": "string", "description": "Asset path destination" }
  },
  "required": ["sourcePath", "destinationPath"]
}
```

### 7.5 Editor Tools

#### execute_menu_item

| Property | Value |
|----------|-------|
| **Name** | `execute_menu_item` |
| **Description** | Executes a Unity menu command |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "menuPath": { "type": "string", "description": "Menu item path (e.g., 'Edit/Play')" }
  },
  "required": ["menuPath"]
}
```

#### run_tests

| Property | Value |
|----------|-------|
| **Name** | `run_tests` |
| **Description** | Runs Unity Test Framework tests |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "mode": { "type": "string", "enum": ["EditMode", "PlayMode", "All"] },
    "filter": { "type": "string", "description": "Test name filter" }
  }
}
```

#### compile_scripts

| Property | Value |
|----------|-------|
| **Name** | `compile_scripts` |
| **Description** | Forces script recompilation |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {}
}
```

#### refresh_assets

| Property | Value |
|----------|-------|
| **Name** | `refresh_assets` |
| **Description** | Refreshes the asset database |

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "importOptions": { "type": "string", "enum": ["Default", "ForceUpdate", "ForceSynchronousImport"] }
  }
}
```

---

## 8. Resource Specifications

### 8.1 unity://hierarchy

| Property | Value |
|----------|-------|
| **URI** | `unity://hierarchy` |
| **Name** | Scene Hierarchy |
| **MIME Type** | application/json |

**Response Format:**
```json
{
  "sceneName": "SampleScene",
  "scenePath": "Assets/Scenes/SampleScene.unity",
  "roots": [
    {
      "instanceId": 12345,
      "name": "Main Camera",
      "path": "/Main Camera",
      "tag": "MainCamera",
      "layer": 0,
      "isActive": true,
      "transform": {
        "position": [0, 1, -10],
        "rotation": [0, 0, 0],
        "scale": [1, 1, 1]
      },
      "components": ["Transform", "Camera", "AudioListener"],
      "children": []
    },
    {
      "instanceId": 12346,
      "name": "Directional Light",
      "path": "/Directional Light",
      "tag": "Untagged",
      "layer": 0,
      "isActive": true,
      "transform": {
        "position": [0, 3, 0],
        "rotation": [50, -30, 0],
        "scale": [1, 1, 1]
      },
      "components": ["Transform", "Light"],
      "children": []
    }
  ]
}
```

### 8.2 unity://gameobject/{instanceId}

| Property | Value |
|----------|-------|
| **URI** | `unity://gameobject/{instanceId}` |
| **Name** | GameObject Details |
| **MIME Type** | application/json |

**Response Format:**
```json
{
  "instanceId": 12345,
  "name": "Player",
  "path": "/Player",
  "tag": "Player",
  "layer": 0,
  "isActive": true,
  "isStatic": false,
  "transform": {
    "position": [0, 1, 0],
    "rotation": [0, 0, 0],
    "scale": [1, 1, 1],
    "localPosition": [0, 1, 0],
    "localRotation": [0, 0, 0],
    "localScale": [1, 1, 1]
  },
  "components": [
    {
      "type": "Transform",
      "instanceId": 12347,
      "enabled": true,
      "properties": {}
    },
    {
      "type": "Rigidbody",
      "instanceId": 12348,
      "enabled": true,
      "properties": {
        "mass": 1.0,
        "drag": 0,
        "angularDrag": 0.05,
        "useGravity": true,
        "isKinematic": false
      }
    },
    {
      "type": "BoxCollider",
      "instanceId": 12349,
      "enabled": true,
      "properties": {
        "center": [0, 0, 0],
        "size": [1, 2, 1],
        "isTrigger": false
      }
    }
  ],
  "parent": null,
  "children": [
    {
      "instanceId": 12350,
      "name": "Model",
      "path": "/Player/Model"
    }
  ]
}
```

### 8.3 unity://assets

| Property | Value |
|----------|-------|
| **URI** | `unity://assets` |
| **Name** | Project Assets |
| **MIME Type** | application/json |

**Query Parameters:**
- `path` - Filter by folder path
- `type` - Filter by asset type
- `label` - Filter by asset label

**Response Format:**
```json
{
  "projectPath": "/Users/dev/MyProject",
  "assetsPath": "Assets",
  "assets": [
    {
      "guid": "abc123def456",
      "path": "Assets/Prefabs/Player.prefab",
      "name": "Player",
      "type": "Prefab",
      "labels": ["Character", "Player"]
    },
    {
      "guid": "def456ghi789",
      "path": "Assets/Materials/Standard.mat",
      "name": "Standard",
      "type": "Material",
      "labels": []
    }
  ],
  "folders": [
    "Assets/Prefabs",
    "Assets/Materials",
    "Assets/Scripts",
    "Assets/Scenes"
  ]
}
```

### 8.4 unity://console

| Property | Value |
|----------|-------|
| **URI** | `unity://console` |
| **Name** | Console Logs |
| **MIME Type** | application/json |

**Query Parameters:**
- `count` - Number of entries (default: 100)
- `type` - Filter by log type (Log, Warning, Error)

**Response Format:**
```json
{
  "entries": [
    {
      "message": "Entering play mode",
      "stackTrace": "",
      "type": "Log",
      "timestamp": "2025-12-27T10:30:00Z"
    },
    {
      "message": "NullReferenceException: Object reference not set",
      "stackTrace": "at PlayerController.Update() in Assets/Scripts/PlayerController.cs:42",
      "type": "Error",
      "timestamp": "2025-12-27T10:30:05Z"
    }
  ],
  "counts": {
    "log": 45,
    "warning": 3,
    "error": 2
  }
}
```

### 8.5 unity://scene

| Property | Value |
|----------|-------|
| **URI** | `unity://scene` |
| **Name** | Active Scene |
| **MIME Type** | application/json |

**Response Format:**
```json
{
  "name": "SampleScene",
  "path": "Assets/Scenes/SampleScene.unity",
  "buildIndex": 0,
  "isLoaded": true,
  "isDirty": false,
  "rootCount": 3,
  "isSubScene": false,
  "lighting": {
    "ambientMode": "Skybox",
    "ambientColor": [0.2, 0.2, 0.2],
    "fog": false
  },
  "physics": {
    "gravity": [0, -9.81, 0]
  },
  "renderSettings": {
    "skybox": "Assets/Materials/DefaultSkybox.mat"
  }
}
```

---

## 9. Security Considerations

### 9.1 Threat Model

| Threat | Mitigation |
|--------|-----------|
| Unauthorized access | Localhost-only binding |
| Malicious commands | Input validation, sandboxed execution |
| Data exfiltration | No network exposure, local-only |
| Code injection | Parameterized operations, no eval |

### 9.2 Security Measures

1. **Network Binding**: Server binds exclusively to `127.0.0.1`
2. **Port Configuration**: Configurable port with sensible default (8090)
3. **Input Validation**: All tool arguments validated against schema
4. **Path Traversal Prevention**: Asset paths validated within project
5. **Operation Allowlist**: Only defined tools can execute
6. **Logging**: All operations logged for audit

### 9.3 Configuration Options

```csharp
[Serializable]
public class McpSecuritySettings
{
    public bool allowDestructiveOperations = true;
    public bool allowAssetModification = true;
    public bool allowSceneLoading = true;
    public string[] blockedMenuItems = { };
    public int maxLogEntries = 1000;
}
```

---

## 10. Deployment Architecture

### 10.1 Installation Flow

```
1. Import Unity Package
   └── Assets/Editor/McpUnity installed

2. Unity compiles scripts
   └── McpUnityServer ready

3. Open MCP Editor Window
   └── Window > MCP > Server

4. Install Node.js Bridge
   └── cd Assets/Server~ && npm install

5. Configure Claude settings.json
   └── Add MCP server configuration

6. Start Server in Unity
   └── Click "Start Server"

7. Launch Claude
   └── MCP bridge connects automatically
```

### 10.2 Claude Configuration

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["path/to/Assets/Server~/dist/index.js"],
      "env": {
        "UNITY_MCP_PORT": "8090"
      }
    }
  }
}
```

### 10.3 Package Distribution

**Unity Package Structure:**
```
UnityMCP.unitypackage
├── Editor/
│   └── McpUnity/
│       ├── *.cs files
│       └── McpUnity.asmdef
├── Server~/
│   ├── src/
│   ├── package.json
│   └── tsconfig.json
└── package.json (Unity Package Manager)
```

**npm Package (Bridge):**
```
unity-mcp-bridge
├── dist/
│   └── *.js files
├── package.json
└── README.md
```

---

## 11. Architecture Decision Records

### ADR-001: WebSocket Transport

**Status:** Accepted

**Context:**
The MCP protocol requires a communication channel between the Node.js bridge and Unity Editor.

**Decision:**
Use WebSocket for Unity-Bridge communication.

**Rationale:**
- Bidirectional communication
- Low latency
- Unity .NET supports System.Net.WebSockets
- Persistent connection model matches editor lifecycle

**Consequences:**
- Need to handle connection lifecycle
- Must implement reconnection logic
- Port configuration required

---

### ADR-002: Separate Bridge Process

**Status:** Accepted

**Context:**
MCP servers communicate via stdio with the LLM client.

**Decision:**
Implement the MCP server as a separate Node.js process that bridges to Unity.

**Rationale:**
- MCP SDK available for Node.js
- stdio transport requirement
- Clean separation of concerns
- Easier to update MCP protocol handling

**Consequences:**
- Two processes to manage
- WebSocket communication overhead
- Node.js runtime dependency

---

### ADR-003: Main Thread Dispatch

**Status:** Accepted

**Context:**
Unity API calls must execute on the main thread, but WebSocket messages arrive on background threads.

**Decision:**
Implement a thread-safe dispatcher using EditorApplication.update.

**Rationale:**
- Unity API thread safety requirement
- Editor context (not runtime)
- Predictable execution timing

**Consequences:**
- Slight latency for queued operations
- Queue management complexity
- Need async/await patterns

---

### ADR-004: Instance ID for Object Reference

**Status:** Accepted

**Context:**
Need a stable way to reference Unity objects across the MCP boundary.

**Decision:**
Use Unity's GetInstanceID() as the object identifier.

**Rationale:**
- Built-in Unity mechanism
- Unique per session
- Works for all UnityEngine.Object types

**Consequences:**
- IDs not stable across sessions
- Need to handle stale references
- Cache management required

---

### ADR-005: JSON Serialization Strategy

**Status:** Accepted

**Context:**
Need to serialize Unity objects for MCP resource responses.

**Decision:**
Use Newtonsoft.Json with custom converters for Unity types.

**Rationale:**
- Unity's default JSON serializer (JsonUtility) limited
- Newtonsoft.Json is Unity-supported package
- Flexible attribute-based control
- Custom converters for Vector3, Quaternion, etc.

**Consequences:**
- Dependency on Newtonsoft.Json package
- Need custom converters
- Serialization depth control required

---

### ADR-006: Tool Execution Pattern

**Status:** Accepted

**Context:**
Tools need to execute Unity operations and return structured results.

**Decision:**
Implement Command pattern with IToolHandler interface.

**Rationale:**
- Clean separation of tool logic
- Easy to add new tools
- Testable in isolation
- Consistent error handling

**Consequences:**
- Interface contract maintenance
- Registration boilerplate
- Schema synchronization needed

---

## Appendix A: Complete Tool Reference

| Tool Name | Category | Description |
|-----------|----------|-------------|
| create_gameobject | GameObject | Create new GameObject |
| update_transform | GameObject | Modify transform |
| delete_gameobject | GameObject | Delete GameObject |
| find_gameobject | GameObject | Search GameObjects |
| add_component | Component | Add component |
| update_component | Component | Modify component |
| remove_component | Component | Remove component |
| load_scene | Scene | Load scene |
| save_scene | Scene | Save scene |
| new_scene | Scene | Create scene |
| create_prefab | Asset | Create prefab |
| instantiate_prefab | Asset | Instantiate prefab |
| import_asset | Asset | Import external file |
| execute_menu_item | Editor | Execute menu |
| run_tests | Editor | Run tests |
| compile_scripts | Editor | Recompile scripts |
| refresh_assets | Editor | Refresh AssetDatabase |

## Appendix B: Complete Resource Reference

| URI Pattern | Name | Description |
|-------------|------|-------------|
| unity://hierarchy | Scene Hierarchy | All GameObjects in scene |
| unity://gameobject/{id} | GameObject Details | Single GameObject data |
| unity://assets | Project Assets | Asset database listing |
| unity://console | Console Logs | Editor console output |
| unity://scene | Active Scene | Current scene metadata |

---

*Document generated for Unity MCP Plugin architecture. Last updated: 2025-12-27*
