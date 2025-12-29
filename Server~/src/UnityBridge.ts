import WebSocket from "ws";
import { EventEmitter } from "events";
import {
  BridgeConfig,
  ConnectionState,
  DEFAULT_BRIDGE_CONFIG,
  JsonRpcRequest,
  JsonRpcResponse,
  McpError,
  McpErrorCode,
} from "./types.js";

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
  timeout: NodeJS.Timeout;
}

/**
 * UnityBridge - WebSocket client for communicating with Unity MCP Server
 *
 * Handles:
 * - WebSocket connection management with auto-reconnect
 * - JSON-RPC 2.0 request/response handling
 * - Request timeout management
 * - Connection state tracking
 */
export class UnityBridge extends EventEmitter {
  private ws: WebSocket | null = null;
  private requestId = 0;
  private pendingRequests = new Map<number | string, PendingRequest>();
  private reconnectAttempts = 0;
  private reconnectTimer: NodeJS.Timeout | null = null;
  private _state: ConnectionState = ConnectionState.Disconnected;
  private config: BridgeConfig;

  constructor(config: Partial<BridgeConfig> = {}) {
    super();
    this.config = { ...DEFAULT_BRIDGE_CONFIG, ...config };
  }

  /**
   * Current connection state
   */
  get state(): ConnectionState {
    return this._state;
  }

  /**
   * Whether the bridge is currently connected
   */
  get isConnected(): boolean {
    return this._state === ConnectionState.Connected && this.ws?.readyState === WebSocket.OPEN;
  }

  /**
   * WebSocket URL for Unity connection
   */
  private get wsUrl(): string {
    return `ws://${this.config.unityHost}:${this.config.unityPort}`;
  }

  /**
   * Log message if debug is enabled
   */
  private log(message: string, ...args: unknown[]): void {
    if (this.config.debug) {
      console.error(`[MCP Unity Bridge] ${message}`, ...args);
    }
  }

  /**
   * Update connection state and emit event
   */
  private setState(state: ConnectionState): void {
    const previousState = this._state;
    this._state = state;
    this.emit("stateChange", state, previousState);
    this.log(`State changed: ${previousState} -> ${state}`);
  }

  /**
   * Connect to Unity WebSocket server
   */
  async connect(): Promise<void> {
    if (this._state === ConnectionState.Connected) {
      return;
    }

    if (this._state === ConnectionState.Connecting) {
      throw new McpError(
        McpErrorCode.ConnectionError,
        "Connection already in progress"
      );
    }

    this.setState(ConnectionState.Connecting);

    return new Promise((resolve, reject) => {
      try {
        this.ws = new WebSocket(this.wsUrl);

        const connectionTimeout = setTimeout(() => {
          if (this._state === ConnectionState.Connecting) {
            this.ws?.close();
            reject(
              new McpError(
                McpErrorCode.TimeoutError,
                `Connection timeout after ${this.config.requestTimeout}ms`
              )
            );
          }
        }, this.config.requestTimeout);

        this.ws.on("open", () => {
          clearTimeout(connectionTimeout);
          this.reconnectAttempts = 0;
          this.setState(ConnectionState.Connected);
          this.log(`Connected to Unity at ${this.wsUrl}`);
          this.emit("connected");
          resolve();
        });

        this.ws.on("message", (data: WebSocket.Data) => {
          this.handleMessage(data);
        });

        this.ws.on("error", (error: Error) => {
          this.log(`WebSocket error:`, error);
          this.emit("error", error);

          if (this._state === ConnectionState.Connecting) {
            clearTimeout(connectionTimeout);
            reject(
              new McpError(
                McpErrorCode.ConnectionError,
                `Connection failed: ${error.message}`
              )
            );
          }
        });

        this.ws.on("close", (code: number, reason: Buffer) => {
          this.log(`WebSocket closed: ${code} - ${reason.toString()}`);
          this.handleDisconnect();
        });

      } catch (error) {
        this.setState(ConnectionState.Failed);
        reject(
          new McpError(
            McpErrorCode.ConnectionError,
            `Failed to create WebSocket: ${error instanceof Error ? error.message : String(error)}`
          )
        );
      }
    });
  }

  /**
   * Handle incoming WebSocket message
   */
  private handleMessage(data: WebSocket.Data): void {
    try {
      const message = data.toString();
      this.log(`Received:`, message);

      const response: JsonRpcResponse = JSON.parse(message);

      if (response.id !== undefined) {
        const pending = this.pendingRequests.get(response.id);
        if (pending) {
          clearTimeout(pending.timeout);
          this.pendingRequests.delete(response.id);

          if (response.error) {
            pending.reject(
              new McpError(
                response.error.code as McpErrorCode,
                response.error.message,
                response.error.data
              )
            );
          } else {
            pending.resolve(response.result);
          }
        } else {
          this.log(`Received response for unknown request ID: ${response.id}`);
        }
      } else {
        // Notification (no id)
        this.emit("notification", response);
      }
    } catch (error) {
      this.log(`Failed to parse message:`, error);
      this.emit("error", new McpError(
        McpErrorCode.ParseError,
        `Failed to parse message: ${error instanceof Error ? error.message : String(error)}`
      ));
    }
  }

  /**
   * Handle disconnect and attempt reconnection
   */
  private handleDisconnect(): void {
    // Clear all pending requests
    for (const [, pending] of this.pendingRequests) {
      clearTimeout(pending.timeout);
      pending.reject(
        new McpError(McpErrorCode.ConnectionError, "Connection closed")
      );
    }
    this.pendingRequests.clear();

    const wasConnected = this._state === ConnectionState.Connected;

    if (wasConnected && this.reconnectAttempts < this.config.maxReconnectAttempts) {
      this.setState(ConnectionState.Reconnecting);
      this.scheduleReconnect();
    } else {
      this.setState(ConnectionState.Disconnected);
    }

    this.emit("disconnected");
  }

  /**
   * Schedule a reconnection attempt
   */
  private scheduleReconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
    }

    this.reconnectTimer = setTimeout(async () => {
      this.reconnectAttempts++;
      this.log(`Reconnection attempt ${this.reconnectAttempts}/${this.config.maxReconnectAttempts}`);

      try {
        await this.connect();
        this.emit("reconnected");
      } catch {
        if (this.reconnectAttempts < this.config.maxReconnectAttempts) {
          this.scheduleReconnect();
        } else {
          this.setState(ConnectionState.Failed);
          this.emit("reconnectFailed");
        }
      }
    }, this.config.reconnectInterval);
  }

  /**
   * Disconnect from Unity
   */
  async disconnect(): Promise<void> {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }

    this.setState(ConnectionState.Disconnected);
  }

  /**
   * Send a JSON-RPC request to Unity and wait for response
   */
  async request<T = unknown>(method: string, params?: unknown): Promise<T> {
    if (!this.isConnected) {
      throw new McpError(
        McpErrorCode.ConnectionError,
        "Not connected to Unity"
      );
    }

    const id = ++this.requestId;
    const request: JsonRpcRequest = {
      jsonrpc: "2.0",
      id,
      method,
      params,
    };

    return new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pendingRequests.delete(id);
        reject(
          new McpError(
            McpErrorCode.TimeoutError,
            `Request timeout after ${this.config.requestTimeout}ms`
          )
        );
      }, this.config.requestTimeout);

      this.pendingRequests.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timeout,
      });

      const message = JSON.stringify(request);
      this.log(`Sending:`, message);

      this.ws!.send(message, (error) => {
        if (error) {
          clearTimeout(timeout);
          this.pendingRequests.delete(id);
          reject(
            new McpError(
              McpErrorCode.ConnectionError,
              `Failed to send request: ${error.message}`
            )
          );
        }
      });
    });
  }

  /**
   * Send a notification (no response expected)
   */
  notify(method: string, params?: unknown): void {
    if (!this.isConnected) {
      throw new McpError(
        McpErrorCode.ConnectionError,
        "Not connected to Unity"
      );
    }

    const notification = {
      jsonrpc: "2.0",
      method,
      params,
    };

    const message = JSON.stringify(notification);
    this.log(`Sending notification:`, message);

    this.ws!.send(message);
  }

  /**
   * Wait for connection to be established
   */
  async waitForConnection(timeout: number = 30000): Promise<void> {
    if (this.isConnected) {
      return;
    }

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.off("connected", onConnect);
        this.off("error", onError);
        reject(new McpError(McpErrorCode.TimeoutError, "Connection timeout"));
      }, timeout);

      const onConnect = () => {
        clearTimeout(timer);
        this.off("error", onError);
        resolve();
      };

      const onError = (error: Error) => {
        clearTimeout(timer);
        this.off("connected", onConnect);
        reject(error);
      };

      this.once("connected", onConnect);
      this.once("error", onError);
    });
  }
}
