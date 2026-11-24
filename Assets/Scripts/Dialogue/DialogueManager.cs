using System;
using UnityEngine;

namespace DialogueSystem
{
    public class DialogueManager : MonoBehaviour
    {
        private const int MaxJumpCount = 32;

        public static DialogueManager Instance { get; private set; }

        [SerializeField] private DialogueUI ui;
        [SerializeField] private TextAsset  eventsFile;

        private readonly DialogueDatabase _db = new DialogueDatabase();

        private EventGroup   _current;
        private Interactable _speaker;
        private int          _index;
        private int          _jumpCount;

        public bool IsTalking => _current != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _db.Load(eventsFile);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public string GetPrompt(Interactable speaker)
        {
            if (speaker != null &&
                _db.TryGet(speaker.EventName, out EventGroup group) &&
                !string.IsNullOrEmpty(group.PromptText))
            {
                return group.PromptText;
            }

            return "对话";
        }

        public bool TryStart(Interactable speaker)
        {
            if (IsTalking || speaker == null || ui == null) return false;

            if (!_db.TryGet(speaker.EventName, out EventGroup group))
            {
                Debug.LogWarning("[Dialogue] 找不到事件组：" + speaker.EventName);
                return false;
            }

            if (group.EventText == null || group.EventText.Length == 0)
            {
                Debug.LogWarning("[Dialogue] 事件组没有文本：" + group.EventName);
                return false;
            }

            _current   = group;
            _speaker   = speaker;
            _index     = 0;
            _jumpCount = 0;

            ui.HidePrompt();
            ui.ShowLine(group.EventText[0], IsLastLine(group, 0));
            return true;
        }

        public void Advance()
        {
            if (!IsTalking) return;

            _index++;
            if (_index < _current.EventText.Length)
            {
                ui.ShowLine(_current.EventText[_index], IsLastLine(_current, _index));
            }
            else
            {
                FinishEvent();
            }
        }

        private void FinishEvent()
        {
            EventGroup group = _current;
            EventEnd end = group.End;

            // 没写 End：关闭对话框，对象保留
            if (end == null)
            {
                CloseSession();
                return;
            }

            // 先执行 Set，再判断 Type / Condition
            ApplySet(end.Set, group.EventName);

            string type = end.Type ?? string.Empty;

            if (string.Equals(type, "None", StringComparison.OrdinalIgnoreCase))
            {
                CloseSession();
                return;
            }

            if (string.Equals(type, "Destroy", StringComparison.OrdinalIgnoreCase))
            {
                DestroySpeaker();
                CloseSession();
                return;
            }

            if (string.Equals(type, "Jump", StringComparison.OrdinalIgnoreCase))
            {
                if (end.DestroySelf) DestroySpeaker();
                GoTo(end.NextEvent, group.EventName);
                return;
            }

            if (string.Equals(type, "Branch", StringComparison.OrdinalIgnoreCase))
            {
                bool result = EventExpression.EvaluateCondition(end.Condition, _speaker, group.EventName);

                if (end.DestroySelf) DestroySpeaker();

                GoTo(result ? end.TrueEvent : end.FalseEvent, group.EventName);
                return;
            }

            Debug.LogError("[Dialogue] 事件 '" + group.EventName + "' 的 End.Type 未知：" + end.Type);
            CloseSession();
        }

        private void ApplySet(string[] assignments, string context)
        {
            if (assignments == null) return;

            for (int i = 0; i < assignments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(assignments[i])) continue;
                EventExpression.ApplyAssignment(assignments[i], _speaker, context);
            }
        }

        private void GoTo(string eventName, string fromEvent)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogError("[Dialogue] 事件 '" + fromEvent + "' 的跳转目标为空");
                CloseSession();
                return;
            }

            _jumpCount++;
            if (_jumpCount > MaxJumpCount)
            {
                Debug.LogError("[Dialogue] 跳转次数超过 " + MaxJumpCount +
                               "，可能存在事件环：" + fromEvent + " -> " + eventName);
                CloseSession();
                return;
            }

            if (!_db.TryGet(eventName, out EventGroup next))
            {
                Debug.LogError("[Dialogue] 找不到跳转目标事件：" + eventName + "（来自 " + fromEvent + "）");
                CloseSession();
                return;
            }

            if (next.EventText == null || next.EventText.Length == 0)
            {
                Debug.LogError("[Dialogue] 跳转目标事件没有文本：" + eventName + "（来自 " + fromEvent + "）");
                CloseSession();
                return;
            }

            _current = next;
            _index   = 0;

            ui.ShowLine(next.EventText[0], IsLastLine(next, 0));
        }

        private static bool IsLastLine(EventGroup group, int index)
        {
            if (index != group.EventText.Length - 1) return false;
            return IsTerminal(group.End);
        }

        private static bool IsTerminal(EventEnd end)
        {
            if (end == null) return true;

            return !string.Equals(end.Type, "Jump", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(end.Type, "Branch", StringComparison.OrdinalIgnoreCase);
        }

        private void DestroySpeaker()
        {
            if (_speaker == null) return;

            GameObject go = _speaker.gameObject;
            _speaker.NotifyFinished();
            _speaker = null;
            Destroy(go);
        }

        private void CloseSession()
        {
            _current   = null;
            _speaker   = null;
            _index     = 0;
            _jumpCount = 0;

            ui.HideDialogue();
            ui.HidePrompt();
        }
    }
}
