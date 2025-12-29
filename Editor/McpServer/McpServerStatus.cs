using System;
using UnityEditor;
using UnityEngine;
using McpUnity.Server;

namespace McpUnity.Editor
{
    /// <summary>
    /// Static class to manage MCP server state across the Editor.
    /// Bridges the Editor UI with the actual McpUnityServer implementation.
    /// </summary>
    [InitializeOnLoad]
    public static class McpServerStatus
    {
        private static DateTime _startTime;
        private static int _connectedClients = 0;

        /// <summary>
        /// Event fired when server state changes
        /// </summary>
        public static event Action<bool> OnServerStateChanged;

        /// <summary>
        /// Event fired when client count changes
        /// </summary>
        public static event Action<int> OnClientCountChanged;

        /// <summary>
        /// Whether the server is currently running
        /// </summary>
        public static bool IsRunning => McpUnityServer.IsRunning;

        /// <summary>
        /// Number of connected clients
        /// </summary>
        public static int ConnectedClients => McpUnityServer.ConnectedClientCount;

        /// <summary>
        /// Server start time
        /// </summary>
        public static DateTime StartTime => _startTime;

        /// <summary>
        /// Server uptime
        /// </summary>
        public static TimeSpan Uptime => IsRunning ? DateTime.Now - _startTime : TimeSpan.Zero;

        /// <summary>
        /// Current server endpoint
        /// </summary>
        public static string Endpoint => $"ws://127.0.0.1:{McpUnityServer.Port}";

        static McpServerStatus()
        {
            // Subscribe to McpUnityServer events
            McpUnityServer.OnServerStarted += OnServerStarted;
            McpUnityServer.OnServerStopped += OnServerStopped;
            McpUnityServer.OnClientConnected += OnClientConnected;
            McpUnityServer.OnClientDisconnected += OnClientDisconnected;
        }

        private static void OnServerStarted()
        {
            _startTime = DateTime.Now;
            OnServerStateChanged?.Invoke(true);
        }

        private static void OnServerStopped()
        {
            OnServerStateChanged?.Invoke(false);
        }

        private static void OnClientConnected(string clientId)
        {
            _connectedClients++;
            OnClientCountChanged?.Invoke(_connectedClients);
        }

        private static void OnClientDisconnected(string clientId)
        {
            _connectedClients = Math.Max(0, _connectedClients - 1);
            OnClientCountChanged?.Invoke(_connectedClients);
        }

        /// <summary>
        /// Start the MCP server
        /// </summary>
        public static bool Start()
        {
            if (IsRunning)
            {
                return false;
            }

            McpUnityServer.Start();
            return IsRunning;
        }

        /// <summary>
        /// Stop the MCP server
        /// </summary>
        public static bool Stop()
        {
            if (!IsRunning)
            {
                return false;
            }

            McpUnityServer.Stop();
            return true;
        }

        /// <summary>
        /// Restart the MCP server
        /// </summary>
        public static bool Restart()
        {
            McpUnityServer.Restart();
            return true;
        }
    }
}
