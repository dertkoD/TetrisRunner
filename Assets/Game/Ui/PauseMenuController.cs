using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseMenuPanel;

    [Header("Sound Toggle")]
    [Tooltip("Image кнопки звука в pause menu. Если пусто — спрайт меняться не будет.")]
    [SerializeField] private Image soundToggleImage;

    [Tooltip("Спрайт, когда звук включён. Если пусто — будет запомнен текущий спрайт из Sound Toggle Image.")]
    [SerializeField] private Sprite soundOnSprite;

    [Tooltip("Спрайт, когда звук выключен.")]
    [SerializeField] private Sprite soundOffSprite;

    [Tooltip("Если true — при старте считать звук выключенным, если AudioListener.volume уже 0.")]
    [SerializeField] private bool readInitialMuteFromAudioListener = true;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Input")]
    [SerializeField] private bool useEscapeKeyboard = true;
    [SerializeField] private bool useGamepadStart = true;

    private InputAction pauseAction;

    private bool isPaused;
    private bool pauseLocked;
    private bool soundMuted;
    private float unmutedListenerVolume = 1f;
    private Sprite cachedSoundOnSprite;

    public bool IsPaused => isPaused;
    public bool SoundMuted => soundMuted;

    private void Awake()
    {
        Time.timeScale = 1f;

        isPaused = false;
        pauseLocked = false;
        InitializeSoundToggle();

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

    /// <summary>
    /// Вызывай из OnClick кнопки звука в pause menu.
    /// </summary>
    public void ToggleSound()
    {
        SetSoundMuted(!soundMuted);
    }

    /// <summary>
    /// Полностью включает/выключает звук игры через AudioListener.volume.
    /// </summary>
    public void SetSoundMuted(bool muted)
    {
        if (soundMuted == muted)
            return;

        soundMuted = muted;

        if (muted)
        {
            if (AudioListener.volume > 0f)
                unmutedListenerVolume = AudioListener.volume;

            AudioListener.volume = 0f;
        }
        else
        {
            AudioListener.volume = Mathf.Max(0.0001f, unmutedListenerVolume);
        }

        ApplySoundToggleSprite();
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

    private void InitializeSoundToggle()
    {
        if (soundToggleImage != null)
        {
            cachedSoundOnSprite = soundOnSprite != null
                ? soundOnSprite
                : soundToggleImage.sprite;
        }

        unmutedListenerVolume = AudioListener.volume > 0f ? AudioListener.volume : 1f;
        soundMuted = readInitialMuteFromAudioListener && AudioListener.volume <= 0f;

        ApplySoundToggleSprite();
    }

    private void ApplySoundToggleSprite()
    {
        if (soundToggleImage == null)
            return;

        if (soundMuted)
        {
            if (soundOffSprite != null)
                soundToggleImage.sprite = soundOffSprite;
        }
        else
        {
            Sprite onSprite = soundOnSprite != null ? soundOnSprite : cachedSoundOnSprite;
            if (onSprite != null)
                soundToggleImage.sprite = onSprite;
        }
    }
}
