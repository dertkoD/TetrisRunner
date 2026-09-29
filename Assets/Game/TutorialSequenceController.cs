using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A one-shot tutorial with player and block controls. Gameplay stays slow
/// throughout each part, while hint animations and fades use real time.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Tutorial/Tutorial Sequence Controller")]
public class TutorialSequenceController : MonoBehaviour
{
    [Serializable]
    public sealed class TutorialHint
    {
        public string name;
        public Key requiredKey;

        [Tooltip("Scene objects shown together for this step, including their child sprites.")]
        public List<GameObject> visuals = new List<GameObject>();

        [Tooltip("Optional. Otherwise found on a visual or its parent, including a shared hint Animator.")]
        public Animator animator;

        [Tooltip("Animator state played from the beginning when this step appears.")]
        public string animationState;

        [Tooltip("Optional trigger used instead of Animation State when that field is empty.")]
        public string animationTrigger;
    }

    private sealed class HintVisual
    {
        public GameObject root;
        public SpriteRenderer[] sprites;
        public float[] originalAlphas;
        public float alpha;
        public float startAlpha;
        public float targetAlpha;
        public float fadeElapsed;
        public float fadeDuration;
    }

    [Header("Scene References")]
    [Tooltip("Optional when this component is on the trigger itself. Disabled after the player controls part.")]
    [SerializeField] private Collider2D triggerCollider;
    [SerializeField] private TetrisBlockSpawnManager blockSpawnManager;
    [SerializeField] private PauseMenuController pauseMenu;

    [Header("Part 1: Player Controls (in order)")]
    [SerializeField] private List<TutorialHint> hints = new List<TutorialHint>
    {
        new TutorialHint { name = "Move left", requiredKey = Key.A, animationState = "Hint1_A" },
        new TutorialHint { name = "Move right", requiredKey = Key.D, animationState = "Hint1_D" },
        new TutorialHint { name = "Jump", requiredKey = Key.Space, animationState = "Hint1_Space" },
    };

    [Header("Part 2: Block Controls (in order)")]
    [SerializeField] private bool enableBlockHints;
    [SerializeField] private List<TutorialHint> blockHints = new List<TutorialHint>
    {
        new TutorialHint { name = "Block left", requiredKey = Key.LeftArrow, animationState = "Arrow_Left" },
        new TutorialHint { name = "Block right", requiredKey = Key.RightArrow, animationState = "Arrow_Right" },
        new TutorialHint { name = "Rotate clockwise", requiredKey = Key.UpArrow, animationState = "Arrow_UP" },
        new TutorialHint { name = "Soft drop", requiredKey = Key.DownArrow, animationState = "Arrow_Down" },
    };

    [Tooltip("Delay after part 1 finishes restoring time and hiding its last hint. Pauses with the pause menu.")]
    [SerializeField, Min(0f)] private float delayBeforeBlockHints = 1f;

    [Header("Part 3: Stacking And Water")]
    [SerializeField] private bool enableStackingHints;
    [SerializeField] private DeathWaterController deathWater;
    [SerializeField] private TutorialHint differentColorHint = new TutorialHint
    {
        name = "Different colors raise water", animationState = "DiffColStack",
    };
    [SerializeField] private TutorialHint sameColorHint = new TutorialHint
    {
        name = "Matching colors lower water", animationState = "WaterClipBlocksStacks",
    };
    [SerializeField, Min(0f)] private float delayBetweenStackingHints = 0.5f;

    [Header("Timing (real-time seconds)")]
    [Tooltip("Gameplay speed while waiting for the required key. Kept above zero so physics and input stay responsive.")]
    [SerializeField, Range(0.01f, 1f)] private float slowMotionScale = 0.08f;

    [Tooltip("Smooth damping time when entering slow motion.")]
    [SerializeField, Min(0.01f)] private float slowDownDuration = 0.35f;

    [Tooltip("Smooth damping time when returning to normal speed after the final required key.")]
    [SerializeField, Min(0.01f)] private float restoreDuration = 0.25f;

    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;

    private readonly List<HintVisual> visualGroups = new List<HintVisual>();
    private readonly List<Animator> hintAnimators = new List<Animator>();
    private readonly HashSet<GameObject> activeVisuals = new HashSet<GameObject>();
    private PlayerStateMachine player;
    private Coroutine sequenceRoutine;
    private int currentHintIndex = -1;
    private bool waitingForKey;
    private bool running;
    private bool hintsPrepared;
    private bool spawnLockHeld;
    private bool showingBlockHints;
    private bool stackingColorsHeld;
    private bool showingSameColorHint;
    private bool differentColorStackObserved;
    private bool sameColorStackObserved;
    private float differentColorWaterTarget;
    private float sameColorWaterTarget;
    private Animator stackingAnimator;
    private bool stackingAnimationPlaying;
    private readonly Dictionary<Animator, float> stackingAnimatorSpeeds = new Dictionary<Animator, float>();
    private readonly HashSet<Animator> stackingHintAnimators = new HashSet<Animator>();
    private float normalTimeScale;
    private float normalFixedDeltaTime;
    private float gameplayTimeScale;
    private float targetTimeScale;
    private float timeScaleVelocity;

    public bool IsRunning => running;
    public bool IsComplete { get; private set; }

    private bool IsSuspended =>
        (pauseMenu != null && pauseMenu.IsPaused) ||
        (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.IsTransitioning);

    private float TutorialDeltaTime => IsSuspended ? 0f : Time.unscaledDeltaTime;
    private List<TutorialHint> CurrentHints => showingBlockHints ? blockHints : hints;

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider2D>();
        if (blockSpawnManager == null)
            blockSpawnManager = FindAnyObjectByType<TetrisBlockSpawnManager>();
        if (pauseMenu == null)
            pauseMenu = FindAnyObjectByType<PauseMenuController>();
        if (enableStackingHints && deathWater == null)
            deathWater = FindAnyObjectByType<DeathWaterController>();

        hintsPrepared = PrepareHints();
        if (!hintsPrepared)
        {
            enabled = false;
            return;
        }

        // All Awakes run before the spawn manager's Start, including when the
        // player already overlaps the trigger on the first physics tick.
        if (enabled)
        {
            LockSpawning();
            BeginStackingColors();
        }
    }

    private void OnEnable()
    {
        if (hintsPrepared && !IsComplete)
        {
            LockSpawning();
            BeginStackingColors();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryBeginTutorial(other, triggerCollider);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Also covers spawning inside the collider and re-enabling this component.
        TryBeginTutorial(other, triggerCollider);
    }

    public bool TryBeginTutorial(Collider2D other, Collider2D sourceTrigger)
    {
        if (!isActiveAndEnabled || !hintsPrepared || running || IsComplete || IsSuspended || other == null ||
            sourceTrigger == null || !sourceTrigger.enabled || !sourceTrigger.isTrigger)
            return false;

        PlayerFacade facade = other.GetComponentInParent<PlayerFacade>();
        if (facade == null)
            return false;

        triggerCollider = sourceTrigger;
        player = facade.GetComponent<PlayerStateMachine>();
        normalTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        normalFixedDeltaTime = Time.fixedDeltaTime;
        gameplayTimeScale = normalTimeScale;
        targetTimeScale = normalTimeScale * Mathf.Clamp(slowMotionScale, 0.01f, 1f);
        timeScaleVelocity = 0f;
        running = true;

        foreach (Animator animator in hintAnimators)
        {
            if (animator == null)
                continue;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        showingBlockHints = false;
        currentHintIndex = 0;
        ShowHint(hints[currentHintIndex]);
        waitingForKey = true;
        return true;
    }

    private void Update()
    {
        if (stackingAnimationPlaying && stackingAnimator != null)
            stackingAnimator.speed = IsSuspended ? 0f : stackingAnimatorSpeeds[stackingAnimator];
        if (!running || IsSuspended)
            return;

        float dampingTime = targetTimeScale < gameplayTimeScale ? slowDownDuration : restoreDuration;
        gameplayTimeScale = Mathf.SmoothDamp(gameplayTimeScale, targetTimeScale,
            ref timeScaleVelocity, Mathf.Max(0.01f, dampingTime), Mathf.Infinity, Time.unscaledDeltaTime);
        Time.timeScale = gameplayTimeScale;
        // Keep the physics tick frequency stable in real time during slow motion.
        Time.fixedDeltaTime = normalFixedDeltaTime * gameplayTimeScale / normalTimeScale;

        Keyboard keyboard = Keyboard.current;
        if (!waitingForKey || keyboard == null)
            return;

        // Block actions use their regular input bindings. Do not consume a key
        // while no controlled block exists (for example, between two spawns).
        if (showingBlockHints && (blockSpawnManager == null || !blockSpawnManager.isActiveAndEnabled ||
            !blockSpawnManager.HasActiveBlock || blockSpawnManager.IsExternallyFrozen))
            return;

        TutorialHint hint = CurrentHints[currentHintIndex];
        if (!keyboard[hint.requiredKey].wasPressedThisFrame)
            return;

        // Preserve a quick A/D tap until the next physics tick. Movement itself
        // still uses the player's regular controls while gameplay remains slow.
        if (!showingBlockHints && player != null)
        {
            Key key = hint.requiredKey;
            if (key == Key.A || key == Key.D)
                player.SetTutorialHorizontalInput(key == Key.A ? -1f : 1f);
        }

        if (currentHintIndex + 1 < CurrentHints.Count)
        {
            currentHintIndex++;
            ShowHint(CurrentHints[currentHintIndex]);
            return;
        }

        waitingForKey = false;
        if (showingBlockHints && enableStackingHints)
        {
            // Start the first water diagram on this key press. Keep the current
            // slow-motion target while the arrow fades out and the diagram fades in.
            BeginStackingHints();
            sequenceRoutine = StartCoroutine(FinishStackingHints());
            return;
        }
        targetTimeScale = normalTimeScale;
        activeVisuals.Clear();
        BeginHintFades();
        if (player != null)
            player.ClearTutorialHorizontalInput();
        sequenceRoutine = StartCoroutine(FinishPart());
    }

    private void LateUpdate()
    {
        foreach (HintVisual group in visualGroups)
        {
            if (group.alpha != group.targetAlpha)
            {
                group.fadeElapsed += TutorialDeltaTime;
                float progress = group.fadeDuration > 0f
                    ? Mathf.Clamp01(group.fadeElapsed / group.fadeDuration)
                    : 1f;
                group.alpha = Mathf.Lerp(group.startAlpha, group.targetAlpha, Mathf.SmoothStep(0f, 1f, progress));
            }
            if (group.targetAlpha == 0f && group.alpha == 0f && group.root != null && group.root.activeSelf)
                group.root.SetActive(false);
        }
        // Applied after Animator sampling so animated colors cannot undo the fade.
        ApplyHintAlpha();
    }

    private IEnumerator FinishPart()
    {
        // Only the final key restores gameplay. Hint fades run concurrently,
        // so no transition or preview can delay the next required key.
        while (IsSuspended || Mathf.Abs(gameplayTimeScale - normalTimeScale) > 0.001f ||
            visualGroups.Exists(group => group.alpha > 0f))
            yield return null;

        // The remaining difference is below 0.001 after smooth restoration.
        RestoreTime();
        gameplayTimeScale = normalTimeScale;
        timeScaleVelocity = 0f;

        if (!showingBlockHints)
        {
            if (triggerCollider != null)
                triggerCollider.enabled = false;
            ReleaseSpawning();

            if (enableBlockHints)
            {
                yield return WaitForTutorialDelay(delayBeforeBlockHints);
                while (IsSuspended || blockSpawnManager == null || !blockSpawnManager.isActiveAndEnabled ||
                    !blockSpawnManager.HasActiveBlock || blockSpawnManager.IsExternallyFrozen)
                    yield return null;

                showingBlockHints = true;
                targetTimeScale = normalTimeScale * Mathf.Clamp(slowMotionScale, 0.01f, 1f);
                currentHintIndex = 0;
                ShowHint(blockHints[currentHintIndex]);
                waitingForKey = true;
                sequenceRoutine = null;
                yield break;
            }
        }

        if (enableStackingHints)
        {
            BeginStackingHints();
            yield return RunStackingHints();
        }

        CompleteTutorial();
    }

    private void BeginStackingHints()
    {
        blockSpawnManager.HoldTutorialAfterContrast();
        targetTimeScale = normalTimeScale * Mathf.Clamp(slowMotionScale, 0.01f, 1f);
        ShowHint(differentColorHint);
    }

    private IEnumerator FinishStackingHints()
    {
        yield return RunStackingHints();
        CompleteTutorial();
    }

    private void CompleteTutorial()
    {
        IsComplete = true;
        running = false;
        sequenceRoutine = null;
    }

    private IEnumerator WaitForTutorialDelay(float duration)
    {
        float remaining = Mathf.Max(0f, duration);
        while (remaining > 0f || IsSuspended)
        {
            yield return null;
            remaining -= TutorialDeltaTime;
        }
    }

    private IEnumerator RunStackingHints()
    {
        yield return WaitForFirstStackingAnimationCycle(differentColorHint);
        targetTimeScale = normalTimeScale;
        yield return WaitForNormalTime();

        // Keep the diagram animating until a real colored landing has raised
        // the visible surface. Misses and water cheats do not count.
        while (IsSuspended || !differentColorStackObserved ||
            deathWater.CurrentTopY < differentColorWaterTarget - 0.01f)
            yield return null;
        yield return HideCurrentHint();
        yield return WaitForTutorialDelay(delayBetweenStackingHints);

        showingSameColorHint = true;
        targetTimeScale = normalTimeScale * Mathf.Clamp(slowMotionScale, 0.01f, 1f);
        ShowHint(sameColorHint);
        while (!blockSpawnManager.BeginTutorialMatchingColor())
            yield return null;
        yield return WaitForFirstStackingAnimationCycle(sameColorHint);
        targetTimeScale = normalTimeScale;
        yield return WaitForNormalTime();

        while (IsSuspended || !sameColorStackObserved ||
            deathWater.CurrentTopY > sameColorWaterTarget + 0.01f)
            yield return null;
        yield return HideCurrentHint();
        EndStackingColors();
        RestoreStackingAnimators();
    }

    private IEnumerator WaitForFirstStackingAnimationCycle(TutorialHint hint)
    {
        stackingAnimator = ResolveAnimator(hint);
        if (!stackingAnimatorSpeeds.ContainsKey(stackingAnimator))
            stackingAnimatorSpeeds.Add(stackingAnimator, stackingAnimator.speed);
        stackingAnimationPlaying = true;
        while (IsSuspended || stackingAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;

        // Only the first cycle holds gameplay in slow motion. Let the clip
        // continue looping afterwards: the same-color clip ends with both
        // blocks transparent, so freezing its final frame hides the diagram.
    }

    private IEnumerator WaitForNormalTime()
    {
        while (IsSuspended || Mathf.Abs(gameplayTimeScale - normalTimeScale) > 0.001f)
            yield return null;
        RestoreTime();
        gameplayTimeScale = normalTimeScale;
        timeScaleVelocity = 0f;
    }

    private IEnumerator HideCurrentHint()
    {
        activeVisuals.Clear();
        BeginHintFades();
        while (IsSuspended || visualGroups.Exists(group => group.alpha > 0f))
            yield return null;
        stackingAnimationPlaying = false;
    }

    private void BeginStackingColors()
    {
        if (!enableStackingHints || stackingColorsHeld)
            return;
        blockSpawnManager.BeginTutorialStackingColors();
        deathWater.ColoredBlockLandingChangedWater += OnColoredBlockLanding;
        stackingColorsHeld = true;
    }

    private void OnColoredBlockLanding(bool sameColor, int landedColorIndex, float targetTopY)
    {
        // Retain a contrast landing that happened while the arrows were still
        // being taught; the instruction must still play its full cycle first.
        if (!showingSameColorHint && !sameColor && !differentColorStackObserved)
        {
            differentColorStackObserved = true;
            differentColorWaterTarget = targetTopY;
        }
        else if (showingSameColorHint && sameColor && !sameColorStackObserved)
        {
            sameColorStackObserved = true;
            sameColorWaterTarget = targetTopY;
        }
    }

    private void EndStackingColors()
    {
        if (!stackingColorsHeld)
            return;
        stackingColorsHeld = false;
        if (deathWater != null)
            deathWater.ColoredBlockLandingChangedWater -= OnColoredBlockLanding;
        if (blockSpawnManager != null)
            blockSpawnManager.EndTutorialStackingColors();
    }

    private void RestoreStackingAnimators()
    {
        stackingAnimationPlaying = false;
        foreach (KeyValuePair<Animator, float> entry in stackingAnimatorSpeeds)
        {
            if (entry.Key != null)
                entry.Key.speed = entry.Value;
        }
        stackingAnimatorSpeeds.Clear();
        stackingAnimator = null;
    }

    private void BeginHintFades()
    {
        foreach (HintVisual group in visualGroups)
        {
            float target = activeVisuals.Contains(group.root) ? 1f : 0f;
            if (group.targetAlpha == target)
                continue;
            group.startAlpha = group.alpha;
            group.targetAlpha = target;
            group.fadeElapsed = 0f;
            group.fadeDuration = target > 0f ? fadeInDuration : fadeOutDuration;
        }
    }

    private void ShowHint(TutorialHint hint)
    {
        activeVisuals.Clear();
        foreach (GameObject visual in hint.visuals)
        {
            if (visual == null)
                continue;
            activeVisuals.Add(visual);
            visual.SetActive(true);
        }

        // Fade the previous hint out while the next hint starts immediately.
        BeginHintFades();
        Animator animator = ResolveAnimator(hint);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.enabled = true;
            if (stackingAnimatorSpeeds.TryGetValue(animator, out float originalSpeed))
                animator.speed = originalSpeed;
            if (!string.IsNullOrWhiteSpace(hint.animationState))
            {
                int stateHash = Animator.StringToHash(hint.animationState);
                if (animator.HasState(0, stateHash))
                    animator.Play(stateHash, 0, 0f);
                else
                    Debug.LogWarning($"Tutorial hint '{hint.name}': Animator state '{hint.animationState}' was not found.", this);
            }
            else if (!string.IsNullOrWhiteSpace(hint.animationTrigger))
            {
                animator.SetTrigger(hint.animationTrigger);
            }
            animator.Update(0f);
        }
        ApplyHintAlpha();
    }

    private bool PrepareHints()
    {
        if ((enableBlockHints || enableStackingHints) && blockSpawnManager == null)
        {
            Debug.LogError("Tutorial: assign a Block Spawn Manager for the block controls part.", this);
            return false;
        }

        HashSet<GameObject> registered = new HashSet<GameObject>();
        if (!PrepareHintList(hints, registered) ||
            (enableBlockHints && !PrepareHintList(blockHints, registered)))
            return false;

        if (enableStackingHints)
        {
            if (deathWater == null)
            {
                Debug.LogError("Tutorial: assign Death Water for the stacking part.", this);
                return false;
            }
            List<TutorialHint> stackingHints = new List<TutorialHint> { differentColorHint, sameColorHint };
            if (!PrepareHintList(stackingHints, registered, false))
                return false;
            foreach (TutorialHint hint in stackingHints)
            {
                Animator animator = ResolveAnimator(hint);
                if (animator == null || animator.runtimeAnimatorController == null ||
                    string.IsNullOrWhiteSpace(hint.animationState) ||
                    !animator.HasState(0, Animator.StringToHash(hint.animationState)) || animator.speed <= 0f)
                {
                    Debug.LogError("Tutorial: each stacking hint needs an active Animator with its Animation State and positive speed.", this);
                    return false;
                }
                stackingHintAnimators.Add(animator);
            }
        }

        HideHintObjects();
        return true;
    }

    private bool PrepareHintList(List<TutorialHint> hintList, HashSet<GameObject> registered, bool requireKey = true)
    {
        if (hintList == null || hintList.Count == 0)
        {
            Debug.LogError("Tutorial: each enabled part needs at least one hint.", this);
            return false;
        }

        foreach (TutorialHint hint in hintList)
        {
            if (hint == null || (requireKey && hint.requiredKey == Key.None) || hint.visuals == null ||
                !hint.visuals.Exists(visual => visual != null))
            {
                Debug.LogError("Tutorial: every hint needs scene objects in Visuals, and keyboard hints also need a Required Key.", this);
                return false;
            }
            foreach (GameObject visual in hint.visuals)
            {
                if (visual == null || !registered.Add(visual))
                    continue;
                if (transform.IsChildOf(visual.transform))
                {
                    Debug.LogError("Tutorial: Visuals must not contain the tutorial controller or its parents.", this);
                    return false;
                }
                SpriteRenderer[] sprites = visual.GetComponentsInChildren<SpriteRenderer>(true);
                float[] alphas = new float[sprites.Length];
                for (int i = 0; i < sprites.Length; i++)
                    alphas[i] = sprites[i].color.a;
                visualGroups.Add(new HintVisual { root = visual, sprites = sprites, originalAlphas = alphas });
                foreach (Animator childAnimator in visual.GetComponentsInChildren<Animator>(true))
                {
                    if (!hintAnimators.Contains(childAnimator))
                        hintAnimators.Add(childAnimator);
                }
                Animator parentAnimator = visual.GetComponentInParent<Animator>(true);
                if (parentAnimator != null && !hintAnimators.Contains(parentAnimator))
                    hintAnimators.Add(parentAnimator);
            }
            Animator animator = ResolveAnimator(hint);
            if (animator != null && !hintAnimators.Contains(animator))
                hintAnimators.Add(animator);
        }
        return true;
    }

    private static Animator ResolveAnimator(TutorialHint hint)
    {
        if (hint.animator != null)
            return hint.animator;
        foreach (GameObject visual in hint.visuals)
        {
            if (visual == null)
                continue;
            Animator animator = visual.GetComponentInParent<Animator>(true);
            if (animator == null)
                animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null)
                return animator;
        }
        return null;
    }

    private void ApplyHintAlpha()
    {
        // Restore base alpha, then sample the current poses without advancing
        // their clocks. Clip fades (including disappearing matching blocks)
        // are multiplied by the tutorial fade instead of being overwritten.
        foreach (HintVisual group in visualGroups)
        {
            for (int i = 0; i < group.sprites.Length; i++)
            {
                SpriteRenderer sprite = group.sprites[i];
                if (sprite == null)
                    continue;
                Color color = sprite.color;
                color.a = group.originalAlphas[i];
                sprite.color = color;
            }
        }
        foreach (Animator animator in hintAnimators)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                continue;
            if (stackingHintAnimators.Contains(animator))
            {
                // Animator can skip rewriting a constant alpha curve after a
                // manual color change. Sample this instruction's clip directly
                // so its authored visibility is reapplied throughout each cycle.
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips.Length > 0)
                {
                    float normalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    float clipTime = (normalizedTime - Mathf.Floor(normalizedTime)) * clips[0].clip.length;
                    clips[0].clip.SampleAnimation(animator.gameObject, clipTime);
                }
            }
            else
                animator.Update(0f);
        }
        foreach (HintVisual group in visualGroups)
        {
            for (int i = 0; i < group.sprites.Length; i++)
            {
                SpriteRenderer sprite = group.sprites[i];
                if (sprite == null)
                    continue;
                Color color = sprite.color;
                color.a *= group.alpha;
                sprite.color = color;
            }
        }
    }

    private void HideHintObjects()
    {
        activeVisuals.Clear();
        foreach (HintVisual group in visualGroups)
        {
            group.alpha = 0f;
            group.startAlpha = 0f;
            group.targetAlpha = 0f;
        }
        ApplyHintAlpha();
        foreach (HintVisual group in visualGroups)
        {
            if (group.root != null)
                group.root.SetActive(false);
        }
    }

    private void LockSpawning()
    {
        if (spawnLockHeld || blockSpawnManager == null)
            return;
        blockSpawnManager.LockSpawningForTutorial();
        spawnLockHeld = true;
    }

    private void ReleaseSpawning()
    {
        if (!spawnLockHeld)
            return;
        spawnLockHeld = false;
        if (blockSpawnManager != null)
            blockSpawnManager.StartSpawningFromTutorial();
    }

    private void RestoreTime()
    {
        Time.timeScale = normalTimeScale;
        Time.fixedDeltaTime = normalFixedDeltaTime;
    }

    private void OnDisable()
    {
        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);
        sequenceRoutine = null;
        if (running)
        {
            // Do not override a menu pause or the time reset on a scene transition.
            if (!IsSuspended)
                RestoreTime();
            else
                Time.fixedDeltaTime = normalFixedDeltaTime;
        }
        running = false;
        waitingForKey = false;
        EndStackingColors();
        RestoreStackingAnimators();
        if (player != null)
            player.ClearTutorialHorizontalInput();
        HideHintObjects();
        ReleaseSpawning();
    }
}
