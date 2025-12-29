#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
  ListPromptsRequestSchema,
  GetPromptRequestSchema,
  ErrorCode,
  McpError as SdkMcpError,
} from "@modelcontextprotocol/sdk/types.js";
import { UnityBridge } from "./UnityBridge.js";
import {
  BridgeConfig,
  ConnectionState,
  ToolDefinition,
  ToolResult,
  ResourceDefinition,
  ResourceReadResult,
  PromptDefinition,
  PromptGetResult,
} from "./types.js";

// ============================================================================
// Configuration
// ============================================================================

function getConfig(): Partial<BridgeConfig> {
  return {
    unityHost: process.env.UNITY_HOST || "localhost",
    unityPort: parseInt(process.env.UNITY_PORT || "8090", 10),
    reconnectInterval: parseInt(process.env.RECONNECT_INTERVAL || "3000", 10),
    requestTimeout: parseInt(process.env.REQUEST_TIMEOUT || "10000", 10), // 10 seconds for Unity Editor response
    maxReconnectAttempts: parseInt(process.env.MAX_RECONNECT_ATTEMPTS || "3", 10),
    debug: process.env.DEBUG === "true" || process.env.DEBUG === "1",
  };
}

// ============================================================================
// Logging
// ============================================================================

function log(message: string, ...args: unknown[]): void {
  console.error(`[MCP Unity] ${message}`, ...args);
}

// ============================================================================
// Main Application
// ============================================================================

async function main(): Promise<void> {
  const config = getConfig();
  log(`Starting MCP Unity Bridge`);
  log(`Unity endpoint: ws://${config.unityHost}:${config.unityPort}`);

  // Create Unity bridge
  const bridge = new UnityBridge(config);

  // Setup bridge event handlers
  bridge.on("connected", () => {
    log("Connected to Unity");
  });

  bridge.on("disconnected", () => {
    log("Disconnected from Unity");
  });

  bridge.on("reconnected", () => {
    log("Reconnected to Unity");
  });

  bridge.on("reconnectFailed", () => {
    log("Failed to reconnect to Unity after maximum attempts");
  });

  bridge.on("error", (error: Error) => {
    log("Bridge error:", error.message);
  });

  bridge.on("stateChange", (newState: ConnectionState, oldState: ConnectionState) => {
    log(`Connection state: ${oldState} -> ${newState}`);
  });

  // ============================================================================
  // Server-side Cache (Phase 3: Token Optimization)
  // ============================================================================

  interface CacheEntry {
    data: unknown;
    expiry: number;
  }

  class ServerCache {
    private cache = new Map<string, CacheEntry>();

    // TTL values in milliseconds
    private static TTL = {
      hierarchy: 30000,      // 30s - changes frequently
      editorState: 5000,     // 5s - changes very often
      components: 60000,     // 1min - moderately stable
      assets: 300000,        // 5min - rarely changes
      scenes: 300000,        // 5min - rarely changes
    };

    get(key: string): unknown | null {
      const entry = this.cache.get(key);
      if (!entry || Date.now() > entry.expiry) {
        if (entry) this.cache.delete(key);
        return null;
      }
      return entry.data;
    }

    set(key: string, data: unknown, category: keyof typeof ServerCache.TTL = 'components'): void {
      const ttl = ServerCache.TTL[category] || 60000;
      this.cache.set(key, { data, expiry: Date.now() + ttl });
    }

    invalidate(pattern: string): void {
      for (const key of this.cache.keys()) {
        if (key.includes(pattern)) this.cache.delete(key);
      }
    }

    clear(): void {
      this.cache.clear();
    }

    stats(): { size: number; keys: string[] } {
      return { size: this.cache.size, keys: Array.from(this.cache.keys()) };
    }
  }

  const serverCache = new ServerCache();

  // Server instructions for Claude - OPTIMIZED (~1200 tokens vs ~4000)
  const serverInstructions = `Unity MCP (52 tools). Use outputMode='tree' for lists (90% smaller).

CORE (always loaded): unity_get_editor_state, unity_list_gameobjects, unity_get_component, unity_modify_component, unity_create_gameobject

CATEGORIES (Tool Search by prefix):
• ANIMATOR:16 (states, transitions, blend trees, clips)
• ASSET:5 (search, info, preview, folders)
• SCENE:4 | PREFAB:4 | MATERIAL:3 | MEMORY:3 | EDITOR:8

TOKEN RULES:
• outputMode='tree' for unity_list_gameobjects
• returnBase64=false for screenshots
• size='small' for previews
• maxResults=20 for searches

WORKFLOW: list_gameobjects(tree) → get_component → modify_component → save_scene

RESOURCES: Read workflows://[category] for detailed docs (animator, materials, prefabs, assets).`;

  // Create MCP server
  const server = new Server(
    {
      name: "mcp-unity",
      version: "1.0.0",
    },
    {
      capabilities: {
        tools: {},
        resources: {},
        prompts: {},
      },
      instructions: serverInstructions,
    }
  );

  // ============================================================================
  // Tool Handlers
  // ============================================================================

  // Default tools - OPTIMIZED descriptions (Phase 2: -40% tokens)
  const defaultTools: ToolDefinition[] = [
    // === CORE TOOLS (always loaded) ===
    {
      name: "unity_get_editor_state",
      description: "Get editor state (play mode, selection, scene)",
      inputSchema: { type: "object", properties: {} }
    },
    {
      name: "unity_list_gameobjects",
      description: "List GameObjects. Use outputMode='tree' (90% smaller)",
      inputSchema: {
        type: "object",
        properties: {
          outputMode: { type: "string", enum: ["names", "tree", "summary", "full"], description: "Format: tree|names|summary|full (default: tree)" },
          maxDepth: { type: "integer", description: "Max depth (default: 3)" },
          includeInactive: { type: "boolean" },
          rootOnly: { type: "boolean" },
          nameFilter: { type: "string", description: "Name pattern (*=wildcard)" },
          componentFilter: { type: "string", description: "Filter by component" },
          tagFilter: { type: "string" },
          includeTransform: { type: "boolean" }
        }
      }
    },
    {
      name: "unity_get_component",
      description: "Get component properties",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "GameObject path" },
          componentType: { type: "string", description: "Component type" }
        },
        required: ["gameObjectPath", "componentType"]
      }
    },
    {
      name: "unity_modify_component",
      description: "Modify component properties",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string" },
          componentType: { type: "string" },
          properties: { type: "object", description: "Properties to set" }
        },
        required: ["gameObjectPath", "componentType", "properties"]
      }
    },
    {
      name: "unity_create_gameobject",
      description: "Create GameObject",
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string" },
          primitiveType: { type: "string", enum: ["Empty", "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"] },
          parentPath: { type: "string" },
          position: { type: "object", properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } } }
        },
        required: ["name"]
      }
    },
    // === DEFERRED TOOLS (loaded on demand) ===
    {
      name: "unity_execute_menu_item",
      description: "EDITOR: Execute menu item",
      inputSchema: { type: "object", properties: { menuPath: { type: "string" } }, required: ["menuPath"] },
      defer_loading: true
    },
    {
      name: "unity_run_tests",
      description: "EDITOR: Run tests",
      inputSchema: { type: "object", properties: { testMode: { type: "string" }, testFilter: { type: "string" } } },
      defer_loading: true
    },
    {
      name: "unity_delete_gameobject",
      description: "CORE: Delete GameObject",
      inputSchema: { type: "object", properties: { path: { type: "string" } }, required: ["path"] },
      defer_loading: true
    },
    {
      name: "unity_add_component",
      description: "CORE: Add component (Unity built-in OR custom scripts)",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string" },
          componentType: { type: "string", description: "Component name (e.g., 'Rigidbody', 'MyCustomScript')" },
          initialProperties: { type: "object" }
        },
        required: ["gameObjectPath", "componentType"]
      },
      defer_loading: true
    },
    {
      name: "unity_list_project_scripts",
      description: "SCRIPT: List all MonoBehaviour scripts in project (use with add_component)",
      inputSchema: {
        type: "object",
        properties: {
          nameFilter: { type: "string", description: "Filter by name (*=wildcard)" },
          includeNamespace: { type: "boolean", description: "Include namespace info" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_get_console_logs",
      description: "EDITOR: Get console logs",
      inputSchema: {
        type: "object",
        properties: {
          logType: { type: "string", enum: ["All", "Error", "Warning", "Log", "Exception", "Assert"] },
          count: { type: "integer" },
          includeStackTrace: { type: "boolean" }
        }
      },
      defer_loading: true
    },
    // === ANIMATOR TOOLS ===
    {
      name: "unity_get_animator_controller",
      description: "ANIMATOR: Get controller structure",
      inputSchema: { type: "object", properties: { controllerPath: { type: "string" }, gameObjectPath: { type: "string" } } },
      defer_loading: true
    },
    {
      name: "unity_get_animator_parameters",
      description: "ANIMATOR: Get parameters",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" } }, required: ["gameObjectPath"] },
      defer_loading: true
    },
    {
      name: "unity_set_animator_parameter",
      description: "ANIMATOR: Set parameter",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string" },
          parameterName: { type: "string" },
          value: { type: "number" },
          parameterType: { type: "string", enum: ["Float", "Int", "Bool", "Trigger"] }
        },
        required: ["gameObjectPath", "parameterName"]
      },
      defer_loading: true
    },
    {
      name: "unity_add_animator_parameter",
      description: "ANIMATOR: Add parameter",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" },
          parameterName: { type: "string" },
          parameterType: { type: "string", enum: ["Float", "Int", "Bool", "Trigger"] },
          defaultValue: { type: "number" }
        },
        required: ["controllerPath", "parameterName", "parameterType"]
      },
      defer_loading: true
    },
    {
      name: "unity_add_animator_state",
      description: "ANIMATOR: Add state",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" },
          stateName: { type: "string" },
          layerIndex: { type: "integer" },
          position: { type: "object", properties: { x: { type: "number" }, y: { type: "number" } } },
          motionClip: { type: "string" }
        },
        required: ["controllerPath", "stateName"]
      },
      defer_loading: true
    },
    {
      name: "unity_add_animator_transition",
      description: "ANIMATOR: Add transition",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" },
          fromState: { type: "string" },
          toState: { type: "string" },
          layerIndex: { type: "integer" },
          conditions: { type: "array", items: { type: "object" } },
          hasExitTime: { type: "boolean" },
          exitTime: { type: "number" },
          transitionDuration: { type: "number" }
        },
        required: ["controllerPath", "fromState", "toState"]
      },
      defer_loading: true
    },
    {
      name: "unity_create_blend_tree",
      description: "ANIMATOR: Create blend tree",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" },
          stateName: { type: "string" },
          blendType: { type: "string", enum: ["1D", "2DSimpleDirectional", "2DFreeformDirectional", "2DFreeformCartesian"] },
          blendParameter: { type: "string" },
          blendParameterY: { type: "string" },
          layerIndex: { type: "integer" }
        },
        required: ["controllerPath", "stateName", "blendParameter"]
      },
      defer_loading: true
    },
    {
      name: "unity_add_blend_motion",
      description: "ANIMATOR: Add blend motion",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" },
          blendTreeState: { type: "string" },
          motionPath: { type: "string" },
          threshold: { type: "number" },
          positionX: { type: "number" },
          positionY: { type: "number" },
          layerIndex: { type: "integer" }
        },
        required: ["controllerPath", "blendTreeState", "motionPath"]
      },
      defer_loading: true
    },
    {
      name: "unity_delete_animator_state",
      description: "ANIMATOR: Delete state",
      inputSchema: {
        type: "object",
        properties: { controllerPath: { type: "string" }, stateName: { type: "string" }, layerIndex: { type: "integer" } },
        required: ["controllerPath", "stateName"]
      },
      defer_loading: true
    },
    {
      name: "unity_delete_animator_transition",
      description: "ANIMATOR: Delete transition",
      inputSchema: {
        type: "object",
        properties: { controllerPath: { type: "string" }, fromState: { type: "string" }, toState: { type: "string" }, transitionIndex: { type: "integer" }, layerIndex: { type: "integer" } },
        required: ["controllerPath", "fromState", "toState"]
      },
      defer_loading: true
    },
    {
      name: "unity_modify_animator_state",
      description: "ANIMATOR: Modify state",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" }, stateName: { type: "string" }, layerIndex: { type: "integer" },
          newName: { type: "string" }, motion: { type: "string" }, speed: { type: "number" },
          speedParameter: { type: "string" }, cycleOffset: { type: "number" }, mirror: { type: "boolean" }, writeDefaultValues: { type: "boolean" }
        },
        required: ["controllerPath", "stateName"]
      },
      defer_loading: true
    },
    {
      name: "unity_modify_transition",
      description: "ANIMATOR: Modify transition",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string" }, fromState: { type: "string" }, toState: { type: "string" },
          transitionIndex: { type: "integer" }, layerIndex: { type: "integer" }, hasExitTime: { type: "boolean" },
          exitTime: { type: "number" }, duration: { type: "number" }, offset: { type: "number" },
          interruptionSource: { type: "string", enum: ["None", "Source", "Destination", "SourceThenDestination", "DestinationThenSource"] },
          canTransitionToSelf: { type: "boolean" }
        },
        required: ["controllerPath", "fromState", "toState"]
      },
      defer_loading: true
    },
    {
      name: "unity_list_animation_clips",
      description: "ANIMATOR: List clips",
      inputSchema: { type: "object", properties: { searchPath: { type: "string" }, nameFilter: { type: "string" }, avatarFilter: { type: "string", enum: ["Humanoid", "Generic", "Legacy"] } } },
      defer_loading: true
    },
    {
      name: "unity_get_clip_info",
      description: "ANIMATOR: Get clip info",
      inputSchema: { type: "object", properties: { clipPath: { type: "string" } }, required: ["clipPath"] },
      defer_loading: true
    },
    {
      name: "unity_validate_animator",
      description: "ANIMATOR: Validate controller",
      inputSchema: { type: "object", properties: { controllerPath: { type: "string" } }, required: ["controllerPath"] },
      defer_loading: true
    },
    {
      name: "unity_get_animator_flow",
      description: "ANIMATOR: Trace flow paths",
      inputSchema: { type: "object", properties: { controllerPath: { type: "string" }, fromState: { type: "string" }, maxDepth: { type: "integer" }, layerIndex: { type: "integer" } }, required: ["controllerPath"] },
      defer_loading: true
    },
    // === ASSET TOOLS ===
    {
      name: "unity_search_assets",
      description: "ASSET: Search assets (t:Type, l:Label)",
      inputSchema: {
        type: "object",
        properties: {
          filter: { type: "string", description: "t:Prefab, t:Texture2D, etc." },
          searchFolders: { type: "array", items: { type: "string" } },
          maxResults: { type: "integer", description: "Default: 20" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_get_asset_info",
      description: "ASSET: Get asset details",
      inputSchema: { type: "object", properties: { assetPath: { type: "string" }, includeDependencies: { type: "boolean" }, includeReferences: { type: "boolean" } }, required: ["assetPath"] },
      defer_loading: true
    },
    {
      name: "unity_list_folders",
      description: "ASSET: List folders",
      inputSchema: { type: "object", properties: { parentPath: { type: "string" }, recursive: { type: "boolean" }, maxDepth: { type: "integer" } } },
      defer_loading: true
    },
    {
      name: "unity_list_folder_contents",
      description: "ASSET: List folder contents",
      inputSchema: { type: "object", properties: { folderPath: { type: "string" }, typeFilter: { type: "string" }, includeSubfolders: { type: "boolean" } }, required: ["folderPath"] },
      defer_loading: true
    },
    {
      name: "unity_get_asset_preview",
      description: "ASSET: Get thumbnail (use size=small)",
      inputSchema: { type: "object", properties: { assetPath: { type: "string" }, size: { type: "string", enum: ["tiny", "small", "medium", "large"] }, format: { type: "string", enum: ["jpg", "png"] }, jpgQuality: { type: "integer" } }, required: ["assetPath"] },
      defer_loading: true
    },
    // === EDITOR TOOLS ===
    {
      name: "unity_clear_console",
      description: "EDITOR: Clear console",
      inputSchema: { type: "object", properties: {} },
      defer_loading: true
    },
    {
      name: "unity_rename_gameobject",
      description: "CORE: Rename GameObject",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" }, newName: { type: "string" } }, required: ["gameObjectPath", "newName"] },
      defer_loading: true
    },
    {
      name: "unity_set_parent",
      description: "CORE: Set parent",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" }, parentPath: { type: "string" }, worldPositionStays: { type: "boolean" } }, required: ["gameObjectPath"] },
      defer_loading: true
    },
    {
      name: "unity_instantiate_prefab",
      description: "PREFAB: Spawn prefab",
      inputSchema: {
        type: "object",
        properties: {
          prefabPath: { type: "string" },
          position: { type: "object", properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } } },
          rotation: { type: "object", properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } } },
          parentPath: { type: "string" },
          name: { type: "string" }
        },
        required: ["prefabPath"]
      },
      defer_loading: true
    },
    {
      name: "unity_take_screenshot",
      description: "EDITOR: Screenshot (returnBase64=false saves tokens)",
      inputSchema: {
        type: "object",
        properties: {
          view: { type: "string", enum: ["Scene", "Game"] },
          format: { type: "string", enum: ["jpg", "png"] },
          jpgQuality: { type: "integer" },
          width: { type: "integer" },
          height: { type: "integer" },
          savePath: { type: "string" },
          returnBase64: { type: "boolean", description: "Default: false" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_get_selection",
      description: "EDITOR: Get selection",
      inputSchema: { type: "object", properties: { includeAssets: { type: "boolean" } } },
      defer_loading: true
    },
    {
      name: "unity_set_selection",
      description: "EDITOR: Set selection",
      inputSchema: { type: "object", properties: { gameObjectPaths: { type: "array", items: { type: "string" } }, assetPaths: { type: "array", items: { type: "string" } }, clear: { type: "boolean" } } },
      defer_loading: true
    },
    // === SCENE TOOLS ===
    {
      name: "unity_get_scene_info",
      description: "SCENE: Get scene info",
      inputSchema: { type: "object", properties: {} },
      defer_loading: true
    },
    {
      name: "unity_load_scene",
      description: "SCENE: Load scene",
      inputSchema: { type: "object", properties: { scenePath: { type: "string" }, mode: { type: "string", enum: ["Single", "Additive"] } }, required: ["scenePath"] },
      defer_loading: true
    },
    {
      name: "unity_save_scene",
      description: "SCENE: Save scene",
      inputSchema: { type: "object", properties: { scenePath: { type: "string" }, saveAll: { type: "boolean" } } },
      defer_loading: true
    },
    {
      name: "unity_create_scene",
      description: "SCENE: Create scene",
      inputSchema: { type: "object", properties: { sceneName: { type: "string" }, savePath: { type: "string" }, setup: { type: "string", enum: ["default", "empty"] }, mode: { type: "string", enum: ["single", "additive"] } }, required: ["sceneName"] },
      defer_loading: true
    },
    // === PREFAB TOOLS ===
    {
      name: "unity_create_prefab",
      description: "PREFAB: Create prefab",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" }, savePath: { type: "string" }, connectInstance: { type: "boolean" } }, required: ["gameObjectPath", "savePath"] },
      defer_loading: true
    },
    {
      name: "unity_unpack_prefab",
      description: "PREFAB: Unpack prefab",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" }, unpackMode: { type: "string", enum: ["completely", "root"] } }, required: ["gameObjectPath"] },
      defer_loading: true
    },
    {
      name: "unity_apply_prefab_overrides",
      description: "PREFAB: Apply overrides",
      inputSchema: { type: "object", properties: { gameObjectPath: { type: "string" } }, required: ["gameObjectPath"] },
      defer_loading: true
    },
    {
      name: "unity_undo",
      description: "EDITOR: Undo/redo",
      inputSchema: { type: "object", properties: { steps: { type: "integer" }, redo: { type: "boolean" } } },
      defer_loading: true
    },
    // === MEMORY TOOLS ===
    {
      name: "unity_memory_get",
      description: "MEMORY: Get cached data",
      inputSchema: { type: "object", properties: { section: { type: "string", enum: ["assets", "scenes", "hierarchy", "operations", "all"] } } },
      defer_loading: true
    },
    {
      name: "unity_memory_refresh",
      description: "MEMORY: Refresh cache",
      inputSchema: { type: "object", properties: { section: { type: "string", enum: ["assets", "scenes", "hierarchy", "all"] } }, required: ["section"] },
      defer_loading: true
    },
    {
      name: "unity_memory_clear",
      description: "MEMORY: Clear cache",
      inputSchema: { type: "object", properties: { section: { type: "string", enum: ["assets", "scenes", "hierarchy", "operations", "all"] } } },
      defer_loading: true
    },
    // === MATERIAL TOOLS ===
    {
      name: "unity_get_material",
      description: "MATERIAL: Get material",
      inputSchema: { type: "object", properties: { materialPath: { type: "string" }, gameObjectPath: { type: "string" }, materialIndex: { type: "integer" } } },
      defer_loading: true
    },
    {
      name: "unity_set_material",
      description: "MATERIAL: Set material properties",
      inputSchema: {
        type: "object",
        properties: {
          materialPath: { type: "string" },
          gameObjectPath: { type: "string" },
          materialIndex: { type: "integer" },
          properties: { type: "object" },
          shader: { type: "string" },
          renderQueue: { type: "integer" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_create_material",
      description: "MATERIAL: Create material",
      inputSchema: { type: "object", properties: { name: { type: "string" }, savePath: { type: "string" }, shader: { type: "string" }, properties: { type: "object" } }, required: ["name", "savePath"] },
      defer_loading: true
    },
    // === PROJECT SETTINGS TOOLS (6 tools) ===
    {
      name: "unity_get_project_settings",
      description: "SETTINGS: Get settings by category (quality|graphics|physics|physics2d|time|audio|render|player)",
      inputSchema: {
        type: "object",
        properties: {
          category: { type: "string", enum: ["quality", "graphics", "physics", "physics2d", "time", "audio", "render", "player"] },
          detailed: { type: "boolean", description: "Include extended info" }
        },
        required: ["category"]
      },
      defer_loading: true
    },
    {
      name: "unity_set_project_settings",
      description: "SETTINGS: Modify settings (quality|physics|physics2d|time|render)",
      inputSchema: {
        type: "object",
        properties: {
          category: { type: "string", enum: ["quality", "physics", "physics2d", "time", "render"] },
          settings: { type: "object", description: "Key-value pairs" }
        },
        required: ["category", "settings"]
      },
      defer_loading: true
    },
    {
      name: "unity_get_quality_level",
      description: "SETTINGS: Get quality level index and name",
      inputSchema: { type: "object", properties: {} },
      defer_loading: true
    },
    {
      name: "unity_set_quality_level",
      description: "SETTINGS: Set quality level by index or name",
      inputSchema: {
        type: "object",
        properties: {
          level: { type: "integer", description: "Level index (0-based)" },
          levelName: { type: "string", description: "Or level name" },
          applyExpensiveChanges: { type: "boolean" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_get_physics_layer_collision",
      description: "SETTINGS: Get physics collision matrix",
      inputSchema: {
        type: "object",
        properties: {
          layer1: { type: "string", description: "First layer (optional)" },
          layer2: { type: "string", description: "Second layer (optional)" }
        }
      },
      defer_loading: true
    },
    {
      name: "unity_set_physics_layer_collision",
      description: "SETTINGS: Set collision between layers",
      inputSchema: {
        type: "object",
        properties: {
          layer1: { type: "string" },
          layer2: { type: "string" },
          collide: { type: "boolean" }
        },
        required: ["layer1", "layer2", "collide"]
      },
      defer_loading: true
    }
  ];

  server.setRequestHandler(ListToolsRequestSchema, async () => {
    // Return default tools immediately - don't block waiting for Unity
    if (!bridge.isConnected) {
      // Try to connect in background, don't wait
      bridge.connect().catch(() => {});
      return { tools: defaultTools };
    }

    try {
      const result = await bridge.request<{ tools: ToolDefinition[] }>("tools/list");
      return { tools: result.tools || defaultTools };
    } catch (error) {
      log("Error listing tools:", error);
      return { tools: defaultTools };
    }
  });

  // Cacheable tools and their TTL categories
  const cacheableTools: Record<string, 'hierarchy' | 'editorState' | 'components' | 'assets' | 'scenes'> = {
    'unity_get_editor_state': 'editorState',
    'unity_list_gameobjects': 'hierarchy',
    'unity_search_assets': 'assets',
    'unity_get_scene_info': 'scenes',
    'unity_list_folders': 'assets',
    'unity_list_folder_contents': 'assets',
    'unity_memory_get': 'components',
  };

  // Tools that invalidate cache
  const cacheInvalidators: Record<string, string[]> = {
    'unity_create_gameobject': ['hierarchy'],
    'unity_delete_gameobject': ['hierarchy'],
    'unity_modify_component': ['hierarchy', 'components'],
    'unity_add_component': ['hierarchy', 'components'],
    'unity_set_parent': ['hierarchy'],
    'unity_rename_gameobject': ['hierarchy'],
    'unity_instantiate_prefab': ['hierarchy'],
    'unity_load_scene': ['hierarchy', 'scenes'],
    'unity_save_scene': ['scenes'],
    'unity_create_scene': ['hierarchy', 'scenes'],
  };

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    if (!bridge.isConnected) {
      try {
        await bridge.connect();
      } catch (error) {
        throw new SdkMcpError(
          ErrorCode.InternalError,
          `Not connected to Unity: ${error instanceof Error ? error.message : String(error)}`
        );
      }
    }

    const { name, arguments: args } = request.params;

    // Check cache for read operations
    const cacheCategory = cacheableTools[name];
    if (cacheCategory) {
      const cacheKey = `${name}:${JSON.stringify(args || {})}`;
      const cached = serverCache.get(cacheKey);
      if (cached) {
        log(`Cache hit for ${name}`);
        return cached as { content: Array<{ type: string; text: string }>; isError: boolean };
      }
    }

    try {
      const result = await bridge.request<ToolResult>("tools/call", {
        name,
        arguments: args || {},
      });

      const response = {
        content: result.content || [{ type: "text", text: "Tool executed successfully" }],
        isError: result.isError || false,
      };

      // Cache read operations
      if (cacheCategory && !response.isError) {
        const cacheKey = `${name}:${JSON.stringify(args || {})}`;
        serverCache.set(cacheKey, response, cacheCategory);
      }

      // Invalidate cache for write operations
      const invalidations = cacheInvalidators[name];
      if (invalidations) {
        invalidations.forEach(pattern => serverCache.invalidate(pattern));
      }

      return response;
    } catch (error) {
      log(`Error calling tool '${name}':`, error);

      return {
        content: [
          {
            type: "text",
            text: `Error executing tool '${name}': ${error instanceof Error ? error.message : String(error)}`,
          },
        ],
        isError: true,
      };
    }
  });

  // ============================================================================
  // Resource Handlers
  // ============================================================================

  // Default resources - OPTIMIZED with workflow documentation (Phase 5)
  const defaultResources: ResourceDefinition[] = [
    {
      uri: "unity://project/settings",
      name: "Project Settings",
      description: "Unity project settings",
      mimeType: "application/json"
    },
    {
      uri: "unity://scene/hierarchy",
      name: "Scene Hierarchy",
      description: "Current scene hierarchy",
      mimeType: "application/json"
    },
    {
      uri: "unity://console/logs",
      name: "Console Logs",
      description: "Recent console logs",
      mimeType: "application/json"
    },
    // === WORKFLOW DOCUMENTATION RESOURCES (Phase 5) ===
    {
      uri: "workflows://core",
      name: "Core Workflows",
      description: "Essential Unity MCP workflows and best practices",
      mimeType: "text/markdown"
    },
    {
      uri: "workflows://animator",
      name: "Animator Guide",
      description: "Complete Animator Controller workflow guide",
      mimeType: "text/markdown"
    },
    {
      uri: "workflows://materials",
      name: "Materials Guide",
      description: "Materials and shaders workflow guide",
      mimeType: "text/markdown"
    },
    {
      uri: "workflows://prefabs",
      name: "Prefabs Guide",
      description: "Prefab workflow guide",
      mimeType: "text/markdown"
    },
    {
      uri: "workflows://assets",
      name: "Assets Guide",
      description: "Asset browser and search workflow guide",
      mimeType: "text/markdown"
    }
  ];

  // Workflow documentation content (served from memory, no Unity connection needed)
  const workflowDocs: Record<string, string> = {
    "workflows://core": `# Unity MCP Core Workflows

## Basic Workflow
1. \`unity_get_editor_state\` - Check play mode
2. \`unity_list_gameobjects\` with outputMode='tree' - See hierarchy
3. \`unity_get_component\` - Inspect properties
4. \`unity_modify_component\` - Make changes
5. \`unity_save_scene\` - Persist changes

## Token Optimization
- ALWAYS use outputMode='tree' for lists (90% smaller)
- Use returnBase64=false for screenshots
- Use size='small' for asset previews
- Limit maxResults to 20 for searches

## Common Components
Transform, Rigidbody, BoxCollider, SphereCollider, MeshRenderer, AudioSource, Animator`,

    "workflows://animator": `# Animator Controller Workflow

## Reading Controllers
\`\`\`
unity_get_animator_controller({ controllerPath: "Assets/Animations/Player.controller" })
// OR from GameObject:
unity_get_animator_controller({ gameObjectPath: "Player" })
\`\`\`

## Runtime Control
\`\`\`
unity_set_animator_parameter({ gameObjectPath: "Player", parameterName: "Speed", value: 5.0 })
unity_set_animator_parameter({ gameObjectPath: "Player", parameterName: "Jump", parameterType: "Trigger" })
\`\`\`

## Building State Machines
1. Add parameters: unity_add_animator_parameter
2. Add states: unity_add_animator_state
3. Add transitions: unity_add_animator_transition with conditions
4. Create blend trees: unity_create_blend_tree + unity_add_blend_motion

## Transition Conditions
Modes: Greater, Less, Equals, NotEqual, If (bool true), IfNot (bool false)`,

    "workflows://materials": `# Materials Workflow

## Reading Materials
\`\`\`
unity_get_material({ materialPath: "Assets/Materials/Player.mat" })
// OR from GameObject:
unity_get_material({ gameObjectPath: "Cube" })
\`\`\`

## Modifying Materials
\`\`\`
unity_set_material({
  materialPath: "Assets/Materials/Player.mat",
  properties: { "_Color": {r:1,g:0,b:0,a:1}, "_Metallic": 0.9 }
})
\`\`\`

## Common Properties
- Standard: _Color, _MainTex, _Metallic, _Glossiness, _BumpMap, _EmissionColor
- URP/Lit: _BaseColor, _BaseMap, _Metallic, _Smoothness
- All properties start with underscore (_)

## Render Pipeline Auto-Detection
System auto-detects URP/HDRP/Built-in. "Standard" maps to "Universal Render Pipeline/Lit" in URP.`,

    "workflows://prefabs": `# Prefab Workflow

## Creating Prefabs
\`\`\`
unity_create_prefab({
  gameObjectPath: "Player",
  savePath: "Assets/Prefabs/Player.prefab"
})
\`\`\`

## Instantiating Prefabs
\`\`\`
unity_instantiate_prefab({
  prefabPath: "Assets/Prefabs/Enemy.prefab",
  position: {x: 5, y: 0, z: 0}
})
\`\`\`

## Modifying Prefabs
1. Modify instance in scene
2. Apply changes: unity_apply_prefab_overrides({ gameObjectPath: "Enemy(Clone)" })

## Breaking Prefab Link
\`\`\`
unity_unpack_prefab({ gameObjectPath: "Enemy(Clone)", unpackMode: "completely" })
\`\`\``,

    "workflows://assets": `# Asset Browser Workflow

## Search Syntax
- Type: t:Texture2D, t:Prefab, t:Material, t:AnimationClip, t:AudioClip, t:Script
- Label: l:Environment, l:Player
- Name: Player, Enemy*
- Combined: "t:Prefab Player", "t:Texture2D l:UI"

## Workflow
1. List folders: unity_list_folders()
2. Browse: unity_list_folder_contents({ folderPath: "Assets/Prefabs" })
3. Search: unity_search_assets({ filter: "t:Prefab Player", maxResults: 20 })
4. Details: unity_get_asset_info({ assetPath: "...", includeDependencies: true })
5. Preview: unity_get_asset_preview({ assetPath: "...", size: "small" })

## Token Tips
- Use maxResults=20 (default)
- Use size='small' for previews
- Avoid includeReferences (slow)`
  };

  server.setRequestHandler(ListResourcesRequestSchema, async () => {
    // Return default resources immediately - don't block waiting for Unity
    if (!bridge.isConnected) {
      bridge.connect().catch(() => {});
      return { resources: defaultResources };
    }

    try {
      const result = await bridge.request<{ resources: ResourceDefinition[] }>("resources/list");
      return { resources: result.resources || defaultResources };
    } catch (error) {
      log("Error listing resources:", error);
      return { resources: defaultResources };
    }
  });

  server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
    const { uri } = request.params;

    // Serve workflow documentation from memory (no Unity connection needed)
    if (uri.startsWith("workflows://")) {
      const content = workflowDocs[uri];
      if (content) {
        return {
          contents: [{
            uri,
            mimeType: "text/markdown",
            text: content
          }]
        };
      }
      throw new SdkMcpError(ErrorCode.InvalidRequest, `Unknown workflow: ${uri}`);
    }

    // For Unity resources, require connection
    if (!bridge.isConnected) {
      try {
        await bridge.connect();
      } catch (error) {
        throw new SdkMcpError(
          ErrorCode.InternalError,
          `Not connected to Unity: ${error instanceof Error ? error.message : String(error)}`
        );
      }
    }

    try {
      const result = await bridge.request<ResourceReadResult>("resources/read", { uri });
      return { contents: result.contents || [] };
    } catch (error) {
      log(`Error reading resource '${uri}':`, error);
      throw new SdkMcpError(
        ErrorCode.InternalError,
        `Failed to read resource: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  });

  // ============================================================================
  // Prompt Handlers
  // ============================================================================

  server.setRequestHandler(ListPromptsRequestSchema, async () => {
    // Return empty prompts immediately - don't block waiting for Unity
    if (!bridge.isConnected) {
      bridge.connect().catch(() => {});
      return { prompts: [] };
    }

    try {
      const result = await bridge.request<{ prompts: PromptDefinition[] }>("prompts/list");
      return { prompts: result.prompts || [] };
    } catch (error) {
      log("Error listing prompts:", error);
      return { prompts: [] };
    }
  });

  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    if (!bridge.isConnected) {
      try {
        await bridge.connect();
      } catch (error) {
        throw new SdkMcpError(
          ErrorCode.InternalError,
          `Not connected to Unity: ${error instanceof Error ? error.message : String(error)}`
        );
      }
    }

    const { name, arguments: args } = request.params;

    try {
      const result = await bridge.request<PromptGetResult>("prompts/get", {
        name,
        arguments: args || {},
      });

      return {
        description: result.description,
        messages: result.messages || [],
      };
    } catch (error) {
      log(`Error getting prompt '${name}':`, error);
      throw new SdkMcpError(
        ErrorCode.InternalError,
        `Failed to get prompt: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  });

  // ============================================================================
  // Server Lifecycle
  // ============================================================================

  // Handle server errors
  server.onerror = (error) => {
    log("Server error:", error);
  };

  // Handle process termination
  const cleanup = async () => {
    log("Shutting down...");
    await bridge.disconnect();
    process.exit(0);
  };

  process.on("SIGINT", cleanup);
  process.on("SIGTERM", cleanup);
  process.on("SIGHUP", cleanup);

  // Start MCP server with stdio transport FIRST (don't block on Unity)
  const transport = new StdioServerTransport();
  await server.connect(transport);
  log("MCP server running");

  // Attempt initial connection to Unity in background (non-blocking)
  bridge.connect().catch((error) => {
    log("Initial connection to Unity failed:", error instanceof Error ? error.message : String(error));
    log("Will retry when requests are made...");
  });
}

// ============================================================================
// Entry Point
// ============================================================================

main().catch((error) => {
  console.error("Fatal error:", error);
  process.exit(1);
});
