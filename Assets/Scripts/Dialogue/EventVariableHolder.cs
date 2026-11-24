using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    public enum EventVariableType
    {
        Bool = 0,
        Number = 1,
    }

    [Serializable]
    public class EventVariable
    {
        public string Name;
        public EventVariableType Type;
        public bool  BoolValue;
        public float NumberValue;
    }

    [DisallowMultipleComponent]
    public class EventVariableHolder : MonoBehaviour
    {
        [SerializeField] private string ownerId;
        [SerializeField] private List<EventVariable> variables = new List<EventVariable>();

        public string OwnerId => string.IsNullOrEmpty(ownerId) ? gameObject.name : ownerId;

        private void OnEnable()
        {
            EventVariableRegistry.Register(this);
        }

        private void OnDisable()
        {
            EventVariableRegistry.Unregister(this);
        }

        public bool TryGetVariable(string name, out EventVariable variable)
        {
            if (variables != null)
            {
                for (int i = 0; i < variables.Count; i++)
                {
                    EventVariable v = variables[i];
                    if (v != null && string.Equals(v.Name, name, StringComparison.Ordinal))
                    {
                        variable = v;
                        return true;
                    }
                }
            }

            variable = null;
            return false;
        }

        public bool TryGetBool(string name, out bool value)
        {
            if (TryGetVariable(name, out EventVariable v) && v.Type == EventVariableType.Bool)
            {
                value = v.BoolValue;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(string name, out float value)
        {
            if (TryGetVariable(name, out EventVariable v) && v.Type == EventVariableType.Number)
            {
                value = v.NumberValue;
                return true;
            }

            value = 0f;
            return false;
        }

        public bool SetBool(string name, bool value)
        {
            if (!TryGetVariable(name, out EventVariable v) || v.Type != EventVariableType.Bool)
                return false;

            v.BoolValue = value;
            return true;
        }

        public bool SetNumber(string name, float value)
        {
            if (!TryGetVariable(name, out EventVariable v) || v.Type != EventVariableType.Number)
                return false;

            v.NumberValue = value;
            return true;
        }
    }
}
