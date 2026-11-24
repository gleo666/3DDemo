using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    public static class EventVariableRegistry
    {
        private static readonly Dictionary<string, EventVariableHolder> Map =
            new Dictionary<string, EventVariableHolder>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Map.Clear();
        }

        public static void Register(EventVariableHolder holder)
        {
            if (holder == null) return;

            string id = holder.OwnerId;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[Dialogue] EventVariableHolder 的 OwnerId 为空，已跳过注册：" + holder.name, holder);
                return;
            }

            if (Map.TryGetValue(id, out EventVariableHolder existing) && existing != null && existing != holder)
            {
                Debug.LogWarning("[Dialogue] OwnerId 重复：" + id + "，只有第一个生效（" + existing.name + "）", holder);
                return;
            }

            Map[id] = holder;
        }

        public static void Unregister(EventVariableHolder holder)
        {
            if (holder == null) return;

            string id = holder.OwnerId;
            if (string.IsNullOrEmpty(id)) return;

            if (Map.TryGetValue(id, out EventVariableHolder existing) && existing == holder)
                Map.Remove(id);
        }

        public static bool TryGet(string id, out EventVariableHolder holder)
        {
            if (string.IsNullOrEmpty(id))
            {
                holder = null;
                return false;
            }

            return Map.TryGetValue(id, out holder) && holder != null;
        }
    }
}
