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

  // Server instructions for Claude - injected into system prompt
  const serverInstructions = `You are connected to the Unity Editor via MCP. Follow these guidelines:

TOKEN-EFFICIENT WORKFLOW (CRITICAL):
1. ALWAYS use outputMode='tree' or 'summary' for unity_list_gameobjects (90% smaller)
2. NEVER request full hierarchy unless specifically needed
3. Use returnBase64=false for screenshots (just saves to file)
4. Use size='small' (64px) for asset previews

UNITY WORKFLOW:
1. Use 'unity_list_gameobjects' with outputMode='tree' FIRST to understand scene structure
2. When creating objects, specify meaningful names and use primitiveType for quick prototyping
3. Always confirm deletions by showing the user what will be deleted
4. Use parentPath to organize objects hierarchically (e.g., "Environment/Props")

TOOL RELATIONSHIPS:
- unity_get_editor_state → Check if Unity is in Play mode before making scene changes
- unity_list_gameobjects → Required context before create/delete operations
- unity_execute_menu_item → Use for actions like "File/Save Project" after making changes
- unity_get_component → Inspect component properties before modifying them
- unity_add_component → Add physics (Rigidbody), colliders, or other components
- unity_modify_component → Change Transform position/rotation/scale, Rigidbody mass, etc.

COMPONENT TOOLS:
- Use unity_get_component to read properties: unity_get_component("Cube", "Transform")
- Add physics with: unity_add_component("Player", "Rigidbody", {mass: 2.0})
- Move objects: unity_modify_component("Cube", "Transform", {localPosition: {x: 0, y: 5, z: 0}})
- Common components: Transform, Rigidbody, BoxCollider, SphereCollider, MeshRenderer, AudioSource

DEBUGGING:
- Use unity_get_console_logs to see recent Unity console output
- Filter logs by type: unity_get_console_logs({logType: "Error"}) for errors only
- Include stack traces for debugging: unity_get_console_logs({includeStackTrace: true})

ANIMATOR CONTROLLER TOOLS:
- unity_get_animator_controller → Read full structure: states, transitions, parameters, layers
- unity_get_animator_parameters → Get runtime parameter values from a GameObject's Animator
- unity_set_animator_parameter → Control animations: SetFloat("Speed", 5.0), SetTrigger("Jump")
- unity_add_animator_parameter → Add new parameters (Float, Int, Bool, Trigger) to a controller
- unity_add_animator_state → Create new animation states in the graph
- unity_add_animator_transition → Connect states with conditions (Speed > 0.1, IsRunning == true)

ANIMATOR WORKFLOW:
1. Read structure first: unity_get_animator_controller(gameObjectPath: "Player")
2. Control animations at runtime: unity_set_animator_parameter("Player", "Speed", 5.0)
3. Build state machines: add states, then connect with transitions and conditions

ASSET BROWSER TOOLS (5 tools):
- unity_search_assets: Search assets with filters (t:Type, l:label, name patterns)
- unity_get_asset_info: Get detailed asset info (type, size, dependencies, type-specific data)
- unity_list_folders: List project folder structure
- unity_list_folder_contents: List assets in a specific folder
- unity_get_asset_preview: Get Base64 preview thumbnail of an asset

ASSET SEARCH SYNTAX:
- Type filter: "t:Texture2D", "t:Prefab", "t:Material", "t:AnimationClip", "t:AudioClip", "t:Script"
- Label filter: "l:Environment", "l:Player"
- Name pattern: "Player", "Enemy*"
- Combined: "t:Prefab Player", "t:Texture2D l:UI"

ASSET WORKFLOW:
1. Explore structure: unity_list_folders() to see project organization
2. Browse folder: unity_list_folder_contents("Assets/Prefabs") to see contents
3. Search specific: unity_search_assets(filter: "t:Prefab Player")
4. Get details: unity_get_asset_info("Assets/Prefabs/Player.prefab", includeDependencies: true)
5. Preview asset: unity_get_asset_preview("Assets/Textures/icon.png") for visual

WORKFLOW ENHANCEMENT TOOLS (5 tools):
- unity_clear_console: Clear Unity console log
- unity_rename_gameobject: Rename a GameObject
- unity_set_parent: Change GameObject parent in hierarchy
- unity_instantiate_prefab: Spawn prefab with prefab link maintained
- unity_take_screenshot: Capture Scene or Game view as PNG

PREFAB TOOLS (4 tools):
- unity_instantiate_prefab: Spawn prefab instance in scene (maintains prefab link)
- unity_create_prefab: Create prefab from existing GameObject
- unity_unpack_prefab: Break prefab link (for one-off modifications)
- unity_apply_prefab_overrides: Push changes from instance to source prefab

PREFAB WORKFLOW:
1. Create prefab: unity_create_prefab("Player", "Assets/Prefabs/Player.prefab")
2. Spawn instances: unity_instantiate_prefab("Assets/Prefabs/Player.prefab", position: {x:5, y:0, z:0})
3. Modify instance, then apply: unity_apply_prefab_overrides("Player(Clone)")
4. Make unique copy: unity_unpack_prefab("Enemy(Clone)", unpackMode: "completely")

SCREENSHOT OPTIONS (OPTIMIZED):
- Format: 'jpg' (default, 10x smaller) or 'png' (lossless)
- Size: 640x360 default (use higher only if needed)
- returnBase64: false (default) - saves tokens, just returns file path
- View: 'Scene' (default) or 'Game' (requires Play Mode)
- Example: unity_take_screenshot() → saves JPG, returns path only (~50 tokens)
- Example: unity_take_screenshot({returnBase64: true}) → includes image data (~15k tokens)

ASSET PREVIEW OPTIONS (OPTIMIZED):
- Size presets: 'tiny'(32px), 'small'(64px default), 'medium'(128px), 'large'(256px)
- Format: 'jpg' (default, smaller) or 'png'
- Example: unity_get_asset_preview({assetPath: "...", size: "tiny"}) → minimal tokens

SELECTION & SCENE TOOLS (7 tools):
- unity_get_selection: Get currently selected objects in editor (Hierarchy + Project)
- unity_set_selection: Select GameObjects or assets programmatically
- unity_get_scene_info: Get active scene info (name, path, isDirty, rootCount)
- unity_create_scene: Create new scene (default with Camera+Light, or empty)
- unity_load_scene: Open a scene (Single or Additive mode)
- unity_save_scene: Save current scene or all open scenes
- unity_undo: Undo/redo editor actions (supports multiple steps)

SELECTION WORKFLOW:
1. Check current selection: unity_get_selection()
2. Select objects: unity_set_selection(gameObjectPaths: ["Player", "Camera"])
3. Select assets: unity_set_selection(assetPaths: ["Assets/Prefabs/Enemy.prefab"])
4. Clear selection: unity_set_selection(clear: true)

SCENE WORKFLOW:
1. Check scene state: unity_get_scene_info() - see name, path, isDirty
2. Create new scene: unity_create_scene("Level1", savePath: "Assets/Scenes/Level1.unity")
3. Create empty scene: unity_create_scene("Empty", setup: "empty")
4. Save if dirty: unity_save_scene()
5. Load existing: unity_load_scene(scenePath: "Assets/Scenes/Level2.unity")
6. Load additively: unity_load_scene(scenePath: "...", mode: "Additive")

UNDO WORKFLOW:
- Undo last action: unity_undo()
- Undo multiple: unity_undo(steps: 3)
- Redo: unity_undo(redo: true)

MEMORY/CACHE TOOLS (3 tools):
- unity_memory_get: Get cached data (assets, scenes, hierarchy) without re-scanning
- unity_memory_refresh: Refresh cache sections when stale (auto-expires after 5 min)
- unity_memory_clear: Clear cache to force fresh fetch

MEMORY WORKFLOW (Token Optimization):
1. First contact: unity_memory_get() - Check if cache exists
2. If stale/empty: unity_memory_refresh(section: "all") - Populate cache
3. Quick queries: unity_memory_get(section: "assets") - Read from cache
4. After changes: unity_memory_refresh(section: "hierarchy") - Update specific section
5. Cache stores: assets by type, scene list, hierarchy summary, recent operations

CACHE STRUCTURE:
- assets: { count, byType: { Prefab: [...], Script: [...], Scene: [...] } }
- scenes: { active, activePath, list: [...] }
- hierarchy: { scene, rootObjects, rootCount, totalCount, summary }
- operations: [{ time, tool, result }, ...] (last 20 operations)

MATERIAL TOOLS (3 tools):
- unity_get_material: Read material properties (colors, floats, textures, shader, keywords)
- unity_set_material: Modify material properties or change shader
- unity_create_material: Create new material asset with specified shader

MATERIAL WORKFLOW:
1. Inspect: unity_get_material(materialPath: "Assets/Materials/Player.mat")
2. Modify: unity_set_material(materialPath: "...", properties: { "_Color": {r:1,g:0,b:0,a:1}, "_Metallic": 0.9 })
3. Create: unity_create_material(name: "NewMat", savePath: "Assets/Materials/NewMat.mat", shader: "Standard")
4. From object: unity_get_material(gameObjectPath: "Cube") to get material from renderer

RENDER PIPELINE AUTO-DETECTION:
- System automatically detects URP, HDRP, or Built-in pipeline
- "Standard" shader auto-converts to "Universal Render Pipeline/Lit" in URP projects
- Property names are auto-mapped (_Color → _BaseColor in URP)
- You can use Standard shader property names (_Color, _Glossiness) - they'll be mapped automatically

COMMON SHADER PROPERTIES:
- Standard/Built-in: _Color, _MainTex, _Metallic, _Glossiness, _BumpMap, _EmissionColor
- URP/Lit: _BaseColor, _BaseMap, _Metallic, _Smoothness, _BumpMap
- Unlit/Color: _Color
- Note: All property names start with underscore (_)

PROPERTY VALUE FORMATS:
- Colors: {r: 1, g: 0, b: 0, a: 1} (values 0-1)
- Floats: 0.5 (e.g., _Metallic, _Glossiness)
- Vectors: {x: 1, y: 1, z: 0, w: 0}
- Textures: "Assets/Textures/albedo.png" (asset path)

BEST PRACTICES:
- Position new objects using the position parameter {x, y, z} for precise placement
- Check the scene hierarchy depth with maxDepth parameter (default: 5) for large scenes
- Use includeInactive: false when you only need active objects
- Use unity_save_scene() directly instead of unity_execute_menu_item("File/Save")
- Always get component properties first before modifying to understand current values
- Check console logs after operations to verify success or catch errors`;

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

  // Default tools available even when Unity is not connected
  const defaultTools: ToolDefinition[] = [
    {
      name: "unity_execute_menu_item",
      description: "Execute a Unity Editor menu item by path (requires Unity connection)",
      inputSchema: {
        type: "object",
        properties: {
          menuPath: { type: "string", description: "The menu item path (e.g., 'File/Save Project')" }
        },
        required: ["menuPath"]
      }
    },
    {
      name: "unity_get_editor_state",
      description: "Get current Unity Editor state including play mode, selected objects, etc.",
      inputSchema: { type: "object", properties: {} }
    },
    {
      name: "unity_run_tests",
      description: "Run Unity Test Runner tests",
      inputSchema: {
        type: "object",
        properties: {
          testMode: { type: "string", description: "Test mode: 'EditMode' or 'PlayMode'" },
          testFilter: { type: "string", description: "Optional filter for test names" }
        }
      }
    },
    {
      name: "unity_list_gameobjects",
      description: "List GameObjects. Use outputMode='tree' for compact view (saves 90% tokens)",
      inputSchema: {
        type: "object",
        properties: {
          outputMode: {
            type: "string",
            description: "Output format: 'tree' (compact ASCII), 'names' (just names), 'summary' (names+components), 'full' (all details). Default: 'summary'",
            enum: ["names", "tree", "summary", "full"]
          },
          maxDepth: { type: "integer", description: "Maximum hierarchy depth (default: 3)" },
          includeInactive: { type: "boolean", description: "Include inactive GameObjects (default: false)" },
          rootOnly: { type: "boolean", description: "Only return root objects, no children (default: false)" },
          nameFilter: { type: "string", description: "Filter by name pattern (supports * wildcard, e.g., 'Enemy*')" },
          componentFilter: { type: "string", description: "Only objects with this component (e.g., 'Rigidbody')" },
          tagFilter: { type: "string", description: "Filter by tag (e.g., 'Player')" },
          includeTransform: { type: "boolean", description: "Include position/rotation/scale in 'full' mode (default: false)" }
        }
      }
    },
    {
      name: "unity_create_gameobject",
      description: "Create a new GameObject in the scene",
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string", description: "Name of the new GameObject" },
          primitiveType: {
            type: "string",
            description: "Optional primitive type: Empty, Cube, Sphere, Capsule, Cylinder, Plane, Quad",
            enum: ["Empty", "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"]
          },
          parentPath: { type: "string", description: "Optional path to parent GameObject (e.g., 'Environment/Props')" },
          position: {
            type: "object",
            description: "Optional position {x, y, z}",
            properties: {
              x: { type: "number" },
              y: { type: "number" },
              z: { type: "number" }
            }
          }
        },
        required: ["name"]
      }
    },
    {
      name: "unity_delete_gameobject",
      description: "Delete a GameObject from the scene by name or path",
      inputSchema: {
        type: "object",
        properties: {
          path: { type: "string", description: "Path or name of the GameObject to delete (e.g., 'Player' or 'Environment/Props/Tree')" }
        },
        required: ["path"]
      }
    },
    {
      name: "unity_get_component",
      description: "Get properties of a specific component on a GameObject",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject (e.g., 'Player' or 'Environment/Props/Tree')" },
          componentType: { type: "string", description: "Type name of the component (e.g., 'Transform', 'Rigidbody', 'MeshRenderer')" }
        },
        required: ["gameObjectPath", "componentType"]
      }
    },
    {
      name: "unity_add_component",
      description: "Add a component to a GameObject",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject" },
          componentType: { type: "string", description: "Type name of the component to add (e.g., 'Rigidbody', 'BoxCollider', 'AudioSource')" },
          initialProperties: { type: "object", description: "Optional initial properties to set on the component (e.g., {\"mass\": 2.0})" }
        },
        required: ["gameObjectPath", "componentType"]
      }
    },
    {
      name: "unity_modify_component",
      description: "Modify properties of an existing component on a GameObject",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject" },
          componentType: { type: "string", description: "Type name of the component to modify (e.g., 'Transform', 'Rigidbody')" },
          properties: { type: "object", description: "Properties to modify (e.g., {\"mass\": 2.0} for Rigidbody, {\"localPosition\": {\"x\": 0, \"y\": 1, \"z\": 0}} for Transform)" }
        },
        required: ["gameObjectPath", "componentType", "properties"]
      }
    },
    {
      name: "unity_get_console_logs",
      description: "Get recent Unity console logs (errors, warnings, and messages)",
      inputSchema: {
        type: "object",
        properties: {
          logType: {
            type: "string",
            description: "Filter by log type: 'All', 'Error', 'Warning', 'Log' (default: 'All')",
            enum: ["All", "Error", "Warning", "Log", "Exception", "Assert"]
          },
          count: { type: "integer", description: "Maximum number of logs to return (default: 50, max: 100)" },
          includeStackTrace: { type: "boolean", description: "Include stack traces in output (default: false)" }
        }
      }
    },
    // Animator Controller tools
    {
      name: "unity_get_animator_controller",
      description: "Get the complete structure of an Animator Controller (states, transitions, parameters, layers)",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string", description: "Path to the AnimatorController asset (e.g., 'Assets/Animations/Player.controller')" },
          gameObjectPath: { type: "string", description: "Alternative: Path to a GameObject with an Animator component" }
        }
      }
    },
    {
      name: "unity_get_animator_parameters",
      description: "Get all animator parameters with their current runtime values from a GameObject",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject with Animator component" }
        },
        required: ["gameObjectPath"]
      }
    },
    {
      name: "unity_set_animator_parameter",
      description: "Set an animator parameter value (Float, Int, Bool, or Trigger) at runtime",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject with Animator component" },
          parameterName: { type: "string", description: "Name of the parameter to set" },
          value: { type: "number", description: "Value to set (number for Float/Int, true/false for Bool, omit for Trigger)" },
          parameterType: {
            type: "string",
            description: "Type of parameter: 'Float', 'Int', 'Bool', 'Trigger'",
            enum: ["Float", "Int", "Bool", "Trigger"]
          }
        },
        required: ["gameObjectPath", "parameterName"]
      }
    },
    {
      name: "unity_add_animator_parameter",
      description: "Add a new parameter to an Animator Controller (Editor only)",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string", description: "Path to the AnimatorController asset" },
          parameterName: { type: "string", description: "Name of the new parameter" },
          parameterType: {
            type: "string",
            description: "Type: 'Float', 'Int', 'Bool', 'Trigger'",
            enum: ["Float", "Int", "Bool", "Trigger"]
          },
          defaultValue: { type: "number", description: "Default value (for Float/Int/Bool)" }
        },
        required: ["controllerPath", "parameterName", "parameterType"]
      }
    },
    {
      name: "unity_add_animator_state",
      description: "Add a new state to an Animator Controller layer (Editor only)",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string", description: "Path to the AnimatorController asset" },
          stateName: { type: "string", description: "Name of the new state" },
          layerIndex: { type: "integer", description: "Layer index (default: 0)" },
          position: {
            type: "object",
            description: "Position in the graph {x, y}",
            properties: {
              x: { type: "number" },
              y: { type: "number" }
            }
          },
          motionClip: { type: "string", description: "Path to AnimationClip asset (optional)" }
        },
        required: ["controllerPath", "stateName"]
      }
    },
    {
      name: "unity_add_animator_transition",
      description: "Add a transition between two states in an Animator Controller (Editor only)",
      inputSchema: {
        type: "object",
        properties: {
          controllerPath: { type: "string", description: "Path to the AnimatorController asset" },
          fromState: { type: "string", description: "Source state name (use 'Any' for AnyState transition)" },
          toState: { type: "string", description: "Destination state name" },
          layerIndex: { type: "integer", description: "Layer index (default: 0)" },
          conditions: {
            type: "array",
            description: "Transition conditions: [{parameter, mode, threshold}]. Modes: Greater, Less, Equals, NotEqual, If, IfNot",
            items: {
              type: "object",
              properties: {
                parameter: { type: "string" },
                mode: { type: "string", enum: ["Greater", "Less", "Equals", "NotEqual", "If", "IfNot"] },
                threshold: { type: "number" }
              }
            }
          },
          hasExitTime: { type: "boolean", description: "Whether transition has exit time (default: true)" },
          exitTime: { type: "number", description: "Exit time normalized (default: 1.0)" },
          transitionDuration: { type: "number", description: "Transition duration in seconds (default: 0.25)" }
        },
        required: ["controllerPath", "fromState", "toState"]
      }
    },
    // Asset Browser tools
    {
      name: "unity_search_assets",
      description: "Search for assets in the project using Unity's AssetDatabase. Supports type filters (t:), label filters (l:), and name patterns.",
      inputSchema: {
        type: "object",
        properties: {
          filter: {
            type: "string",
            description: "Search filter. Examples: 't:Texture2D', 't:Prefab', 'l:Environment', 'Player t:Prefab', 't:AnimationClip Run'"
          },
          searchFolders: {
            type: "array",
            items: { type: "string" },
            description: "Folders to search in (default: entire project). Example: ['Assets/Prefabs', 'Assets/Materials']"
          },
          maxResults: { type: "integer", description: "Maximum number of results to return (default: 50, max: 200)" }
        }
      }
    },
    {
      name: "unity_get_asset_info",
      description: "Get detailed information about a specific asset including type, size, labels, and dependencies",
      inputSchema: {
        type: "object",
        properties: {
          assetPath: { type: "string", description: "Path to the asset (e.g., 'Assets/Textures/Player.png')" },
          includeDependencies: { type: "boolean", description: "Include list of assets this asset depends on (default: false)" },
          includeReferences: { type: "boolean", description: "Include list of assets that reference this asset (default: false, can be slow)" }
        },
        required: ["assetPath"]
      }
    },
    {
      name: "unity_list_folders",
      description: "List all folders in the project Assets directory",
      inputSchema: {
        type: "object",
        properties: {
          parentPath: { type: "string", description: "Parent folder path (default: 'Assets')" },
          recursive: { type: "boolean", description: "Include subfolders recursively (default: true)" },
          maxDepth: { type: "integer", description: "Maximum folder depth when recursive (default: 5)" }
        }
      }
    },
    {
      name: "unity_list_folder_contents",
      description: "List all assets in a specific folder with optional type filtering",
      inputSchema: {
        type: "object",
        properties: {
          folderPath: { type: "string", description: "Folder path (e.g., 'Assets/Prefabs')" },
          typeFilter: { type: "string", description: "Filter by asset type: 'Prefab', 'Texture2D', 'Material', 'AudioClip', 'AnimationClip', 'Script', etc." },
          includeSubfolders: { type: "boolean", description: "Include assets from subfolders (default: false)" }
        },
        required: ["folderPath"]
      }
    },
    {
      name: "unity_get_asset_preview",
      description: "Get asset preview as JPG (compact). Use size='small' for minimal tokens",
      inputSchema: {
        type: "object",
        properties: {
          assetPath: { type: "string", description: "Path to the asset" },
          size: { type: "string", description: "Preset: 'tiny'(32), 'small'(64 default), 'medium'(128), 'large'(256)", enum: ["tiny", "small", "medium", "large"] },
          format: { type: "string", description: "Image format: 'jpg' (default, smaller) or 'png'", enum: ["jpg", "png"] },
          jpgQuality: { type: "integer", description: "JPG quality 1-100 (default: 75)" }
        },
        required: ["assetPath"]
      }
    },
    // Workflow Enhancement tools
    {
      name: "unity_clear_console",
      description: "Clear the Unity console log",
      inputSchema: {
        type: "object",
        properties: {}
      }
    },
    {
      name: "unity_rename_gameobject",
      description: "Rename a GameObject in the scene",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject to rename" },
          newName: { type: "string", description: "New name for the GameObject" }
        },
        required: ["gameObjectPath", "newName"]
      }
    },
    {
      name: "unity_set_parent",
      description: "Change the parent of a GameObject in the hierarchy",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path or name of the GameObject to reparent" },
          parentPath: { type: "string", description: "Path or name of the new parent (null or empty to unparent to scene root)" },
          worldPositionStays: { type: "boolean", description: "If true, keeps world position; if false, keeps local position relative to new parent (default: true)" }
        },
        required: ["gameObjectPath"]
      }
    },
    {
      name: "unity_instantiate_prefab",
      description: "Instantiate a prefab in the scene, maintaining the prefab link",
      inputSchema: {
        type: "object",
        properties: {
          prefabPath: { type: "string", description: "Asset path to the prefab (e.g., 'Assets/Prefabs/Enemy.prefab')" },
          position: {
            type: "object",
            description: "World position {x, y, z} (default: 0,0,0)",
            properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } }
          },
          rotation: {
            type: "object",
            description: "Euler rotation {x, y, z} in degrees (default: 0,0,0)",
            properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } }
          },
          parentPath: { type: "string", description: "Optional parent GameObject path" },
          name: { type: "string", description: "Override name for the instance (optional)" }
        },
        required: ["prefabPath"]
      }
    },
    {
      name: "unity_take_screenshot",
      description: "Take screenshot. JPG default (10x smaller). Use returnBase64=false to save tokens",
      inputSchema: {
        type: "object",
        properties: {
          view: { type: "string", description: "Which view: 'Scene' or 'Game' (default: 'Scene')", enum: ["Scene", "Game"] },
          format: { type: "string", description: "Image format: 'jpg' (smaller, default) or 'png'", enum: ["jpg", "png"] },
          jpgQuality: { type: "integer", description: "JPG quality 1-100 (default: 75)" },
          width: { type: "integer", description: "Width in pixels (default: 640, max: 4096)" },
          height: { type: "integer", description: "Height in pixels (default: 360, max: 4096)" },
          savePath: { type: "string", description: "Save path (auto-generated if not specified)" },
          returnBase64: { type: "boolean", description: "Return image as base64 (default: false - saves tokens)" }
        }
      }
    },
    // Selection & Scene tools
    {
      name: "unity_get_selection",
      description: "Get currently selected objects in the Unity Editor (Hierarchy and Project windows)",
      inputSchema: {
        type: "object",
        properties: {
          includeAssets: { type: "boolean", description: "Include selected assets from Project window (default: false)" }
        }
      }
    },
    {
      name: "unity_set_selection",
      description: "Select GameObjects or assets programmatically in the Unity Editor",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPaths: {
            type: "array",
            items: { type: "string" },
            description: "Array of GameObject paths to select (e.g., ['Player', 'Environment/Tree'])"
          },
          assetPaths: {
            type: "array",
            items: { type: "string" },
            description: "Array of asset paths to select (e.g., ['Assets/Prefabs/Enemy.prefab'])"
          },
          clear: { type: "boolean", description: "Clear current selection (default: false)" }
        }
      }
    },
    {
      name: "unity_get_scene_info",
      description: "Get information about the current scene (name, path, dirty state, root objects count)",
      inputSchema: {
        type: "object",
        properties: {}
      }
    },
    {
      name: "unity_load_scene",
      description: "Load/open a scene in the Unity Editor",
      inputSchema: {
        type: "object",
        properties: {
          scenePath: { type: "string", description: "Path to the scene asset (e.g., 'Assets/Scenes/Level1.unity')" },
          mode: { type: "string", description: "How to open: 'Single' (replace current) or 'Additive' (add to current). Default: 'Single'", enum: ["Single", "Additive"] }
        },
        required: ["scenePath"]
      }
    },
    {
      name: "unity_save_scene",
      description: "Save the current scene or all open scenes",
      inputSchema: {
        type: "object",
        properties: {
          scenePath: { type: "string", description: "Optional: Save As path (e.g., 'Assets/Scenes/Level1_backup.unity')" },
          saveAll: { type: "boolean", description: "Save all open scenes (default: false, saves only active scene)" }
        }
      }
    },
    {
      name: "unity_create_scene",
      description: "Create a new Unity scene. Use setup='default' for Camera+Light, 'empty' for blank scene.",
      inputSchema: {
        type: "object",
        properties: {
          sceneName: { type: "string", description: "Name for the new scene" },
          savePath: { type: "string", description: "Path to save (e.g., 'Assets/Scenes/Level1.unity')" },
          setup: { type: "string", description: "'default' (Camera+Light) or 'empty' (blank)", enum: ["default", "empty"] },
          mode: { type: "string", description: "'single' (replace current) or 'additive' (add alongside)", enum: ["single", "additive"] }
        },
        required: ["sceneName"]
      }
    },
    {
      name: "unity_create_prefab",
      description: "Create a prefab from a GameObject in the scene. Use connectInstance=true to keep the link.",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Path in hierarchy (e.g., 'Player' or 'Environment/Tree')" },
          savePath: { type: "string", description: "Where to save (e.g., 'Assets/Prefabs/Player.prefab')" },
          connectInstance: { type: "boolean", description: "Keep GameObject connected as prefab instance (default: true)" }
        },
        required: ["gameObjectPath", "savePath"]
      }
    },
    {
      name: "unity_unpack_prefab",
      description: "Unpack a prefab instance to regular GameObjects. Breaks the prefab link.",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Prefab instance path in hierarchy" },
          unpackMode: { type: "string", description: "'completely' (all nested) or 'root' (outermost only)", enum: ["completely", "root"] }
        },
        required: ["gameObjectPath"]
      }
    },
    {
      name: "unity_apply_prefab_overrides",
      description: "Apply all overrides from a prefab instance to the source prefab. Affects all instances.",
      inputSchema: {
        type: "object",
        properties: {
          gameObjectPath: { type: "string", description: "Modified prefab instance in hierarchy" }
        },
        required: ["gameObjectPath"]
      }
    },
    {
      name: "unity_undo",
      description: "Undo or redo the last editor action(s)",
      inputSchema: {
        type: "object",
        properties: {
          steps: { type: "integer", description: "Number of undo/redo steps (default: 1)" },
          redo: { type: "boolean", description: "Perform redo instead of undo (default: false)" }
        }
      }
    },
    // Memory/Cache Tools
    {
      name: "unity_memory_get",
      description: "Get cached data from MCP memory (assets, scenes, hierarchy). Use this to quickly retrieve previously fetched data without re-scanning. Returns stale indicators.",
      inputSchema: {
        type: "object",
        properties: {
          section: { type: "string", description: "Section to retrieve: 'assets', 'scenes', 'hierarchy', 'operations', or 'all' (default: 'all')", enum: ["assets", "scenes", "hierarchy", "operations", "all"] }
        }
      }
    },
    {
      name: "unity_memory_refresh",
      description: "Refresh a section of the MCP memory cache by re-fetching data from Unity. Use when cache is stale or you need up-to-date data.",
      inputSchema: {
        type: "object",
        properties: {
          section: { type: "string", description: "Section to refresh: 'assets', 'scenes', 'hierarchy', or 'all'", enum: ["assets", "scenes", "hierarchy", "all"] }
        },
        required: ["section"]
      }
    },
    {
      name: "unity_memory_clear",
      description: "Clear the MCP memory cache (or a specific section). Use to force a fresh fetch next time.",
      inputSchema: {
        type: "object",
        properties: {
          section: { type: "string", description: "Section to clear: 'assets', 'scenes', 'hierarchy', 'operations', or 'all' (default: 'all')", enum: ["assets", "scenes", "hierarchy", "operations", "all"] }
        }
      }
    },
    // Material Tools
    {
      name: "unity_get_material",
      description: "Get material properties including colors, floats, textures, shader info, and keywords",
      inputSchema: {
        type: "object",
        properties: {
          materialPath: { type: "string", description: "Path to material asset (e.g., 'Assets/Materials/Player.mat')" },
          gameObjectPath: { type: "string", description: "Alternative: Get material from a GameObject's renderer" },
          materialIndex: { type: "integer", description: "Material index for multi-material objects (default: 0)" }
        }
      }
    },
    {
      name: "unity_set_material",
      description: "Modify material properties (colors, floats, textures) or change shader",
      inputSchema: {
        type: "object",
        properties: {
          materialPath: { type: "string", description: "Path to material asset" },
          gameObjectPath: { type: "string", description: "Alternative: Modify material on a GameObject's renderer" },
          materialIndex: { type: "integer", description: "Material index for multi-material objects (default: 0)" },
          properties: {
            type: "object",
            description: "Properties to modify. Keys are property names (e.g., '_Color', '_Metallic'), values depend on type"
          },
          shader: { type: "string", description: "Optional: Change the material's shader (e.g., 'Standard', 'Universal Render Pipeline/Lit')" },
          renderQueue: { type: "integer", description: "Optional: Set render queue value" }
        }
      }
    },
    {
      name: "unity_create_material",
      description: "Create a new material asset with specified shader and properties",
      inputSchema: {
        type: "object",
        properties: {
          name: { type: "string", description: "Material name" },
          savePath: { type: "string", description: "Path to save the material (e.g., 'Assets/Materials/NewMat.mat')" },
          shader: { type: "string", description: "Shader to use (default: 'Standard'). Examples: 'Standard', 'Unlit/Color', 'Universal Render Pipeline/Lit'" },
          properties: {
            type: "object",
            description: "Initial properties to set (e.g., { '_Color': {r:1,g:0,b:0,a:1}, '_Metallic': 0.8 })"
          }
        },
        required: ["name", "savePath"]
      }
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

    try {
      const result = await bridge.request<ToolResult>("tools/call", {
        name,
        arguments: args || {},
      });

      return {
        content: result.content || [{ type: "text", text: "Tool executed successfully" }],
        isError: result.isError || false,
      };
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

  // Default resources available even when Unity is not connected
  const defaultResources: ResourceDefinition[] = [
    {
      uri: "unity://project/settings",
      name: "Project Settings",
      description: "Current Unity project settings (requires Unity connection)",
      mimeType: "application/json"
    },
    {
      uri: "unity://scene/hierarchy",
      name: "Scene Hierarchy",
      description: "Current scene object hierarchy (requires Unity connection)",
      mimeType: "application/json"
    },
    {
      uri: "unity://console/logs",
      name: "Console Logs",
      description: "Recent Unity console log entries (requires Unity connection)",
      mimeType: "application/json"
    }
  ];

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

    const { uri } = request.params;

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
