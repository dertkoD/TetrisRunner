using UnityEngine;

public class WinMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject winPanel;

    [Header("Pause")]
    [SerializeField] private PauseMenuController pauseMenuController;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private bool hasWon;

    private void Awake()
    {
        hasWon = false;

        if (winPanel != null)
        {
            winPanel.SetActive(false);
        }
    }

    public void ShowWinMenu()
    {
        if (hasWon)
            return;

        hasWon = true;

        if (pauseMenuController != null)
        {
            pauseMenuController.LockPause();
        }

        if (winPanel != null)
        {
            winPanel.SetActive(true);
        }

        GameAudioController.PlayVictory();

        Time.timeScale = 0f;
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;

        if (winPanel != null)
        {
            winPanel.SetActive(false);
        }

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("SceneTransitionManager not found in scene.");
            return;
        }

        SceneTransitionManager.Instance.LoadScene(mainMenuSceneName);
    }
}
