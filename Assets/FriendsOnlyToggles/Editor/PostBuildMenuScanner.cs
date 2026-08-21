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

            var menuParameters = new HashSet<string>();
            ScanMenu(descriptor.expressionsMenu, string.Empty, new HashSet<int>(), result, menuParameters);

            var expressionParameters = descriptor.expressionParameters;
            if (expressionParameters != null && expressionParameters.parameters != null)
            {
                foreach (var parameter in expressionParameters.parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) || menuParameters.Contains(parameter.name)) continue;
                    var supported = parameter.valueType == VRCExpressionParameters.ValueType.Bool ||
                                    parameter.valueType == VRCExpressionParameters.ValueType.Int;
                    result.Add(MakeRule("[Parameter] " + parameter.name, parameter.name,
                        parameter.valueType.ToString(), 1f, supported));
                }
            }

            return result.OrderBy(r => r.menuPath, System.StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ScanMenu(VRCExpressionsMenu menu, string prefix, HashSet<int> stack,
            List<FriendsOnlyToggles.ToggleRule> result, HashSet<string> menuParameters)
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
                        ScanMenu(control.subMenu, path, stack, result, menuParameters);
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

        private static FriendsOnlyToggles.ToggleRule MakeRule(string path, string parameter, string type,
            float activeValue, bool supported)
        {
            return new FriendsOnlyToggles.ToggleRule
            {
                key = path + "\u001f" + parameter + "\u001f" + activeValue.ToString("R", CultureInfo.InvariantCulture),
                menuPath = path,
                parameter = parameter,
                controlType = type,
                activeValue = activeValue,
                supported = supported
            };
        }
    }
}

