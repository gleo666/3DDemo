using UnityEngine;

namespace DialogueSystem
{
    public class InteractionDetector : MonoBehaviour
    {
        [Header("瞄准")]
        [SerializeField] private Camera    aimCamera;
        [SerializeField] private float     maxDistance     = 3f;
        [SerializeField] private float     maxFacingAngle  = 90f;
        [SerializeField] private LayerMask hitMask          = ~0;

        public Interactable Current { get; private set; }

        public void Refresh()
        {
            Current = null;

            DialogueManager mgr = DialogueManager.Instance;
            if (mgr != null && mgr.IsTalking) return;

            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, hitMask,
                    QueryTriggerInteraction.Ignore))
                return;

            // 距离用“玩家 → 命中点”，不从相机算
            if ((hit.point - transform.position).sqrMagnitude > maxDistance * maxDistance)
                return;

            Interactable target = hit.collider.GetComponentInParent<Interactable>();
            if (target == null || !target.CanInteract) return;

            // 角色必须面向目标（背对不触发）
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            if (Vector3.Angle(transform.forward, toTarget) > maxFacingAngle) return;

            Current = target;
        }
    }
}