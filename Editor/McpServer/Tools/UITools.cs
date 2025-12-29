using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using McpUnity.Protocol;
using McpUnity.Helpers;

namespace McpUnity.Server
{
    /// <summary>
    /// UI Tools - Canvas, UI elements, RectTransform configuration
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all UI-related tools
        /// </summary>
        static partial void RegisterUITools()
        {
            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_canvas",
                description = "Create a UI Canvas with EventSystem. Canvas is required for all UI elements. Supports ScreenSpaceOverlay (default), ScreenSpaceCamera, and WorldSpace render modes.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["name"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Canvas name (default: 'Canvas')"
                        },
                        ["renderMode"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Render mode: 'ScreenSpaceOverlay' (default), 'ScreenSpaceCamera', or 'WorldSpace'"
                        },
                        ["createEventSystem"] = new McpPropertySchema
                        {
                            type = "boolean",
                            description = "Create EventSystem if none exists (default: true)"
                        }
                    },
                    required = new List<string>()
                }
            }, CreateCanvas);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_create_ui_element",
                description = "Create a UI element (Button, Text, Image, Panel, Slider, Toggle, InputField, Dropdown, ScrollView) under a Canvas. Automatically parents to first Canvas if no parent specified.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["elementType"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "UI element type: Panel, Button, Text, Image, RawImage, Slider, Toggle, InputField, Dropdown, ScrollView"
                        },
                        ["name"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Element name (default: same as elementType)"
                        },
                        ["parent"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Parent path (default: first Canvas in scene)"
                        },
                        ["text"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Text content for Button, Text, Toggle, InputField elements"
                        },
                        ["posX"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "X position (anchored position)"
                        },
                        ["posY"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Y position (anchored position)"
                        },
                        ["width"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Element width"
                        },
                        ["height"] = new McpPropertySchema
                        {
                            type = "number",
                            description = "Element height"
                        }
                    },
                    required = new List<string> { "elementType" }
                }
            }, CreateUIElement);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_set_rect_transform",
                description = "Configure RectTransform properties for UI elements (anchors, pivot, position, size). Supports anchor presets like 'TopLeft', 'Center', 'StretchAll', etc.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Path to the UI GameObject"
                        },
                        ["anchorPreset"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Anchor preset: TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight, StretchHorizontal, StretchVertical, StretchAll"
                        },
                        ["anchorMinX"] = new McpPropertySchema { type = "number", description = "Anchor min X (0-1)" },
                        ["anchorMinY"] = new McpPropertySchema { type = "number", description = "Anchor min Y (0-1)" },
                        ["anchorMaxX"] = new McpPropertySchema { type = "number", description = "Anchor max X (0-1)" },
                        ["anchorMaxY"] = new McpPropertySchema { type = "number", description = "Anchor max Y (0-1)" },
                        ["pivotX"] = new McpPropertySchema { type = "number", description = "Pivot X (0-1)" },
                        ["pivotY"] = new McpPropertySchema { type = "number", description = "Pivot Y (0-1)" },
                        ["posX"] = new McpPropertySchema { type = "number", description = "Anchored position X" },
                        ["posY"] = new McpPropertySchema { type = "number", description = "Anchored position Y" },
                        ["width"] = new McpPropertySchema { type = "number", description = "Width" },
                        ["height"] = new McpPropertySchema { type = "number", description = "Height" },
                        ["sizeDeltaX"] = new McpPropertySchema { type = "number", description = "Size delta X" },
                        ["sizeDeltaY"] = new McpPropertySchema { type = "number", description = "Size delta Y" },
                        ["offsetMinX"] = new McpPropertySchema { type = "number", description = "Offset min X (left)" },
                        ["offsetMinY"] = new McpPropertySchema { type = "number", description = "Offset min Y (bottom)" },
                        ["offsetMaxX"] = new McpPropertySchema { type = "number", description = "Offset max X (right)" },
                        ["offsetMaxY"] = new McpPropertySchema { type = "number", description = "Offset max Y (top)" }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, SetRectTransform);

            _toolRegistry.RegisterTool(new McpToolDefinition
            {
                name = "unity_get_ui_hierarchy",
                description = "Get the UI hierarchy of all Canvas elements in the scene, showing element types, positions, and sizes.",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["canvasPath"] = new McpPropertySchema
                        {
                            type = "string",
                            description = "Optional: specific Canvas path to inspect (default: all canvases)"
                        }
                    },
                    required = new List<string>()
                }
            }, GetUIHierarchy);
        }

        #region UI Tool Handlers

        private static McpToolResult CreateCanvas(Dictionary<string, object> args)
        {
            try
            {
                string canvasName = ArgumentParser.GetString(args, "name", "Canvas");
                string renderMode = ArgumentParser.GetString(args, "renderMode", "ScreenSpaceOverlay");
                bool createEventSystem = ArgumentParser.GetBool(args, "createEventSystem", true);

                var canvasGO = new GameObject(canvasName);
                Undo.RegisterCreatedObjectUndo(canvasGO, $"Create Canvas '{canvasName}'");

                var canvas = canvasGO.AddComponent<Canvas>();

                switch (renderMode.ToLowerInvariant())
                {
                    case "screenspaceoverlay":
                    case "overlay":
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        break;
                    case "screenspacecamera":
                    case "camera":
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        break;
                    case "worldspace":
                    case "world":
                        canvas.renderMode = RenderMode.WorldSpace;
                        var rectTransform = canvasGO.GetComponent<RectTransform>();
                        rectTransform.sizeDelta = new Vector2(800, 600);
                        break;
                    default:
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        break;
                }

                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();

                if (createEventSystem && UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                {
                    var eventSystemGO = new GameObject("EventSystem");
                    Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
                    eventSystemGO.AddComponent<EventSystem>();
                    eventSystemGO.AddComponent<StandaloneInputModule>();
                }

                return McpResponse.Success($"Created Canvas '{canvasName}'", new
                {
                    name = canvasName,
                    renderMode = canvas.renderMode.ToString(),
                    hasEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null
                });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create Canvas: {ex.Message}");
            }
        }

        private static McpToolResult CreateUIElement(Dictionary<string, object> args)
        {
            try
            {
                string elementType = ArgumentParser.RequireString(args, "elementType", out var typeError);
                if (typeError != null)
                    return McpToolResult.Error(typeError);

                string elementName = ArgumentParser.GetString(args, "name", elementType);
                string parentPath = ArgumentParser.GetString(args, "parent", null);
                string text = ArgumentParser.GetString(args, "text", null);

                Transform parent = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parentGO = GameObject.Find(parentPath);
                    if (parentGO == null)
                        return McpToolResult.Error($"Parent not found: {parentPath}");
                    parent = parentGO.transform;
                }
                else
                {
                    var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
                    if (canvas == null)
                        return McpToolResult.Error("No Canvas found in scene. Create one first with unity_create_canvas.");
                    parent = canvas.transform;
                }

                GameObject uiElement = null;
                string createdType = elementType.ToLowerInvariant();

                switch (createdType)
                {
                    case "panel":
                        uiElement = CreateUIPanel(elementName, parent);
                        break;
                    case "button":
                        uiElement = CreateUIButton(elementName, parent, text ?? "Button");
                        break;
                    case "text":
                    case "label":
                        uiElement = CreateUIText(elementName, parent, text ?? "New Text");
                        break;
                    case "image":
                        uiElement = CreateUIImage(elementName, parent);
                        break;
                    case "rawimage":
                        uiElement = CreateUIRawImage(elementName, parent);
                        break;
                    case "slider":
                        uiElement = CreateUISlider(elementName, parent);
                        break;
                    case "toggle":
                    case "checkbox":
                        uiElement = CreateUIToggle(elementName, parent, text ?? "Toggle");
                        break;
                    case "inputfield":
                    case "input":
                        uiElement = CreateUIInputField(elementName, parent, text ?? "Enter text...");
                        break;
                    case "dropdown":
                        uiElement = CreateUIDropdown(elementName, parent);
                        break;
                    case "scrollview":
                        uiElement = CreateUIScrollView(elementName, parent);
                        break;
                    default:
                        return McpToolResult.Error($"Unknown UI element type: {elementType}. Valid types: Panel, Button, Text, Image, RawImage, Slider, Toggle, InputField, Dropdown, ScrollView");
                }

                if (uiElement == null)
                    return McpToolResult.Error($"Failed to create UI element: {elementType}");

                Undo.RegisterCreatedObjectUndo(uiElement, $"Create UI {elementType}");

                var posX = ArgumentParser.GetFloat(args, "posX", 0);
                var posY = ArgumentParser.GetFloat(args, "posY", 0);
                var width = ArgumentParser.GetFloat(args, "width", 0);
                var height = ArgumentParser.GetFloat(args, "height", 0);

                var rect = uiElement.GetComponent<RectTransform>();
                if (rect != null)
                {
                    if (posX != 0 || posY != 0)
                        rect.anchoredPosition = new Vector2(posX, posY);
                    if (width > 0)
                        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    if (height > 0)
                        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                }

                return McpResponse.Success($"Created UI {elementType} '{elementName}'", new
                {
                    name = elementName,
                    type = createdType,
                    path = GameObjectHelpers.GetGameObjectPath(uiElement),
                    parent = parent.name
                });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to create UI element: {ex.Message}");
            }
        }

        private static McpToolResult SetRectTransform(Dictionary<string, object> args)
        {
            try
            {
                string gameObjectPath = ArgumentParser.RequireString(args, "gameObjectPath", out var pathError);
                if (pathError != null)
                    return McpToolResult.Error(pathError);

                var go = GameObject.Find(gameObjectPath);
                if (go == null)
                    return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

                var rectTransform = go.GetComponent<RectTransform>();
                if (rectTransform == null)
                    return McpToolResult.Error($"GameObject '{gameObjectPath}' does not have a RectTransform component");

                Undo.RecordObject(rectTransform, "Set RectTransform");

                var modified = new List<string>();

                if (ArgumentParser.HasKey(args, "anchorPreset"))
                {
                    string preset = ArgumentParser.GetString(args, "anchorPreset", "");
                    ApplyAnchorPreset(rectTransform, preset);
                    modified.Add("anchorPreset");
                }

                if (ArgumentParser.HasKey(args, "anchorMinX") || ArgumentParser.HasKey(args, "anchorMinY"))
                {
                    rectTransform.anchorMin = new Vector2(
                        ArgumentParser.GetFloat(args, "anchorMinX", rectTransform.anchorMin.x),
                        ArgumentParser.GetFloat(args, "anchorMinY", rectTransform.anchorMin.y)
                    );
                    modified.Add("anchorMin");
                }
                if (ArgumentParser.HasKey(args, "anchorMaxX") || ArgumentParser.HasKey(args, "anchorMaxY"))
                {
                    rectTransform.anchorMax = new Vector2(
                        ArgumentParser.GetFloat(args, "anchorMaxX", rectTransform.anchorMax.x),
                        ArgumentParser.GetFloat(args, "anchorMaxY", rectTransform.anchorMax.y)
                    );
                    modified.Add("anchorMax");
                }

                if (ArgumentParser.HasKey(args, "pivotX") || ArgumentParser.HasKey(args, "pivotY"))
                {
                    rectTransform.pivot = new Vector2(
                        ArgumentParser.GetFloat(args, "pivotX", rectTransform.pivot.x),
                        ArgumentParser.GetFloat(args, "pivotY", rectTransform.pivot.y)
                    );
                    modified.Add("pivot");
                }

                if (ArgumentParser.HasKey(args, "posX") || ArgumentParser.HasKey(args, "posY"))
                {
                    rectTransform.anchoredPosition = new Vector2(
                        ArgumentParser.GetFloat(args, "posX", rectTransform.anchoredPosition.x),
                        ArgumentParser.GetFloat(args, "posY", rectTransform.anchoredPosition.y)
                    );
                    modified.Add("anchoredPosition");
                }

                if (ArgumentParser.HasKey(args, "width"))
                {
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ArgumentParser.GetFloat(args, "width", 100));
                    modified.Add("width");
                }
                if (ArgumentParser.HasKey(args, "height"))
                {
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ArgumentParser.GetFloat(args, "height", 100));
                    modified.Add("height");
                }

                if (ArgumentParser.HasKey(args, "sizeDeltaX") || ArgumentParser.HasKey(args, "sizeDeltaY"))
                {
                    rectTransform.sizeDelta = new Vector2(
                        ArgumentParser.GetFloat(args, "sizeDeltaX", rectTransform.sizeDelta.x),
                        ArgumentParser.GetFloat(args, "sizeDeltaY", rectTransform.sizeDelta.y)
                    );
                    modified.Add("sizeDelta");
                }

                if (ArgumentParser.HasKey(args, "offsetMinX") || ArgumentParser.HasKey(args, "offsetMinY"))
                {
                    rectTransform.offsetMin = new Vector2(
                        ArgumentParser.GetFloat(args, "offsetMinX", rectTransform.offsetMin.x),
                        ArgumentParser.GetFloat(args, "offsetMinY", rectTransform.offsetMin.y)
                    );
                    modified.Add("offsetMin");
                }
                if (ArgumentParser.HasKey(args, "offsetMaxX") || ArgumentParser.HasKey(args, "offsetMaxY"))
                {
                    rectTransform.offsetMax = new Vector2(
                        ArgumentParser.GetFloat(args, "offsetMaxX", rectTransform.offsetMax.x),
                        ArgumentParser.GetFloat(args, "offsetMaxY", rectTransform.offsetMax.y)
                    );
                    modified.Add("offsetMax");
                }

                EditorUtility.SetDirty(rectTransform);

                return McpResponse.Success($"Modified RectTransform on '{gameObjectPath}'", new
                {
                    gameObject = gameObjectPath,
                    modifiedProperties = modified,
                    current = new
                    {
                        anchorMin = new { x = rectTransform.anchorMin.x, y = rectTransform.anchorMin.y },
                        anchorMax = new { x = rectTransform.anchorMax.x, y = rectTransform.anchorMax.y },
                        pivot = new { x = rectTransform.pivot.x, y = rectTransform.pivot.y },
                        anchoredPosition = new { x = rectTransform.anchoredPosition.x, y = rectTransform.anchoredPosition.y },
                        sizeDelta = new { x = rectTransform.sizeDelta.x, y = rectTransform.sizeDelta.y }
                    }
                });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to set RectTransform: {ex.Message}");
            }
        }

        private static McpToolResult GetUIHierarchy(Dictionary<string, object> args)
        {
            try
            {
                string canvasPath = ArgumentParser.GetString(args, "canvasPath", null);

                Canvas targetCanvas = null;
                if (!string.IsNullOrEmpty(canvasPath))
                {
                    var canvasGO = GameObject.Find(canvasPath);
                    if (canvasGO != null)
                        targetCanvas = canvasGO.GetComponent<Canvas>();
                }

                var canvases = targetCanvas != null
                    ? new Canvas[] { targetCanvas }
                    : UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);

                var result = new List<object>();
                foreach (var canvas in canvases)
                {
                    result.Add(new
                    {
                        name = canvas.gameObject.name,
                        path = GameObjectHelpers.GetGameObjectPath(canvas.gameObject),
                        renderMode = canvas.renderMode.ToString(),
                        sortingOrder = canvas.sortingOrder,
                        children = GetUIChildrenInfo(canvas.transform)
                    });
                }

                return McpResponse.Success("UI Hierarchy", new { canvases = result, count = canvases.Length });
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to get UI hierarchy: {ex.Message}");
            }
        }

        #endregion

        #region UI Element Factory Methods

        private static GameObject CreateUIPanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(1, 1, 1, 0.4f);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 200);
            return go;
        }

        private static GameObject CreateUIButton(string name, Transform parent, string buttonText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.GetComponent<Text>();
            text.text = buttonText;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            return go;
        }

        private static GameObject CreateUIText(string name, Transform parent, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);
            return go;
        }

        private static GameObject CreateUIImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);
            return go;
        }

        private static GameObject CreateUIRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);
            return go;
        }

        private static GameObject CreateUISlider(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 20);

            var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(go.transform, false);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-15, 0);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(10, 0);
            fill.GetComponent<Image>().color = new Color(0.3f, 0.6f, 1f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20, 0);
            handle.GetComponent<Image>().color = Color.white;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;

            return go;
        }

        private static GameObject CreateUIToggle(string name, Transform parent, string labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 20);

            var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(go.transform, false);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 1);
            bgRect.anchorMax = new Vector2(0, 1);
            bgRect.anchoredPosition = new Vector2(10, -10);
            bgRect.sizeDelta = new Vector2(20, 20);
            background.GetComponent<Image>().color = Color.white;

            var checkmark = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkmark.transform.SetParent(background.transform, false);
            var checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-4, -4);
            checkmark.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);

            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(23, 1);
            labelRect.offsetMax = new Vector2(-5, -2);
            var text = label.GetComponent<Text>();
            text.text = labelText;
            text.color = Color.black;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background.GetComponent<Image>();
            toggle.graphic = checkmark.GetComponent<Image>();

            return go;
        }

        private static GameObject CreateUIInputField(string name, Transform parent, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);
            go.GetComponent<Image>().color = Color.white;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            placeholderGO.transform.SetParent(go.transform, false);
            var phRect = placeholderGO.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 6);
            phRect.offsetMax = new Vector2(-10, -7);
            var phText = placeholderGO.GetComponent<Text>();
            phText.text = placeholder;
            phText.fontStyle = FontStyle.Italic;
            phText.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 6);
            textRect.offsetMax = new Vector2(-10, -7);
            var text = textGO.GetComponent<Text>();
            text.color = Color.black;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.supportRichText = false;

            var inputField = go.GetComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = phText;

            return go;
        }

        private static GameObject CreateUIDropdown(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);
            go.GetComponent<Image>().color = Color.white;

            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 6);
            labelRect.offsetMax = new Vector2(-25, -7);
            var labelText = label.GetComponent<Text>();
            labelText.color = Color.black;
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.alignment = TextAnchor.MiddleLeft;

            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            arrow.transform.SetParent(go.transform, false);
            var arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0.5f);
            arrowRect.anchorMax = new Vector2(1, 0.5f);
            arrowRect.sizeDelta = new Vector2(20, 20);
            arrowRect.anchoredPosition = new Vector2(-15, 0);
            arrow.GetComponent<Image>().color = Color.black;

            var dropdown = go.GetComponent<Dropdown>();
            dropdown.captionText = labelText;
            dropdown.options.Add(new Dropdown.OptionData("Option A"));
            dropdown.options.Add(new Dropdown.OptionData("Option B"));
            dropdown.options.Add(new Dropdown.OptionData("Option C"));
            dropdown.RefreshShownValue();

            return go;
        }

        private static GameObject CreateUIScrollView(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);
            go.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.5f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(go.transform, false);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            vpRect.pivot = new Vector2(0, 1);
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.sizeDelta = new Vector2(0, 300);

            var scrollRect = go.GetComponent<ScrollRect>();
            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            return go;
        }

        #endregion

        #region UI Helper Methods

        private static void ApplyAnchorPreset(RectTransform rt, string preset)
        {
            switch (preset.ToLowerInvariant())
            {
                case "topleft":
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                    break;
                case "topcenter":
                case "top":
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
                    break;
                case "topright":
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                    break;
                case "middleleft":
                case "left":
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
                    break;
                case "middlecenter":
                case "center":
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    break;
                case "middleright":
                case "right":
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
                    break;
                case "bottomleft":
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
                    break;
                case "bottomcenter":
                case "bottom":
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
                    break;
                case "bottomright":
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
                    break;
                case "stretchhorizontal":
                    rt.anchorMin = new Vector2(0, 0.5f);
                    rt.anchorMax = new Vector2(1, 0.5f);
                    break;
                case "stretchvertical":
                    rt.anchorMin = new Vector2(0.5f, 0);
                    rt.anchorMax = new Vector2(0.5f, 1);
                    break;
                case "stretchall":
                case "stretch":
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    break;
            }
        }

        private static List<object> GetUIChildrenInfo(Transform parent)
        {
            var children = new List<object>();
            foreach (Transform child in parent)
            {
                var rect = child.GetComponent<RectTransform>();
                var uiComponents = new List<string>();

                if (child.GetComponent<Button>()) uiComponents.Add("Button");
                if (child.GetComponent<Text>()) uiComponents.Add("Text");
                if (child.GetComponent<Image>()) uiComponents.Add("Image");
                if (child.GetComponent<RawImage>()) uiComponents.Add("RawImage");
                if (child.GetComponent<Slider>()) uiComponents.Add("Slider");
                if (child.GetComponent<Toggle>()) uiComponents.Add("Toggle");
                if (child.GetComponent<InputField>()) uiComponents.Add("InputField");
                if (child.GetComponent<Dropdown>()) uiComponents.Add("Dropdown");
                if (child.GetComponent<ScrollRect>()) uiComponents.Add("ScrollRect");

                children.Add(new
                {
                    name = child.name,
                    uiType = uiComponents.Count > 0 ? string.Join(", ", uiComponents) : "Container",
                    position = rect != null ? new { x = rect.anchoredPosition.x, y = rect.anchoredPosition.y } : null,
                    size = rect != null ? new { x = rect.sizeDelta.x, y = rect.sizeDelta.y } : null,
                    children = GetUIChildrenInfo(child)
                });
            }
            return children;
        }

        #endregion
    }
}
