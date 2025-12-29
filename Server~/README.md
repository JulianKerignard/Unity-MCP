# MCP Unity Bridge

Node.js bridge that connects Claude (via MCP - Model Context Protocol) to a Unity WebSocket server.

## Architecture

```
Claude <-> MCP (stdio) <-> Node.js Bridge <-> WebSocket <-> Unity
```

## Installation

```bash
cd Server~
npm install
npm run build
```

## Usage

### Direct Execution

```bash
npm start
```

### With Claude Desktop

Add to your Claude Desktop configuration (`~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["/path/to/Server~/build/index.js"],
      "env": {
        "UNITY_HOST": "localhost",
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `UNITY_HOST` | `localhost` | Unity WebSocket server host |
| `UNITY_PORT` | `8090` | Unity WebSocket server port |
| `RECONNECT_INTERVAL` | `5000` | Reconnection interval in ms |
| `REQUEST_TIMEOUT` | `30000` | Request timeout in ms |
| `MAX_RECONNECT_ATTEMPTS` | `10` | Max reconnection attempts |
| `DEBUG` | `false` | Enable debug logging |

## Development

```bash
# Run with hot reload
npm run dev

# Watch mode for TypeScript
npm run watch

# Type checking
npm run typecheck
```

## Protocol

The bridge uses JSON-RPC 2.0 over WebSocket to communicate with Unity.

### Supported Methods

- `tools/list` - List available tools from Unity
- `tools/call` - Call a tool in Unity
- `resources/list` - List available resources
- `resources/read` - Read a resource
- `prompts/list` - List available prompts
- `prompts/get` - Get a prompt

## Project Structure

```
Server~/
  src/
    index.ts        # Entry point, MCP server setup
    UnityBridge.ts  # WebSocket client for Unity
    types.ts        # TypeScript types and Zod schemas
    utils.ts        # Utility functions
  build/            # Compiled JavaScript
  package.json
  tsconfig.json
```
