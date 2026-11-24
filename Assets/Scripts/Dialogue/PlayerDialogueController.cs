using StarterAssets;
using UnityEngine;

namespace DialogueSystem
{
    [RequireComponent(typeof(InteractionDetector))]
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class PlayerDialogueController : MonoBehaviour
    {
        [SerializeField] private DialogueUI ui;

        private InteractionDetector _detector;
        private StarterAssetsInputs _input;

        private void Awake()
        {
            _detector = GetComponent<InteractionDetector>();
            _input    = GetComponent<StarterAssetsInputs>();

            if (ui == null)
            {
                Debug.LogError("[Dialogue] PlayerDialogueController 没有指定 DialogueUI");
                enabled = false;
                return;
            }

            ui.PromptClicked += HandleInteract;
        }

        private void OnDestroy()
        {
            if (ui != null) ui.PromptClicked -= HandleInteract;
        }

        private void Update()
        {
            DialogueManager mgr = DialogueManager.Instance;
            if (mgr == null) return;

            _detector.Refresh();

            if (!mgr.IsTalking)
            {
                Interactable target = _detector.Current;
                if (target != null) ui.ShowPrompt(mgr.GetPrompt(target));
                else                ui.HidePrompt();
            }

            if (!_input.interact) return;
            _input.interact = false;      // 消费掉，按住不连发
            HandleInteract();
        }

        private void HandleInteract()
        {
            DialogueManager mgr = DialogueManager.Instance;
            if (mgr == null) return;

            if (mgr.IsTalking)                 mgr.Advance();
            else if (_detector.Current != null) mgr.TryStart(_detector.Current);
        }
    }
}