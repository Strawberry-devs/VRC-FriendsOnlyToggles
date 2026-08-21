using UnityEditor;
using UnityEngine;

namespace Strawberry.FriendsOnlyToggles.Editor
{
    internal static class FriendsOnlyGameObjectMenu
    {
        [MenuItem("GameObject/Noir/Friends-Only Toggles", false, 10)]
        private static void Create(MenuCommand command)
        {
            var gameObject = new GameObject("Friends-Only Toggles");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Friends-Only Toggles");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            Undo.AddComponent<FriendsOnlyToggles>(gameObject);
            Selection.activeGameObject = gameObject;
        }
    }
}

