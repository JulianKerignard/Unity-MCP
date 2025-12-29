import { z } from "zod";

// ============================================================================
// JSON-RPC Types
// ============================================================================

export const JsonRpcRequestSchema = z.object({
  jsonrpc: z.literal("2.0"),
  id: z.union([z.string(), z.number()]),
  method: z.string(),
  params: z.unknown().optional(),
});

export type JsonRpcRequest = z.infer<typeof JsonRpcRequestSchema>;

export const JsonRpcErrorSchema = z.object({
  code: z.number(),
  message: z.string(),
  data: z.unknown().optional(),
});

export type JsonRpcError = z.infer<typeof JsonRpcErrorSchema>;

export const JsonRpcResponseSchema = z.object({
  jsonrpc: z.literal("2.0"),
  id: z.union([z.string(), z.number()]),
  result: z.unknown().optional(),
  error: JsonRpcErrorSchema.optional(),
});

export type JsonRpcResponse = z.infer<typeof JsonRpcResponseSchema>;

// ============================================================================
// MCP Tool Types
// ============================================================================

export const ToolInputSchemaSchema = z.object({
  type: z.literal("object"),
  properties: z.record(z.unknown()).optional(),
  required: z.array(z.string()).optional(),
});

export type ToolInputSchema = z.infer<typeof ToolInputSchemaSchema>;

export const ToolDefinitionSchema = z.object({
  name: z.string(),
  description: z.string().optional(),
  inputSchema: ToolInputSchemaSchema,
});

export type ToolDefinition = z.infer<typeof ToolDefinitionSchema>;

export const ToolCallSchema = z.object({
  name: z.string(),
  arguments: z.record(z.unknown()).optional(),
});

export type ToolCall = z.infer<typeof ToolCallSchema>;

export const ToolResultContentSchema = z.object({
  type: z.enum(["text", "image", "resource"]),
  text: z.string().optional(),
  data: z.string().optional(),
  mimeType: z.string().optional(),
  resource: z.unknown().optional(),
});

export type ToolResultContent = z.infer<typeof ToolResultContentSchema>;

export const ToolResultSchema = z.object({
  content: z.array(ToolResultContentSchema),
  isError: z.boolean().optional(),
});

export type ToolResult = z.infer<typeof ToolResultSchema>;

// ============================================================================
// MCP Resource Types
// ============================================================================

export const ResourceDefinitionSchema = z.object({
  uri: z.string(),
  name: z.string(),
  description: z.string().optional(),
  mimeType: z.string().optional(),
});

export type ResourceDefinition = z.infer<typeof ResourceDefinitionSchema>;

export const ResourceReadSchema = z.object({
  uri: z.string(),
});

export type ResourceRead = z.infer<typeof ResourceReadSchema>;

export const ResourceContentSchema = z.object({
  uri: z.string(),
  mimeType: z.string().optional(),
  text: z.string().optional(),
  blob: z.string().optional(),
});

export type ResourceContent = z.infer<typeof ResourceContentSchema>;

export const ResourceReadResultSchema = z.object({
  contents: z.array(ResourceContentSchema),
});

export type ResourceReadResult = z.infer<typeof ResourceReadResultSchema>;

// ============================================================================
// MCP Prompt Types
// ============================================================================

export const PromptArgumentSchema = z.object({
  name: z.string(),
  description: z.string().optional(),
  required: z.boolean().optional(),
});

export type PromptArgument = z.infer<typeof PromptArgumentSchema>;

export const PromptDefinitionSchema = z.object({
  name: z.string(),
  description: z.string().optional(),
  arguments: z.array(PromptArgumentSchema).optional(),
});

export type PromptDefinition = z.infer<typeof PromptDefinitionSchema>;

export const PromptMessageSchema = z.object({
  role: z.enum(["user", "assistant"]),
  content: z.object({
    type: z.literal("text"),
    text: z.string(),
  }),
});

export type PromptMessage = z.infer<typeof PromptMessageSchema>;

export const PromptGetResultSchema = z.object({
  description: z.string().optional(),
  messages: z.array(PromptMessageSchema),
});

export type PromptGetResult = z.infer<typeof PromptGetResultSchema>;

// ============================================================================
// Unity-specific Types
// ============================================================================

export const UnitySceneObjectSchema = z.object({
  instanceId: z.number(),
  name: z.string(),
  type: z.string(),
  active: z.boolean(),
  layer: z.number(),
  tag: z.string(),
  position: z.object({
    x: z.number(),
    y: z.number(),
    z: z.number(),
  }).optional(),
  rotation: z.object({
    x: z.number(),
    y: z.number(),
    z: z.number(),
    w: z.number(),
  }).optional(),
  scale: z.object({
    x: z.number(),
    y: z.number(),
    z: z.number(),
  }).optional(),
  components: z.array(z.string()).optional(),
  children: z.array(z.number()).optional(),
});

export type UnitySceneObject = z.infer<typeof UnitySceneObjectSchema>;

export const UnitySceneInfoSchema = z.object({
  name: z.string(),
  path: z.string(),
  buildIndex: z.number(),
  isDirty: z.boolean(),
  isLoaded: z.boolean(),
  rootCount: z.number(),
});

export type UnitySceneInfo = z.infer<typeof UnitySceneInfoSchema>;

export const UnityProjectInfoSchema = z.object({
  productName: z.string(),
  companyName: z.string(),
  version: z.string(),
  unityVersion: z.string(),
  platform: z.string(),
  dataPath: z.string(),
  persistentDataPath: z.string(),
});

export type UnityProjectInfo = z.infer<typeof UnityProjectInfoSchema>;

// ============================================================================
// Bridge Configuration Types
// ============================================================================

export interface BridgeConfig {
  unityHost: string;
  unityPort: number;
  reconnectInterval: number;
  requestTimeout: number;
  maxReconnectAttempts: number;
  debug: boolean;
}

export const DEFAULT_BRIDGE_CONFIG: BridgeConfig = {
  unityHost: "localhost",
  unityPort: 8090,
  reconnectInterval: 5000,
  requestTimeout: 30000,
  maxReconnectAttempts: 10,
  debug: false,
};

// ============================================================================
// Connection State
// ============================================================================

export enum ConnectionState {
  Disconnected = "disconnected",
  Connecting = "connecting",
  Connected = "connected",
  Reconnecting = "reconnecting",
  Failed = "failed",
}

// ============================================================================
// Error Codes
// ============================================================================

export enum McpErrorCode {
  ParseError = -32700,
  InvalidRequest = -32600,
  MethodNotFound = -32601,
  InvalidParams = -32602,
  InternalError = -32603,
  // Custom error codes (aligned with C# McpProtocol.cs)
  ConnectionError = -32000,
  ToolNotFound = -32001,      // Previously TimeoutError - now aligned with C#
  ResourceNotFound = -32002,  // Previously UnityError - now aligned with C#
  ExecutionError = -32003,    // New - aligned with C#
  TimeoutError = -32004,      // Moved to new code
  UnityError = -32005,        // Moved to new code (generic Unity errors)
}

export class McpError extends Error {
  constructor(
    public code: McpErrorCode,
    message: string,
    public data?: unknown
  ) {
    super(message);
    this.name = "McpError";
  }

  toJsonRpcError(): JsonRpcError {
    return {
      code: this.code,
      message: this.message,
      data: this.data,
    };
  }
}
