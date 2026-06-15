using UnityEngine;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseMenuPanel;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Input")]
    [SerializeField] private bool useEscapeKeyboard = true;
    [SerializeField] private bool useGamepadStart = true;

    private InputAction pauseAction;

    private bool isPaused;
    private bool pauseLocked;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        Time.timeScale = 1f;

        isPaused = false;
        pauseLocked = false;

        CreatePauseAction();
    }

    private void OnEnable()
    {
        if (pauseAction != null)
        {
            pauseAction.performed += OnPausePerformed;
            pauseAction.Enable();
        }
    }

    private void OnDisable()
    {
        if (pauseAction != null)
        {
            pauseAction.performed -= OnPausePerformed;
            pauseAction.Disable();
        }
    }

    private void OnDestroy()
    {
        if (pauseAction != null)
        {
            pauseAction.Dispose();
            pauseAction = null;
        }
    }

    private void CreatePauseAction()
    {
        pauseAction = new InputAction(
            name: "Pause",
            type: InputActionType.Button
        );

        if (useEscapeKeyboard)
        {
            pauseAction.AddBinding("<Keyboard>/escape");
        }

        if (useGamepadStart)
        {
            pauseAction.AddBinding("<Gamepad>/start");
        }
    }

    private void OnPausePerformed(InputAction.CallbackContext context)
    {
        if (pauseLocked)
            return;

        if (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.IsTransitioning)
            return;

        TogglePause();
    }

    public void TogglePause()
    {
        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (pauseLocked)
            return;

        isPaused = true;

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("PauseMenuController: Pause Menu Panel is not assigned.");
        }

        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        isPaused = false;

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        Time.timeScale = 1f;
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        isPaused = false;

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("PauseMenuController: SceneTransitionManager not found in scene.");
            return;
        }

        SceneTransitionManager.Instance.LoadScene(mainMenuSceneName);
    }

    public void LockPause()
    {
        pauseLocked = true;

        if (isPaused)
        {
            ResumeGame();
        }
    }

    public void UnlockPause()
    {
        pauseLocked = false;
    }
}
