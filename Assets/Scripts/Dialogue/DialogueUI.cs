using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DialogueSystem
{
    public class DialogueUI : MonoBehaviour
    {
        [Header("入口按钮（未进入对话时显示）")]
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Button     promptButton;
        [SerializeField] private TMP_Text   promptLabel;

        [Header("对话框")]
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private TMP_Text   bodyText;
        [SerializeField] private TMP_Text   hintText;

        [SerializeField] private string continueHint = "【按 E 继续】";
        [SerializeField] private string endHint      = "【按 E 结束】";

        public event Action PromptClicked;

        private string _lastPrompt;

        private void Awake()
        {
            if (promptButton != null)
                promptButton.onClick.AddListener(OnPromptButtonClicked);
        }

        private void OnPromptButtonClicked()
        {
            PromptClicked?.Invoke();
        }

        public void ShowPrompt(string text)
        {
            if (promptRoot.activeSelf && _lastPrompt == text) return;
            _lastPrompt = text;
            promptRoot.SetActive(true);
            if (promptLabel != null) promptLabel.text = text;
        }

        public void HidePrompt()
        {
            if (promptRoot != null && promptRoot.activeSelf) promptRoot.SetActive(false);
        }

        public void ShowLine(string text, bool isLast)
        {
            dialogueRoot.SetActive(true);
            bodyText.text = text;
            hintText.text = isLast ? endHint : continueHint;
        }

        public void HideDialogue()
        {
            if (dialogueRoot != null && dialogueRoot.activeSelf) dialogueRoot.SetActive(false);
        }
    }
}