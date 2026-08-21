using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strawberry.FriendsOnlyToggles
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Noir/Friends-Only Toggles")]
    public sealed class FriendsOnlyToggles : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [SerializeField, HideInInspector] public List<ToggleRule> rules = new List<ToggleRule>();

        [Serializable]
        public sealed class ToggleRule
        {
            public string key;
            public string menuPath;
            public string parameter;
            public string controlType;
            public float activeValue = 1f;
            public float defaultValue;
            public bool continuous;
            public bool supported = true;
            public bool friendsOnly;
        }
    }
}
