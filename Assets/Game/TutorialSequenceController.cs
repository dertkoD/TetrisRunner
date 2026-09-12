using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the keyboard tutorial used only by PrototypeScene_Tutorial.
/// All timing and fades use unscaled time, so input and UI continue to work
/// while gameplay is completely paused with Time.timeScale = 0.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Tutorial/Tutorial Sequence Controller")]
public class TutorialSequenceController : MonoBehaviour
{
    private const string TutorialSceneName = "PrototypeScene_Tutorial";

    private enum TutorialStep
    {
        Inactive,
        MoveRight,
        MoveLeft,
        WaitingForJumpTrigger,
        Jump,
        BlockLeft,
        BlockRight,
        BlockRotate,
        Transitioning,
        Complete,
    }

    [Serializable]
    public sealed class TutorialHint
    {
        [TextArea(2, 3)]
        [Tooltip("English instruction shown at the bottom of the hint panel.")]
        public string text;

        [Tooltip("Optional content object for this hint. It is enabled while this hint is active.")]
        public GameObject visual;

        [Tooltip("Optional animator containing the key-press animation for this hint.")]
        public Animator animator;

        [Tooltip("Optional Animator state to restart when the hint becomes active.")]
        public string animationState;
    }

    [Header("Scene References")]
    [SerializeField] private TetrisBlockSpawnManager blockSpawnManager;
    [SerializeField] private CanvasGroup hintPanel;
    [SerializeField] private TMP_Text hintText;

    [Header("Keyboard Layout")]
    [Tooltip("All player movement keys that stay visible together (normally W, A, S and D).")]
    [SerializeField] private GameObject[] movementKeyObjects;

    [Tooltip("Space key object. It is shown only for the jump hint.")]
    [SerializeField] private GameObject spaceKeyObject;

    [Tooltip("All block-control keys that stay visible together (normally all four arrow keys).")]
    [SerializeField] private GameObject[] blockArrowKeyObjects;

    [Tooltip("Every key Animator, including keys that never animate during the current tutorial. Non-highlighted Animators are stopped while their key images remain visible.")]
    [SerializeField] private Animator[] allKeyAnimators;

    [Header("Hint Timing (real-time seconds)")]
    [Tooltip("Delay before the very first hint panel starts fading in.")]
    [SerializeField, Min(0f)] private float initialHintDelay = 0.5f;

    [Tooltip("Gameplay speed used while the player watches a hint before the full pause.")]
    [SerializeField, Range(0.01f, 1f)] private float slowMotionScale = 0.25f;

    [Tooltip("How long gameplay remains at Slow Motion Scale before stopping completely.")]
    [SerializeField, Min(0f)] private float slowMotionDuration = 2.5f;

    [Tooltip("Duration of the smooth transition into slow motion.")]
    [SerializeField, Min(0f)] private float slowDownDuration = 0.35f;

    [Tooltip("Duration of the smooth transition from slow motion to a full pause.")]
    [SerializeField, Min(0f)] private float stopDuration = 0.25f;

    [Tooltip("Duration of the smooth transition back to normal gameplay speed.")]
    [SerializeField, Min(0f)] private float restoreDuration = 0.25f;

    [Tooltip("Fade-in and fade-out duration of the hint panel.")]
    [SerializeField, Min(0f)] private float panelFadeDuration = 0.5f;

    [Header("Block Action Preview")]
    [Tooltip("Gameplay speed used briefly after each demonstrated arrow-key action.")]
    [SerializeField, Range(0.01f, 1f)] private float blockActionTimeScale = 0.5f;

    [Tooltip("How long each block action remains visible before the next instruction.")]
    [SerializeField, Min(0f)] private float blockActionPreviewDuration = 0.5f;

    [Header("Player Movement Hints")]
    [SerializeField] private TutorialHint moveRightHint = new TutorialHint
    {
        text = "Press D to move right",
    };

    [SerializeField] private TutorialHint moveLeftHint = new TutorialHint
    {
        text = "Press A to move left",
    };

    [Header("Jump Hint")]
    [SerializeField] private TutorialHint jumpHint = new TutorialHint
    {
        text = "Press Space to jump",
    };

    [Header("Block Hints")]
    [SerializeField] private TutorialHint blockLeftHint = new TutorialHint
    {
        text = "Press the Left Arrow key to move the block left",
    };

    [SerializeField] private TutorialHint blockRightHint = new TutorialHint
    {
        text = "Press the Right Arrow key to move the block right",
    };

    [SerializeField] private TutorialHint blockRotateHint = new TutorialHint
    {
        text = "Press the Up Arrow key to rotate the block",
    };

    private TutorialStep currentStep = TutorialStep.Inactive;
    private Coroutine timeRoutine;
    private Coroutine panelRoutine;
    private float normalTimeScale = 1f;
    private float blockHorizontalOverride;
    private bool tutorialSceneActive;
    private bool completed;

    public bool IsWaitingForJumpTrigger =>
        tutorialSceneActive && currentStep == TutorialStep.WaitingForJumpTrigger;

    private void Awake()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, TutorialSceneName, StringComparison.Ordinal))
        {
            enabled = false;
            return;
        }

        tutorialSceneActive = true;
        normalTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;

        if (blockSpawnManager == null)
            blockSpawnManager = FindAnyObjectByType<TetrisBlockSpawnManager>();

        if (blockSpawnManager != null)
            blockSpawnManager.LockSpawningForTutorial();
        else
            Debug.LogError($"{nameof(TutorialSequenceController)}: Block Spawn Manager is not assigned.", this);

        EnsureHintsExist();
        PreparePanel();
    }

    private void Start()
    {
        if (!tutorialSceneActive)
            return;

        StartCoroutine(ShowInitialHintRoutine());
    }

    private IEnumerator ShowInitialHintRoutine()
    {
        if (initialHintDelay > 0f)
            yield return WaitForRealSeconds(initialHintDelay);

        SetHint(TutorialStep.MoveRight, moveRightHint, true);
        BeginSlowMotionPause();
    }

    private void Update()
    {
        if (!tutorialSceneActive || completed)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (Mathf.Abs(blockHorizontalOverride) > 0.01f && blockSpawnManager != null)
            blockSpawnManager.SetTutorialHorizontalInput(blockHorizontalOverride);

        switch (currentStep)
        {
            case TutorialStep.MoveRight:
                if (keyboard.dKey.wasPressedThisFrame)
                    CompleteMoveRightHint();
                break;

            case TutorialStep.MoveLeft:
                if (keyboard.aKey.wasPressedThisFrame)
                    CompleteMoveLeftHint();
                break;

            case TutorialStep.Jump:
                if (keyboard.spaceKey.wasPressedThisFrame)
                    CompleteJumpHint();
                break;

            case TutorialStep.BlockLeft:
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                    BeginBlockMove(-1f, TutorialStep.BlockRight, blockRightHint);
                break;

            case TutorialStep.BlockRight:
                if (keyboard.rightArrowKey.wasPressedThisFrame)
                    BeginBlockMove(1f, TutorialStep.BlockRotate, blockRotateHint);
                break;

            case TutorialStep.BlockRotate:
                if (keyboard.upArrowKey.wasPressedThisFrame && HasActiveBlock())
                {
                    currentStep = TutorialStep.Transitioning;
                    StartExclusiveTimeRoutine(FinishBlockTutorialRoutine());
                }
                break;
        }
    }

    private void OnDisable()
    {
        if (!tutorialSceneActive || completed)
            return;

        StopAllCoroutines();
        Time.timeScale = normalTimeScale;

        if (blockSpawnManager != null)
            blockSpawnManager.SetTutorialHorizontalInput(0f);

        blockHorizontalOverride = 0f;

        HidePanelImmediately();
    }

    /// <summary>
    /// Called by TutorialJumpHintTrigger. Returns true only when the movement
    /// portion is complete and the jump hint was accepted.
    /// </summary>
    public bool TryShowJumpHint()
    {
        if (!tutorialSceneActive || currentStep != TutorialStep.WaitingForJumpTrigger)
            return false;

        SetHint(TutorialStep.Jump, jumpHint, true);
        BeginSlowMotionPause();
        return true;
    }

    private void CompleteMoveRightHint()
    {
        SetHint(TutorialStep.MoveLeft, moveLeftHint, false);
        RestoreGameplayTime();
    }

    private void CompleteMoveLeftHint()
    {
        currentStep = TutorialStep.WaitingForJumpTrigger;
        FadePanel(false);
        RestoreGameplayTime();
    }

    private void CompleteJumpHint()
    {
        currentStep = TutorialStep.Transitioning;
        FadePanel(false);
        StartExclusiveTimeRoutine(StartBlockTutorialRoutine());
    }

    private void BeginBlockMove(float horizontal, TutorialStep nextStep, TutorialHint nextHint)
    {
        if (blockSpawnManager == null || !blockSpawnManager.SetTutorialHorizontalInput(horizontal))
            return;

        blockHorizontalOverride = horizontal;
        currentStep = TutorialStep.Transitioning;
        StartExclusiveTimeRoutine(PreviewBlockMoveRoutine(nextStep, nextHint));
    }

    private bool HasActiveBlock()
    {
        return blockSpawnManager != null && blockSpawnManager.HasActiveBlock;
    }

    private IEnumerator StartBlockTutorialRoutine()
    {
        yield return ChangeTimeScale(normalTimeScale, restoreDuration);

        float remainingFadeOut = panelFadeDuration - restoreDuration;
        if (remainingFadeOut > 0f)
            yield return WaitForRealSeconds(remainingFadeOut);

        if (blockSpawnManager != null)
            blockSpawnManager.StartSpawningFromTutorial();

        SetHint(TutorialStep.BlockLeft, blockLeftHint, true);

        timeRoutine = null;
        BeginSlowMotionPause();
    }

    private IEnumerator PreviewBlockMoveRoutine(TutorialStep nextStep, TutorialHint nextHint)
    {
        yield return ChangeTimeScale(blockActionTimeScale, restoreDuration);
        yield return WaitForRealSeconds(blockActionPreviewDuration);

        if (blockSpawnManager != null)
            blockSpawnManager.SetTutorialHorizontalInput(0f);

        blockHorizontalOverride = 0f;

        yield return ChangeTimeScale(0f, stopDuration);

        SetHint(nextStep, nextHint, false);
        timeRoutine = null;
    }

    private IEnumerator FinishBlockTutorialRoutine()
    {
        yield return ChangeTimeScale(blockActionTimeScale, restoreDuration);
        yield return WaitForRealSeconds(blockActionPreviewDuration);

        FadePanel(false);
        yield return ChangeTimeScale(normalTimeScale, restoreDuration);

        currentStep = TutorialStep.Complete;
        completed = true;
        timeRoutine = null;
    }

    private void BeginSlowMotionPause()
    {
        StartExclusiveTimeRoutine(SlowMotionPauseRoutine());
    }

    private IEnumerator SlowMotionPauseRoutine()
    {
        yield return ChangeTimeScale(slowMotionScale, slowDownDuration);
        yield return WaitForRealSeconds(slowMotionDuration);
        yield return ChangeTimeScale(0f, stopDuration);
        timeRoutine = null;
    }

    private void RestoreGameplayTime()
    {
        StartExclusiveTimeRoutine(RestoreGameplayTimeRoutine());
    }

    private IEnumerator RestoreGameplayTimeRoutine()
    {
        yield return ChangeTimeScale(normalTimeScale, restoreDuration);
        timeRoutine = null;
    }

    private void StartExclusiveTimeRoutine(IEnumerator routine)
    {
        if (timeRoutine != null)
            StopCoroutine(timeRoutine);

        timeRoutine = StartCoroutine(routine);
    }

    private static IEnumerator WaitForRealSeconds(float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static IEnumerator ChangeTimeScale(float target, float duration)
    {
        target = Mathf.Max(0f, target);

        if (duration <= 0f)
        {
            Time.timeScale = target;
            yield break;
        }

        float start = Time.timeScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Time.timeScale = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        Time.timeScale = target;
    }

    private void SetHint(TutorialStep step, TutorialHint hint, bool fadeIn)
    {
        currentStep = step;

        // Keep the panel hierarchy alive at all times. Visibility is controlled
        // only by CanvasGroup alpha, so child Animators can be prepared safely
        // and the panel never pops in because of SetActive.
        if (hintPanel != null && !hintPanel.gameObject.activeSelf)
            hintPanel.gameObject.SetActive(true);

        ShowKeyboardLayout(step, hint);

        if (hintText != null)
            hintText.text = hint != null ? hint.text : string.Empty;

        PlayOnlyHintAnimation(hint);

        if (hintPanel == null)
            return;

        hintPanel.blocksRaycasts = false;
        hintPanel.interactable = false;

        if (fadeIn)
            FadePanel(true);
    }

    private void ShowKeyboardLayout(TutorialStep step, TutorialHint activeHint)
    {
        SetObjectsActive(movementKeyObjects, false);
        SetObjectActive(spaceKeyObject, false);
        SetObjectsActive(blockArrowKeyObjects, false);

        switch (step)
        {
            case TutorialStep.MoveRight:
            case TutorialStep.MoveLeft:
                SetObjectsActive(movementKeyObjects, true);
                break;

            case TutorialStep.Jump:
                SetObjectActive(spaceKeyObject, true);
                break;

            case TutorialStep.BlockLeft:
            case TutorialStep.BlockRight:
            case TutorialStep.BlockRotate:
                SetObjectsActive(blockArrowKeyObjects, true);
                break;
        }

        // Keeps old scene setups functional if the group arrays have not been
        // assigned yet. In a configured scene this object is already in its group.
        if (activeHint != null)
            SetObjectActive(activeHint.visual, true);
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
            SetObjectActive(objects[i], active);
    }

    private static void SetObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private void PlayOnlyHintAnimation(TutorialHint activeHint)
    {
        Animator activeAnimator = activeHint != null ? activeHint.animator : null;

        StopHintAnimator(moveRightHint, activeAnimator);
        StopHintAnimator(moveLeftHint, activeAnimator);
        StopHintAnimator(jumpHint, activeAnimator);
        StopHintAnimator(blockLeftHint, activeAnimator);
        StopHintAnimator(blockRightHint, activeAnimator);
        StopHintAnimator(blockRotateHint, activeAnimator);

        if (allKeyAnimators != null)
        {
            for (int i = 0; i < allKeyAnimators.Length; i++)
                StopAnimator(allKeyAnimators[i], activeAnimator);
        }

        if (activeAnimator == null)
            return;

        activeAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        activeAnimator.enabled = true;
        activeAnimator.Rebind();

        if (!string.IsNullOrWhiteSpace(activeHint.animationState))
            activeAnimator.Play(activeHint.animationState, 0, 0f);

        activeAnimator.Update(0f);
    }

    private static void StopHintAnimator(TutorialHint hint, Animator activeAnimator)
    {
        if (hint != null)
            StopAnimator(hint.animator, activeAnimator);
    }

    private static void StopAnimator(Animator animator, Animator activeAnimator)
    {
        if (animator == null || animator == activeAnimator)
            return;

        animator.updateMode = AnimatorUpdateMode.UnscaledTime;

        if (animator.gameObject.activeInHierarchy)
        {
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
        }

        animator.enabled = false;
    }

    private void PreparePanel()
    {
        ShowKeyboardLayout(TutorialStep.Inactive, null);
        PlayOnlyHintAnimation(null);

        if (hintPanel == null)
            return;

        hintPanel.alpha = 0f;
        hintPanel.blocksRaycasts = false;
        hintPanel.interactable = false;
        hintPanel.gameObject.SetActive(true);
    }

    private void FadePanel(bool show)
    {
        if (hintPanel == null)
            return;

        if (panelRoutine != null)
            StopCoroutine(panelRoutine);

        if (!hintPanel.gameObject.activeSelf)
            hintPanel.gameObject.SetActive(true);

        panelRoutine = StartCoroutine(FadePanelRoutine(show ? 1f : 0f));
    }

    private IEnumerator FadePanelRoutine(float targetAlpha)
    {
        float startAlpha = hintPanel.alpha;
        float elapsed = 0f;

        if (panelFadeDuration <= 0f)
        {
            hintPanel.alpha = targetAlpha;
        }
        else
        {
            while (elapsed < panelFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / panelFadeDuration);
                hintPanel.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            hintPanel.alpha = targetAlpha;
        }

        if (targetAlpha <= 0f)
            PlayOnlyHintAnimation(null);

        panelRoutine = null;
    }

    private void HidePanelImmediately()
    {
        if (hintPanel == null)
            return;

        hintPanel.alpha = 0f;
        hintPanel.blocksRaycasts = false;
        hintPanel.interactable = false;

        if (!hintPanel.gameObject.activeSelf)
            hintPanel.gameObject.SetActive(true);

        PlayOnlyHintAnimation(null);
    }

    private void EnsureHintsExist()
    {
        moveRightHint ??= new TutorialHint { text = "Press D to move right" };
        moveLeftHint ??= new TutorialHint { text = "Press A to move left" };
        jumpHint ??= new TutorialHint { text = "Press Space to jump" };
        blockLeftHint ??= new TutorialHint { text = "Press the Left Arrow key to move the block left" };
        blockRightHint ??= new TutorialHint { text = "Press the Right Arrow key to move the block right" };
        blockRotateHint ??= new TutorialHint { text = "Press the Up Arrow key to rotate the block" };
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        slowMotionScale = Mathf.Clamp(slowMotionScale, 0.01f, 1f);
        blockActionTimeScale = Mathf.Clamp(blockActionTimeScale, 0.01f, 1f);
        initialHintDelay = Mathf.Max(0f, initialHintDelay);
        slowMotionDuration = Mathf.Max(0f, slowMotionDuration);
        slowDownDuration = Mathf.Max(0f, slowDownDuration);
        stopDuration = Mathf.Max(0f, stopDuration);
        restoreDuration = Mathf.Max(0f, restoreDuration);
        panelFadeDuration = Mathf.Max(0f, panelFadeDuration);
        blockActionPreviewDuration = Mathf.Max(0f, blockActionPreviewDuration);
    }
#endif
}
