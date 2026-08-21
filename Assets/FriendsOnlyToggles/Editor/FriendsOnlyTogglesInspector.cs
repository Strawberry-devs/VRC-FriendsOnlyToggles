using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Strawberry.FriendsOnlyToggles.Editor
{
    [CustomEditor(typeof(FriendsOnlyToggles))]
    internal sealed class FriendsOnlyTogglesInspector : UnityEditor.Editor
    {
        private string search = string.Empty;

        public override void OnInspectorGUI()
        {
            var settings = (FriendsOnlyToggles)target;
            var descriptor = settings.GetComponentInParent<VRCAvatarDescriptor>();

            EditorGUILayout.HelpBox(
                "Checked toggles work normally for you and friends. Strangers are kept in each toggle's inactive state. The Expressions Menu itself is not changed.",
                MessageType.Info);

            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("Place this GameObject anywhere under a VRChat avatar.", MessageType.Error);
                return;
            }

            if (GUILayout.Button("Scan Post-Build Menu", GUILayout.Height(28))) Scan(settings);

            if (settings.rules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Run a scan to build the final menu through Modular Avatar and VRCFury, then choose toggles here.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.Space(4);
            search = EditorGUILayout.TextField("Filter", search);

            var changed = false;
            var shown = 0;
            foreach (var rule in settings.rules)
            {
                if (!string.IsNullOrWhiteSpace(search) &&
                    rule.menuPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    rule.parameter.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;

                using (new EditorGUI.DisabledScope(!rule.supported))
                {
                    var label = new GUIContent(rule.menuPath,
                        rule.parameter + " = " + rule.activeValue + " (" + rule.controlType + ")");
                    var value = EditorGUILayout.ToggleLeft(label, rule.friendsOnly);
                    if (value != rule.friendsOnly)
                    {
                        Undo.RecordObject(settings, "Change friends-only toggle");
                        rule.friendsOnly = value;
                        changed = true;
                    }
                }

                if (!rule.supported)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField(rule.controlType + " controls are listed for visibility but are not supported in 0.1.", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
            }

            if (shown == 0) EditorGUILayout.LabelField("No matching entries.", EditorStyles.centeredGreyMiniLabel);

            var enabledCount = settings.rules.Count(r => r.supported && r.friendsOnly);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(enabledCount + " toggle(s) protected", EditorStyles.boldLabel);
            if (changed) EditorUtility.SetDirty(settings);
        }

        private static void Scan(FriendsOnlyToggles settings)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Friends-Only Toggles", "Building a temporary post-build menu...", 0.5f);
                var oldValues = settings.rules.GroupBy(r => r.key).ToDictionary(g => g.Key, g => g.First().friendsOnly);
                var descriptor = settings.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor == null) throw new InvalidOperationException("Place this GameObject under a VRChat avatar first.");
                var scanned = PostBuildMenuScanner.Scan(descriptor.gameObject);
                foreach (var rule in scanned)
                {
                    if (oldValues.TryGetValue(rule.key, out var enabled)) rule.friendsOnly = enabled;
                }

                Undo.RecordObject(settings, "Scan post-build avatar menu");
                settings.rules = scanned;
                EditorUtility.SetDirty(settings);
                Debug.Log("Friends-Only Toggles: found " + scanned.Count + " final menu/parameter entries on " + settings.name + ".", settings);
            }
            catch (Exception e)
            {
                Debug.LogException(e, settings);
                EditorUtility.DisplayDialog("Friends-Only Toggles", e.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
