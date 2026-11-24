using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Three bracket presses jump to the requested level from any gameplay scene.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Level Skip Cheats")]
public sealed class LevelSkipCheats : MonoBehaviour
{
    private const string MainMenuSceneName = "MainMenu";
    private const string FirstLevelSceneName = "PrototypeScene_Lvl1";
    private const string SecondLevelSceneName = "PrototypeScene_Lvl2";
    private const int RequiredPresses = 3;

    private Key sequenceKey = Key.None;
    private int pressCount;
    private bool loadRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // Also works when Play Mode starts directly in a level. Reuse an
        // existing component when entering Play Mode without a scene reload.
        if (FindAnyObjectByType<LevelSkipCheats>() == null)
            new GameObject(nameof(LevelSkipCheats)).AddComponent<LevelSkipCheats>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        ResetSequence();
        loadRequested = false;
    }

    private void HandleActiveSceneChanged(Scene previous, Scene current)
    {
        ResetSequence();
        loadRequested = false;
    }

    private void Update()
    {
        if (loadRequested || SceneManager.GetActiveScene().name == MainMenuSceneName ||
            (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.IsTransitioning))
        {
            ResetSequence();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            ResetSequence();
            return;
        }

        bool leftPressed = keyboard.leftBracketKey.wasPressedThisFrame;
        bool rightPressed = keyboard.rightBracketKey.wasPressedThisFrame;
        if (leftPressed == rightPressed)
        {
            if (leftPressed || keyboard.anyKey.wasPressedThisFrame)
                ResetSequence();
            return;
        }

        foreach (var key in keyboard.allKeys)
        {
            if (key.wasPressedThisFrame && key.keyCode != Key.LeftBracket && key.keyCode != Key.RightBracket)
            {
                ResetSequence();
                return;
            }
        }

        Key pressedKey = leftPressed ? Key.LeftBracket : Key.RightBracket;
        if (sequenceKey != pressedKey)
        {
            sequenceKey = pressedKey;
            pressCount = 0;
        }

        if (++pressCount < RequiredPresses)
            return;

        ResetSequence();
        loadRequested = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(leftPressed ? FirstLevelSceneName : SecondLevelSceneName, LoadSceneMode.Single);
    }

    private void ResetSequence()
    {
        sequenceKey = Key.None;
        pressCount = 0;
    }
}
