using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    public class DialogueDatabase
    {
        private readonly Dictionary<string, EventGroup> _map =
            new Dictionary<string, EventGroup>(StringComparer.Ordinal);

        public int Count => _map.Count;

        public void Load(TextAsset asset)
        {
            _map.Clear();

            if (asset == null)
            {
                Debug.LogError("[Dialogue] 没有指定 Events 文件");
                return;
            }

            EventDatabase db = null;
            try
            {
                db = JsonUtility.FromJson<EventDatabase>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[Dialogue] JSON 解析失败：" + e.Message);
                return;
            }

            if (db == null || db.Events == null)
            {
                Debug.LogError("[Dialogue] JSON 缺少 Events 数组");
                return;
            }

            foreach (EventGroup g in db.Events)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.EventName)) continue;
                if (_map.ContainsKey(g.EventName))
                    Debug.LogWarning("[Dialogue] EventName 重复：" + g.EventName);
                _map[g.EventName] = g;
            }

            Debug.Log("[Dialogue] 已加载 " + _map.Count + " 个事件组");
        }

        public bool TryGet(string eventName, out EventGroup group)
        {
            return _map.TryGetValue(eventName, out group);
        }
    }
}