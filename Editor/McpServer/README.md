# MCP Unity Server

A Model Context Protocol (MCP) server for Unity Editor integration with Claude AI.

## Features

- **70 MCP Tools** for complete Unity Editor control
- WebSocket server for real-time communication
- Full animator controller management (states, transitions, blend trees)
- Asset browser and management
- Scene operations (load, save, create)
- Prefab instantiation and management
- Material creation and modification
- Tag/Layer management
- Project settings configuration
- Memory caching for optimized performance

## Installation

1. Extract the zip to your Unity project's `Assets/` directory
2. The structure should be:
   ```
   Assets/
   ├── Editor/
   │   └── McpServer/      # C# Editor scripts
   ├── Plugins/
   │   └── websocket-sharp.dll  # WebSocket library (REQUIRED)
   └── Server~/            # Node.js MCP bridge
   ```
3. Unity will automatically compile the scripts
4. Run `npm install` in the `Server~` folder to install Node dependencies

## Configuration

### In Unity Editor

1. Go to **Tools > MCP Unity > Server Window**
2. Configure the port (default: 8090)
3. Enable/disable auto-start
4. Toggle **"Log to Unity Console"** to reduce console spam

### For Claude Desktop

Add to your Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": ["/path/to/Server~/build/index.js"],
      "env": {
        "UNITY_PORT": "8090",
        "UNITY_HOST": "localhost"
      }
    }
  }
}
```

### For Claude Code CLI

```bash
claude mcp add mcp-unity -- node /path/to/Server~/build/index.js
```

## Tool Categories

| Category | Tools | Description |
|----------|-------|-------------|
| UI | 4 | Editor state, console logs, menu items |
| GameObject | 7 | Create, delete, rename, hierarchy management |
| Component | 4 | Get, add, modify components (including custom scripts) |
| Animator | 16 | States, transitions, parameters, blend trees |
| Asset | 5 | Search, info, preview, folder browsing |
| Scene | 4 | Load, save, create scenes |
| Prefab | 4 | Instantiate, create, unpack, apply |
| Material | 3 | Get, set, create materials |
| Tag/Layer | 6 | Create and manage tags and layers |
| Editor | 8 | Screenshot, selection, undo, tests |
| Memory | 3 | Cache management |
| Project Settings | 6 | Player settings, quality, physics, time |

## Settings

Access settings via **Tools > MCP Unity > Server Window > Settings tab**:

- **Port**: WebSocket server port (default: 8090)
- **Auto Start**: Automatically start server when Unity opens
- **Log to Unity Console**: Toggle MCP logs in Unity console (reduces spam)
- **Log to File**: Save logs to file
- **Max Log Entries**: Maximum logs to keep in memory

## Requirements

- Unity 2021.3 or later
- Node.js 18+ (for the bridge server)
- websocket-sharp (included via DLL or Package Manager)

## Architecture

```
Assets/
├── Editor/
│   └── McpServer/
│       ├── McpUnityServer.cs      # Main WebSocket server
│       ├── McpJsonRpc.cs          # JSON-RPC 2.0 handler
│       ├── McpToolRegistry.cs     # Tool registration
│       ├── McpResourceRegistry.cs # Resource registration
│       ├── McpEditorWindow.cs     # UI Window
│       ├── McpSettings.cs         # Persistent settings
│       ├── McpDebug.cs            # Conditional logging
│       ├── Tools/                 # Tool implementations
│       │   ├── UITools.cs
│       │   ├── GameObjectTools.cs
│       │   ├── ComponentTools.cs
│       │   ├── AnimatorTools.cs
│       │   ├── AssetTools.cs
│       │   ├── SceneTools.cs
│       │   ├── PrefabTools.cs
│       │   ├── MaterialTools.cs
│       │   ├── TagLayerTools.cs
│       │   ├── EditorTools.cs
│       │   ├── MemoryTools.cs
│       │   └── ProjectSettingsTools.cs
│       ├── Helpers/               # Utility classes
│       ├── Models/                # Data models
│       └── Utils/                 # Additional utilities
└── Server~/                       # Node.js MCP bridge
    ├── src/
    │   └── index.ts
    ├── build/
    │   └── index.js
    └── package.json
```

## License

MIT License
