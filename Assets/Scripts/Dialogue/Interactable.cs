using System;
using UnityEngine;

namespace DialogueSystem
{
    [DisallowMultipleComponent]
    public class Interactable : MonoBehaviour
    {
        [SerializeField] private string eventName;
        [SerializeField] private bool   canInteract = true;

        public string EventName  => eventName;
        public bool   CanInteract => canInteract && isActiveAndEnabled;

        public event Action Finished;

        internal void NotifyFinished() => Finished?.Invoke();
    }
}