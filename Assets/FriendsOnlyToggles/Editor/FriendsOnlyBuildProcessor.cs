using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Strawberry.FriendsOnlyToggles.Editor
{
    internal sealed class FriendsOnlyBuildProcessor : IVRCSDKPreprocessAvatarCallback
    {
        private const string FriendsParameter = "IsOnFriendsList";
        private const string LocalParameter = "IsLocal";

        // NDMF runs at -11000 and VRCFury at -10000. This sees their final menu/controller output.
        public int callbackOrder => -9000;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var components = avatar.GetComponentsInChildren<FriendsOnlyToggles>(true);
            if (components.Length == 0) return true;

            try
            {
                if (PostBuildMenuScanner.TryCapture(avatar))
                {
                    foreach (var component in components) UnityEngine.Object.DestroyImmediate(component);
                    return true;
                }

                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null) throw new InvalidOperationException("Friends Only Toggles must be on a VRChat avatar.");

                var controller = FindFxController(descriptor);
                if (controller == null) throw new InvalidOperationException("The post-build avatar does not have an editable FX Animator Controller.");

                var friendsParameterType = EnsureViewerParameter(controller, FriendsParameter);
                var localParameterType = EnsureViewerParameter(controller, LocalParameter);

                var protectedCount = 0;
                var generatedParameterIndex = 0;
                foreach (var component in components)
                {
                    foreach (var rule in component.rules.Where(r => r.supported && r.friendsOnly))
                    {
                        var changes = ProtectRule(controller, rule, friendsParameterType, localParameterType,
                            generatedParameterIndex++);
                        if (changes == 0)
                        {
                            Debug.LogWarning("Friends-Only Toggles: '" + rule.menuPath + "' uses parameter '" +
                                             rule.parameter + "', but no compatible binary transitions were found. It was not modified.", component);
                        }
                        else
                        {
                            protectedCount++;
                        }
                    }
                }

                EditorUtility.SetDirty(controller);
                foreach (var component in components) UnityEngine.Object.DestroyImmediate(component);
                Debug.Log("Friends-Only Toggles: protected " + protectedCount + " toggle(s) in the final FX controller.", avatar);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, avatar);
                return false;
            }
        }

        private static AnimatorController FindFxController(VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers == null) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                return layer.animatorController as AnimatorController;
            }
            return null;
        }

        private static AnimatorControllerParameterType EnsureViewerParameter(AnimatorController controller, string name)
        {
            var existing = controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing == null)
            {
                controller.AddParameter(name, AnimatorControllerParameterType.Bool);
                return AnimatorControllerParameterType.Bool;
            }

            if (existing.type != AnimatorControllerParameterType.Bool &&
                existing.type != AnimatorControllerParameterType.Float &&
                existing.type != AnimatorControllerParameterType.Int)
                throw new InvalidOperationException("FX parameter '" + name + "' has unsupported type " + existing.type + ".");
            return existing.type;
        }

        private static int ProtectRule(AnimatorController controller, FriendsOnlyToggles.ToggleRule rule,
            AnimatorControllerParameterType friendsParameterType, AnimatorControllerParameterType localParameterType,
            int generatedParameterIndex)
        {
            var parameter = controller.parameters.FirstOrDefault(p => p.name == rule.parameter);
            if (parameter == null) return 0;

            if (rule.continuous)
            {
                if (parameter.type != AnimatorControllerParameterType.Float)
                {
                    Debug.LogWarning("Friends-Only Toggles: continuous control '" + rule.menuPath +
                                     "' does not use a Float animator parameter and was skipped.");
                    return 0;
                }
                return ProtectContinuousRule(controller, rule, friendsParameterType, localParameterType,
                    generatedParameterIndex);
            }

            var inactiveValue = Mathf.Approximately(rule.activeValue, 0f) ? 1f : 0f;
            var changes = 0;
            foreach (var layer in controller.layers)
                changes += ProcessStateMachine(layer.stateMachine, rule.parameter, rule.activeValue, inactiveValue,
                    friendsParameterType, localParameterType);

            var effectiveParameter = "__FOT/" + generatedParameterIndex + "/" + rule.parameter;
            var directBlendChanges = RewriteDirectBlendParameters(controller, rule.parameter, effectiveParameter);
            if (directBlendChanges > 0)
            {
                controller.AddParameter(effectiveParameter, AnimatorControllerParameterType.Float);
                AddDirectBlendGateLayer(controller, rule, parameter.type, effectiveParameter, inactiveValue,
                    friendsParameterType, localParameterType);
                changes += directBlendChanges;
            }
            return changes;
        }

        private static int ProtectContinuousRule(AnimatorController controller,
            FriendsOnlyToggles.ToggleRule rule, AnimatorControllerParameterType friendsParameterType,
            AnimatorControllerParameterType localParameterType, int generatedParameterIndex)
        {
            var defaultParameter = "__FOT/default/" + generatedParameterIndex + "/" + rule.parameter;
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = defaultParameter,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = rule.defaultValue
            });

            var changes = 0;
            foreach (var layer in controller.layers)
            {
                changes += GateContinuousMotions(controller, layer.stateMachine, rule.parameter,
                    defaultParameter, generatedParameterIndex);
                changes += ProcessContinuousStateMachine(layer.stateMachine, rule.parameter, rule.defaultValue,
                    friendsParameterType, localParameterType);
            }

            return changes;
        }

        private static int GateContinuousMotions(AnimatorController controller, AnimatorStateMachine machine,
            string sourceParameter, string defaultParameter, int generatedParameterIndex)
        {
            var changes = 0;
            foreach (var childState in machine.states)
            {
                var state = childState.state;
                if (!MotionUsesParameter(state.motion, sourceParameter, new HashSet<int>())) continue;
                var defaultMotion = CloneWithParameter(controller, state.motion, sourceParameter,
                    defaultParameter, generatedParameterIndex);
                state.motion = CreateViewerGate(controller, state.motion, defaultMotion,
                    generatedParameterIndex, state.name);
                EditorUtility.SetDirty(state);
                changes++;
            }

            foreach (var childMachine in machine.stateMachines)
                changes += GateContinuousMotions(controller, childMachine.stateMachine, sourceParameter,
                    defaultParameter, generatedParameterIndex);
            return changes;
        }

        private static bool MotionUsesParameter(Motion motion, string parameter, HashSet<int> visited)
        {
            var tree = motion as BlendTree;
            if (tree == null || !visited.Add(tree.GetInstanceID())) return false;
            if (tree.blendParameter == parameter || tree.blendParameterY == parameter) return true;

            foreach (var child in tree.children)
            {
                if (child.directBlendParameter == parameter ||
                    MotionUsesParameter(child.motion, parameter, visited)) return true;
            }
            return false;
        }

        private static Motion CloneWithParameter(AnimatorController controller, Motion motion, string source,
            string replacement, int generatedParameterIndex)
        {
            var tree = motion as BlendTree;
            if (tree == null || !MotionUsesParameter(tree, source, new HashSet<int>())) return motion;

            var clone = UnityEngine.Object.Instantiate(tree);
            clone.name = "FOT Default " + generatedParameterIndex + " - " + tree.name;
            clone.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(clone, controller);
            if (clone.blendParameter == source) clone.blendParameter = replacement;
            if (clone.blendParameterY == source) clone.blendParameterY = replacement;

            var children = clone.children;
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child.directBlendParameter == source) child.directBlendParameter = replacement;
                child.motion = CloneWithParameter(controller, child.motion, source, replacement,
                    generatedParameterIndex);
                children[i] = child;
            }
            clone.children = children;
            EditorUtility.SetDirty(clone);
            return clone;
        }

        private static Motion CreateViewerGate(AnimatorController controller, Motion liveMotion,
            Motion defaultMotion, int generatedParameterIndex, string stateName)
        {
            var friendGate = CreateBlendTree(controller,
                "FOT Friend Gate " + generatedParameterIndex + " - " + stateName, FriendsParameter);
            friendGate.AddChild(defaultMotion, 0f);
            friendGate.AddChild(liveMotion, 1f);

            var localGate = CreateBlendTree(controller,
                "FOT Local Gate " + generatedParameterIndex + " - " + stateName, LocalParameter);
            localGate.AddChild(friendGate, 0f);
            localGate.AddChild(liveMotion, 1f);
            return localGate;
        }

        private static BlendTree CreateBlendTree(AnimatorController controller, string name, string parameter)
        {
            var tree = new BlendTree
            {
                name = name,
                hideFlags = HideFlags.HideInHierarchy,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static int ProcessContinuousStateMachine(AnimatorStateMachine machine, string parameter,
            float defaultValue, AnimatorControllerParameterType friendsParameterType,
            AnimatorControllerParameterType localParameterType)
        {
            var changes = 0;
            foreach (var childState in machine.states)
            {
                var state = childState.state;
                foreach (var transition in state.transitions.ToArray())
                    changes += ProcessContinuousTransition(transition, parameter, defaultValue,
                        () => CloneStateTransition(state, transition), friendsParameterType, localParameterType);
            }

            foreach (var transition in machine.anyStateTransitions.ToArray())
                changes += ProcessContinuousTransition(transition, parameter, defaultValue,
                    () => CloneAnyStateTransition(machine, transition), friendsParameterType, localParameterType);

            foreach (var childMachine in machine.stateMachines)
                changes += ProcessContinuousStateMachine(childMachine.stateMachine, parameter, defaultValue,
                    friendsParameterType, localParameterType);
            return changes;
        }

        private static int ProcessContinuousTransition(AnimatorStateTransition transition, string parameter,
            float defaultValue, Func<AnimatorStateTransition> cloneFactory,
            AnimatorControllerParameterType friendsParameterType, AnimatorControllerParameterType localParameterType)
        {
            if (transition.conditions.Any(c => c.parameter == FriendsParameter || c.parameter == LocalParameter))
                return 0;

            var relevant = transition.conditions.Where(c => c.parameter == parameter).ToArray();
            if (relevant.Length == 0) return 0;

            var localCopy = cloneFactory();
            localCopy.conditions = Append(localCopy.conditions,
                ViewerCondition(LocalParameter, localParameterType, true));
            transition.conditions = Append(transition.conditions,
                ViewerCondition(FriendsParameter, friendsParameterType, true));

            if (relevant.All(c => Evaluate(c, defaultValue)))
            {
                var strangerCopy = cloneFactory();
                var conditions = strangerCopy.conditions.Where(c => c.parameter != parameter &&
                    c.parameter != FriendsParameter && c.parameter != LocalParameter).ToArray();
                conditions = Append(conditions,
                    ViewerCondition(FriendsParameter, friendsParameterType, false));
                strangerCopy.conditions = Append(conditions,
                    ViewerCondition(LocalParameter, localParameterType, false));
                strangerCopy.hasExitTime = false;
                strangerCopy.duration = 0f;
                strangerCopy.offset = 0f;
            }

            return 1;
        }

        private static int RewriteDirectBlendParameters(AnimatorController controller, string source,
            string replacement)
        {
            var changes = 0;
            var visited = new HashSet<int>();
            foreach (var layer in controller.layers)
                changes += RewriteStateMachineMotions(layer.stateMachine, source, replacement, visited);
            return changes;
        }

        private static int RewriteStateMachineMotions(AnimatorStateMachine machine, string source,
            string replacement, HashSet<int> visited)
        {
            var changes = 0;
            foreach (var childState in machine.states)
                changes += RewriteMotion(childState.state.motion, source, replacement, visited);
            foreach (var childMachine in machine.stateMachines)
                changes += RewriteStateMachineMotions(childMachine.stateMachine, source, replacement, visited);
            return changes;
        }

        private static int RewriteMotion(Motion motion, string source, string replacement, HashSet<int> visited)
        {
            var tree = motion as BlendTree;
            if (tree == null || !visited.Add(tree.GetInstanceID())) return 0;

            var changes = 0;
            var children = tree.children;
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child.directBlendParameter == source)
                {
                    child.directBlendParameter = replacement;
                    children[i] = child;
                    changes++;
                }
                changes += RewriteMotion(child.motion, source, replacement, visited);
            }
            if (changes > 0)
            {
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
            return changes;
        }

        private static void AddDirectBlendGateLayer(AnimatorController controller,
            FriendsOnlyToggles.ToggleRule rule, AnimatorControllerParameterType sourceType, string effectiveParameter,
            float inactiveValue, AnimatorControllerParameterType friendsParameterType,
            AnimatorControllerParameterType localParameterType)
        {
            controller.AddLayer("Friends-Only: " + rule.menuPath);
            var layer = controller.layers[controller.layers.Length - 1];
            layer.defaultWeight = 1f;
            var machine = layer.stateMachine;

            var blocked = machine.AddState("Stranger (inactive)");
            var allowedInactive = machine.AddState("Allowed (inactive)");
            var allowedActive = machine.AddState("Allowed (active)");
            machine.defaultState = blocked;
            AddSetDriver(blocked, effectiveParameter, inactiveValue);
            AddSetDriver(allowedInactive, effectiveParameter, inactiveValue);
            AddSetDriver(allowedActive, effectiveParameter, rule.activeValue);

            AddAnyTransition(machine, blocked,
                ViewerCondition(FriendsParameter, friendsParameterType, false),
                ViewerCondition(LocalParameter, localParameterType, false));

            AddAllowedTransitions(machine, allowedInactive,
                ValueCondition(rule.parameter, sourceType, inactiveValue), friendsParameterType, localParameterType);
            AddAllowedTransitions(machine, allowedActive,
                ValueCondition(rule.parameter, sourceType, rule.activeValue), friendsParameterType, localParameterType);
        }

        private static void AddAllowedTransitions(AnimatorStateMachine machine, AnimatorState destination,
            AnimatorCondition valueCondition, AnimatorControllerParameterType friendsParameterType,
            AnimatorControllerParameterType localParameterType)
        {
            AddAnyTransition(machine, destination, valueCondition,
                ViewerCondition(FriendsParameter, friendsParameterType, true));
            AddAnyTransition(machine, destination, valueCondition,
                ViewerCondition(LocalParameter, localParameterType, true));
        }

        private static void AddAnyTransition(AnimatorStateMachine machine, AnimatorState destination,
            params AnimatorCondition[] conditions)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.conditions = conditions;
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
        }

        private static void AddSetDriver(AnimatorState state, string parameter, float value)
        {
            var driver = state.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver>();
            driver.localOnly = false;
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                name = parameter,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value
            });
        }

        private static AnimatorCondition ValueCondition(string parameter, AnimatorControllerParameterType type,
            float value)
        {
            if (type == AnimatorControllerParameterType.Bool)
                return new AnimatorCondition
                {
                    parameter = parameter,
                    mode = Mathf.Approximately(value, 0f) ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If
                };
            if (type == AnimatorControllerParameterType.Int)
                return new AnimatorCondition
                {
                    parameter = parameter,
                    mode = AnimatorConditionMode.Equals,
                    threshold = value
                };
            return new AnimatorCondition
            {
                parameter = parameter,
                mode = Mathf.Approximately(value, 0f) ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater,
                threshold = 0.5f
            };
        }

        private static int ProcessStateMachine(AnimatorStateMachine machine, string parameter,
            float activeValue, float inactiveValue, AnimatorControllerParameterType friendsParameterType,
            AnimatorControllerParameterType localParameterType)
        {
            var changes = 0;

            foreach (var childState in machine.states)
            {
                var state = childState.state;
                foreach (var transition in state.transitions.ToArray())
                {
                    changes += ProcessTransition(transition, parameter, activeValue, inactiveValue,
                        () => CloneStateTransition(state, transition), friendsParameterType, localParameterType);
                }
            }

            foreach (var transition in machine.anyStateTransitions.ToArray())
            {
                changes += ProcessTransition(transition, parameter, activeValue, inactiveValue,
                    () => CloneAnyStateTransition(machine, transition), friendsParameterType, localParameterType);
            }

            foreach (var childMachine in machine.stateMachines)
                changes += ProcessStateMachine(childMachine.stateMachine, parameter, activeValue, inactiveValue,
                    friendsParameterType, localParameterType);

            return changes;
        }

        private static int ProcessTransition(AnimatorStateTransition transition, string parameter,
            float activeValue, float inactiveValue, Func<AnimatorStateTransition> cloneFactory,
            AnimatorControllerParameterType friendsParameterType, AnimatorControllerParameterType localParameterType)
        {
            if (transition.conditions.Any(c => c.parameter == FriendsParameter || c.parameter == LocalParameter)) return 0;

            var relevant = transition.conditions.Where(c => c.parameter == parameter).ToArray();
            if (relevant.Length == 0) return 0;

            var activeMatches = relevant.All(c => Evaluate(c, activeValue));
            var inactiveMatches = relevant.All(c => Evaluate(c, inactiveValue));

            if (activeMatches && !inactiveMatches)
            {
                // Unity transition conditions are AND-only, so duplicate the transition to express friend OR local.
                var localCopy = cloneFactory();
                localCopy.conditions = Append(localCopy.conditions, ViewerCondition(LocalParameter, localParameterType, true));
                transition.conditions = Append(transition.conditions, ViewerCondition(FriendsParameter, friendsParameterType, true));
                return 1;
            }

            if (!activeMatches && inactiveMatches)
            {
                // Preserve the ordinary off route, plus an immediate route to the same inactive state for strangers.
                var strangerCopy = cloneFactory();
                strangerCopy.conditions = new[]
                {
                    ViewerCondition(FriendsParameter, friendsParameterType, false),
                    ViewerCondition(LocalParameter, localParameterType, false)
                };
                strangerCopy.hasExitTime = false;
                strangerCopy.duration = 0f;
                strangerCopy.offset = 0f;
                return 1;
            }

            return 0;
        }

        private static AnimatorStateTransition CloneStateTransition(AnimatorState state,
            AnimatorStateTransition source)
        {
            AnimatorStateTransition copy;
            if (source.isExit) copy = state.AddExitTransition();
            else if (source.destinationState != null) copy = state.AddTransition(source.destinationState);
            else copy = state.AddTransition(source.destinationStateMachine);
            EditorUtility.CopySerialized(source, copy);
            return copy;
        }

        private static AnimatorStateTransition CloneAnyStateTransition(AnimatorStateMachine machine,
            AnimatorStateTransition source)
        {
            AnimatorStateTransition copy;
            if (source.destinationState != null) copy = machine.AddAnyStateTransition(source.destinationState);
            else copy = machine.AddAnyStateTransition(source.destinationStateMachine);
            EditorUtility.CopySerialized(source, copy);
            return copy;
        }

        private static AnimatorCondition[] Append(AnimatorCondition[] source, AnimatorCondition condition)
        {
            var result = new AnimatorCondition[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = condition;
            return result;
        }

        private static AnimatorCondition ViewerCondition(string parameter, AnimatorControllerParameterType type,
            bool expected)
        {
            if (type == AnimatorControllerParameterType.Bool)
                return new AnimatorCondition
                {
                    parameter = parameter,
                    mode = expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    threshold = 0f
                };

            return new AnimatorCondition
            {
                parameter = parameter,
                mode = expected ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                threshold = 0.5f
            };
        }

        private static bool Evaluate(AnimatorCondition condition, float value)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return !Mathf.Approximately(value, 0f);
                case AnimatorConditionMode.IfNot: return Mathf.Approximately(value, 0f);
                case AnimatorConditionMode.Greater: return value > condition.threshold;
                case AnimatorConditionMode.Less: return value < condition.threshold;
                case AnimatorConditionMode.Equals: return Mathf.Approximately(value, condition.threshold);
                case AnimatorConditionMode.NotEqual: return !Mathf.Approximately(value, condition.threshold);
                default: return false;
            }
        }
    }
}
