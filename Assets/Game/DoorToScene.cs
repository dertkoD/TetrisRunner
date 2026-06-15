using UnityEngine;

public class DoorToScene : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private string targetSceneName = "Level2";

    [Header("Player")]
    [SerializeField] private string playerTag = "Player";

    private bool wasTriggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (wasTriggered)
            return;

        if (!other.CompareTag(playerTag))
            return;

        wasTriggered = true;

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("SceneTransitionManager not found in scene.");
            return;
        }

        SceneTransitionManager.Instance.LoadScene(targetSceneName);
    }
}
