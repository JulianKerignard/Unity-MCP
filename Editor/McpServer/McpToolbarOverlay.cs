using UnityEditor;
using UnityEngine;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace McpUnity.Editor
{
    /// <summary>
    /// Toolbar overlay for quick MCP server status and controls.
    /// Shows in the Scene view toolbar for easy access.
    /// </summary>
    [Overlay(typeof(SceneView), "MCP Status", true)]
    public class McpToolbarOverlay : ToolbarOverlay
    {
        public McpToolbarOverlay() : base(
            McpStatusElement.Id,
            McpToggleElement.Id)
        { }
    }

    /// <summary>
    /// Status indicator element for the toolbar
    /// </summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    public class McpStatusElement : EditorToolbarButton
    {
        public const string Id = "McpUnity/Status";

        public McpStatusElement()
        {
            text = "MCP";
            tooltip = "Click to open MCP Unity Server window";
            clicked += OnClick;

            UpdateStatus();
            McpServerStatus.OnServerStateChanged += _ => UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (McpServerStatus.IsRunning)
            {
                icon = EditorGUIUtility.IconContent("d_GreenCheckmark").image as Texture2D;
                tooltip = $"MCP Server Running - {McpServerStatus.ConnectedClients} clients";
            }
            else
            {
                icon = EditorGUIUtility.IconContent("d_redLight").image as Texture2D;
                tooltip = "MCP Server Stopped - Click to open settings";
            }
        }

        private void OnClick()
        {
            McpEditorWindow.ShowWindow();
        }
    }

    /// <summary>
    /// Quick toggle button for starting/stopping server
    /// </summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    public class McpToggleElement : EditorToolbarButton
    {
        public const string Id = "McpUnity/Toggle";

        public McpToggleElement()
        {
            UpdateButton();
            clicked += OnClick;
            McpServerStatus.OnServerStateChanged += _ => UpdateButton();
        }

        private void UpdateButton()
        {
            if (McpServerStatus.IsRunning)
            {
                text = "Stop";
                icon = EditorGUIUtility.IconContent("d_PauseButton").image as Texture2D;
                tooltip = "Stop MCP Server";
            }
            else
            {
                text = "Start";
                icon = EditorGUIUtility.IconContent("d_PlayButton").image as Texture2D;
                tooltip = "Start MCP Server";
            }
        }

        private void OnClick()
        {
            if (McpServerStatus.IsRunning)
            {
                McpServerStatus.Stop();
            }
            else
            {
                McpServerStatus.Start();
            }
        }
    }
}
