using UnityEngine;
using UnityEngine.SceneManagement;

public class TrapHazard : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float restartDelay = 0.2f;

    private bool _triggered;

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (!IsPlayer(other)) return;

        _triggered = true;

        if (restartDelay > 0f)
            Invoke(nameof(Restart), restartDelay);
        else
            Restart();
    }

    private bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.CompareTag(playerTag)) return true;
        return other.transform.root.CompareTag(playerTag);
    }

    private void Restart()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (scene.buildIndex < 0)
        {
            Debug.LogError("[Trap] 场景没有加入 Build Settings，无法重载：" + scene.name);
            return;
        }

        SceneManager.LoadScene(scene.buildIndex);
    }
}