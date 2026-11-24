using DialogueSystem;
using UnityEngine;

public class TrapVisibility : MonoBehaviour
{
    [SerializeField] private EventVariableHolder variables;
    [SerializeField] private string visibleKey = "Visible";

    [Tooltip("电锯的模型子物体；留空则关闭本物体下所有 Renderer")]
    [SerializeField] private GameObject visualRoot;

    [Tooltip("隐藏时一起关闭的碰撞体（含伤害触发器）")]
    [SerializeField] private Collider[] hazardColliders;

    private bool _applied;
    private bool _last;

    private void Update()
    {
        if (variables == null) return;
        if (!variables.TryGetBool(visibleKey, out bool visible)) return;
        if (_applied && visible == _last) return;

        _applied = true;
        _last = visible;

        if (visualRoot != null)
        {
            visualRoot.SetActive(visible);
        }
        else
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visible;
        }

        if (hazardColliders != null)
        {
            for (int i = 0; i < hazardColliders.Length; i++)
            {
                if (hazardColliders[i] != null)
                    hazardColliders[i].enabled = visible;
            }
        }
    }
}