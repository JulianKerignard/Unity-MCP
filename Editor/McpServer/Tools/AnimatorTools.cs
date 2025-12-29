using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using McpUnity.Helpers;
using McpUnity.Protocol;

namespace McpUnity.Server
{
    /// <summary>
    /// Animator Controller tools for MCP Unity Server.
    /// Provides 16 tools for animation system management.
    /// </summary>
    public partial class McpUnityServer
    {
        /// <summary>
        /// Register all Animator-related tools.
        /// </summary>
        static partial void RegisterAnimatorTools()
        {
            // Animator Controller tools
            RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_controller",
                description = "Get detailed information about an Animator Controller including layers, states, transitions, and parameters",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset (optional if gameObjectPath provided)" },
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to a GameObject with Animator component (optional if controllerPath provided)" }
                    }
                }
            }, GetAnimatorController);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_parameters",
                description = "Get runtime parameter values from an Animator component on a GameObject",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the GameObject with Animator component" }
                    },
                    required = new List<string> { "gameObjectPath" }
                }
            }, GetAnimatorParameters);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_set_animator_parameter",
                description = "Set a runtime parameter value on an Animator component",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["gameObjectPath"] = new McpPropertySchema { type = "string", description = "Path to the GameObject with Animator component" },
                        ["parameterName"] = new McpPropertySchema { type = "string", description = "Name of the parameter to set" },
                        ["value"] = new McpPropertySchema { description = "Value to set (type depends on parameter type)" },
                        ["parameterType"] = new McpPropertySchema { type = "string", description = "Parameter type: Float, Int, Bool, or Trigger (optional, auto-detected)" }
                    },
                    required = new List<string> { "gameObjectPath", "parameterName" }
                }
            }, SetAnimatorParameter);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_parameter",
                description = "Add a new parameter to an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["parameterName"] = new McpPropertySchema { type = "string", description = "Name of the new parameter" },
                        ["parameterType"] = new McpPropertySchema { type = "string", description = "Type: Float, Int, Bool, or Trigger" },
                        ["defaultValue"] = new McpPropertySchema { description = "Default value for the parameter (optional)" }
                    },
                    required = new List<string> { "controllerPath", "parameterName", "parameterType" }
                }
            }, AddAnimatorParameter);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_state",
                description = "Add a new state to an Animator Controller layer",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["stateName"] = new McpPropertySchema { type = "string", description = "Name of the new state" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["position"] = new McpPropertySchema { type = "object", description = "Position {x, y} in the Animator window (optional)" },
                        ["motionClip"] = new McpPropertySchema { type = "string", description = "Path to an AnimationClip to assign (optional)" }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, AddAnimatorState);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_add_animator_transition",
                description = "Add a transition between states in an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["fromState"] = new McpPropertySchema { type = "string", description = "Source state name (use 'Any' for AnyState)" },
                        ["toState"] = new McpPropertySchema { type = "string", description = "Destination state name" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["hasExitTime"] = new McpPropertySchema { type = "boolean", description = "Whether transition has exit time (default: true)" },
                        ["exitTime"] = new McpPropertySchema { type = "number", description = "Exit time (default: 1.0)" },
                        ["transitionDuration"] = new McpPropertySchema { type = "number", description = "Transition duration (default: 0.25)" },
                        ["conditions"] = new McpPropertySchema { type = "array", description = "Array of conditions: [{parameter, mode, threshold}]" }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, AddAnimatorTransition);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_validate_animator",
                description = "Validate an Animator Controller for common issues (orphan states, unused parameters, etc.)",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" }
                    },
                    required = new List<string> { "controllerPath" }
                }
            }, ValidateAnimator);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_get_animator_flow",
                description = "Trace all possible paths through an Animator Controller from a starting state",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["fromState"] = new McpPropertySchema { type = "string", description = "Starting state name (default: 'Entry' for default state)" },
                        ["maxDepth"] = new McpPropertySchema { type = "integer", description = "Maximum path depth to trace (default: 10)" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" }
                    },
                    required = new List<string> { "controllerPath" }
                }
            }, GetAnimatorFlow);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_delete_animator_state",
                description = "Delete a state from an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["stateName"] = new McpPropertySchema { type = "string", description = "Name of the state to delete" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, DeleteAnimatorState);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_delete_animator_transition",
                description = "Delete a transition from an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["fromState"] = new McpPropertySchema { type = "string", description = "Source state name (use 'Any' for AnyState)" },
                        ["toState"] = new McpPropertySchema { type = "string", description = "Destination state name" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["transitionIndex"] = new McpPropertySchema { type = "integer", description = "If multiple transitions exist, specify which one (default: 0)" }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, DeleteAnimatorTransition);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_modify_animator_state",
                description = "Modify properties of an existing Animator state",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["stateName"] = new McpPropertySchema { type = "string", description = "Name of the state to modify" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["newName"] = new McpPropertySchema { type = "string", description = "New name for the state (optional)" },
                        ["motion"] = new McpPropertySchema { type = "string", description = "Path to AnimationClip to assign (optional)" },
                        ["speed"] = new McpPropertySchema { type = "number", description = "Playback speed (optional)" },
                        ["speedParameter"] = new McpPropertySchema { type = "string", description = "Parameter to control speed (optional)" },
                        ["cycleOffset"] = new McpPropertySchema { type = "number", description = "Cycle offset (optional)" },
                        ["mirror"] = new McpPropertySchema { type = "boolean", description = "Mirror animation (optional)" },
                        ["writeDefaultValues"] = new McpPropertySchema { type = "boolean", description = "Write default values (optional)" }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, ModifyAnimatorState);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_modify_transition",
                description = "Modify properties of an existing Animator transition",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["fromState"] = new McpPropertySchema { type = "string", description = "Source state name (use 'Any' for AnyState)" },
                        ["toState"] = new McpPropertySchema { type = "string", description = "Destination state name" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["transitionIndex"] = new McpPropertySchema { type = "integer", description = "If multiple transitions exist, specify which one (default: 0)" },
                        ["hasExitTime"] = new McpPropertySchema { type = "boolean", description = "Whether transition has exit time" },
                        ["exitTime"] = new McpPropertySchema { type = "number", description = "Exit time" },
                        ["duration"] = new McpPropertySchema { type = "number", description = "Transition duration" },
                        ["offset"] = new McpPropertySchema { type = "number", description = "Transition offset" },
                        ["interruptionSource"] = new McpPropertySchema { type = "string", description = "Interruption source: None, Source, Destination, SourceThenDestination, DestinationThenSource" },
                        ["canTransitionToSelf"] = new McpPropertySchema { type = "boolean", description = "Whether can transition to self" }
                    },
                    required = new List<string> { "controllerPath", "fromState", "toState" }
                }
            }, ModifyTransition);

            // Blend Tree tools
            RegisterTool(new McpToolDefinition
            {
                name = "unity_create_blend_tree",
                description = "Create a new BlendTree state in an Animator Controller",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["stateName"] = new McpPropertySchema { type = "string", description = "Name for the BlendTree state" },
                        ["blendType"] = new McpPropertySchema { type = "string", description = "Blend type: 1D, 2DSimpleDirectional, 2DFreeformDirectional, 2DFreeformCartesian, Direct (default: 1D)" },
                        ["blendParameter"] = new McpPropertySchema { type = "string", description = "Parameter name for blending (X-axis for 2D)" },
                        ["blendParameterY"] = new McpPropertySchema { type = "string", description = "Second parameter for 2D blend trees" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" }
                    },
                    required = new List<string> { "controllerPath", "stateName" }
                }
            }, CreateBlendTree);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_add_blend_motion",
                description = "Add a motion (animation clip) to an existing BlendTree",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["controllerPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimatorController asset" },
                        ["blendTreeState"] = new McpPropertySchema { type = "string", description = "Name of the BlendTree state" },
                        ["motionPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimationClip" },
                        ["layerIndex"] = new McpPropertySchema { type = "integer", description = "Layer index (default: 0)" },
                        ["threshold"] = new McpPropertySchema { type = "number", description = "Threshold value for 1D blend trees" },
                        ["positionX"] = new McpPropertySchema { type = "number", description = "X position for 2D blend trees" },
                        ["positionY"] = new McpPropertySchema { type = "number", description = "Y position for 2D blend trees" }
                    },
                    required = new List<string> { "controllerPath", "blendTreeState", "motionPath" }
                }
            }, AddBlendMotion);

            // Animation Clip tools
            RegisterTool(new McpToolDefinition
            {
                name = "unity_list_animation_clips",
                description = "List all animation clips in the project with optional filtering",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["searchPath"] = new McpPropertySchema { type = "string", description = "Folder to search in (default: 'Assets')" },
                        ["nameFilter"] = new McpPropertySchema { type = "string", description = "Filter clips by name (case-insensitive)" },
                        ["avatarFilter"] = new McpPropertySchema { type = "string", description = "Filter by type: 'humanoid', 'generic', 'legacy'" }
                    }
                }
            }, ListAnimationClips);

            RegisterTool(new McpToolDefinition
            {
                name = "unity_get_clip_info",
                description = "Get detailed information about an animation clip including curves and events",
                inputSchema = new McpInputSchema
                {
                    type = "object",
                    properties = new Dictionary<string, McpPropertySchema>
                    {
                        ["clipPath"] = new McpPropertySchema { type = "string", description = "Path to the AnimationClip asset" }
                    },
                    required = new List<string> { "clipPath" }
                }
            }, GetClipInfo);
        }

        #region Animator Controller Helpers

        private static AnimatorController LoadAnimatorController(string controllerPath, string gameObjectPath)
        {
            // Try loading from asset path first
            if (!string.IsNullOrEmpty(controllerPath))
            {
                // SECURITY: Validate path before loading
                try
                {
                    controllerPath = SanitizePath(controllerPath);
                }
                catch (ArgumentException)
                {
                    return null; // Invalid path, return null
                }

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller != null) return controller;
            }

            // Try getting from GameObject's Animator
            if (!string.IsNullOrEmpty(gameObjectPath))
            {
                var go = GameObject.Find(gameObjectPath);
                if (go != null)
                {
                    var animator = go.GetComponent<Animator>();
                    if (animator != null && animator.runtimeAnimatorController != null)
                    {
                        // Get the source controller if it's a runtime controller
                        var path = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                        if (!string.IsNullOrEmpty(path))
                        {
                            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                        }
                    }
                }
            }

            return null;
        }

        private static AnimatorState FindStateByName(AnimatorStateMachine stateMachine, string stateName)
        {
            // Check direct states
            foreach (var state in stateMachine.states)
            {
                if (state.state.name == stateName)
                    return state.state;
            }

            // Check sub-state machines recursively
            foreach (var subMachine in stateMachine.stateMachines)
            {
                var found = FindStateByName(subMachine.stateMachine, stateName);
                if (found != null) return found;
            }

            return null;
        }

        private static AnimatorConditionMode ParseConditionMode(string mode)
        {
            switch (mode?.ToLower())
            {
                case "greater": return AnimatorConditionMode.Greater;
                case "less": return AnimatorConditionMode.Less;
                case "equals": return AnimatorConditionMode.Equals;
                case "notequal": return AnimatorConditionMode.NotEqual;
                case "if": return AnimatorConditionMode.If;
                case "ifnot": return AnimatorConditionMode.IfNot;
                default: return AnimatorConditionMode.If;
            }
        }

        private static Dictionary<string, object> SerializeAnimatorController(AnimatorController controller)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = controller.name,
                ["assetPath"] = AssetDatabase.GetAssetPath(controller)
            };

            // Serialize parameters
            var parameters = new List<Dictionary<string, object>>();
            foreach (var param in controller.parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = param.name,
                    ["type"] = param.type.ToString()
                };

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramInfo["defaultValue"] = param.defaultFloat;
                        break;
                    case AnimatorControllerParameterType.Int:
                        paramInfo["defaultValue"] = param.defaultInt;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        paramInfo["defaultValue"] = param.defaultBool;
                        break;
                }

                parameters.Add(paramInfo);
            }
            result["parameters"] = parameters;

            // Serialize layers
            var layers = new List<Dictionary<string, object>>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var layerInfo = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = layer.name,
                    ["defaultWeight"] = layer.defaultWeight
                };

                // Serialize states
                var states = new List<Dictionary<string, object>>();
                var transitions = new List<Dictionary<string, object>>();

                SerializeStateMachine(layer.stateMachine, states, transitions, layer.stateMachine.defaultState?.name);

                layerInfo["states"] = states;
                layerInfo["transitions"] = transitions;
                layerInfo["anyStateTransitions"] = SerializeAnyStateTransitions(layer.stateMachine);

                layers.Add(layerInfo);
            }
            result["layers"] = layers;

            return result;
        }

        private static void SerializeStateMachine(AnimatorStateMachine sm, List<Dictionary<string, object>> states,
            List<Dictionary<string, object>> transitions, string defaultStateName)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                var stateInfo = new Dictionary<string, object>
                {
                    ["name"] = state.name,
                    ["position"] = new Dictionary<string, object>
                    {
                        ["x"] = childState.position.x,
                        ["y"] = childState.position.y
                    },
                    ["isDefault"] = state.name == defaultStateName,
                    ["speed"] = state.speed,
                    ["motion"] = state.motion != null ? state.motion.name : null
                };
                states.Add(stateInfo);

                // Serialize transitions from this state
                foreach (var transition in state.transitions)
                {
                    var transInfo = new Dictionary<string, object>
                    {
                        ["from"] = state.name,
                        ["to"] = transition.destinationState?.name ?? transition.destinationStateMachine?.name ?? "Exit",
                        ["hasExitTime"] = transition.hasExitTime,
                        ["exitTime"] = transition.exitTime,
                        ["duration"] = transition.duration,
                        ["conditions"] = SerializeConditions(transition.conditions)
                    };
                    transitions.Add(transInfo);
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                SerializeStateMachine(subMachine.stateMachine, states, transitions, defaultStateName);
            }
        }

        private static List<Dictionary<string, object>> SerializeAnyStateTransitions(AnimatorStateMachine sm)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var transition in sm.anyStateTransitions)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["from"] = "Any",
                    ["to"] = transition.destinationState?.name ?? "Unknown",
                    ["hasExitTime"] = transition.hasExitTime,
                    ["duration"] = transition.duration,
                    ["conditions"] = SerializeConditions(transition.conditions)
                });
            }
            return result;
        }

        private static List<Dictionary<string, object>> SerializeConditions(AnimatorCondition[] conditions)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var cond in conditions)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["parameter"] = cond.parameter,
                    ["mode"] = cond.mode.ToString(),
                    ["threshold"] = cond.threshold
                });
            }
            return result;
        }

        private static void ValidateStateMachine(AnimatorStateMachine sm, int layerIndex,
            AnimatorController controller, List<object> errors, List<object> warnings,
            HashSet<string> usedParams)
        {
            // Get all states that have incoming transitions
            var statesWithIncomingTransitions = new HashSet<string>();
            if (sm.defaultState != null)
                statesWithIncomingTransitions.Add(sm.defaultState.name);

            // Collect incoming transitions from anyState
            foreach (var anyTrans in sm.anyStateTransitions)
            {
                if (anyTrans.destinationState != null)
                    statesWithIncomingTransitions.Add(anyTrans.destinationState.name);
            }

            // Validate each state
            foreach (var childState in sm.states)
            {
                var state = childState.state;

                // Check for missing motion
                if (state.motion == null)
                {
                    warnings.Add(new
                    {
                        type = "MissingMotion",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no motion assigned"
                    });
                }

                // Collect outgoing transitions and their destinations
                bool hasOutgoingTransitions = state.transitions.Length > 0;

                foreach (var transition in state.transitions)
                {
                    // Mark destination as having incoming transition
                    if (transition.destinationState != null)
                        statesWithIncomingTransitions.Add(transition.destinationState.name);

                    // Check for transition without conditions (warning)
                    if (transition.conditions.Length == 0 && !transition.hasExitTime)
                    {
                        warnings.Add(new
                        {
                            type = "TransitionWithoutCondition",
                            layer = layerIndex,
                            state = state.name,
                            to = transition.destinationState?.name ?? "Exit",
                            message = $"Transition from '{state.name}' to '{transition.destinationState?.name ?? "Exit"}' has no conditions and no exit time"
                        });
                    }

                    // Collect used parameters
                    foreach (var cond in transition.conditions)
                    {
                        usedParams.Add(cond.parameter);
                    }
                }

                // Check for dead-end state (no outgoing transitions)
                if (!hasOutgoingTransitions && state.name != "Exit")
                {
                    warnings.Add(new
                    {
                        type = "DeadEndState",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no outgoing transitions (dead end)"
                    });
                }
            }

            // Check for orphan states (no incoming transitions, not default state)
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (!statesWithIncomingTransitions.Contains(state.name))
                {
                    warnings.Add(new
                    {
                        type = "OrphanState",
                        layer = layerIndex,
                        state = state.name,
                        message = $"State '{state.name}' has no incoming transitions (orphan)"
                    });
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                ValidateStateMachine(subMachine.stateMachine, layerIndex, controller, errors, warnings, usedParams);
            }
        }

        private static void CollectStateNames(AnimatorStateMachine sm, Dictionary<string, int> stateNames)
        {
            foreach (var childState in sm.states)
            {
                var name = childState.state.name;
                if (stateNames.ContainsKey(name))
                    stateNames[name]++;
                else
                    stateNames[name] = 1;
            }

            foreach (var subMachine in sm.stateMachines)
            {
                CollectStateNames(subMachine.stateMachine, stateNames);
            }
        }

        private static void CollectAllStateNames(AnimatorStateMachine sm, HashSet<string> stateNames)
        {
            foreach (var childState in sm.states)
            {
                stateNames.Add(childState.state.name);
            }

            foreach (var subMachine in sm.stateMachines)
            {
                CollectAllStateNames(subMachine.stateMachine, stateNames);
            }
        }

        private static int CountStates(AnimatorStateMachine sm)
        {
            int count = sm.states.Length;
            foreach (var subMachine in sm.stateMachines)
            {
                count += CountStates(subMachine.stateMachine);
            }
            return count;
        }

        private static int CountTransitions(AnimatorStateMachine sm)
        {
            int count = sm.anyStateTransitions.Length;
            foreach (var childState in sm.states)
            {
                count += childState.state.transitions.Length;
            }
            foreach (var subMachine in sm.stateMachines)
            {
                count += CountTransitions(subMachine.stateMachine);
            }
            return count;
        }

        private static bool IsParameterUsedInTransitions(AnimatorController controller, string paramName)
        {
            foreach (var layer in controller.layers)
            {
                if (IsParameterUsedInStateMachine(layer.stateMachine, paramName))
                    return true;
            }
            return false;
        }

        private static bool IsParameterUsedInStateMachine(AnimatorStateMachine sm, string paramName)
        {
            // Check anyState transitions
            foreach (var transition in sm.anyStateTransitions)
            {
                foreach (var cond in transition.conditions)
                {
                    if (cond.parameter == paramName)
                        return true;
                }
            }

            // Check state transitions
            foreach (var childState in sm.states)
            {
                foreach (var transition in childState.state.transitions)
                {
                    foreach (var cond in transition.conditions)
                    {
                        if (cond.parameter == paramName)
                            return true;
                    }
                }
            }

            // Recurse into sub-state machines
            foreach (var subMachine in sm.stateMachines)
            {
                if (IsParameterUsedInStateMachine(subMachine.stateMachine, paramName))
                    return true;
            }

            return false;
        }

        private static void TracePaths(AnimatorStateMachine sm, AnimatorState current,
            List<string> currentPath, List<string> currentConditions, int depth, int maxDepth,
            List<object> allPaths, HashSet<string> visited, HashSet<string> reachableStates)
        {
            // Mark current state as reachable
            reachableStates.Add(current.name);

            // Check depth limit
            if (depth >= maxDepth)
            {
                allPaths.Add(new
                {
                    sequence = new List<string>(currentPath),
                    conditions = new List<string>(currentConditions),
                    truncated = true
                });
                return;
            }

            // If no outgoing transitions, record this path
            if (current.transitions.Length == 0)
            {
                allPaths.Add(new
                {
                    sequence = new List<string>(currentPath),
                    conditions = new List<string>(currentConditions),
                    truncated = false
                });
                return;
            }

            // Explore each transition
            foreach (var transition in current.transitions)
            {
                var destState = transition.destinationState;
                if (destState == null) continue;

                // Build condition string
                var conditionStr = BuildConditionString(transition);

                // Check for cycle
                var visitKey = $"{current.name}->{destState.name}";
                if (visited.Contains(visitKey))
                {
                    // Record path with cycle indicator
                    var cyclePath = new List<string>(currentPath) { $"{destState.name} (cycle)" };
                    var cycleConds = new List<string>(currentConditions) { conditionStr };
                    allPaths.Add(new
                    {
                        sequence = cyclePath,
                        conditions = cycleConds,
                        truncated = false,
                        hasCycle = true
                    });
                    continue;
                }

                // Add to path and recurse
                visited.Add(visitKey);
                currentPath.Add(destState.name);
                currentConditions.Add(conditionStr);

                TracePaths(sm, destState, currentPath, currentConditions, depth + 1, maxDepth, allPaths, visited, reachableStates);

                // Backtrack
                currentPath.RemoveAt(currentPath.Count - 1);
                currentConditions.RemoveAt(currentConditions.Count - 1);
                visited.Remove(visitKey);
            }
        }

        private static string BuildConditionString(AnimatorStateTransition transition)
        {
            if (transition.conditions.Length == 0)
            {
                if (transition.hasExitTime)
                    return $"exitTime >= {transition.exitTime:F2}";
                return "(no condition)";
            }

            var parts = new List<string>();
            foreach (var cond in transition.conditions)
            {
                string op;
                switch (cond.mode)
                {
                    case AnimatorConditionMode.Greater: op = ">"; break;
                    case AnimatorConditionMode.Less: op = "<"; break;
                    case AnimatorConditionMode.Equals: op = "=="; break;
                    case AnimatorConditionMode.NotEqual: op = "!="; break;
                    case AnimatorConditionMode.If: op = "== true"; break;
                    case AnimatorConditionMode.IfNot: op = "== false"; break;
                    default: op = "?"; break;
                }

                if (cond.mode == AnimatorConditionMode.If || cond.mode == AnimatorConditionMode.IfNot)
                    parts.Add($"{cond.parameter} {op}");
                else
                    parts.Add($"{cond.parameter} {op} {cond.threshold}");
            }

            return string.Join(" && ", parts);
        }

        private static TransitionInterruptionSource ParseInterruptionSource(string source)
        {
            switch (source?.ToLower())
            {
                case "source": return TransitionInterruptionSource.Source;
                case "destination": return TransitionInterruptionSource.Destination;
                case "sourcethendestination": return TransitionInterruptionSource.SourceThenDestination;
                case "destinationthensource": return TransitionInterruptionSource.DestinationThenSource;
                default: return TransitionInterruptionSource.None;
            }
        }

        private static BlendTreeType ParseBlendType(string type)
        {
            switch (type?.ToLower())
            {
                case "1d": return BlendTreeType.Simple1D;
                case "2dsimpledirectional": return BlendTreeType.SimpleDirectional2D;
                case "2dfreeformdirectional": return BlendTreeType.FreeformDirectional2D;
                case "2dfreeformcartesian": return BlendTreeType.FreeformCartesian2D;
                case "direct": return BlendTreeType.Direct;
                default: return BlendTreeType.Simple1D;
            }
        }

        #endregion

        #region Animator Controller Handlers

        private static McpToolResult GetAnimatorController(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();

            if (string.IsNullOrEmpty(controllerPath) && string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("Either controllerPath or gameObjectPath is required");

            var controller = LoadAnimatorController(controllerPath, gameObjectPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found. Path: '{controllerPath}', GameObject: '{gameObjectPath}'");

            var serialized = SerializeAnimatorController(controller);

            return new McpToolResult
            {
                content = new List<McpContent> { McpContent.Json(serialized) },
                isError = false
            };
        }

        private static McpToolResult GetAnimatorParameters(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return McpToolResult.Error($"No Animator component on: {gameObjectPath}");

            var parameters = new List<Dictionary<string, object>>();
            foreach (var param in animator.parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = param.name,
                    ["type"] = param.type.ToString()
                };

                // Get current runtime value
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramInfo["value"] = animator.GetFloat(param.name);
                        break;
                    case AnimatorControllerParameterType.Int:
                        paramInfo["value"] = animator.GetInteger(param.name);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        paramInfo["value"] = animator.GetBool(param.name);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        paramInfo["value"] = null; // Triggers don't have persistent values
                        break;
                }

                parameters.Add(paramInfo);
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        gameObject = gameObjectPath,
                        parameterCount = parameters.Count,
                        parameters = parameters
                    })
                },
                isError = false
            };
        }

        private static McpToolResult SetAnimatorParameter(Dictionary<string, object> args)
        {
            var gameObjectPath = args.GetValueOrDefault("gameObjectPath")?.ToString();
            var parameterName = args.GetValueOrDefault("parameterName")?.ToString();
            var valueObj = args.GetValueOrDefault("value");
            var parameterType = args.GetValueOrDefault("parameterType")?.ToString();

            if (string.IsNullOrEmpty(gameObjectPath))
                return McpToolResult.Error("gameObjectPath is required");
            if (string.IsNullOrEmpty(parameterName))
                return McpToolResult.Error("parameterName is required");

            var go = GameObject.Find(gameObjectPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {gameObjectPath}");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                return McpToolResult.Error($"No Animator component on: {gameObjectPath}");

            // Find the parameter to determine type if not specified
            UnityEngine.AnimatorControllerParameter foundParam = null;
            foreach (var param in animator.parameters)
            {
                if (param.name == parameterName)
                {
                    foundParam = param;
                    break;
                }
            }

            if (foundParam == null)
                return McpToolResult.Error($"Parameter '{parameterName}' not found on Animator");

            var type = !string.IsNullOrEmpty(parameterType) ? parameterType : foundParam.type.ToString();

            try
            {
                switch (type.ToLower())
                {
                    case "float":
                        if (!float.TryParse(valueObj?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floatVal))
                            return McpToolResult.Error($"Invalid float value: {valueObj}");
                        animator.SetFloat(parameterName, floatVal);
                        return McpToolResult.Success($"Set {parameterName} = {floatVal} (Float)");

                    case "int":
                    case "integer":
                        if (!int.TryParse(valueObj?.ToString(), out var intVal))
                            return McpToolResult.Error($"Invalid int value: {valueObj}");
                        animator.SetInteger(parameterName, intVal);
                        return McpToolResult.Success($"Set {parameterName} = {intVal} (Int)");

                    case "bool":
                    case "boolean":
                        if (!bool.TryParse(valueObj?.ToString(), out var boolVal))
                            return McpToolResult.Error($"Invalid bool value: {valueObj}");
                        animator.SetBool(parameterName, boolVal);
                        return McpToolResult.Success($"Set {parameterName} = {boolVal} (Bool)");

                    case "trigger":
                        animator.SetTrigger(parameterName);
                        return McpToolResult.Success($"Triggered {parameterName}");

                    default:
                        return McpToolResult.Error($"Unknown parameter type: {type}");
                }
            }
            catch (Exception ex)
            {
                return McpToolResult.Error($"Failed to set parameter: {ex.Message}");
            }
        }

        private static McpToolResult AddAnimatorParameter(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var parameterName = args.GetValueOrDefault("parameterName")?.ToString();
            var parameterType = args.GetValueOrDefault("parameterType")?.ToString();
            var defaultValue = args.GetValueOrDefault("defaultValue");

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(parameterName))
                return McpToolResult.Error("parameterName is required");
            if (string.IsNullOrEmpty(parameterType))
                return McpToolResult.Error("parameterType is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            // Check if parameter already exists
            foreach (var existing in controller.parameters)
            {
                if (existing.name == parameterName)
                    return McpToolResult.Error($"Parameter '{parameterName}' already exists");
            }

            AnimatorControllerParameterType type;
            switch (parameterType.ToLower())
            {
                case "float": type = AnimatorControllerParameterType.Float; break;
                case "int":
                case "integer": type = AnimatorControllerParameterType.Int; break;
                case "bool":
                case "boolean": type = AnimatorControllerParameterType.Bool; break;
                case "trigger": type = AnimatorControllerParameterType.Trigger; break;
                default:
                    return McpToolResult.Error($"Invalid parameter type: {parameterType}. Use Float, Int, Bool, or Trigger");
            }

            Undo.RecordObject(controller, "Add Animator Parameter");
            controller.AddParameter(parameterName, type);

            // Set default value if provided
            if (defaultValue != null)
            {
                var parameters = controller.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parameterName)
                    {
                        var param = parameters[i];
                        switch (type)
                        {
                            case AnimatorControllerParameterType.Float:
                                if (float.TryParse(defaultValue?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floatDefault))
                                    param.defaultFloat = floatDefault;
                                break;
                            case AnimatorControllerParameterType.Int:
                                if (int.TryParse(defaultValue?.ToString(), out var intDefault))
                                    param.defaultInt = intDefault;
                                break;
                            case AnimatorControllerParameterType.Bool:
                                if (bool.TryParse(defaultValue?.ToString(), out var boolDefault))
                                    param.defaultBool = boolDefault;
                                break;
                        }
                        parameters[i] = param;
                        break;
                    }
                }
                controller.parameters = parameters;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added parameter '{parameterName}' ({parameterType}) to {controller.name}",
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);
            var positionObj = args.GetValueOrDefault("position") as Dictionary<string, object>;
            var motionClip = args.GetValueOrDefault("motionClip")?.ToString();

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range. Controller has {controller.layers.Length} layers");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Check if state already exists
            if (FindStateByName(stateMachine, stateName) != null)
                return McpToolResult.Error($"State '{stateName}' already exists in layer {layerIndex}");

            // Calculate position
            Vector3 position = new Vector3(300, 100, 0);
            if (positionObj != null)
            {
                if (positionObj.TryGetValue("x", out var xObj) && float.TryParse(xObj?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var posX))
                    position.x = posX;
                if (positionObj.TryGetValue("y", out var yObj) && float.TryParse(yObj?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var posY))
                    position.y = posY;
            }
            else
            {
                // Auto-position based on existing states
                int stateCount = stateMachine.states.Length;
                position = new Vector3(300 + (stateCount % 3) * 200, 100 + (stateCount / 3) * 100, 0);
            }

            Undo.RecordObject(stateMachine, "Add Animator State");
            var newState = stateMachine.AddState(stateName, position);

            // Attach motion clip if provided
            if (!string.IsNullOrEmpty(motionClip))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionClip);
                if (clip != null)
                {
                    newState.motion = clip;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added state '{stateName}' to layer {layerIndex}",
                        state = new
                        {
                            name = newState.name,
                            position = new { x = position.x, y = position.y },
                            motion = newState.motion?.name
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddAnimatorTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);
            var conditionsObj = args.GetValueOrDefault("conditions");
            bool hasExitTime = ArgumentParser.GetBool(args, "hasExitTime", true);
            float exitTime = ArgumentParser.GetFloat(args, "exitTime", 1.0f);
            float transitionDuration = ArgumentParser.GetFloat(args, "transitionDuration", 0.25f);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Find destination state
            var destState = FindStateByName(stateMachine, toState);
            if (destState == null)
                return McpToolResult.Error($"Destination state '{toState}' not found in layer {layerIndex}");

            AnimatorStateTransition transition;

            Undo.RecordObject(controller, "Add Animator Transition");

            if (fromState.ToLower() == "any" || fromState.ToLower() == "anystate")
            {
                // Create AnyState transition
                transition = stateMachine.AddAnyStateTransition(destState);
            }
            else
            {
                // Find source state
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                transition = srcState.AddTransition(destState);
            }

            // Configure transition
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = transitionDuration;

            // Add conditions
            var addedConditions = new List<string>();
            if (conditionsObj is IList<object> conditionsList)
            {
                foreach (var condObj in conditionsList)
                {
                    if (condObj is Dictionary<string, object> cond)
                    {
                        var paramName = cond.GetValueOrDefault("parameter")?.ToString();
                        var mode = cond.GetValueOrDefault("mode")?.ToString() ?? "If";
                        float threshold = 0;
                        if (cond.TryGetValue("threshold", out var threshObj) && threshObj != null)
                            float.TryParse(threshObj.ToString(), out threshold);

                        if (!string.IsNullOrEmpty(paramName))
                        {
                            transition.AddCondition(ParseConditionMode(mode), threshold, paramName);
                            addedConditions.Add($"{paramName} {mode} {threshold}");
                        }
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added transition from '{fromState}' to '{toState}'",
                        transition = new
                        {
                            from = fromState,
                            to = toState,
                            hasExitTime = hasExitTime,
                            exitTime = exitTime,
                            duration = transitionDuration,
                            conditions = addedConditions
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ValidateAnimator(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            var errors = new List<object>();
            var warnings = new List<object>();
            var usedParams = new HashSet<string>();
            int totalStates = 0;
            int totalTransitions = 0;

            // Validate each layer
            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                var layer = controller.layers[layerIndex];
                var sm = layer.stateMachine;

                ValidateStateMachine(sm, layerIndex, controller, errors, warnings, usedParams);
                totalStates += CountStates(sm);
                totalTransitions += CountTransitions(sm);

                // Check for duplicate state names within the layer
                var stateNames = new Dictionary<string, int>();
                CollectStateNames(sm, stateNames);
                foreach (var kvp in stateNames)
                {
                    if (kvp.Value > 1)
                    {
                        errors.Add(new
                        {
                            type = "DuplicateStateName",
                            layer = layerIndex,
                            state = kvp.Key,
                            message = $"State name '{kvp.Key}' appears {kvp.Value} times in layer {layerIndex}"
                        });
                    }
                }
            }

            // Check for unused parameters
            foreach (var param in controller.parameters)
            {
                if (!usedParams.Contains(param.name))
                {
                    if (!IsParameterUsedInTransitions(controller, param.name))
                    {
                        warnings.Add(new
                        {
                            type = "UnusedParameter",
                            parameter = param.name,
                            message = $"Parameter '{param.name}' is never used in any transition condition"
                        });
                    }
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        isValid = errors.Count == 0,
                        errors = errors,
                        warnings = warnings,
                        stats = new
                        {
                            totalStates = totalStates,
                            totalTransitions = totalTransitions,
                            totalParameters = controller.parameters.Length,
                            layers = controller.layers.Length
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetAnimatorFlow(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString() ?? "Entry";
            int maxDepth = ArgumentParser.GetIntClamped(args, "maxDepth", 10, 1, 50);
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var sm = controller.layers[layerIndex].stateMachine;
            var allPaths = new List<object>();
            var reachableStates = new HashSet<string>();
            var anyStateTargets = new List<string>();

            // Get AnyState targets
            foreach (var transition in sm.anyStateTransitions)
            {
                if (transition.destinationState != null)
                    anyStateTargets.Add(transition.destinationState.name);
            }

            // Determine starting state
            AnimatorState startState = null;
            if (fromState.ToLower() == "entry")
            {
                startState = sm.defaultState;
                if (startState == null)
                    return McpToolResult.Error("No default state defined in this layer");
            }
            else
            {
                startState = FindStateByName(sm, fromState);
                if (startState == null)
                    return McpToolResult.Error($"State '{fromState}' not found in layer {layerIndex}");
            }

            // Trace paths using BFS
            var pathSequence = new List<string> { startState.name };
            var conditionSequence = new List<string> { "(start)" };
            var visited = new HashSet<string>();

            TracePaths(sm, startState, pathSequence, conditionSequence, 0, maxDepth, allPaths, visited, reachableStates);

            // Find all states in the layer
            var allStates = new HashSet<string>();
            CollectAllStateNames(sm, allStates);

            // Calculate unreachable states
            var unreachableStates = new List<string>();
            foreach (var state in allStates)
            {
                if (!reachableStates.Contains(state) && state != startState.name)
                {
                    // Check if reachable via AnyState
                    if (!anyStateTargets.Contains(state))
                        unreachableStates.Add(state);
                }
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        paths = allPaths,
                        reachableStates = reachableStates.ToList(),
                        unreachableStates = unreachableStates,
                        anyStateTargets = anyStateTargets
                    })
                },
                isError = false
            };
        }

        private static McpToolResult DeleteAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range. Controller has {controller.layers.Length} layers");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            // Find the state to delete
            var stateToDelete = FindStateByName(stateMachine, stateName);
            if (stateToDelete == null)
                return McpToolResult.Error($"State '{stateName}' not found in layer {layerIndex}");

            // Check if it's the default state
            bool wasDefaultState = stateMachine.defaultState == stateToDelete;

            Undo.RecordObject(stateMachine, "Delete Animator State");
            stateMachine.RemoveState(stateToDelete);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Deleted state '{stateName}' from layer {layerIndex}",
                        deletedState = stateName,
                        wasDefaultState = wasDefaultState,
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult DeleteAnimatorTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int transitionIndex = ArgumentParser.GetInt(args, "transitionIndex", 0);
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            Undo.RecordObject(controller, "Delete Animator Transition");

            bool isAnyState = fromState.ToLower() == "any" || fromState.ToLower() == "anystate";

            if (isAnyState)
            {
                // Handle AnyState transition
                var anyTransitions = stateMachine.anyStateTransitions;
                AnimatorStateTransition toRemove = null;

                // Find the transition to the specified destination state
                foreach (var t in anyTransitions)
                {
                    if (t.destinationState != null && t.destinationState.name == toState)
                    {
                        toRemove = t;
                        break;
                    }
                }

                if (toRemove == null)
                    return McpToolResult.Error($"No AnyState transition to '{toState}' found in layer {layerIndex}");

                Undo.RecordObject(stateMachine, "Delete AnyState Transition");
                stateMachine.RemoveAnyStateTransition(toRemove);
            }
            else
            {
                // Handle regular state transition
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                var transitions = srcState.transitions;
                if (transitions == null || transitions.Length == 0)
                    return McpToolResult.Error($"State '{fromState}' has no transitions");

                // Find the transition to the specified destination state
                AnimatorStateTransition toRemove = null;
                int matchCount = 0;

                for (int i = 0; i < transitions.Length; i++)
                {
                    var t = transitions[i];
                    if (t.destinationState != null && t.destinationState.name == toState)
                    {
                        if (matchCount == transitionIndex)
                        {
                            toRemove = t;
                            break;
                        }
                        matchCount++;
                    }
                }

                if (toRemove == null)
                {
                    if (matchCount == 0)
                        return McpToolResult.Error($"No transition from '{fromState}' to '{toState}' found");
                    else
                        return McpToolResult.Error($"Transition index {transitionIndex} out of range. Found {matchCount} transitions from '{fromState}' to '{toState}'");
                }

                Undo.RecordObject(srcState, "Delete State Transition");
                srcState.RemoveTransition(toRemove);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Deleted transition from '{fromState}' to '{toState}'",
                        fromState = fromState,
                        toState = toState,
                        transitionIndex = transitionIndex,
                        controllerPath = controllerPath
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ModifyAnimatorState(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;
            var state = FindStateByName(stateMachine, stateName);
            if (state == null)
                return McpToolResult.Error($"State '{stateName}' not found in layer {layerIndex}");

            Undo.RecordObject(state, "Modify Animator State");

            var modifiedProperties = new List<string>();

            // Apply optional properties
            if (args.TryGetValue("newName", out var newNameObj) && newNameObj != null)
            {
                var newName = newNameObj.ToString();
                state.name = newName;
                modifiedProperties.Add($"name: {newName}");
            }

            if (args.TryGetValue("motion", out var motionObj) && motionObj != null)
            {
                var motionPath = motionObj.ToString();
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                if (clip != null)
                {
                    state.motion = clip;
                    modifiedProperties.Add($"motion: {motionPath}");
                }
                else
                {
                    return McpToolResult.Error($"AnimationClip not found: {motionPath}");
                }
            }

            if (args.TryGetValue("speed", out var speedObj) && speedObj != null)
            {
                if (float.TryParse(speedObj.ToString(), out float speed))
                {
                    state.speed = speed;
                    modifiedProperties.Add($"speed: {speed}");
                }
            }

            if (args.TryGetValue("speedParameter", out var speedParamObj) && speedParamObj != null)
            {
                var speedParam = speedParamObj.ToString();
                state.speedParameterActive = true;
                state.speedParameter = speedParam;
                modifiedProperties.Add($"speedParameter: {speedParam}");
            }

            if (args.TryGetValue("cycleOffset", out var cycleOffsetObj) && cycleOffsetObj != null)
            {
                if (float.TryParse(cycleOffsetObj.ToString(), out float cycleOffset))
                {
                    state.cycleOffset = cycleOffset;
                    modifiedProperties.Add($"cycleOffset: {cycleOffset}");
                }
            }

            if (args.TryGetValue("mirror", out var mirrorObj) && mirrorObj != null)
            {
                if (bool.TryParse(mirrorObj.ToString(), out bool mirror))
                {
                    state.mirror = mirror;
                    modifiedProperties.Add($"mirror: {mirror}");
                }
            }

            if (args.TryGetValue("writeDefaultValues", out var writeDefaultsObj) && writeDefaultsObj != null)
            {
                if (bool.TryParse(writeDefaultsObj.ToString(), out bool writeDefaults))
                {
                    state.writeDefaultValues = writeDefaults;
                    modifiedProperties.Add($"writeDefaultValues: {writeDefaults}");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Modified state '{stateName}' in layer {layerIndex}",
                        modifiedProperties = modifiedProperties
                    })
                },
                isError = false
            };
        }

        private static McpToolResult ModifyTransition(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var fromState = args.GetValueOrDefault("fromState")?.ToString();
            var toState = args.GetValueOrDefault("toState")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);
            int transitionIndex = ArgumentParser.GetInt(args, "transitionIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(fromState))
                return McpToolResult.Error("fromState is required");
            if (string.IsNullOrEmpty(toState))
                return McpToolResult.Error("toState is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            AnimatorStateTransition transition = null;

            // Handle AnyState transitions
            if (fromState.ToLower() == "any" || fromState.ToLower() == "anystate")
            {
                var anyTransitions = stateMachine.anyStateTransitions
                    .Where(t => t.destinationState != null && t.destinationState.name == toState)
                    .ToArray();

                if (anyTransitions.Length == 0)
                    return McpToolResult.Error($"No AnyState transition to '{toState}' found");
                if (transitionIndex >= anyTransitions.Length)
                    return McpToolResult.Error($"Transition index {transitionIndex} out of range (found {anyTransitions.Length})");

                transition = anyTransitions[transitionIndex];
            }
            else
            {
                // Find source state and its transitions
                var srcState = FindStateByName(stateMachine, fromState);
                if (srcState == null)
                    return McpToolResult.Error($"Source state '{fromState}' not found in layer {layerIndex}");

                var stateTransitions = srcState.transitions
                    .Where(t => t.destinationState != null && t.destinationState.name == toState)
                    .ToArray();

                if (stateTransitions.Length == 0)
                    return McpToolResult.Error($"No transition from '{fromState}' to '{toState}' found");
                if (transitionIndex >= stateTransitions.Length)
                    return McpToolResult.Error($"Transition index {transitionIndex} out of range (found {stateTransitions.Length})");

                transition = stateTransitions[transitionIndex];
            }

            Undo.RecordObject(transition, "Modify Transition");

            var modifiedProperties = new List<string>();

            // Apply optional properties
            if (args.TryGetValue("hasExitTime", out var hasExitTimeObj) && hasExitTimeObj != null)
            {
                if (bool.TryParse(hasExitTimeObj.ToString(), out bool hasExitTime))
                {
                    transition.hasExitTime = hasExitTime;
                    modifiedProperties.Add($"hasExitTime: {hasExitTime}");
                }
            }

            if (args.TryGetValue("exitTime", out var exitTimeObj) && exitTimeObj != null)
            {
                if (float.TryParse(exitTimeObj.ToString(), out float exitTime))
                {
                    transition.exitTime = exitTime;
                    modifiedProperties.Add($"exitTime: {exitTime}");
                }
            }

            if (args.TryGetValue("duration", out var durationObj) && durationObj != null)
            {
                if (float.TryParse(durationObj.ToString(), out float duration))
                {
                    transition.duration = duration;
                    modifiedProperties.Add($"duration: {duration}");
                }
            }

            if (args.TryGetValue("offset", out var offsetObj) && offsetObj != null)
            {
                if (float.TryParse(offsetObj.ToString(), out float offset))
                {
                    transition.offset = offset;
                    modifiedProperties.Add($"offset: {offset}");
                }
            }

            if (args.TryGetValue("interruptionSource", out var interruptObj) && interruptObj != null)
            {
                var source = ParseInterruptionSource(interruptObj.ToString());
                transition.interruptionSource = source;
                modifiedProperties.Add($"interruptionSource: {source}");
            }

            if (args.TryGetValue("canTransitionToSelf", out var canTransitionObj) && canTransitionObj != null)
            {
                if (bool.TryParse(canTransitionObj.ToString(), out bool canTransitionToSelf))
                {
                    transition.canTransitionToSelf = canTransitionToSelf;
                    modifiedProperties.Add($"canTransitionToSelf: {canTransitionToSelf}");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Modified transition from '{fromState}' to '{toState}'",
                        modifiedProperties = modifiedProperties
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Blend Tree Handlers

        private static McpToolResult CreateBlendTree(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var stateName = args.GetValueOrDefault("stateName")?.ToString();
            var blendTypeStr = args.GetValueOrDefault("blendType")?.ToString() ?? "1D";
            var blendParameter = args.GetValueOrDefault("blendParameter")?.ToString();
            var blendParameterY = args.GetValueOrDefault("blendParameterY")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(stateName))
                return McpToolResult.Error("stateName is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found at: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} is out of range. Controller has {controller.layers.Length} layers.");

            Undo.RecordObject(controller, "Create Blend Tree");

            BlendTree blendTree;
            controller.CreateBlendTreeInController(stateName, out blendTree, layerIndex);

            if (blendTree == null)
                return McpToolResult.Error("Failed to create blend tree");

            blendTree.blendType = ParseBlendType(blendTypeStr);

            if (!string.IsNullOrEmpty(blendParameter))
                blendTree.blendParameter = blendParameter;

            if (!string.IsNullOrEmpty(blendParameterY) && blendTree.blendType != BlendTreeType.Simple1D)
                blendTree.blendParameterY = blendParameterY;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Find the created state to return its info
            var layer = controller.layers[layerIndex];
            var createdState = layer.stateMachine.states
                .FirstOrDefault(s => s.state.name == stateName).state;

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Created blend tree '{stateName}' in layer {layerIndex}",
                        blendTree = new
                        {
                            stateName = stateName,
                            blendType = blendTree.blendType.ToString(),
                            blendParameter = blendTree.blendParameter,
                            blendParameterY = blendTree.blendParameterY,
                            layerIndex = layerIndex,
                            childCount = blendTree.children.Length
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult AddBlendMotion(Dictionary<string, object> args)
        {
            var controllerPath = args.GetValueOrDefault("controllerPath")?.ToString();
            var blendTreeState = args.GetValueOrDefault("blendTreeState")?.ToString();
            var motionPath = args.GetValueOrDefault("motionPath")?.ToString();
            int layerIndex = ArgumentParser.GetInt(args, "layerIndex", 0);

            // For 1D blend trees
            float threshold = ArgumentParser.GetFloat(args, "threshold", 0f);

            // For 2D blend trees
            float positionX = ArgumentParser.GetFloat(args, "positionX", 0f);
            float positionY = ArgumentParser.GetFloat(args, "positionY", 0f);

            if (string.IsNullOrEmpty(controllerPath))
                return McpToolResult.Error("controllerPath is required");
            if (string.IsNullOrEmpty(blendTreeState))
                return McpToolResult.Error("blendTreeState is required");
            if (string.IsNullOrEmpty(motionPath))
                return McpToolResult.Error("motionPath is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return McpToolResult.Error($"AnimatorController not found at: {controllerPath}");

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return McpToolResult.Error($"Layer index {layerIndex} is out of range. Controller has {controller.layers.Length} layers.");

            var layer = controller.layers[layerIndex];
            var state = layer.stateMachine.states
                .FirstOrDefault(s => s.state.name == blendTreeState).state;

            if (state == null)
                return McpToolResult.Error($"State '{blendTreeState}' not found in layer {layerIndex}");

            var blendTree = state.motion as BlendTree;
            if (blendTree == null)
                return McpToolResult.Error($"State '{blendTreeState}' does not contain a BlendTree");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
            if (clip == null)
                return McpToolResult.Error($"AnimationClip not found at: {motionPath}");

            Undo.RecordObject(blendTree, "Add Blend Motion");

            // Add child based on blend tree type
            if (blendTree.blendType == BlendTreeType.Simple1D)
            {
                blendTree.AddChild(clip, threshold);
            }
            else
            {
                blendTree.AddChild(clip, new Vector2(positionX, positionY));
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(blendTree);
            AssetDatabase.SaveAssets();

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        success = true,
                        message = $"Added motion '{clip.name}' to blend tree '{blendTreeState}'",
                        motion = new
                        {
                            clipName = clip.name,
                            clipPath = motionPath,
                            blendTreeState = blendTreeState,
                            blendType = blendTree.blendType.ToString(),
                            threshold = blendTree.blendType == BlendTreeType.Simple1D ? threshold : (float?)null,
                            position = blendTree.blendType != BlendTreeType.Simple1D
                                ? new { x = positionX, y = positionY }
                                : null,
                            totalChildren = blendTree.children.Length
                        }
                    })
                },
                isError = false
            };
        }

        #endregion

        #region Animation Clip Handlers

        private static McpToolResult ListAnimationClips(Dictionary<string, object> args)
        {
            string searchPath = ArgumentParser.GetString(args, "searchPath", "Assets");
            string nameFilter = ArgumentParser.GetString(args, "nameFilter", null)?.ToLowerInvariant();
            string avatarFilter = ArgumentParser.GetString(args, "avatarFilter", null)?.ToLowerInvariant();

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { searchPath });
            var clips = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                if (clip == null) continue;

                // Apply name filter
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    if (!clip.name.ToLowerInvariant().Contains(nameFilter))
                        continue;
                }

                // Apply avatar filter
                if (!string.IsNullOrEmpty(avatarFilter))
                {
                    bool isHumanoid = clip.isHumanMotion;
                    bool isLegacy = clip.legacy;

                    if (avatarFilter == "humanoid" && !isHumanoid) continue;
                    if (avatarFilter == "generic" && (isHumanoid || isLegacy)) continue;
                    if (avatarFilter == "legacy" && !isLegacy) continue;
                }

                clips.Add(new
                {
                    path = path,
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    isHumanMotion = clip.isHumanMotion,
                    hasRootMotion = clip.hasMotionCurves,
                    isLegacy = clip.legacy
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        clips = clips,
                        totalCount = clips.Count,
                        searchPath = searchPath,
                        filters = new
                        {
                            nameFilter = nameFilter,
                            avatarFilter = avatarFilter
                        }
                    })
                },
                isError = false
            };
        }

        private static McpToolResult GetClipInfo(Dictionary<string, object> args)
        {
            var clipPath = args.GetValueOrDefault("clipPath")?.ToString();

            if (string.IsNullOrEmpty(clipPath))
                return McpToolResult.Error("clipPath is required");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return McpToolResult.Error($"AnimationClip not found at: {clipPath}");

            // Get animation events
            var animationEvents = AnimationUtility.GetAnimationEvents(clip);
            var events = new List<object>();
            foreach (var evt in animationEvents)
            {
                events.Add(new
                {
                    time = evt.time,
                    functionName = evt.functionName,
                    intParameter = evt.intParameter,
                    floatParameter = evt.floatParameter,
                    stringParameter = evt.stringParameter
                });
            }

            // Get curve bindings
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            var curves = new List<object>();
            foreach (var binding in curveBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                curves.Add(new
                {
                    path = binding.path,
                    propertyName = binding.propertyName,
                    type = binding.type.Name,
                    keyCount = curve != null ? curve.length : 0
                });
            }

            // Also get object reference curve bindings
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objectBindings)
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                curves.Add(new
                {
                    path = binding.path,
                    propertyName = binding.propertyName,
                    type = binding.type.Name,
                    keyCount = keyframes != null ? keyframes.Length : 0,
                    isObjectReference = true
                });
            }

            return new McpToolResult
            {
                content = new List<McpContent>
                {
                    McpContent.Json(new
                    {
                        name = clip.name,
                        path = clipPath,
                        length = clip.length,
                        frameRate = clip.frameRate,
                        wrapMode = clip.wrapMode.ToString(),
                        isLooping = clip.isLooping,
                        isHumanMotion = clip.isHumanMotion,
                        hasRootMotion = clip.hasMotionCurves,
                        isLegacy = clip.legacy,
                        localBounds = new
                        {
                            center = new { x = clip.localBounds.center.x, y = clip.localBounds.center.y, z = clip.localBounds.center.z },
                            size = new { x = clip.localBounds.size.x, y = clip.localBounds.size.y, z = clip.localBounds.size.z }
                        },
                        events = events,
                        eventCount = events.Count,
                        curves = curves,
                        curveCount = curves.Count
                    })
                },
                isError = false
            };
        }

        #endregion
    }
}
