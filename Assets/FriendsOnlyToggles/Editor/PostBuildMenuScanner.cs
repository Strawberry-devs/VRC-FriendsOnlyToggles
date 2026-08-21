using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Strawberry.FriendsOnlyToggles.Editor
{
    internal static class PostBuildMenuScanner
    {
        private static GameObject scanTarget;
        private static List<FriendsOnlyToggles.ToggleRule> scanResult;

        internal static List<FriendsOnlyToggles.ToggleRule> Scan(GameObject avatar)
        {
            var clone = Object.Instantiate(avatar);
            clone.name = avatar.name + " (Friends-Only scan)";
            scanTarget = clone;
            scanResult = null;

            try
            {
                if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(clone))
                    throw new System.InvalidOperationException("The VRChat avatar preprocessors rejected the temporary scan build. Check the Console for the originating error.");

                if (scanResult == null)
                    throw new System.InvalidOperationException("The Friends-Only post-build callback did not run.");

                return scanResult;
            }
            finally
            {
                scanTarget = null;
                if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        internal static bool TryCapture(GameObject avatar)
        {
            if (avatar != scanTarget) return false;
            scanResult = Collect(avatar);
            return true;
        }

        private static List<FriendsOnlyToggles.ToggleRule> Collect(GameObject avatar)
        {
            var result = new List<FriendsOnlyToggles.ToggleRule>();
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return result;

            var parameterTypes = new Dictionary<string, VRCExpressionParameters.ValueType>();
            var parameterDefaults = new Dictionary<string, float>();
            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters != null && expressionParameters.parameters != null)
            {
                foreach (var parameter in expressionParameters.parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.name)) continue;
                    parameterTypes[parameter.name] = parameter.valueType;
                    parameterDefaults[parameter.name] = parameter.defaultValue;
                }
            }

            var menuParameters = new HashSet<string>();
            ScanMenu(descriptor.expressionsMenu, string.Empty, new HashSet<int>(), result, menuParameters,
                parameterTypes, parameterDefaults);

            if (expressionParameters != null && expressionParameters.parameters != null)
            {
                foreach (var parameter in expressionParameters.parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) || menuParameters.Contains(parameter.name)) continue;
                    var supported = parameter.valueType == VRCExpressionParameters.ValueType.Bool ||
                                    parameter.valueType == VRCExpressionParameters.ValueType.Int ||
                                    parameter.valueType == VRCExpressionParameters.ValueType.Float;
                    result.Add(MakeRule("[Parameter] " + parameter.name, parameter.name,
                        parameter.valueType.ToString(), 1f, parameter.defaultValue,
                        parameter.valueType == VRCExpressionParameters.ValueType.Float, supported));
                }
            }

            return result.OrderBy(r => r.menuPath, System.StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ScanMenu(VRCExpressionsMenu menu, string prefix, HashSet<int> stack,
            List<FriendsOnlyToggles.ToggleRule> result, HashSet<string> menuParameters,
            Dictionary<string, VRCExpressionParameters.ValueType> parameterTypes,
            Dictionary<string, float> parameterDefaults)
        {
            if (menu == null || !stack.Add(menu.GetInstanceID())) return;
            try
            {
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    var path = string.IsNullOrEmpty(prefix) ? control.name : prefix + "/" + control.name;
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        ScanMenu(control.subMenu, path, stack, result, menuParameters, parameterTypes,
                            parameterDefaults);
                        continue;
                    }

                    if (control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                    {
                        AddPuppetParameter(control, 0, path, "Radial", result, menuParameters,
                            parameterTypes, parameterDefaults);
                        continue;
                    }

                    if (control.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet)
                    {
                        AddPuppetParameter(control, 0, path + " [Horizontal]", "Two Axis", result,
                            menuParameters, parameterTypes, parameterDefaults);
                        AddPuppetParameter(control, 1, path + " [Vertical]", "Two Axis", result,
                            menuParameters, parameterTypes, parameterDefaults);
                        continue;
                    }

                    if (control.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet)
                    {
                        var directions = new[] { "Up", "Right", "Down", "Left" };
                        for (var i = 0; i < directions.Length; i++)
                            AddPuppetParameter(control, i, path + " [" + directions[i] + "]", "Four Axis",
                                result, menuParameters, parameterTypes, parameterDefaults);
                        continue;
                    }

                    var parameter = control.parameter == null ? null : control.parameter.name;
                    if (string.IsNullOrWhiteSpace(parameter)) continue;
                    menuParameters.Add(parameter);
                    var supported = control.type == VRCExpressionsMenu.Control.ControlType.Toggle ||
                                    control.type == VRCExpressionsMenu.Control.ControlType.Button;
                    result.Add(MakeRule(path, parameter, control.type.ToString(), control.value, supported));
                }
            }
            finally
            {
                stack.Remove(menu.GetInstanceID());
            }
        }

        private static void AddPuppetParameter(VRCExpressionsMenu.Control control, int index, string path,
            string type, List<FriendsOnlyToggles.ToggleRule> result, HashSet<string> menuParameters,
            Dictionary<string, VRCExpressionParameters.ValueType> parameterTypes,
            Dictionary<string, float> parameterDefaults)
        {
            if (control.subParameters == null || index >= control.subParameters.Length ||
                control.subParameters[index] == null) return;
            var parameter = control.subParameters[index].name;
            if (string.IsNullOrWhiteSpace(parameter)) return;

            menuParameters.Add(parameter);
            var isFloat = parameterTypes.TryGetValue(parameter, out var parameterType) &&
                          parameterType == VRCExpressionParameters.ValueType.Float;
            var defaultValue = parameterDefaults.TryGetValue(parameter, out var value) ? value : 0f;
            result.Add(MakeRule(path, parameter, type + " Puppet", 0f, defaultValue, true, isFloat));
        }

        private static FriendsOnlyToggles.ToggleRule MakeRule(string path, string parameter, string type,
            float activeValue, bool supported)
        {
            return MakeRule(path, parameter, type, activeValue, 0f, false, supported);
        }

        private static FriendsOnlyToggles.ToggleRule MakeRule(string path, string parameter, string type,
            float activeValue, float defaultValue, bool continuous, bool supported)
        {
            return new FriendsOnlyToggles.ToggleRule
            {
                key = path + "\u001f" + parameter + "\u001f" + activeValue.ToString("R", CultureInfo.InvariantCulture),
                menuPath = path,
                parameter = parameter,
                controlType = type,
                activeValue = activeValue,
                defaultValue = defaultValue,
                continuous = continuous,
                supported = supported
            };
        }
    }
}
