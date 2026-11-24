using DialogueSystem;
using UnityEngine;

public class TrapPathMover : MonoBehaviour
{
    [Header("开关")]
    [SerializeField] private EventVariableHolder variables;
    [SerializeField] private string movingKey = "IsMoving";

    [Header("路径")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float speed = 3.5f;
    [SerializeField] private float arriveDistance = 0.1f;
    [SerializeField] private bool faceMoveDirection = true;
    [SerializeField] private bool keepY = true;

    private int _index;
    private float _fixedY;

    private void Awake()
    {
        _fixedY = transform.position.y;
    }

    private void Update()
    {
        if (variables == null || waypoints == null || waypoints.Length == 0) return;
        if (!variables.TryGetBool(movingKey, out bool moving) || !moving) return;
        if (_index >= waypoints.Length) return;

        Transform wp = waypoints[_index];
        if (wp == null)
        {
            _index++;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 target = wp.position;

        if (keepY)
        {
            pos.y = _fixedY;
            target.y = _fixedY;
        }

        Vector3 to = target - pos;
        float dist = to.magnitude;

        if (dist <= arriveDistance)
        {
            _index++;
            return;
        }

        Vector3 dir = to / dist;
        float step = Mathf.Min(speed * Time.deltaTime, dist);

        Vector3 next = pos + dir * step;
        if (keepY) next.y = _fixedY;
        transform.position = next;

        if (faceMoveDirection)
        {
            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * Time.deltaTime);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (waypoints == null) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;

            Gizmos.DrawWireSphere(waypoints[i].position, 0.2f);
            if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
        }
    }
}