using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string firstLevelSceneName = "Level1";

    private void Start()
    {
        Time.timeScale = 1f;
    }

    public void StartGame()
    {
        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("SceneTransitionManager not found in scene.");
            return;
        }

        SceneTransitionManager.Instance.LoadScene(firstLevelSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
